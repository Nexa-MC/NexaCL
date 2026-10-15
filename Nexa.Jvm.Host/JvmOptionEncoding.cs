using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Nexa.Jvm.Host;

/// <summary>The invocation API consumes VM options in the runtime's native encoding.</summary>
internal static unsafe class JvmOptionEncoding
{
    internal static uint GetWindowsCodePage(string javaHome)
    {
        uint processCodePage = GetACP();
        char* text = stackalloc char[16];
        int length = GetLocaleInfoW(GetSystemDefaultLCID(), 0x1004, text, 16);
        if (length <= 1 || !uint.TryParse(new ReadOnlySpan<char>(text, length - 1),
            NumberStyles.None, CultureInfo.InvariantCulture, out uint localeCodePage)) throw Unsupported();
        if (processCodePage == localeCodePage && processCodePage != 65001) return processCodePage;
        (int major, int update) = ReadJavaVersion(javaHome);
        return SelectWindowsCodePage(processCodePage, localeCodePage, major, update);
    }

    internal static uint SelectWindowsCodePage(uint processCodePage, uint localeCodePage, int javaMajor, int javaUpdate = 0)
    {
        if (javaMajor == 8 || javaMajor == 17 && javaUpdate < 5)
        {
            // Earlier runtimes map zero to UTF-8, but their Cp65001 name is unsupported.
            if (localeCodePage == 65001) throw Unsupported();
            return localeCodePage == 0 ? 65001 : localeCodePage;
        }
        if (javaMajor == 17 && javaUpdate >= 5 || javaMajor is 21 or 25) return processCodePage;
        if (processCodePage == localeCodePage && processCodePage != 65001) return processCodePage;
        // Earlier update lines differ in whether the GetACP fix was backported.
        throw Unsupported();
    }

    internal static nint Allocate(string value, uint windowsCodePage = 0)
    {
        if (value.Contains('\0')) throw new InvalidDataException("Invalid JVM option.");
        if (!OperatingSystem.IsWindows()) return Marshal.StringToCoTaskMemUTF8(value);
        uint codePage = windowsCodePage == 0 ? GetACP() : windowsCodePage;
        bool unicodeCodePage = codePage is 65001 or 54936;
        uint flags = unicodeCodePage ? 0x80u : 0x400u;
        int substituted = 0;
        int* defaultUsed = unicodeCodePage ? null : &substituted;
        fixed (char* chars = value)
        {
            int count = WideCharToMultiByte(codePage, flags, chars, value.Length, null, 0, 0, defaultUsed);
            if (count == 0 && value.Length != 0 || substituted != 0) throw Unsupported();
            nint allocated = Marshal.AllocCoTaskMem(checked(count + 1));
            try
            {
                if (value.Length != 0 && (WideCharToMultiByte(codePage, flags, chars, value.Length,
                    (byte*)allocated, count, 0, defaultUsed) != count || substituted != 0)) throw Unsupported();
                ((byte*)allocated)[count] = 0;
                return allocated;
            }
            catch { Marshal.FreeCoTaskMem(allocated); throw; }
        }
    }

    private static (int Major, int Update) ReadJavaVersion(string javaHome)
    {
        try
        {
            using FileStream stream = File.OpenRead(Path.Combine(javaHome, "release"));
            if (stream.Length > 64 * 1024) return default;
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            while (reader.ReadLine() is { } line)
            {
                if (!line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal)) continue;
                ReadOnlySpan<char> version = line.AsSpan(13).Trim('"');
                if (version.StartsWith("1.8.", StringComparison.Ordinal)) return (8, 0);
                int end = 0;
                while (end < version.Length && (char.IsAsciiDigit(version[end]) || version[end] == '.')) end++;
                if (int.TryParse(version[..end], NumberStyles.None, CultureInfo.InvariantCulture, out int major)) return (major, 0);
                return Version.TryParse(version[..end], out Version? parsed) ? (parsed.Major, Math.Max(0, parsed.Build)) : default;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return default;
    }

    private static EncoderFallbackException Unsupported() => new("Unsupported JVM native option encoding.");

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
    [DllImport("kernel32.dll")]
    private static extern uint GetSystemDefaultLCID();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetLocaleInfoW(uint locale, uint type, char* data, int capacity);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int WideCharToMultiByte(uint codePage, uint flags, char* text, int length,
        byte* bytes, int capacity, nint defaultCharacter, int* usedDefaultCharacter);
}
