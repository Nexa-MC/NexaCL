namespace Nexa.Sidecar.Protocol;

/// <summary>Optional update presentation, carried by a hash-bound Resource in REGISTER.</summary>
public sealed record SidecarUpdateRegistration(string DisplayName, string RuntimeVersion)
{
    public const string SemanticId = "nexa.update.registration";

    public byte[] Encode()
    {
        Validate(DisplayName, 128); Validate(RuntimeVersion, 64);
        var writer = new SidecarPayloadWriter();
        writer.WriteUInt32(1, 1); writer.WriteString(2, DisplayName); writer.WriteString(3, RuntimeVersion);
        return writer.ToArray();
    }

    public static SidecarUpdateRegistration Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 2048) throw new SidecarProtocolException("Update registration exceeds its budget.");
        uint? schema = null; string? name = null, version = null;
        var reader = new SidecarPayloadReader(payload);
        while (reader.HasMore)
        {
            var field = reader.ReadNext();
            switch (field.Id)
            {
                case 1 when schema is null: schema = field.ReadUInt32(); break;
                case 2 when name is null: name = field.ReadString(); break;
                case 3 when version is null: version = field.ReadString(); break;
                case 1 or 2 or 3: throw new SidecarProtocolException("Duplicate update registration field.");
            }
        }
        if (schema != 1) throw new SidecarProtocolException("Unsupported update registration schema.");
        Validate(name, 128); Validate(version, 64);
        return new(name!, version!);
    }

    private static void Validate(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value.Any(char.IsControl))
            throw new SidecarProtocolException("Invalid update registration text.");
    }
}
