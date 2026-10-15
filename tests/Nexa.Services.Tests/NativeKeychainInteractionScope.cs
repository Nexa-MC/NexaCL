using System.Runtime.InteropServices;

namespace Nexa.Services.Tests;

/// <summary>Suppresses UI only in the explicitly enabled, isolated macOS keyring fixture process.</summary>
internal sealed partial class NativeKeychainInteractionScope : IDisposable
{
    private readonly byte _previous;
    private int _disposed;

    private NativeKeychainInteractionScope(byte previous) => _previous = previous;

    internal static NativeKeychainInteractionScope? CreateForIsolatedTest()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("NEXA_TEST_SYSTEM_KEYRING") != "1") return null;
        int status = SecKeychainGetUserInteractionAllowed(out byte previous);
        if (status != 0) throw new IOException($"Unable to read isolated keychain interaction policy (OSStatus {status}).");
        status = SecKeychainSetUserInteractionAllowed(0);
        if (status != 0) throw new IOException($"Unable to disable isolated keychain interaction (OSStatus {status}).");
        return new(previous);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        int status = SecKeychainSetUserInteractionAllowed(_previous);
        if (status != 0) throw new IOException($"Unable to restore isolated keychain interaction policy (OSStatus {status}).");
    }

    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    [LibraryImport(Security)]
    private static partial int SecKeychainGetUserInteractionAllowed(out byte state);
    [LibraryImport(Security)]
    private static partial int SecKeychainSetUserInteractionAllowed(byte state);
}
