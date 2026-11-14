using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop;

internal sealed partial class DesktopSidecarNxml
{
    private readonly List<DialogWork> _dialogWork = [];
    internal int PendingDialogControls { get { lock (_controlGate) return _dialogWork.Count; } }

    internal ValueTask<HostDialogState> ReadDialogsAsync(SidecarHostSession session, long revision, CancellationToken token)
        => QueueDialogWork(session, revision, null, false, token);
    internal async ValueTask ChangeDialogAsync(SidecarHostSession session, long revision, int document, bool open, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(document);
        await QueueDialogWork(session, revision, document, open, token).ConfigureAwait(false);
    }

    private async ValueTask<HostDialogState> QueueDialogWork(SidecarHostSession session, long revision, int? document, bool open, CancellationToken token)
    {
        if (!session.NegotiatedFeatures.HasFlag(SidecarFeatures.Dialogs)) throw new InvalidOperationException("Modal documents were not negotiated.");
        var snapshot = session.CapturePresentation(revision);
        if (document is { } index && !snapshot.Documents.Any(item => item.Index == index && item.Source.Operation == SidecarNxmlOperation.Dialog))
            throw new InvalidOperationException("Only a session-owned modal document can be controlled.");
        DialogWork work;
        lock (_controlGate)
        {
            token.ThrowIfCancellationRequested();
            if (_attached is null) throw new InvalidOperationException("Presentation is not attached.");
            if (_dialogWork.Count >= 64 || _dialogWork.Count(item => item.Owner == session) >= 8)
                throw new InvalidOperationException("Modal control queue is full.");
            work = new(session, snapshot, document, open, _attached, CancellationTokenSource.CreateLinkedTokenSource(token, snapshot.Retirement, _attached.ControlLifetime));
            _dialogWork.Add(work);
        }
        using var stop = work.Stop;
        stop.CancelAfter(TimeSpan.FromSeconds(5));
        using var cancelled = stop.Token.Register(() =>
        {
            lock (_controlGate) _dialogWork.Remove(work);
            work.Completion.TrySetCanceled(stop.Token);
        });
        try
        {
            work.Attachment.Pulse(Interlocked.Increment(ref _controlPulse));
            return await work.Completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !snapshot.Retirement.IsCancellationRequested && !work.Attachment.ControlLifetime.IsCancellationRequested)
        { throw new TimeoutException("Modal control expired before presentation."); }
        finally { lock (_controlGate) _dialogWork.Remove(work); }
    }

    private sealed record DialogWork(SidecarHostSession Owner, XsrNxmlControlSnapshot Snapshot, int? Document, bool Open, Session Attachment, CancellationTokenSource Stop)
    {
        internal TaskCompletionSource<HostDialogState> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed partial class Session
    {
        private void ProcessDialogControls()
        {
            DialogWork[] work;
            lock (_owner._controlGate)
            {
                work = _owner._dialogWork.Where(item => item.Attachment == this).ToArray();
                foreach (var item in work) _owner._dialogWork.Remove(item);
            }
            foreach (var item in work)
            {
                if (item.Completion.Task.IsCompleted) continue;
                try
                {
                    item.Stop.Token.ThrowIfCancellationRequested();
                    var current = item.Owner.CapturePresentation(item.Snapshot.Revision);
                    item.Snapshot.Retirement.ThrowIfCancellationRequested(); _stop.Token.ThrowIfCancellationRequested();
                    if (item.Document is { } index)
                    {
                        var source = current.Documents.SingleOrDefault(document => document.Index == index);
                        if (source is null || source.Source.Operation != SidecarNxmlOperation.Dialog || !_dialogs.TryGetValue(source.Exposure, out var dialog))
                            throw new InvalidOperationException("Modal document is not mounted.");
                        if (item.Open && !dialog.Open)
                        {
                            if (_onboarding != default || _shell.Stage.Navigation.Current == _guideStart || _shell.Stage.Navigation.Current == _guideEnd || _firstRun is { Completed: false })
                                throw new InvalidOperationException("Onboarding owns presentation.");
                            var modal = _shell.Stage.Overlays.LastOrDefault(node => _shell.Tree.GetComponent<XsrUiOverlayLayer>(node)?.IsModal == true);
                            if (modal.IsAssigned && !_dialogs.Values.Any(value => value.Open && value.Chrome == modal))
                                throw new InvalidOperationException("Host modal UI has priority.");
                            if (_dialogs.Values.Count(value => value.Open) >= 16 || _dialogs.Values.Count(value => value.Open && value.OwnerSession == item.Owner.SessionId) >= 8)
                                throw new InvalidOperationException("Modal document limit exceeded.");
                            dialog.OwnerSession = item.Owner.SessionId;
                            _shell.Stage.Show(dialog.Chrome, modal: true); dialog.Open = true;
                        }
                        else if (!item.Open) CloseDialog(dialog);
                    }
                    item.Completion.TrySetResult(new(current.Revision, current.Documents
                        .Where(document => document.Source.Operation == SidecarNxmlOperation.Dialog)
                        .Select(document => new HostDialogEntry(document.Index, document.Source.Target,
                            _dialogs.TryGetValue(document.Exposure, out var dialog) && dialog.Open)).ToArray()));
                }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
                { item.Completion.TrySetException(error); }
            }
        }
    }
}
