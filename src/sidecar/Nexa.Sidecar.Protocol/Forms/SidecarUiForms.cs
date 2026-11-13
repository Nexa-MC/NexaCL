using System.Text;
using Nexa.Sidecar.Protocol.Forms.Wire;

namespace Nexa.Sidecar.Protocol;

/// <summary>Generated DTO envelope for NXML commands after UiForms negotiation.</summary>
public static class SidecarUiForms
{
    public const int MaximumFields = 128, MaximumKeyLength = 256, MaximumValueLength = 512 * 1024,
        MaximumEncodedLength = 4 * 1024 * 1024;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    public static string SchemaFingerprint => ServiceWireSchema.Fingerprint;

    public static byte[] Encode(string argument, IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count > MaximumFields) throw new InvalidDataException("Form field budget exceeded.");
        List<SidecarFormField> ordered = [];
        long bytes = 0;
        foreach (var item in fields.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            ValidateKey(item.Key);
            if (item.Value is null) throw new InvalidDataException("Missing form value.");
            int size = Utf8.GetByteCount(item.Value);
            bytes += size + Utf8.GetByteCount(item.Key) + 9;
            if (size > MaximumValueLength || bytes > MaximumEncodedLength) throw new InvalidDataException("Form value budget exceeded.");
            ordered.Add(new(item.Key, Utf8.GetBytes(item.Value)));
        }
        return Encode(new(argument, ordered.ToArray()));
    }

    public static byte[] Encode(SidecarFormSubmission submission)
    {
        Validate(submission);
        using var writer = new ServiceWireWriter();
        writer.Int32(1); writer.String(SchemaFingerprint); writer.SidecarFormSubmission(submission);
        return writer.Finish();
    }

    public static SidecarFormSubmission Decode(ReadOnlyMemory<byte> payload)
    {
        using var reader = new ServiceWireReader(payload);
        if (reader.Int32() != 1 || reader.String() != SchemaFingerprint) throw new InvalidDataException("Unknown form schema.");
        var result = reader.SidecarFormSubmission() ?? throw new InvalidDataException("Missing form submission.");
        reader.End(); Validate(result); return result;
    }

    private static void Validate(SidecarFormSubmission submission)
    {
        if (submission is null || submission.Argument is null || Utf8.GetByteCount(submission.Argument) > 32768
            || submission.Fields is null || submission.Fields.Length > MaximumFields) throw new InvalidDataException("Invalid form submission.");
        string? previous = null;
        long bytes = 0;
        foreach (var field in submission.Fields)
        {
            if (field is null) throw new InvalidDataException("Missing form field.");
            ValidateKey(field.Key);
            if (previous is not null && StringComparer.Ordinal.Compare(previous, field.Key) >= 0)
                throw new InvalidDataException("Form keys must be strictly increasing.");
            previous = field.Key;
            if (field.Utf8Value is null || field.Utf8Value.Length > MaximumValueLength) throw new InvalidDataException("Invalid form value.");
            bytes += Utf8.GetByteCount(field.Key) + field.Utf8Value.Length + 9;
            if (bytes > MaximumEncodedLength) throw new InvalidDataException("Form byte budget exceeded.");
            _ = Utf8.GetCharCount(field.Utf8Value);
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaximumKeyLength || key.Any(char.IsControl))
            throw new InvalidDataException("Invalid form field key.");
        _ = Utf8.GetByteCount(key);
    }
}
