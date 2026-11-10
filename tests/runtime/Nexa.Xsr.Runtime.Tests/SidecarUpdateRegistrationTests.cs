using System.Security.Cryptography;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;

namespace Nexa.Xsr.Runtime.Tests;

internal static partial class Program
{
    private static async ValueTask UpdateDeclarationsAreValidatedAndSessionOwned()
    {
        var metadata = new SidecarUpdateRegistration("Market", "1.2.3-beta.1");
        AssertEqual(metadata, SidecarUpdateRegistration.Decode(metadata.Encode()));
        AssertThrows<SidecarProtocolException>(() => new SidecarUpdateRegistration("bad\nname", "1").Encode());
        AssertThrows<SidecarProtocolException>(() => new SidecarUpdateRegistration("Market", new string('v', 65)).Encode());
        var duplicate = new SidecarPayloadWriter(); duplicate.WriteUInt32(1, 1);
        AssertThrows<SidecarProtocolException>(() => SidecarUpdateRegistration.Decode(duplicate.ToArray().Concat(duplicate.ToArray()).ToArray()));
        AssertThrows<SidecarProtocolException>(() => SidecarUpdateRegistration.Decode([]));
        var forward = new SidecarPayloadWriter(); forward.WriteUInt32(1, 1); forward.WriteString(2, "Market");
        forward.WriteString(3, "1.2.3-beta.1"); forward.WriteString(99, "ignored");
        AssertEqual(metadata, SidecarUpdateRegistration.Decode(forward.ToArray()));

        foreach (bool corrupted in new[] { false, true })
        {
            var (hostStream, pluginStream) = SidecarLoopbackStream.CreatePair();
            using var host = new SidecarConnection(hostStream);
            using var plugin = new SidecarConnection(pluginStream);
            using var session = new SidecarHostSession(host, "Authenticated.Package");
            await CompleteHandshake(session, plugin);
            var registration = session.AcceptRegistrationAsync().AsTask();
            byte[] payload = metadata.Encode(), hash = SHA256.HashData(payload);
            if (corrupted) hash[0] ^= 0xff;
            await Send(SidecarMessageType.RegisterBegin, SidecarRegistration.EncodeBegin(1));
            await Send(SidecarMessageType.RegisterItem, SidecarRegistration.EncodeItem(new(
                SidecarRegistrationKind.Resource, SidecarUpdateRegistration.SemanticId, 0, 0, payload, hash)));
            await Send(SidecarMessageType.RegisterEnd, []);
            if (corrupted)
            {
                try { await registration; throw new InvalidOperationException("Corrupt declaration admitted."); }
                catch (SidecarProtocolException) { }
                AssertTrue(session.UpdateRegistration is null);
            }
            else
            {
                await registration;
                AssertEqual(metadata, session.UpdateRegistration);
                session.Dispose(); AssertTrue(session.UpdateRegistration is null);
            }
            async ValueTask Send(SidecarMessageType type, byte[] bytes) => await plugin.SendAsync(new(
                SidecarProtocol.Version, type, SidecarFrameTraits.None, SidecarCorrelationId.Create(), bytes));
        }
    }
}
