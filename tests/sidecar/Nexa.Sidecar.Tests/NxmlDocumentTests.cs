using System.Security.Cryptography;
using Nexa.Sidecar.Protocol;

namespace Nexa.Sidecar.Tests;

internal static partial class Program
{
    private static void NxmlSourceBundlesRoundTripAndRejectAmbiguity()
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal)
        {
            ["main.nxml"] = "<nxml-page><Page Key=\"FuturePage\"><FutureControl /></Page></nxml-page>",
            ["FutureControl.c.nxml"] = "<nxml-component><Node Kind=\"FutureControl\" Recipe=\"Text\" Role=\"Text\" Content=\"未来控件\" /></nxml-component>",
            ["content.d.nxml"] = "<nxml-dynamic><Text Content=\"{data caption}\" /></nxml-dynamic>",
            ["style.nss"] = new string(' ', 70 * 1024) + ".future { FontSize: 14; }",
        };
        var document = new SidecarNxmlDocument("#FuturePage/#Toolbar", SidecarNxmlOperation.After, "main.nxml", files);
        files["main.nxml"] = "changed after registration";
        byte[] encoded = SidecarNxmlDocument.Encode(document);
        var copy = SidecarNxmlDocument.Decode(encoded);
        AssertEqual("1", copy.OnboardingRevision);
        var revision = new SidecarNxmlDocument("#FuturePage", SidecarNxmlOperation.OnboardingPage, "main.nxml", document.Files) { OnboardingRevision = "2.0.alpha.17" };
        AssertEqual("2.0.alpha.17", SidecarNxmlDocument.Decode(SidecarNxmlDocument.Encode(revision)).OnboardingRevision);
        foreach (string invalid in new[] { "", "../page", "revision with spaces", new string('x', 65) })
            AssertThrows<SidecarProtocolException>(() => SidecarNxmlDocument.Encode(
                new("#FuturePage", SidecarNxmlOperation.OnboardingPage, "main.nxml", document.Files) { OnboardingRevision = invalid }));
        var registration = new SidecarRegistrationItem(SidecarRegistrationKind.UiModule,
            "test.future.nxml", 0, 0, encoded, SHA256.HashData(encoded));
        var transported = SidecarRegistration.DecodeItem(SidecarRegistration.EncodeItem(registration));
        AssertTrue(transported.Payload!.AsSpan().SequenceEqual(encoded));
        AssertEqual(document.Target, SidecarNxmlDocument.Decode(transported.Payload).Target);
        AssertEqual(document.Target, copy.Target);
        AssertEqual(document.Operation, copy.Operation);
        foreach (var operation in new[] { SidecarNxmlOperation.Page, SidecarNxmlOperation.OnboardingPage })
        {
            var page = new SidecarNxmlDocument("#FuturePage", operation, "main.nxml", document.Files);
            AssertEqual(operation, SidecarNxmlDocument.Decode(SidecarNxmlDocument.Encode(page)).Operation);
        }
        AssertEqual(document.EntryPoint, copy.EntryPoint);
        AssertEqual(4, copy.Files.Count);
        foreach (var file in document.Files) AssertEqual(file.Value, copy.Files[file.Key]);
        AssertThrows<SidecarProtocolException>(() => SidecarNxmlDocument.Decode(encoded.AsSpan(0, encoded.Length - 1)));
        AssertThrows<SidecarProtocolException>(() => _ = new SidecarNxmlDocument("#Shell", SidecarNxmlOperation.Replace, "main.nxml",
            new Dictionary<string, string> { ["main.nxml"] = "", ["../outside.nxml"] = "" }));
        AssertThrows<SidecarProtocolException>(() => _ = new SidecarNxmlDocument("#Shell", SidecarNxmlOperation.Append, "main.nxml",
            new Dictionary<string, string> { ["main.nxml"] = "", ["MAIN.nxml"] = "" }));
        AssertThrows<SidecarProtocolException>(() => _ = new SidecarNxmlDocument("#Shell", SidecarNxmlOperation.Modify, "main.nxml",
            new Dictionary<string, string> { ["main.nxml"] = new string('x', SidecarNxmlDocument.MaximumSourceBytes) }));
    }
}
