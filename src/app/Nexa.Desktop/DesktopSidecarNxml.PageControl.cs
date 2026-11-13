using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop;

internal sealed partial class DesktopSidecarNxml
{
    private readonly object _controlGate = new();
    private readonly List<PageWork> _pageWork = [];
    private Session? _attached;
    private long _controlPulse;

    internal ValueTask<HostPageState> ReadPagesAsync(SidecarHostSession session, long revision, CancellationToken token)
        => QueuePageWork(session, revision, null, false, token);

    internal async ValueTask ChangePageAsync(SidecarHostSession session, long revision, int document, bool open, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(document);
        await QueuePageWork(session, revision, document, open, token).ConfigureAwait(false);
    }

    internal int PendingPageControls { get { lock (_controlGate) return _pageWork.Count; } }

    private async ValueTask<HostPageState> QueuePageWork(SidecarHostSession session, long revision, int? document, bool open, CancellationToken token)
    {
        var snapshot = session.CapturePresentation(revision);
        if (document is { } index && !snapshot.Documents.Any(item => item.Index == index && item.Source.Operation == SidecarNxmlOperation.Page))
            throw new InvalidOperationException("Only a session-owned ordinary page can be controlled.");
        PageWork work;
        lock (_controlGate)
        {
            token.ThrowIfCancellationRequested();
            if (_attached is null) throw new InvalidOperationException("Presentation is not attached.");
            if (_pageWork.Count >= 64 || _pageWork.Count(item => item.Owner == session) >= 8)
                throw new InvalidOperationException("Page control queue is full.");
            work = new(session, snapshot, document, open, _attached, CancellationTokenSource.CreateLinkedTokenSource(token, snapshot.Retirement, _attached.ControlLifetime));
            _pageWork.Add(work);
        }
        using var stop = work.Stop;
        stop.CancelAfter(TimeSpan.FromSeconds(5));
        // Remove cancelled work immediately, even if no further frame is rendered.
        using var cancelled = stop.Token.Register(() =>
        {
            lock (_controlGate) _pageWork.Remove(work);
            work.Completion.TrySetCanceled(stop.Token);
        });
        try
        {
            work.Attachment.Pulse(Interlocked.Increment(ref _controlPulse));
            return await work.Completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !snapshot.Retirement.IsCancellationRequested && !work.Attachment.ControlLifetime.IsCancellationRequested)
        { throw new TimeoutException("Page control expired before presentation."); }
        finally { lock (_controlGate) _pageWork.Remove(work); }
    }

    private sealed record PageWork(SidecarHostSession Owner, XsrNxmlControlSnapshot Snapshot, int? Document, bool Open, Session Attachment, CancellationTokenSource Stop)
    {
        internal TaskCompletionSource<HostPageState> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed partial class Session
    {
        internal CancellationToken ControlLifetime { get; private set; }
        internal void Pulse(long value) => _store.Publish(_store.Resolve(SidecarUiPresentationState.ControlPulse), value);
        private void ProcessPageControls()
        {
            PageWork[] work;
            lock (_owner._controlGate)
            {
                work = _owner._pageWork.Where(item => item.Attachment == this).ToArray();
                foreach (var item in work) _owner._pageWork.Remove(item);
            }
            foreach (var item in work)
            {
                if (item.Completion.Task.IsCompleted) continue;
                try
                {
                    item.Stop.Token.ThrowIfCancellationRequested();
                    var current = item.Owner.CapturePresentation(item.Snapshot.Revision);
                    item.Snapshot.Retirement.ThrowIfCancellationRequested();
                    _stop.Token.ThrowIfCancellationRequested();
                    if (item.Document is { } index)
                    {
                        var document = current.Documents.SingleOrDefault(value => value.Index == index);
                        if (document is null || document.Source.Operation != SidecarNxmlOperation.Page
                            || !_pages.TryGetValue(document.Exposure, out var page))
                            throw new InvalidOperationException("Page is not mounted.");
                        // Onboarding is Host-owned; a Sidecar cannot navigate out of it.
                        if (_onboarding != default || _shell.Stage.Navigation.Current == _guideStart || _shell.Stage.Navigation.Current == _guideEnd
                            || _firstRun is { Completed: false })
                            throw new InvalidOperationException("Onboarding owns navigation.");
                        if (item.Open)
                        {
                            if (_shell.Stage.Navigation.Current != page.Root)
                            {
                                if (_opened == default || !_pages.TryGetValue(_opened, out var prior) || _shell.Stage.Navigation.Current != prior.Root)
                                { _returnPage = _shell.Stage.Navigation.Current; _returnNavigation = _shell.SelectedNavigationId; }
                                _opened = document.Exposure;
                                _shell.Stage.Navigation.Replace(page.Root);
                            }
                        }
                        else if (_shell.Stage.Navigation.Current == page.Root)
                        {
                            if (!_shell.Tree.IsAlive(_returnPage) || _returnPage == page.Root)
                                throw new InvalidOperationException("Page has no live return target.");
                            _shell.Stage.Navigation.Replace(_returnPage);
                            _shell.Select(_returnNavigation);
                            _opened = default;
                        }
                    }
                    item.Completion.TrySetResult(new(current.Revision, current.Documents
                        .Where(document => document.Source.Operation == SidecarNxmlOperation.Page)
                        .Select(document => new HostPageEntry(document.Index, document.Source.Target,
                            _pages.TryGetValue(document.Exposure, out var page) && _shell.Stage.Navigation.Current == page.Root)).ToArray()));
                }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
                { item.Completion.TrySetException(error); }
            }
        }
    }
}
