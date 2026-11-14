using Nexa.Desktop.Ui;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop;

/// <summary>General Sidecar decisions use Host controls and literal text, without a guest SDK dependency.</summary>
internal sealed class DesktopSidecarConfirmation(TimeProvider? timeProvider = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private Binding? _binding;
    private bool _disposed;

    internal IDisposable Attach(DesktopFeedbackService feedback)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_binding is not null) throw new InvalidOperationException("Confirmation UI is already attached.");
            return _binding = new(this, feedback);
        }
    }

    internal async ValueTask<bool> AskAsync(SidecarHostCaller caller, string title, string details, CancellationToken token)
    {
        Binding binding;
        lock (_gate) binding = _binding ?? throw new InvalidOperationException("Confirmation UI is unavailable.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2), _time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, binding.Lifetime, deadline.Token);
        stop.Token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!binding.Feedback.TryShowDecisionDialog("sidecar.confirm." + Guid.NewGuid().ToString("N"),
            DesktopOverlayText.Format("SidecarConfirmationTitle", new() { ["package"] = caller.PackageName }), title + "\n\n" + details,
            DesktopOverlayText.Raw("common.confirm"), DesktopOverlayText.Raw("common.cancel"),
            accepted => completion.TrySetResult(accepted), out var dialog, localizeMessage: false, localizeTitle: false))
            throw new InvalidOperationException("A Host decision is already active.");
        using var cancelled = stop.Token.Register(() =>
        {
            completion.TrySetCanceled(stop.Token);
            binding.Feedback.DismissDialog(dialog);
        });
        try
        {
            bool accepted = await completion.Task.ConfigureAwait(false);
            stop.Token.ThrowIfCancellationRequested();
            return accepted;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested && !binding.Lifetime.IsCancellationRequested)
        { throw new TimeoutException("Confirmation expired."); }
        finally { binding.Feedback.DismissDialog(dialog); }
    }

    public void Dispose()
    {
        Binding? binding;
        lock (_gate) { if (_disposed) return; _disposed = true; binding = _binding; _binding = null; }
        binding?.Dispose();
    }

    private sealed class Binding : IDisposable
    {
        private readonly DesktopSidecarConfirmation _owner;
        internal DesktopFeedbackService Feedback { get; }
        internal Binding(DesktopSidecarConfirmation owner, DesktopFeedbackService feedback)
        { _owner = owner; Feedback = feedback; Lifetime = _stop.Token; }
        private readonly CancellationTokenSource _stop = new();
        internal CancellationToken Lifetime { get; }
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (_owner._gate) if (ReferenceEquals(_owner._binding, this)) _owner._binding = null;
            _stop.Cancel(); _stop.Dispose();
        }
    }
}
