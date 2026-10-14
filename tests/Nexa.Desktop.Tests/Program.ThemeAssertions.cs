namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void AssertNull(object? value) => AssertTrue(value is null);

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }
}
