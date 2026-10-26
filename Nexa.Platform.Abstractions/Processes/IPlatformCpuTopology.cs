namespace Nexa.Platform;

public interface IPlatformCpuTopology
{
    IReadOnlyList<int> ChooseProcessors(string preference);
}
