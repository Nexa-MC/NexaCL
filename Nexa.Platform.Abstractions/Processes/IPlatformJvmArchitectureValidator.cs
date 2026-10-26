namespace Nexa.Platform;

/// <summary>Validates selected native binaries before a private JVM host is started.</summary>
public interface IPlatformJvmArchitectureValidator
{
    IReadOnlyList<string> Validate(string hostExecutable, string javaExecutable, string? nativesDirectory,
        CancellationToken cancellationToken = default);
}
