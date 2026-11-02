using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace Nexa.Sidecar.Protocol;

public enum SidecarNxmlOperation : uint
{
    Append = 1, Prepend = 2, Before = 3, After = 4, Modify = 5, Replace = 6,
    Page = 7, OnboardingPage = 8,
}

/// <summary>A source bundle for the shared NXML compiler, not a second UI control model.</summary>
public sealed class SidecarNxmlDocument
{
    public const uint Schema = 3;
    public const int MaximumSourceBytes = 512 * 1024;
    public const int MaximumFiles = 96;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly ReadOnlyDictionary<string, string> _files;

    public string OnboardingRevision { get; init; } = "1";
    public string Target { get; }
    public SidecarNxmlOperation Operation { get; }
    public string EntryPoint { get; }
    public IReadOnlyDictionary<string, string> Files => _files;

    public static bool HasSchema(ReadOnlySpan<byte> payload)
    {
        try { return SidecarUiDocument.ReadSchema(payload) == Schema; }
        catch (SidecarProtocolException) { return false; }
    }

    public SidecarNxmlDocument(string target, SidecarNxmlOperation operation, string entryPoint,
        IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (string.IsNullOrWhiteSpace(target) || target.Length > 512 || target.Any(char.IsControl)
            || !Enum.IsDefined(operation) || files.Count is < 1 or > MaximumFiles)
            throw new SidecarProtocolException("Invalid NXML target, operation or file count.");
        ValidateName(entryPoint);
        if (!entryPoint.EndsWith(".nxml", StringComparison.Ordinal))
            throw new SidecarProtocolException("NXML entry point must be a document.");
        Dictionary<string, string> owned = new(StringComparer.Ordinal);
        HashSet<string> portableNames = new(StringComparer.OrdinalIgnoreCase);
        long bytes = 2;
        foreach (var file in files)
        {
            ValidateName(file.Key);
            if (file.Value is null || !portableNames.Add(file.Key))
                throw new SidecarProtocolException("NXML bundle contains missing or duplicate source.");
            bytes += Utf8.GetByteCount(file.Key) + Utf8.GetByteCount(file.Value) + 6;
            if (bytes > MaximumSourceBytes) throw new SidecarProtocolException("NXML source budget exceeded.");
            owned.Add(file.Key, file.Value);
        }
        if (!owned.ContainsKey(entryPoint)) throw new SidecarProtocolException("NXML entry point is missing.");
        Target = target; Operation = operation; EntryPoint = entryPoint;
        _files = new(owned);
    }

    public static byte[] Encode(SidecarNxmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var source = new MemoryStream();
        using (var files = new BinaryWriter(source, Utf8, leaveOpen: true))
        {
            files.Write((ushort)document.Files.Count);
            foreach (var file in document.Files.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                byte[] name = Utf8.GetBytes(file.Key), content = Utf8.GetBytes(file.Value);
                files.Write((ushort)name.Length); files.Write(name);
                files.Write((uint)content.Length); files.Write(content);
            }
        }
        using var writer = new SidecarPayloadWriter();
        writer.WriteUInt32(1, Schema);
        writer.WriteString(2, document.Target);
        writer.WriteUInt32(3, (uint)document.Operation);
        writer.WriteString(4, document.EntryPoint);
        byte[] packed = source.ToArray();
        const int chunkSize = 60 * 1024;
        for (int offset = 0, chunk = 0; offset < packed.Length; offset += chunkSize, chunk++)
            writer.WriteBytes((ushort)(5 + chunk), packed.AsSpan(offset, Math.Min(chunkSize, packed.Length - offset)));
        ValidateRevision(document.OnboardingRevision);
        if (document.OnboardingRevision != "1") writer.WriteString(64, document.OnboardingRevision);
        return writer.ToArray();
    }

    public static SidecarNxmlDocument Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaximumSourceBytes + 4096) throw new SidecarProtocolException("NXML payload budget exceeded.");
        var reader = new SidecarPayloadReader(payload);
        uint schema = 0;
        string? target = null, entry = null;
        string revision = "1";
        SidecarNxmlOperation operation = 0;
        using var sources = new MemoryStream();
        int chunks = 0;
        ushort previous = 0;
        while (reader.HasMore)
        {
            var field = reader.ReadNext();
            if (field.Id <= previous) throw new SidecarProtocolException("NXML fields must be ordered and unique.");
            previous = field.Id;
            switch (field.Id)
            {
                case 1: schema = field.ReadUInt32(); break;
                case 2: target = field.ReadString(); break;
                case 3: operation = (SidecarNxmlOperation)field.ReadUInt32(); break;
                case 4: entry = field.ReadString(); break;
                case 64: revision = field.ReadString(); ValidateRevision(revision); break;
                case >= 5 and < 64:
                    if (field.Id != 5 + chunks++) throw new SidecarProtocolException("NXML source chunks must be consecutive.");
                    var chunk = field.ReadBytes();
                    if (sources.Length + chunk.Length > MaximumSourceBytes) throw new SidecarProtocolException("NXML source budget exceeded.");
                    sources.Write(chunk);
                    break;
            }
        }
        if (schema != Schema || target is null || entry is null || sources.Length is < 2 or > MaximumSourceBytes)
            throw new SidecarProtocolException("Incomplete or unsupported NXML document.");
        ReadOnlySpan<byte> source = sources.GetBuffer().AsSpan(0, (int)sources.Length);
        int cursor = 2;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(source);
        if (count is < 1 or > MaximumFiles) throw new SidecarProtocolException("NXML file count exceeded.");
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        try
        {
            for (int i = 0; i < count; i++)
            {
                var length = Take(source, ref cursor, 2);
                string name = Utf8.GetString(Take(source, ref cursor, BinaryPrimitives.ReadUInt16LittleEndian(length)));
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(Take(source, ref cursor, 4));
                if (size > MaximumSourceBytes) throw new SidecarProtocolException("NXML file budget exceeded.");
                string content = Utf8.GetString(Take(source, ref cursor, (int)size));
                if (!files.TryAdd(name, content)) throw new SidecarProtocolException("Duplicate NXML file.");
            }
        }
        catch (DecoderFallbackException) { throw new SidecarProtocolException("NXML source is not strict UTF-8."); }
        if (cursor != source.Length) throw new SidecarProtocolException("Trailing NXML source bytes.");
        return new(target, operation, entry, files) { OnboardingRevision = revision };
    }

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> source, ref int cursor, int count)
    {
        if (count < 0 || count > source.Length - cursor) throw new SidecarProtocolException("Truncated NXML source.");
        var result = source.Slice(cursor, count); cursor += count; return result;
    }

    private static void ValidateRevision(string revision)
    {
        if (revision.Length is < 1 or > 64 || revision.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
            throw new SidecarProtocolException("Invalid onboarding revision.");
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 192 || name.Any(char.IsControl)
            || name.Contains('\\') || name.Contains(':') || name.StartsWith('/')
            || name.Split('/').Any(part => part is "" or "." or "..")
            || !(name.EndsWith(".nxml", StringComparison.Ordinal) || name.EndsWith(".nss", StringComparison.Ordinal)))
            throw new SidecarProtocolException("Invalid NXML bundle source name.");
    }
}
