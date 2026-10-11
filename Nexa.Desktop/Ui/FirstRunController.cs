using Nexa.Xsr.Renderer.Nxml;
using Nexa.Services.Composition;
using Nexa.Services.Setup;
using Nexa.Xsr.Renderer;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

internal static class FirstRunState
{
    public static readonly XsrSemanticId Wake = XsrSemanticId.Parse("setup.presentation.wake");
    public static readonly XsrSemanticId Step=XsrSemanticId.Parse("setup.presentation.step");
    public static readonly XsrSemanticId NavigationVisible=XsrSemanticId.Parse("setup.presentation.navigation_visible");
    public static void Declare(XsrStateStoreBuilder builder)
    {builder.Cell<long>(Wake,"Nexa.Desktop.Setup");builder.Cell<string>(Step,"Nexa.Desktop.Setup");builder.Cell<bool>(NavigationVisible,"Nexa.Desktop.Setup");}
}

/// <summary>Presentation-only setup draft. Persistence crosses sealed Service routes.</summary>
internal sealed class FirstRunController : IDisposable
{
    private readonly XsrUiShell _shell;
    private readonly DesktopUiIntentSink _intents;
    private readonly FirstRunRuntime _runtime;
    private readonly Func<Task<string?>> _pickDirectory;
    private readonly Action _close;
    private readonly FirstRunStatus _status;
    private readonly XsrStateStore _store;
    private long _wake;
    private readonly Dictionary<string, XsrUiEntityId> _nodes = [];
    private readonly Queue<string> _pending = [];
    private readonly CancellationTokenSource _stop = new();
    private Task<string?>? _picking;
    private Task<XsrResult>? _saving;
    private int _pickStep;
    private long _viewEpoch,_pickEpoch;
    private bool _active=true;
    private readonly NxmlDynamicContent _dynamic;
    private readonly XsrUiEntityId _workspace;
    private Dictionary<string,string> _projection=[];
    private string _error="";
    private static readonly Lazy<NxmlIrNode> Metrics=new(()=>DesktopMarkup.Load("FirstRunMetrics.nxml").Root);

    private string _directory;
    private bool _consent;
    private bool _disposed;
    internal int Step { get; private set; }
    internal bool Completed { get; private set; }
    internal XsrUiEntityId Page { get; }

    public FirstRunController(XsrUiShell shell, DesktopUiIntentSink intents, XsrStateStore store,
        FirstRunRuntime runtime, FirstRunStatus status, Func<Task<string?>> pickDirectory, Action close)
    {
        _shell = shell; _intents = intents; _runtime = runtime; _status = status;
        _store = store;
        _pickDirectory = pickDirectory; _close = close; _directory = status.DataDirectory;
        _consent = status.TelemetryRequired;
        _dynamic=new(shell.Tree,store);
        Page=NxmlUiLoader.LoadDetached(DesktopMarkup.Load("FirstRunPage.nxml"),shell.Tree,store);
        var slot=shell.Tree.Children(Page).Single();
        _projection=Projection();
        _workspace=_dynamic.Append(slot,DesktopMarkup.Dynamic("FirstRunWorkspace.d.nxml",_projection));
        shell.Tree.Walk(_workspace,entity=>{_nodes[shell.Tree.Name(entity)]=entity;return true;});
        _store.Publish(_store.Resolve(FirstRunState.NavigationVisible),false);
        shell.Stage.Navigation.Replace(Page);
        string iconPath=Path.Combine(AppContext.BaseDirectory,DesktopBootstrapText.Raw("setup.icon"));
        if(File.Exists(iconPath) && new FileInfo(iconPath).Length<=16*1048576
            && Nexa.Core.Media.PngImage.TryCreate(File.ReadAllBytes(iconPath)) is { } icon)
            shell.Tree.GetComponent<XsrUiImage>(_nodes["SetupHero"])!.Raster=new(icon,[]){FitToBounds=true};
        _intents.IntentEmitted += OnIntent;
        shell.Renderer.FramePreparing += OnFrame;
        Update();
    }

    private void OnIntent(object? sender, DesktopUiIntentEventArgs args)
    {
        if(!_disposed && _shell.Stage.Navigation.Current==Page
            && args.Intent.Command.Value.StartsWith("ui.setup.",StringComparison.Ordinal)
            && (!args.Intent.Source.IsAssigned || Admitted(args.Intent.Source,args.Intent.Command)))
            _pending.Enqueue(args.Intent.Command.Value);
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        if (_disposed || Completed) return;
        if(_shell.Stage.Navigation.Current!=Page){if(_active){_active=false;_viewEpoch++;}return;}
        _active=true;
        if (_picking is { IsCompleted: true } picked)
        {
            _picking = null;
            if (Step == _pickStep && _pickEpoch==_viewEpoch)
            {
                if (picked.IsCompletedSuccessfully && picked.Result is { } directory) _directory = directory;
                else if (picked.IsFaulted) Error(Copy("setup.error.picker"));
                Update();
            }
        }
        if (_saving is { IsCompleted: true } saved)
        {
            _saving = null;
            if (PendingQuery.Succeeded(saved))
            { Completed = true; _close(); return; }
            Error(saved.IsCompletedSuccessfully ? saved.Result.Error?.Message ?? Copy("setup.error.save") : Copy("setup.error.retry"));
            Update();
        }
        while (_pending.TryDequeue(out string? command))
        {
            if (_saving is not null || _picking is not null) continue;
            Error("");
            int previous=Step;
            switch (command)
            {
                case "ui.setup.back" when Step > 0: Step--; break;
                case "ui.setup.next" when Step < 3: Step++; break;
                case "ui.setup.next":
                    if (_runtime.Commands.TryResolve(FirstRunContract.Complete, out var route))
                    {
                        _saving = _runtime.Commands.Dispatch(route, new FirstRunCompleteCommand(_directory, _consent), cancellationToken: _stop.Token).Completion;
                        WakeWhenCompleted(_saving);
                    }
                    else Error(Copy("setup.error.route"));
                    break;
                case "ui.setup.browse" when Step == 1 && !_status.LocationLocked:
                    _pickStep = Step; _pickEpoch=_viewEpoch; _picking = PickAsync(); WakeWhenCompleted(_picking); break;
                case "ui.setup.private" when Step == 2 && !_status.TelemetryRequired: _consent = false; break;
                case "ui.setup.share" when Step == 2: _consent = true; break;
            }
            if(Step!=previous)_viewEpoch++;
            Update();
        }
        Update();
    }

    private async Task<string?> PickAsync() => await _pickDirectory().ConfigureAwait(false);
    private void WakeWhenCompleted(Task task) => _ = task.ContinueWith(_ =>
    {
        if (!_disposed) _store.Publish(_store.Resolve(FirstRunState.Wake), Interlocked.Increment(ref _wake));
    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private string Copy(string key)=>_shell.Renderer.LocalizeText(DesktopBootstrapText.Raw(key));
    private static string Flag(bool value)=>value?"true":"false";
    private Dictionary<string,string> Projection()=>Project(_shell,_status,_directory,_consent,Step,_saving is not null,_picking is not null,_error);
    internal static Dictionary<string,string> Project(XsrUiShell shell,FirstRunStatus status,string directory,
        bool consent,int step,bool saving,bool picking,string error)
    {
        if(step is <0 or >3 || status.TelemetryRequired&&!consent)throw new ArgumentException("Invalid setup projection.");
        string Copy(string key)=>shell.Renderer.LocalizeText(DesktopBootstrapText.Raw(key));
        string Format(string key,Dictionary<string,string> data)=>DesktopBootstrapText.Format(key,data,shell.Renderer.LocalizeText);
        var metrics=Metrics.Value;
        if(metrics.MinWidth is not >0 || metrics.MaxWidth is not >0)throw new NxmlCompileException("First-run viewport metrics are incomplete.");
        double width=Math.Min(metrics.MaxWidth.Value,Math.Max(metrics.MinWidth.Value,
            shell.Renderer.Viewport.Width-metrics.Padding.Left-metrics.Padding.Right));
        bool welcome=step==0,enabled=!saving&&!picking;
        string stepKey=step.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string title=Copy(step==2&&status.TelemetryRequired?"setup.title.required":"setup.title."+stepKey);
        string description=Copy(step==1&&status.LocationLocked?"setup.description.locked"
            :step==2&&status.TelemetryRequired?"setup.description.required":"setup.description."+stepKey);
        string ConsentText(bool selected,string label)=>Format("Consent",new(){["marker"]=selected?"✓":"  ",["label"]=Copy(label)});
        return new(){["frameWidth"]=width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bodyStyle"]="setup-base setup-body-"+(welcome?"welcome":"flow"),["heroStyle"]="setup-base setup-hero-"+(welcome?"welcome":"flow"),
            ["formStyle"]="setup-base setup-form-"+(welcome?"welcome":"flow"),
            ["progress"]=Format("Step",new(){["step"]=(step+1).ToString(System.Globalization.CultureInfo.InvariantCulture),["label"]=Copy("setup.label."+stepKey)}),
            ["title"]=title,["description"]=description,["directory"]=directory,
            ["summary"]=Format("Summary",new(){["directory"]=directory,["diagnostic"]=Copy(consent?"setup.enabled":"setup.disabled")}),
            ["welcome"]=Flag(welcome),["flow"]=Flag(!welcome),["directoryVisible"]=Flag(step==1),["consentVisible"]=Flag(step==2),["summaryVisible"]=Flag(step==3),
            ["browseVisible"]=Flag(!status.LocationLocked),["privateVisible"]=Flag(!status.TelemetryRequired),
            ["privateText"]=ConsentText(!consent,"setup.consent.private"),["shareText"]=ConsentText(consent,status.TelemetryRequired?"setup.consent.required":"setup.consent.share"),
            ["privateStyle"]="layout-FirstRunPage-SetupPrivate setup-base setup-button setup-consent-"+(!consent?"on":"off"),
            ["shareStyle"]="layout-FirstRunPage-SetupShare setup-base setup-button setup-consent-"+(consent?"on":"off"),
            ["nextText"]=Copy(saving?"setup.action.saving":step==3?"setup.action.finish":welcome?"setup.action.start":"setup.action.continue"),
            ["enabled"]=Flag(enabled),["shareEnabled"]=Flag(enabled&&!status.TelemetryRequired),["error"]=error,["errorVisible"]=Flag(error.Length>0)};
    }
    private void Update()
    {
        string step=Step.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var id=_store.Resolve(FirstRunState.Step);if(!Equals(_store.ReadAppliedValue(id),step))_store.Publish(id,step);
        var data=Projection();
        if(data.Count==_projection.Count && data.All(pair=>_projection.GetValueOrDefault(pair.Key)==pair.Value))return;
        _dynamic.Reload(_workspace,DesktopMarkup.Dynamic("FirstRunWorkspace.d.nxml",data));_projection=data;
    }
    private void Error(string message)=>_error=message;
    private bool Admitted(XsrUiEntityId source,XsrSemanticId command)
    {
        if(!_shell.Tree.IsAlive(source) || _shell.Tree.GetComponent<XsrUiInput>(source) is not {Enabled:true}
            || _shell.Tree.GetComponent<XsrUiCommandBinding>(source)?.Command!=command)return false;
        for(var node=source;node.IsAssigned;node=_shell.Tree.Parent(node))
        {if(_shell.Tree.GetComponent<XsrUiElement>(node) is {IsVisible:false})return false;if(node==_workspace)return true;}
        return false;
    }
    public void Dispose()
    {
        if(_disposed)return;
        _disposed = true; _stop.Cancel();
        _intents.IntentEmitted -= OnIntent; _shell.Renderer.FramePreparing -= OnFrame;
        _dynamic.Remove(_workspace);
        _store.Publish(_store.Resolve(FirstRunState.NavigationVisible),true);
    }
}
