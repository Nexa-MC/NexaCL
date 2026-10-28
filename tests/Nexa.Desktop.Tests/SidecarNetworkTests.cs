using Nexa.Services.Composition;
using Nexa.Sidecar.Protocol;

namespace Nexa.Desktop.Tests;
internal static partial class Program
{
    private static void SidecarNetworkGrantRejectsUntrustedRequests()
    {
        static byte[] Request(string profile, string bearer, byte[] body, string? id = null)
        {
            using var writer = new SidecarPayloadWriter();
            writer.WriteString(1, profile); writer.WriteString(2, id ?? Guid.NewGuid().ToString("D"));
            writer.WriteString(3, bearer); writer.WriteBytes(4, body); return writer.ToArray();
        }
        var valid = SidecarAuthenticatedNetworkPump.DecodeRequest(Request("test", "token", [1]), "test");
        AssertEqual("token", valid.Bearer); AssertTrue(valid.Body.SequenceEqual(new byte[] { 1 }));
        foreach (byte[] invalid in new[] {
            Request("other", "token", [1]), Request("https://attacker.invalid", "token", [1]),
            Request("test", "token\r\nInjected: yes", [1]), Request("test", "", [1]),
            Request("test", new string('a', 4097), [1]), Request("test", "token", new byte[4097]),
            Request("test", "token", []), Request("test", "token", [1], "not-a-uuid") })
        {
            bool rejected = false;
            try { SidecarAuthenticatedNetworkPump.DecodeRequest(invalid, "test"); }
            catch (ArgumentException) { rejected = true; }
            AssertTrue(rejected);
        }
    }
}
