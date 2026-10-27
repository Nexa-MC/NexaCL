using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace Nexa.Platform;

/// <summary>Explicit registration in one selected Steam profile; unknown VDF is never overwritten.</summary>
public static class PlatformSteamShortcut
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static async Task RegisterAsync(string config, string executable, bool gamescope, CancellationToken token = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        config = Path.GetFullPath(config); executable = Path.GetFullPath(executable);
        var info = new DirectoryInfo(config);
        if (info.Name != "config" || info.Parent?.Parent?.Name != "userdata" || !info.Parent.Name.All(char.IsAsciiDigit)
            || !File.Exists(Path.Combine(config, "localconfig.vdf")) || !File.Exists(executable))
            throw new IOException("请选择 Steam userdata/<用户编号>/config 目录。");
        for (var parent = info; parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Steam 配置目录不能经过符号链接。");
        if (gamescope && !File.Exists("/usr/bin/gamescope")) throw new IOException("请先安装 Gamescope。");
        EnsureStopped();
        string path = Path.Combine(config, "shortcuts.vdf");
        if (File.Exists(path) && ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > MaximumBytes))
            throw new IOException("Steam 快捷方式文件无效或过大。");
        byte[] original = File.Exists(path) ? await File.ReadAllBytesAsync(path, token).ConfigureAwait(false) : [];
        byte[] updated = Update(original, executable, gamescope);
        string temporary = path + ".nexacl-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            { await output.WriteAsync(updated, token).ConfigureAwait(false); await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true); }
            EnsureStopped(); token.ThrowIfCancellationRequested();
            byte[] current = File.Exists(path) ? await File.ReadAllBytesAsync(path, token).ConfigureAwait(false) : [];
            if (!current.AsSpan().SequenceEqual(original)) throw new IOException("Steam 快捷方式已发生变化，请重新操作。");
            if (File.Exists(path)) File.Copy(path, path + ".nexacl-backup-" + Guid.NewGuid().ToString("N"), false);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void EnsureStopped()
    {
        var processes = Process.GetProcessesByName("steam");
        try { if (processes.Length != 0) throw new IOException("请完全退出 Steam 后再注册快捷方式。"); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    internal static byte[] Update(byte[] bytes, string executable, bool gamescope)
    {
        if (bytes.Length > MaximumBytes || executable.Length > 4096 || executable.Any(c => char.IsControl(c) || c is '"' or '$' or '`' or '%')) throw new InvalidDataException();
        int offset = 0;
        List<Node> roots = bytes.Length == 0 ? [new(0, "shortcuts", [], [])] : Read(bytes, ref offset, 0);
        if (bytes.Length != 0 && offset != bytes.Length || roots.Count != 1 || roots[0].Type != 0 || roots[0].Key != "shortcuts") throw new InvalidDataException("Steam VDF 根节点无效。");
        var entries = roots[0].Children;
        if (entries.Any(n => n.Type != 0 || !int.TryParse(n.Key, out int key) || key < 0) || entries.Select(n => n.Key).Distinct(StringComparer.Ordinal).Count() != entries.Count) throw new InvalidDataException();
        string name = gamescope ? "NexaCL (Gamescope)" : "NexaCL";
        string exe = Quote(gamescope ? "/usr/bin/gamescope" : executable);
        uint id = Crc32(Utf8.GetBytes(exe + name)) | 0x80000000;
        var matching = entries.Where(n => n.Children.Any(f => f.Key == "appid" && f.Type == 2 && BinaryPrimitives.ReadUInt32LittleEndian(f.Value) == id)).ToArray();
        if (matching.Length > 1) throw new InvalidDataException();
        var existing = matching.SingleOrDefault();
        if (existing is not null && !existing.Children.Any(f => f.Key == "tags" && f.Type == 0 && f.Children.Any(t => t.Type == 1 && Text(t) == "NexaCL-managed"))) throw new IOException("Steam 中已有同名入口，未覆盖用户快捷方式。");
        string keyName = existing?.Key ?? (entries.Select(n => int.Parse(n.Key, System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).Max() + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var managed = new Node(0, keyName, [], [Integer("appid", id), String("appname", name), String("Exe", exe),
            String("StartDir", Quote(Path.GetDirectoryName(executable)!)), String("icon", ""), String("ShortcutPath", ""),
            String("LaunchOptions", gamescope ? "-f -- " + Quote(executable) + " --command=launch" : "--command=launch"),
            Integer("IsHidden", 0), Integer("AllowDesktopConfig", 1), Integer("AllowOverlay", 1), Integer("OpenVR", 0),
            new(0, "tags", [], [String("0", "NexaCL"), String("1", "NexaCL-managed")])]);
        if (existing is null) entries.Add(managed); else entries[entries.IndexOf(existing)] = managed;
        using var stream = new MemoryStream(); Write(stream, roots);
        if (stream.Length > MaximumBytes) throw new InvalidDataException();
        return stream.ToArray();
    }
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"";
    private sealed record Node(byte Type, string Key, byte[] Value, List<Node> Children);
    private static string Text(Node node) => Utf8.GetString(node.Value.AsSpan(0, node.Value.Length - 1));
    private static Node String(string key, string value) => new(1, key, Utf8.GetBytes(value + '\0'), []);
    private static Node Integer(string key, uint value) { byte[] bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); return new(2, key, bytes, []); }
    private static uint Crc32(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes) { crc ^= value; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320U : 0); }
        return ~crc;
    }
    private static List<Node> Read(byte[] data, ref int offset, int depth)
    {
        if (depth > 8) throw new InvalidDataException();
        List<Node> result = [];
        while (offset < data.Length)
        {
            byte type = data[offset++]; if (type == 8) return result;
            int end = Array.IndexOf(data, (byte)0, offset);
            if (end < offset || end - offset > 1024 || result.Count >= 4096) throw new InvalidDataException();
            string key = Utf8.GetString(data.AsSpan(offset, end - offset)); offset = end + 1;
            if (type == 0) { result.Add(new(type, key, [], Read(data, ref offset, depth + 1))); continue; }
            int length = type switch { 1 => Array.IndexOf(data, (byte)0, offset) - offset + 1, 2 or 3 or 4 or 6 => 4, 7 => 8, _ => throw new InvalidDataException("不支持的 Steam VDF 类型，未修改原文件。") };
            if (length <= 0 || length > 65536 || offset + length > data.Length) throw new InvalidDataException();
            if (type == 1) _ = Utf8.GetString(data.AsSpan(offset, length - 1));
            result.Add(new(type, key, data.AsSpan(offset, length).ToArray(), [])); offset += length;
        }
        throw new InvalidDataException("Steam VDF 未完整结束。");
    }
    private static void Write(Stream output, List<Node> nodes)
    {
        foreach (var node in nodes)
        { output.WriteByte(node.Type); output.Write(Utf8.GetBytes(node.Key + '\0')); if (node.Type == 0) Write(output, node.Children); else output.Write(node.Value); }
        output.WriteByte(8);
    }
}
