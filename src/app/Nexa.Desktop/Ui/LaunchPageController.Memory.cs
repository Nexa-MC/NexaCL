using System.Globalization;
using Nexa.Services.Foundation;
using Nexa.Services.Memory;
using Nexa.Xsr;

namespace Nexa.Desktop.Ui;

internal sealed partial class LaunchPageController
{
    private Task<XsrResult>? _memoryCommand;
    private Task<XsrResult>? _memorySetting;
    private readonly Dictionary<string, string> _memorySettingsPending = [];

    private void ProjectMemoryOptimization()
    {
        var snapshot = _store.Read<MemoryOptimizationSnapshot>(_store.Resolve(MemoryOptimizationContract.Snapshot)).Value;
        bool supported = snapshot?.Supported == true;
        bool automatic = _store.Read<bool>(_store.Resolve(XsrSemanticId.Parse(MemoryOptimizationContract.AutomaticSetting))).Value;
        int threshold = Math.Clamp(_store.Read<int>(_store.Resolve(XsrSemanticId.Parse(MemoryOptimizationContract.ThresholdSetting))).Value, 50, 95);
        Publish(LaunchPageState.MemorySupportedKey, supported);
        Publish(LaunchPageState.MemoryAutomaticKey, automatic);
        Publish(LaunchPageState.MemoryDeepKey, _store.Read<bool>(_store.Resolve(XsrSemanticId.Parse(MemoryOptimizationContract.DeepSetting))).Value);
        Publish(LaunchPageState.MemoryThresholdKey, (double)threshold);
        Publish(LaunchPageState.MemoryCanCleanKey, supported && snapshot?.Busy != true && _memoryCommand?.IsCompleted != false
            && _foundationCommands.TryResolve(MemoryOptimizationContract.Clean, out _));
        Publish(LaunchPageState.MemoryThresholdLabelKey, HomeFormat("MemoryThreshold", new()
        { ["threshold"] = threshold.ToString(CultureInfo.CurrentCulture) }));
        Publish(LaunchPageState.MemoryUsageKey, snapshot?.Memory is { IsValid: true } memory
            ? HomeFormat("MemoryUsage", new()
            {
                ["percent"] = memory.UsedPercent.ToString("F0", CultureInfo.CurrentCulture),
                ["available"] = (memory.AvailableBytes / 1073741824d).ToString("F1", CultureInfo.CurrentCulture),
                ["total"] = (memory.TotalBytes / 1073741824d).ToString("F1", CultureInfo.CurrentCulture),
            }) : HomeCopy(supported ? "memory.waiting" : "memory.unsupported"));
        string detail = HomeCopy("memory." + (snapshot?.Status is "cleaning" or "unavailable" or "cancelled" or "elevation-cancelled" ? snapshot.Status : "idle"));
        if (snapshot is { Busy: false, Status: "complete", LastResult: { } result }) detail = HomeFormat("MemoryResult", new()
        {
            ["cleaned"] = result.Cleaned.ToString(CultureInfo.CurrentCulture),
            ["amount"] = (result.WorkingSetReductionBytes / 1048576d).ToString("F0", CultureInfo.CurrentCulture),
            ["skipped"] = result.Skipped.ToString(CultureInfo.CurrentCulture),
            ["failed"] = result.Failed.ToString(CultureInfo.CurrentCulture),
        });
        Publish(LaunchPageState.MemoryDetailKey, detail);
        FinishMemoryCommand(ref _memoryCommand);
        FinishMemoryCommand(ref _memorySetting);
        DispatchPendingMemorySetting();
    }

    private void FinishMemoryCommand(ref Task<XsrResult>? task)
    {
        if (task?.IsCompleted != true) return;
        if (task.IsCompletedSuccessfully && !task.Result.IsSuccess) _feedback.Warn(task.Result.Error!.Message);
        else if (task.IsFaulted) _feedback.Warn(HomeCopy("memory.unavailable"));
        task = null;
    }

    private bool HandleMemoryIntent(XsrSemanticId command)
    {
        if (command.Value is not ("ui.launch.memory.clean" or "ui.launch.memory.automatic" or "ui.launch.memory.threshold" or "ui.launch.memory.deep")) return false;
        if (_store.Read<MemoryOptimizationSnapshot>(_store.Resolve(MemoryOptimizationContract.Snapshot)).Value?.Supported != true) return true;
        if (command.Value == "ui.launch.memory.clean")
        {
            if (_memoryCommand?.IsCompleted != false && _foundationCommands.TryResolve(MemoryOptimizationContract.Clean, out var route))
                _memoryCommand = _foundationCommands.Dispatch(route, new MemoryCleanupCommand(), cancellationToken: _lifetimeCancellation.Token).Completion;
        }
        else
        {
            bool toggle = command.Value != "ui.launch.memory.threshold";
            string key = command.Value == "ui.launch.memory.deep" ? MemoryOptimizationContract.DeepSetting
                : toggle ? MemoryOptimizationContract.AutomaticSetting : MemoryOptimizationContract.ThresholdSetting;
            string value = toggle
                ? ((_memorySettingsPending.GetValueOrDefault(key) ?? (_store.Read<bool>(_store.Resolve(XsrSemanticId.Parse(key))).Value ? "true" : "false")) == "true" ? "false" : "true")
                : Math.Clamp((int)Math.Round(_shell.Renderer.GetRangeValue(_pageEntities["MemoryThreshold"]) ?? 80), 50, 95).ToString(CultureInfo.InvariantCulture);
            _memorySettingsPending[key] = value;
            DispatchPendingMemorySetting();
        }
        return true;
    }

    private void DispatchPendingMemorySetting()
    {
        if (_memorySetting?.IsCompleted == false || _memorySettingsPending.Count == 0
            || !_foundationCommands.TryResolve(FoundationRouteIds.SettingsSet, out var route)) return;
        var setting = _memorySettingsPending.First();
        _memorySettingsPending.Remove(setting.Key);
        _memorySetting = _foundationCommands.Dispatch(route, new SettingsSetCommand(setting.Key, setting.Value),
            cancellationToken: _lifetimeCancellation.Token).Completion;
    }
}
