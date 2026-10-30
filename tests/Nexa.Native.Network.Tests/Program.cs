namespace Nexa.Services.Tests;

/// <summary>Runs the same ownership/TLS fixtures with an actual NativeAOT caller runtime.</summary>
internal static partial class Program
{
    private static async Task Main()
    {
        NativeInstrumentationWorks();
        await NativeUpdateCodecsWork();
        await NetworkConnectionTransfersStreamsAndSurvivesRetirement();
        await NetworkConnectionCancellationAndRefusal();
        await NetworkTlsPreservesSpecializedIdentity();
        Console.WriteLine("Native network caller: transfer, cancellation, retirement and mTLS passed.");
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, received {actual}.");
    }
}
