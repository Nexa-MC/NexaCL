using System.Globalization;
using System.Text.Json.Nodes;
using Nexa.Services.Accounts;
using Nexa.Services.Logging;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Libraries;
using Nexa.Services.Minecraft.ModLoaders;
using Nexa.Services.Minecraft.Process;
using Nexa.Services.Scheduling;
using Nexa.Services.Settings;
using Nexa.Xsr;

namespace Nexa.Services.Minecraft.Launch;

public sealed partial class MinecraftLaunchCoordinator
{

    private async ValueTask<XsrResult<ResolvedJava>> ResolveJavaAsync(
        JavaSelectionResult selection,
        JavaPreference preference,
        bool hasForge,
        LogOperation? operation,
        string javaRuntimeRootDirectory,
        bool automaticallyInstall,
        CancellationToken cancellationToken)
    {
        _log?.Info("Java", $"Java selection completed success={selection.Success} failure={selection.FailureReason} minimum={selection.Requirement.Range.Minimum} maximum={selection.Requirement.Range.Maximum}");
        if (selection.Success && selection.SelectedJava is { } selected)
        {
            string installedExecutable = SelectExecutable(selected.Installation);
            return XsrResult.Success(new ResolvedJava(
                installedExecutable,
                selected.Installation.MajorVersion));
        }

        if (selection.FailureReason != JavaSelectionFailureReason.NoCompatibleRuntime)
        {
            return XsrResult.Failure<ResolvedJava>(MinecraftErrors.JavaUnavailable(
                selection.Detail ?? "the Java requirement could not be resolved."));
        }

        // An explicit, incompatible Java choice is a user-visible error. Never replace it with
        // a downloaded runtime behind the user's back.
        if (preference is ExistingJavaPreference)
        {
            return XsrResult.Failure<ResolvedJava>(MinecraftErrors.JavaUnavailable(
                "the selected Java executable is missing or incompatible with this instance."));
        }

        JavaRuntimeAcquisitionDecision acquisition = JavaRuntimeAcquisitionPlanner.Plan(
            selection.Requirement,
            hasForge);
        if (!acquisition.CanAutoDownload || string.IsNullOrWhiteSpace(acquisition.DownloadComponent))
        {
            return XsrResult.Failure<ResolvedJava>(MinecraftErrors.JavaUnavailable(
                $"no compatible Java runtime is installed and automatic acquisition is blocked ({acquisition.BlockReason})."));
        }

        JavaChoice choice = automaticallyInstall ? new(true) : await RequestAcquisitionApprovalAsync(
                acquisition,
                JavaMajor(selection.Requirement.Range.Minimum),
                operation,
                selection.Requirement, cancellationToken).ConfigureAwait(false);
        if (choice.Manual is { } manual) return XsrResult.Success(manual);
        if (!choice.Approve)
        {
            return XsrResult.Failure<ResolvedJava>(MinecraftErrors.JavaUnavailable(
                "the Java runtime acquisition was declined."));
        }

        if (choice.Component is not null)
            _progress?.Report(new MinecraftLaunchStageReport(MinecraftLaunchStages.GetJava, 0, Method: $"未找到 Java {choice.Major}，正在自动下载"));
        operation?.Stage("install_java", $"component={acquisition.DownloadComponent}");
        string acquiredExecutable = await _javaInstaller.InstallAsync(
            choice.Component ?? acquisition.DownloadComponent,
            javaRuntimeRootDirectory,
            progress: null,
            cancellationToken).ConfigureAwait(false);
        if (!File.Exists(acquiredExecutable))
        {
            return XsrResult.Failure<ResolvedJava>(MinecraftErrors.JavaUnavailable(
                "the acquired Java runtime did not provide an executable."));
        }

        int major = choice.Major ?? JavaMajor(selection.Requirement.Range.Minimum);
        return XsrResult.Success(new ResolvedJava(SelectWindowedSibling(acquiredExecutable), major));
    }

    /// <summary>
    /// Asks the user before auto-downloading a Java runtime (legacy behavior): the pipeline
    /// pauses with the acquisition cells published until a decision command or cancellation
    /// resolves it.
    /// </summary>
    private async ValueTask<JavaChoice> RequestAcquisitionApprovalAsync(
        JavaRuntimeAcquisitionDecision acquisition,
        int majorVersion,
        LogOperation? operation,
        JavaRequirementResolution requirement,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<JavaChoice> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_launchGate)
        {
            _acquisitionDecision = decision;
            _pendingJavaRequirement = requirement;
        }
        int[] choices = SelectableJavaMajors.Where(candidate => requirement.Range.TryIntersect(
            candidate == 8 ? new JavaVersionRange(new Version(1, 8), JavaVersionRange.Java8Maximum)
                : new JavaVersionRange(new Version(candidate, 0), new Version(candidate, int.MaxValue)), out _)).ToArray();
        _progress?.RequestAcquisition(acquisition.DownloadComponent ?? "unknown", majorVersion, Array.AsReadOnly(choices));
        _log?.Info("Java", $"Runtime acquisition awaiting approval component={acquisition.DownloadComponent}.");
        try
        {
            return await decision.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_launchGate)
            {
                if (ReferenceEquals(_acquisitionDecision, decision))
                {
                    _acquisitionDecision = null;
                    _pendingJavaRequirement = null;
                }
            }

            _progress?.ResolveAcquisition();
        }
    }
}
