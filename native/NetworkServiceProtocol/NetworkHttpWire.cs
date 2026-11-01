using System.Net.Http.Headers;
using Nexa.Native.Services.Wire;

namespace Nexa.Native.Network.Services;

internal static class NetworkHttpWire
{
    internal const int Create = 1000, Claim = 1001, Headers = 1002, Upload = 1003,
        EndUpload = 1004, Read = 1005, Release = 1006;
    internal const int Chunk = 64 * 1024;
    internal static void WriteHeaders(ServiceWireWriter writer, HttpHeaders headers)
    {
        var values = headers.ToArray(); writer.Count(values.Length);
        foreach (var value in values)
        { writer.String(value.Key); var items = value.Value.ToArray(); writer.Count(items.Length); foreach (var item in items) writer.String(item); }
    }
    internal static void ReadHeaders(ServiceWireReader reader, HttpHeaders headers)
    {
        int count = reader.Count(); if (count < 0) throw new InvalidDataException("Missing HTTP headers.");
        for (int index = 0; index < count; index++)
        {
            string name = reader.String() ?? throw new InvalidDataException("Missing HTTP header name.");
            int length = reader.Count(); if (length < 0) throw new InvalidDataException("Missing HTTP header values.");
            string[] values = new string[length];
            for (int value = 0; value < length; value++) values[value] = reader.String() ?? throw new InvalidDataException("Missing HTTP header value.");
            if (!headers.TryAddWithoutValidation(name, values)) throw new InvalidDataException("Invalid HTTP header placement.");
        }
    }

}
