using Nexa.Services.Capabilities;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Minecraft.Launch;

/// <summary>A single attempt's asynchronous, non-bypassable final launch gate.</summary>
public sealed class LaunchPreflightGate(XsrStateStore store,
    Func<string, string, MinecraftLaunchPlan, CancellationToken, Task<CapabilityPreflightReport>> evaluate)
{
    public static readonly XsrSemanticId StateKey = LaunchPreflightContract.StateKey;
    public static readonly XsrSemanticId DecisionCommand = LaunchPreflightContract.DecisionCommand;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (bool Blocked, TaskCompletionSource<bool> Completion, LaunchPreflightPrompt Prompt)> _pending = [];
    public static void DeclareState(XsrStateStoreBuilder builder) => LaunchPreflightContract.DeclareState(builder);

    public async Task<bool> CheckAsync(string root, string instance, MinecraftLaunchPlan plan, CancellationToken token)
    {
        CapabilityPreflightReport report = await evaluate(root, instance, plan, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (report.OverallSeverity < PreflightSeverity.Warning) return true;
        Guid attempt = Guid.NewGuid();
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                _pending.Add(attempt, (report.Issues.Any(static issue => issue.Severity == PreflightSeverity.Blocked), completion, new(attempt, report)));
                if (_pending.Count == 1) store.Publish(StateId(), _pending[attempt].Prompt, token);
            }
            return await completion.Task.WaitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) { _pending.Remove(attempt); store.Publish<LaunchPreflightPrompt?>(StateId(), _pending.Count > 0 ? _pending.First().Value.Prompt : null, CancellationToken.None); }
        }
    }
    private XsrStateId StateId() => store.Resolve(StateKey);
    public bool Decide(LaunchPreflightDecision decision)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(decision.Attempt, out var pending) || decision.Continue && pending.Blocked) return false;
            return pending.Completion.TrySetResult(decision.Continue);
        }
    }
}
