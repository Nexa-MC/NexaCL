using System.Text.Json.Nodes;
using Nexa.Services.Themes;

namespace Nexa.Services.Themes.Tests;

internal static partial class Program
{
    private static async Task OptionControlsPreserveTypedValuesAndLegacyManifests()
    {
        using var workspace = new Workspace();
        string manifest = ControlManifest("""
            [{"id":"switch","kind":"Boolean","control":"Switch","default":true,"label":{"en":"Switch"}},
             {"id":"check","kind":"Boolean","control":"CheckBox","default":false,"label":{"en":"Check"}},
             {"id":"boolean-choice","kind":"Boolean","control":"Segmented","default":false,"label":{"en":"Boolean choice"}},
             {"id":"numeric","kind":"Number","control":"Slider","default":4,"min":0,"max":10,"step":2,"label":{"en":"Numeric"}},
             {"id":"choice","kind":"Select","control":"Slider","default":"large","choices":[{"value":"small","label":{"en":"Small"}},{"value":"large","label":{"en":"Large"}}],"label":{"en":"Choice"}},
             {"id":"input","kind":"Range","control":"Input","default":4,"min":0,"max":10,"step":2,"label":{"en":"Input"}},
             {"id":"last-range","kind":"Range","default":3,"min":0,"max":10,"step":3,"label":{"en":"Last range"}},
             {"id":"last-number","kind":"Number","control":"Slider","default":3,"min":0,"max":10,"step":3,"label":{"en":"Last number"}},
             {"id":"legacy-boolean","kind":"Boolean","default":false,"label":{"en":"Legacy"}},
             {"id":"legacy-single","kind":"Select","default":"only","choices":[{"value":"only","label":{"en":"Only"}}],"label":{"en":"Single"}}]
            """);
        string source = workspace.Theme(manifest: manifest);
        string identity;
        using (var service = workspace.Service())
        {
            var prepared = await service.PrepareAsync(new(source, ThemeSourceKind.Directory));
            Require(prepared.IsSuccess, prepared.Error?.Message ?? "Control manifest did not prepare.");
            var candidate = prepared.Value; identity = candidate.SourceIdentity;
            Require(candidate.Manifest.Options.Single(option => option.Id == "switch").Control == ThemeOptionControl.Switch);
            Require(candidate.Manifest.Options.Single(option => option.Id == "legacy-boolean").Control == ThemeOptionControl.Automatic);
            Require(candidate.Options["choice"] == new ThemeOptionValue(ThemeOptionKind.Select, "large"));
            Require(candidate.Options["numeric"] == new ThemeOptionValue(ThemeOptionKind.Number, "4"));
            Require((await service.CommitAsync(new(candidate.CandidateId, 0))).IsSuccess);
            var values = candidate.Options.ToDictionary(pair => pair.Key, pair => pair.Value);
            values["choice"] = new(ThemeOptionKind.Select, "small"); values["numeric"] = new(ThemeOptionKind.Number, "8");
            values["last-range"] = new(ThemeOptionKind.Range, "9"); values["last-number"] = new(ThemeOptionKind.Number, "9");
            Require((await service.SaveOptionsAsync(new(identity, values, 1))).IsSuccess);
            var offStep = values.ToDictionary(pair => pair.Key, pair => pair.Value);
            offStep["last-range"] = new(ThemeOptionKind.Range, "10");
            Require(!(await service.SaveOptionsAsync(new(identity, offStep, 2))).IsSuccess, "The presentation hint must not relax service step admission.");
            offStep["last-range"] = values["last-range"]; offStep["last-number"] = new(ThemeOptionKind.Number, "10");
            Require(!(await service.SaveOptionsAsync(new(identity, offStep, 2))).IsSuccess, "A numeric slider must preserve its declared step type.");
        }
        using var restored = workspace.Service();
        var state = (await restored.CurrentAsync(new())).Value.Options.Single(option => option.SourceIdentity == identity);
        Require(state.Values["choice"] == new ThemeOptionValue(ThemeOptionKind.Select, "small"));
        Require(state.Values["numeric"] == new ThemeOptionValue(ThemeOptionKind.Number, "8"));
        Require(state.Values["last-range"] == new ThemeOptionValue(ThemeOptionKind.Range, "9"));
        Require(state.Values["last-number"] == new ThemeOptionValue(ThemeOptionKind.Number, "9"));
    }

    private static async Task OptionControlsRejectIncompatibleAndUnboundedEditors()
    {
        using var workspace = new Workspace(); using var service = workspace.Service();
        string[] invalid =
        [
            "\"kind\":\"Text\",\"control\":\"Switch\",\"default\":\"text\"",
            "\"kind\":\"Boolean\",\"control\":\"Slider\",\"default\":false",
            "\"kind\":\"Boolean\",\"control\":\"Input\",\"default\":false",
            "\"kind\":\"Range\",\"control\":\"Segmented\",\"default\":4",
            "\"kind\":\"Text\",\"control\":\"Executable\",\"default\":\"text\"",
            "\"kind\":\"Boolean\",\"control\":\"1\",\"default\":false",
            "\"kind\":\"Number\",\"control\":\"Slider\",\"default\":4",
            "\"kind\":\"Number\",\"control\":\"Slider\",\"default\":4,\"min\":4,\"max\":4",
            "\"kind\":\"Number\",\"control\":\"Slider\",\"default\":0,\"min\":0,\"max\":10,\"step\":20",
            "\"kind\":\"Number\",\"control\":\"Slider\",\"default\":0,\"min\":0,\"max\":10,\"step\":0.000000001",
            "\"kind\":\"Number\",\"control\":\"Slider\",\"default\":0,\"min\":0,\"max\":1000000000001",
            "\"kind\":\"Number\",\"control\":\"Slider\",\"default\":3,\"min\":0,\"max\":10,\"step\":2",
            "\"kind\":\"Select\",\"control\":\"Slider\",\"default\":\"only\",\"choices\":[{\"value\":\"only\",\"label\":{\"en\":\"Only\"}}]",
        ];
        for (int index = 0; index < invalid.Length; index++)
        {
            string manifest = ControlManifest("[{\"id\":\"test\",\"label\":{\"en\":\"Test\"}," + invalid[index] + "}]");
            Require(!(await service.PrepareAsync(new(workspace.Theme("invalid-" + index, manifest), ThemeSourceKind.Directory))).IsSuccess,
                "Incompatible or unbounded control was admitted: " + invalid[index]);
        }
    }

    private static string ControlManifest(string options)
    {
        var manifest = JsonNode.Parse(Manifest)!.AsObject(); manifest["options"] = JsonNode.Parse(options);
        return manifest.ToJsonString();
    }
}
