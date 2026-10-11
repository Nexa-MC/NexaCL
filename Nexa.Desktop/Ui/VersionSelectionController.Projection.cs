using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexa.Core;
using Nexa.Services.Minecraft;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;
namespace Nexa.Desktop.Ui;
internal sealed partial class VersionSelectionController
{
    private sealed record LayoutMetrics(NxmlIrNode Row,NxmlIrNode Directory,NxmlIrNode DirectoryRows,NxmlIrNode Popup,NxmlIrNode Editor);
    private static readonly Lazy<LayoutMetrics> Layout = new(() =>
    {
        var nodes=DesktopMarkup.Load("VersionSelectionMetrics.nxml").Root.Children.ToDictionary(node=>node.Key!,StringComparer.Ordinal);
        var result=new LayoutMetrics(nodes["Row"],nodes["Directory"],nodes["DirectoryRows"],nodes["Popup"],nodes["Editor"]);
        if(result.Row.Height is not >0 || result.Row.Margin.Bottom<0 || result.Directory.Height is not >0
            || result.Editor.Height is not >0 || result.Popup.MinHeight is not >0)
            throw new NxmlCompileException("Version selection metrics are incomplete.");
        return result;
    });
    private static LayoutMetrics Metrics=>Layout.Value;
    private static string Flag(bool value)=>value?"true":"false";
    private static string Number(double value)=>value.ToString(CultureInfo.InvariantCulture);
    private string Copy(string key)=>_shell.Renderer.LocalizeText(DesktopVersionText.Raw(key));
    private string Format(string name,Dictionary<string,string> data)=>DesktopVersionText.Format(name,data,_shell.Renderer.LocalizeText);
    private string DirectoryName(MinecraftLibraryDirectory directory)=>directory.IsOfficial?Copy("directory.official"):directory.DisplayName;
    private static string SafeSuffix(string prefix,string value)
        => value.Length<=80 && !value.Any(character=>char.IsWhiteSpace(character)||char.IsControl(character))
            ? prefix+value : prefix+"sha256:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string RowSuffix(string id)=>SafeSuffix("version:",id);
    private static string DirectorySuffix(string path)=>SafeSuffix("directory:",path);
    private Dictionary<string,XsrUiEntityId> Nodes(XsrUiEntityId root)
    {
        Dictionary<string,XsrUiEntityId> result=[];
        _shell.Tree.Walk(root,entity=>{result[_shell.Tree.Name(entity)]=entity;return true;});return result;
    }
    private XsrUiEntityId Append(XsrUiEntityId slot,string template,Dictionary<string,string> data)
    {
        var root=_dynamic.Append(slot,DesktopMarkup.Dynamic(template,data));_views[root]=(template,data);return root;
    }
    private void Reload(XsrUiEntityId root,Dictionary<string,string> data)
    {
        var view=_views[root];
        if(view.Data.Count==data.Count && view.Data.All(pair=>data.GetValueOrDefault(pair.Key)==pair.Value))return;
        _dynamic.Reload(root,DesktopMarkup.Dynamic(view.Template,data));_views[root]=(view.Template,data);
    }
    private Dictionary<string,string> PopupData()
    {
        double directoryHeight = _chooseDirectories ? Snapshot.Directories.Count*Metrics.Directory.Height!.Value
            +Math.Max(0,Snapshot.Directories.Count-1)*Metrics.DirectoryRows.Spacing:0;
        double natural = directoryHeight+Metrics.Popup.Padding.Top+Metrics.Popup.Padding.Bottom
            +(_editingRoot is null?0:Metrics.Editor.Height!.Value+Metrics.Popup.Spacing)
            +(_manualAdd?Metrics.Editor.Height!.Value+Metrics.Popup.Spacing:0);
        double height = Math.Min(natural,Math.Max(Metrics.Popup.MinHeight!.Value,
            _shell.Renderer.Viewport.Height-Metrics.Popup.Margin.Bottom));
        return new(){["visible"]=Flag(_chooseDirectories),["height"]=Number(height),["indicator"]=Flag(natural>height),
            ["renameVisible"]=Flag(_editingRoot is not null),["addVisible"]=Flag(_manualAdd)};
    }
    private void ProjectPopup()=>Reload(_dropdown,PopupData());
    private Dictionary<string,string> VersionRowData(MinecraftInstanceDescriptor instance,MinecraftLibrarySnapshot snapshot)
    {
        string suffix=RowSuffix(instance.Id);
        string name=string.IsNullOrWhiteSpace(instance.Metadata.DisplayName)?instance.Id:instance.Metadata.DisplayName;
        bool current=instance.Id==snapshot.SelectedInstanceId;
        bool selected=_transferSelection.Count>0?_transferSelection.Contains(instance.Id):current;
        string accessible=current?Format("Selection",new(){["name"]=name,["marker"]=Copy("version.current")})
            :Format("Select",new(){["name"]=name});
        if(_transferSelection.Contains(instance.Id))accessible=Format("TransferSelection",new(){["label"]=accessible});
        string detail=instance.MetadataError is null ? Format("Detail",new(){["kind"]=VersionKindLabel(instance.Version.Kind),
            ["version"]=instance.Version.InheritsFrom is {Length:>0} parent?parent:instance.VersionId}) :Copy("version.unavailable");
        return new(){["row"]="LibraryRow:"+suffix,["accessible"]=accessible,["selected"]=Flag(selected),["current"]=Flag(current),
            ["check"]="LibraryRowCheck:"+suffix,["iconKey"]="LibraryRowIcon:"+suffix,["icon"]=VersionIcon(instance.Version.Kind),
            ["nameKey"]="LibraryRowName:"+suffix,["name"]=name,["detailKey"]="LibraryRowDetail:"+suffix,["detail"]=detail,
            ["modify"]="LibraryRowModify:"+suffix,["settings"]="LibraryRowSettings:"+suffix,["delete"]="LibraryRowDelete:"+suffix,
            ["style"]="layout-VersionSelectionRow-LibraryRow version-base version-row"+(selected?" version-selected":"")
                +(_shownIndices[instance.Id]==_shown.Count-1?" version-row-last":"")};
    }
    private Dictionary<string,string> DirectoryRowData(MinecraftLibraryDirectory directory,MinecraftLibrarySnapshot snapshot)
    {
        string suffix=DirectorySuffix(directory.Path),name=DirectoryName(directory);
        bool selected=PathIdentity.Comparer.Equals(directory.Path,snapshot.RootDirectory);
        return new(){["row"]="LibraryRow:"+suffix,["accessible"]=selected?Format("Selection",new(){["name"]=name,["marker"]=Copy("directory.current")})
            :Format("Select",new(){["name"]=name}),["selected"]=Flag(selected),["nameKey"]="LibraryDirectoryRowName:"+suffix,["name"]=name,
            ["pathKey"]="LibraryDirectoryRowPath:"+suffix,["path"]=directory.Path,["named"]=Flag(directory.HasName),
            ["check"]="LibraryDirectoryRowCheck:"+suffix,["rename"]="LibraryDirectoryRename:"+suffix,["renameVisible"]=Flag(!directory.IsOfficial),
            ["forget"]="LibraryDirectoryForget:"+suffix,["forgetVisible"]=Flag(snapshot.Directories.Count>1),
            ["style"]="layout-VersionDirectoryRow-LibraryRow version-base version-directory-row"+(selected?" version-selected":"")};
    }
    private void ReconcileRows(MinecraftLibrarySnapshot snapshot)
    {
        var focused=_shell.Renderer.Focused;SyncVersionWindow(snapshot,refresh:true);
        var slot=_entities["LibraryDirectoryRows"];
        var wanted=new HashSet<string>(snapshot.Directories.Select(item=>item.Path),PathIdentity.Comparer);
        foreach(var pair in _directories.ToArray())
        {
            if(wanted.Contains(pair.Value))continue;
            foreach(var entity in Nodes(pair.Key).Values){_forget.Remove(entity);_rename.Remove(entity);}
            _directories.Remove(pair.Key);_views.Remove(pair.Key);_dynamic.Remove(pair.Key);
        }
        List<XsrUiEntityId> order=[];
        foreach(var directory in snapshot.Directories)
        {
            var row=_directories.FirstOrDefault(pair=>PathIdentity.Comparer.Equals(pair.Value,directory.Path)).Key;
            if(!row.IsAssigned)
            {
                row=Append(slot,"VersionDirectoryRow.d.nxml",DirectoryRowData(directory,snapshot));
                _directories[row]=directory.Path;var nodes=Nodes(row);string suffix=DirectorySuffix(directory.Path);
                _forget[nodes["LibraryDirectoryForget:"+suffix]]=directory.Path;
                _rename[nodes["LibraryDirectoryRename:"+suffix]]=directory.Path;
            }
            else Reload(row,DirectoryRowData(directory,snapshot));
            order.Add(row);
        }
        _dynamic.Reorder(slot,order);
        if(_chooseDirectories&&!_shell.Tree.IsAlive(focused))FocusDirectory();
    }
    private bool Admitted(XsrUiEntityId source,XsrSemanticId command)
    {
        if(!_shell.Tree.IsAlive(source))return false;
        bool inPage=false,inPopup=false;
        for(var current=source;current.IsAssigned;current=_shell.Tree.Parent(current))
        {
            if(_shell.Tree.GetComponent<XsrUiElement>(current) is {IsVisible:false})return false;
            if(current==_dropdown)inPopup=true;
            if(current==Page){inPage=true;break;}
        }
        if(!inPage || _chooseDirectories&&!inPopup)return false;
        if(command.Value=="ui.versions.dismiss"&&source==_dropdown)return _chooseDirectories;
        if(_shell.Tree.GetComponent<XsrUiInput>(source) is not {Enabled:true})return false;
        if((_versions.TryGetValue(source,out var version)||_actions.TryGetValue(source,out version))
            &&!PathIdentity.Comparer.Equals(version.Root,Snapshot.RootDirectory))return false;
        if(_shell.Tree.GetComponent<XsrUiCommandBinding>(source)?.Command==command)return true;
        if(_versions.ContainsKey(source)&&_shell.Tree.GetComponent<XsrUiFileDrag>(source)?.Completed==command)return true;
        return _versions.ContainsKey(source) && command.Value is "ui.versions.toggle-transfer" or "ui.versions.extend-transfer"
            or "ui.versions.add-range" or "ui.versions.modify" or "ui.versions.settings" or "ui.versions.delete";
    }
}
