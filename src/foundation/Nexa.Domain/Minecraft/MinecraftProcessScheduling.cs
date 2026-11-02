using System.Globalization;

namespace Nexa.Services.Minecraft.Launch;

public enum MinecraftProcessQuality { Default, Efficiency, Background }

public static class MinecraftProcessScheduling
{
    public static IReadOnlyList<int> ParseCpuSets(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 4096) throw new ArgumentException("CPU selection exceeds 4096 characters.", nameof(value));
        if (string.IsNullOrWhiteSpace(value)) return [];
        SortedSet<int> processors = [];
        foreach (string part in value.Split(','))
        {
            string[] range = part.Trim().Split('-');
            if (range.Length is < 1 or > 2 || !Read(range[0], out int first)) throw Invalid();
            int last = first;
            if (range.Length == 2 && (!Read(range[1], out last) || last < first)) throw Invalid();
            for (int cpu = first; cpu <= last; cpu++)
                if (!processors.Add(cpu)) throw Invalid();
        }
        return Array.AsReadOnly(processors.ToArray());
        static bool Read(string text, out int value) => int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value is >= 0 and < 1024;
        static ArgumentException Invalid() => new("Use unique CPU indices from 0 to 1023, such as 0,2-4; leave empty for the OS default.");
    }
}
