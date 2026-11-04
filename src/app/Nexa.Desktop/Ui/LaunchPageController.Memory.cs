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
        if (snapshot is { Busy: false, Status: "complete", LastResult: { } result }) detail = HomeFormat("MemoryResultV2", new()
        {
            ["cleaned"] = result.Cleaned.ToString(CultureInfo.CurrentCulture),
            ["amount"] = (result.WorkingSetReductionBytes / 1048576d).ToString("F0", CultureInfo.CurrentCulture),
            ["skipped"] = result.Skipped.ToString(CultureInfo.CurrentCulture),
            ["failed"] = result.Failed.ToString(CultureInfo.CurrentCulture),
            ["available"] = (result.AvailableChangeBytes / 1048576d).ToString("+0;-0;0", CultureInfo.CurrentCulture),
            ["passed"] = result.SystemOperations.Count(x => x.Outcome == SystemMemoryCleanupOutcome.Succeeded).ToString(CultureInfo.CurrentCulture),
            ["total"] = result.SystemOperations.Count.ToString(CultureInfo.CurrentCulture),
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
        if (command.Value is not ("ui.launch.memory.clean" or "ui.launch.memory.automatic" or "ui.launch.memory.threshold" or "ui.launch.memory.deep" or "ui.launch.memory.report")) return false;
        if (_store.Read<MemoryOptimizationSnapshot>(_store.Resolve(MemoryOptimizationContract.Snapshot)).Value?.Supported != true) return true;
        if (command.Value == "ui.launch.memory.report")
        {
            var result = _store.Read<MemoryOptimizationSnapshot>(_store.Resolve(MemoryOptimizationContract.Snapshot)).Value?.LastResult;
            if (result is null) return true;
            string T(string source) => _shell.Renderer.LocalizeText(source);
            string message = T("工作集观察减少量") + ": " + (result.WorkingSetReductionBytes / 1048576d).ToString("F1", CultureInfo.CurrentCulture) + " MiB\n"
                + T("可用内存变化") + ": " + (result.AvailableChangeBytes / 1048576d).ToString("+0.0;-0.0;0", CultureInfo.CurrentCulture) + " MiB\n"
                + T("进程：已清理 / 已保护 / 失败") + ": " + result.Cleaned + " / " + result.Skipped + " / " + result.Failed + "\n"
                + T("总耗时") + ": " + result.ElapsedMilliseconds + " ms\n"
                + string.Join("\n", result.SystemOperations.Select(step => MemoryOperationName(step.Operation) + ": " + MemoryOutcomeName(step.Outcome)
                    + " · " + (step.AvailableChangeBytes / 1048576d).ToString("+0.0;-0.0;0", CultureInfo.CurrentCulture) + " MiB · " + step.ElapsedMilliseconds + " ms"
                    + (step.Outcome is SystemMemoryCleanupOutcome.Failed or SystemMemoryCleanupOutcome.AccessDenied or SystemMemoryCleanupOutcome.Unsupported
                        ? " · NT 0x" + step.NativeStatus.ToString("X8", CultureInfo.InvariantCulture) : "")));
            _feedback.ShowDialog("memory.report", T("内存清理报告"), message, T("确定"), T("关闭"), _ => { }, localizeMessage: false);
            return true;
        }
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

    private string MemoryOperationName(SystemMemoryCleanupOperation operation) => _shell.Renderer.LocalizeText(operation switch
    {
        SystemMemoryCleanupOperation.WorkingSets => "系统工作集",
        SystemMemoryCleanupOperation.FileCache => "系统文件缓存",
        SystemMemoryCleanupOperation.VolumeCache => "卷缓存刷新",
        SystemMemoryCleanupOperation.ModifiedPages => "修改页写回",
        SystemMemoryCleanupOperation.StandbyPages => "待机页列表",
        SystemMemoryCleanupOperation.LowPriorityStandby => "低优先级待机页",
        SystemMemoryCleanupOperation.RegistryCache => "注册表缓存",
        SystemMemoryCleanupOperation.CombinePages => "内存页合并",
        _ => operation.ToString()
    });
    private string MemoryOutcomeName(SystemMemoryCleanupOutcome outcome) => _shell.Renderer.LocalizeText(outcome switch
    {
        SystemMemoryCleanupOutcome.Succeeded => "已完成",
        SystemMemoryCleanupOutcome.GameProtected => "已跳过：保护运行中的游戏",
        SystemMemoryCleanupOutcome.Unsupported => "系统不支持",
        SystemMemoryCleanupOutcome.AccessDenied => "权限不足",
        _ => "执行失败"
    });

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
