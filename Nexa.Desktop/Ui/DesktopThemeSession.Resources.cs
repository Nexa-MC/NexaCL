using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Nexa.Core.Media;
using Nexa.Services.Settings;
using Nexa.Services.Themes;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class DesktopThemeSession
{
    private static readonly ReadOnlyDictionary<string, string> ThemeAppearancePolicyKeys =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["background-fit"] = "appearance.background-fit",
            ["background-opacity"] = "appearance.background-opacity",
            ["background-color"] = "appearance.background-color",
            ["window-opacity"] = "appearance.window-opacity",
            ["window-blur"] = "appearance.window-blur",
            ["video.auto-pause"] = "appearance.video-auto-pause",
            ["video.fit"] = "appearance.background-fit",
            ["animation-fps"] = "appearance.animation-fps",
            ["music.enabled"] = "music.enabled",
            ["music.startup"] = "music.startup",
            ["music.autoplay"] = "music.autoplay",
            ["music.shuffle"] = "music.shuffle",
            ["music.volume"] = "music.volume",
            ["music.media-controls"] = "music.media-controls",
            ["music.auto-pause"] = "music.auto-pause",
        });

    private async Task<PreparedTheme> BuildPreparedAsync(ThemeCandidate candidate, long activation, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(candidate); token.ThrowIfCancellationRequested();
        SettingsEffectiveSnapshot basis = _baseSettings ?? throw new InvalidOperationException("Theme preparation requires committed settings.");
        if (_safeMode)
            basis = basis with
            {
                Values = basis.Values.Select(value => value.Key is "appearance.logo-path" or "appearance.background-path" or "appearance.video-path" or "appearance.launcher-icon-path" or "music.path" or "appearance.custom-theme"
                ? value with { Value = new(SettingsOverrideMode.Custom, "") }
                : value.Key == "appearance.launcher-icon-mode" ? value with { Value = new(SettingsOverrideMode.Custom, "default") } : value).ToArray()
            };
        bool builtIn = candidate.SourceKind == ThemeSourceKind.BuiltIn;
        Dictionary<string, SettingsEffectiveValue> effective = basis.Values.ToDictionary(item => item.Key, StringComparer.Ordinal);
        HashSet<string> protectedPolicy = new(HostProtectedAppearancePolicies, StringComparer.Ordinal);
        if (!builtIn && candidate.Manifest.Appearance.Properties?.ContainsKey("music.volume") != true
            && !candidate.Manifest.Options.Any(option => option.Id == "music.volume")) protectedPolicy.Add("music.volume");
        void Set(string key, string value)
        {
            if (!SettingsPolicySchema.ByKey.TryGetValue(key, out var definition) || protectedPolicy.Contains(key)) return;
            var setting = new SettingsOverride(SettingsOverrideMode.Custom, value);
            string? error = definition.Validate(setting);
            if (error is not null) throw new InvalidDataException("Theme appearance value is invalid for " + key + ": " + error);
            effective[key] = new(key, setting, SettingsLayer.Temporary, definition.Timing, null);
        }
        if (!builtIn)
            foreach (var definition in SettingsPolicySchema.Definitions)
                if (definition.Key.StartsWith("appearance.", StringComparison.Ordinal) || definition.Key.StartsWith("music.", StringComparison.Ordinal))
                    Set(definition.Key, definition.DefaultValue);
        foreach (var option in candidate.Options)
            if (builtIn && option.Value.Kind is not (ThemeOptionKind.File or ThemeOptionKind.Files or ThemeOptionKind.Directory)) Set(option.Key, option.Value.Value);
        foreach (string pathKey in new[] { "appearance.logo-path", "appearance.background-path", "appearance.video-path", "music.path" }) Set(pathKey, "");
        foreach (var property in candidate.Manifest.Appearance.Properties ?? new Dictionary<string, string>())
            if (ThemeAppearancePolicyKeys.TryGetValue(property.Key, out string? key)) Set(key, AppearanceLiteral(property.Value, candidate));
        string Read(string key) => effective.GetValueOrDefault(key)?.Value.Value ?? "";
        bool Enabled(string name, bool fallback)
        {
            var properties = candidate.Manifest.Appearance.Properties;
            return properties is not null && properties.TryGetValue(name, out string? expression)
                ? bool.TryParse(AppearanceLiteral(expression, candidate), out bool value) && value : fallback;
        }

        List<Guid> leases = [];
        Dictionary<(string Expression, ThemeResourceKind Kind), IReadOnlyList<ThemeResourcePayload>> prepared = [];
        Dictionary<string, AvaloniaUiThemeResource> native = new(StringComparer.Ordinal);
        Dictionary<string, string> resourceBindings = new(StringComparer.Ordinal);
        HashSet<string> optionalFallbackResources = new(StringComparer.Ordinal);
        Dictionary<string, ThemeResourcePayload> slots = new(StringComparer.Ordinal);
        List<string> playlist = [];
        List<string> sources = candidate.Styles.Select(style => style.Text).ToList();
        Dictionary<string, string> canonicalOptions = candidate.Options.ToDictionary(pair => pair.Key,
            pair => CanonicalThemeOption(pair.Value), StringComparer.Ordinal);
        var inputs = new NssThemeCompileInputs { Options = canonicalOptions, Platform = PlatformName, Mode = IsDark ? "Dark" : "Light", Capabilities = _capabilities };
        NssThemeStyleProgram initial = NssThemeCompiler.Compile(sources.Count == 0 ? [""] : sources, inputs, activation);
        try
        {
            async Task<IReadOnlyList<ThemeResourcePayload>> Prepare(string expression, ThemeResourceKind kind)
            {
                token.ThrowIfCancellationRequested(); expression = CanonicalResourceExpression(expression, candidate);
                if (prepared.TryGetValue((expression, kind), out var previous)) return previous;
                IReadOnlyList<ThemeResourcePayload> payloads;
                if (expression.StartsWith("folder(", StringComparison.Ordinal))
                {
                    var result = await QueryAsync<ThemeResourceListQuery, ThemeResourceListPayload>(ThemesContract.ListResources,
                        new(candidate.CandidateId, expression, kind), token).ConfigureAwait(false);
                    if (!result.IsSuccess) throw new IOException(result.Error!.Message);
                    payloads = result.Value!.Entries;
                }
                else
                {
                    var result = await QueryAsync<ThemeResourceQuery, ThemeResourcePayload>(ThemesContract.ReadResource,
                        new(candidate.CandidateId, expression, kind), token).ConfigureAwait(false);
                    if (!result.IsSuccess) throw new IOException(result.Error!.Message);
                    payloads = [result.Value!];
                }
                foreach (var payload in payloads)
                {
                    leases.Add(payload.LeaseId);
                    if (payload.SourceIdentity != candidate.SourceIdentity || payload.Generation != candidate.Generation || payload.Kind != kind)
                        throw new InvalidDataException("Prepared resource belongs to a retired or different theme scope.");
                }
                foreach (var payload in payloads)
                    if (kind is ThemeResourceKind.Audio or ThemeResourceKind.Video)
                        await AvaloniaUiLocalMediaPlayer.ValidateThemeMediaAsync(payload.PreparedPath
                            ?? throw new InvalidDataException("Media has no immutable prepared resource path."), kind == ThemeResourceKind.Video, token).ConfigureAwait(false);
                prepared.Add((expression, kind), payloads); return payloads;
            }
            void Native(string id, ThemeResourcePayload payload)
            {
                if (payload.Kind is not (ThemeResourceKind.Image or ThemeResourceKind.Icon or ThemeResourceKind.Font)) return;
                if (payload.ContentType is not ("image/png" or "image/jpeg" or "image/webp" or "image/gif" or "image/bmp" or "image/x-icon" or "image/svg+xml" or "font/ttf" or "font/otf"))
                    throw new PlatformNotSupportedException("This theme resource format has no prepared native decoder: " + payload.ContentType);
                native.TryAdd(id, new(id, payload.ContentType, payload.Bytes.ToArray()));
            }
            async Task ValidateOptionalVisual(string expression, ThemeResourcePayload payload)
            {
                if (payload.Kind is not (ThemeResourceKind.Image or ThemeResourceKind.Icon or ThemeResourceKind.Font)) return;
                // Validate the exact optional decoder before retaining it in the candidate's native map.
                using var decoded = await _platform.PrepareThemeAppearanceAsync(new(activation,
                    Resources: [new(expression, payload.ContentType, payload.Bytes)], UseNativeTextLayout: false), token).ConfigureAwait(false);
            }
            async Task AddAudio(string expression)
            {
                foreach (var payload in await Prepare(expression, ThemeResourceKind.Audio).ConfigureAwait(false))
                {
                    if (playlist.Count >= 128 || string.IsNullOrEmpty(payload.PreparedPath)) throw new InvalidDataException("Prepared playlist exceeds its limit or has no stable lease.");
                    playlist.Add(payload.PreparedPath);
                }
            }
            async Task PrepareSlot(string slot, ThemeAppearanceResource resource, string expression, bool validateVisual)
            {
                expression = CanonicalResourceExpression(expression, candidate);
                var payloads = await Prepare(expression, resource.Kind).ConfigureAwait(false);
                if (resource.Kind == ThemeResourceKind.Audio)
                {
                    foreach (var payload in payloads)
                    {
                        if (playlist.Count >= 128 || string.IsNullOrEmpty(payload.PreparedPath)) throw new InvalidDataException("Theme playlist exceeds its stable resource budget.");
                        playlist.Add(payload.PreparedPath);
                    }
                }
                else
                {
                    if (payloads.Count != 1) throw new InvalidDataException("Appearance slot requires one prepared resource.");
                    if (validateVisual) await ValidateOptionalVisual(expression, payloads[0]).ConfigureAwait(false);
                    Native(expression, payloads[0]); slots[slot] = payloads[0];
                }
            }
            foreach (var slot in candidate.Manifest.Appearance.Resources)
            {
                if (!slot.Value.Required && (slot.Key == "video" && !Enabled("video.enabled", true) || slot.Key == "music" && !Enabled("music.enabled", true))) continue;
                int previousLeaseCount = leases.Count, previousPlaylistCount = playlist.Count;
                try
                {
                    await PrepareSlot(slot.Key, slot.Value, slot.Value.Expression, !slot.Value.Required).ConfigureAwait(false);
                }
                catch (Exception error) when (!slot.Value.Required && slot.Value.Fallback is not null && error is IOException or InvalidDataException or PlatformNotSupportedException)
                {
                    Guid[] ignoredLeases = leases.Skip(previousLeaseCount).ToArray();
                    foreach (Guid ignored in ignoredLeases)
                        await CommandAsync(ThemesContract.ReleaseResource, new ThemeResourceReleaseCommand(ignored), CancellationToken.None).ConfigureAwait(false);
                    leases.RemoveRange(previousLeaseCount, leases.Count - previousLeaseCount);
                    if (playlist.Count > previousPlaylistCount) playlist.RemoveRange(previousPlaylistCount, playlist.Count - previousPlaylistCount);
                    foreach (var key in prepared.Where(pair => pair.Value.Any(payload => ignoredLeases.Contains(payload.LeaseId))).Select(pair => pair.Key).ToArray()) prepared.Remove(key);
                    _log("Theme optional resource fallback (" + slot.Key + "/" + slot.Value.Fallback + "): " + error.Message);
                    string? primary = null;
                    try { primary = CanonicalResourceExpression(slot.Value.Expression, candidate); optionalFallbackResources.Add(primary); }
                    catch (InvalidDataException) { _log("Theme fallback applied to an unset optional resource slot: " + slot.Key); }
                    if (slot.Value.Fallback != "none")
                    {
                        string fallback = CanonicalResourceExpression(slot.Value.Fallback, candidate);
                        await PrepareSlot(slot.Key, slot.Value, fallback, validateVisual: true).ConfigureAwait(false);
                        if (primary is not null) resourceBindings[primary] = fallback;
                    }
                }
            }
            foreach (var resource in initial.Resources)
            {
                // A declared optional visual uses its admitted fallback for every selector and condition.
                if (resourceBindings.TryGetValue(resource.Expression, out string? fallback) && native.ContainsKey(fallback)) continue;
                ThemeResourceKind kind = Path.GetExtension(resource.Value).ToLowerInvariant() is ".ttf" or ".otf" or ".woff" or ".woff2" ? ThemeResourceKind.Font : ThemeResourceKind.Image;
                var payloads = await Prepare(resource.Expression, kind).ConfigureAwait(false);
                if (payloads.Count != 1) throw new InvalidDataException("A style resource requires exactly one prepared visual resource.");
                Native(resource.Expression, payloads[0]);
            }
            inputs = inputs with { ResourceBindings = resourceBindings };
            // File/Files/Directory options are read only through source-scoped grants, including the built-in pack.
            foreach (var definition in candidate.Manifest.Options)
            {
                if (definition.ResourceKind is not { } kind || !candidate.Options.TryGetValue(definition.Id, out var option)) continue;
                try
                {
                    if (option.Kind == ThemeOptionKind.Files)
                    {
                        foreach (string file in option.Values ?? [])
                            if (!string.IsNullOrWhiteSpace(file))
                            {
                                bool legacyDirectory = builtIn && Snapshot.Current.Grants.Any(grant => grant.SourceIdentity == candidate.SourceIdentity
                                    && grant.Kind == kind && grant.Scope == ThemeGrantScope.Directory
                                    && string.Equals(grant.CanonicalPath, file, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                                string expression = legacyDirectory ? FolderExpression(file) : FileExpression(file);
                                if (optionalFallbackResources.Contains(CanonicalResourceExpression(expression, candidate))) continue;
                                if (kind == ThemeResourceKind.Audio) await AddAudio(expression).ConfigureAwait(false);
                                else foreach (var payload in await Prepare(expression, kind).ConfigureAwait(false))
                                {
                                    if (builtIn) await ValidateOptionalVisual(expression, payload).ConfigureAwait(false);
                                    Native(CanonicalResourceExpression(expression, candidate), payload);
                                }
                            }
                    }
                    else if (!string.IsNullOrWhiteSpace(option.Value))
                    {
                        string expression = option.Kind == ThemeOptionKind.Directory ? FolderExpression(option.Value) : FileExpression(option.Value);
                        if (optionalFallbackResources.Contains(CanonicalResourceExpression(expression, candidate))) continue;
                        if (kind == ThemeResourceKind.Audio) await AddAudio(expression).ConfigureAwait(false);
                        else
                        {
                            var payloads = await Prepare(expression, kind).ConfigureAwait(false);
                            if (payloads.Count != 1) throw new InvalidDataException("A resource option requires a single resource.");
                            var payload = payloads[0];
                            if (builtIn) await ValidateOptionalVisual(expression, payload).ConfigureAwait(false);
                            Native(CanonicalResourceExpression(expression, candidate), payload);
                            if (definition.Id is "appearance.background-path" or "background-path") slots["background"] = payload;
                            else if (definition.Id is "appearance.logo-path" or "logo-path") slots["logo"] = payload;
                            else if (definition.Id is "appearance.video-path" or "video-path") slots["video"] = payload;
                        }
                    }
                }
                catch (Exception error) when (builtIn && error is IOException or InvalidDataException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    _log("Legacy built-in resource fallback (" + definition.Id + "): " + error.Message);
                }
            }
            if (builtIn && !candidate.Options.ContainsKey("appearance.logo-path"))
            {
                string legacyLogo = basis.Values.FirstOrDefault(value => value.Key == "appearance.logo-path")?.Value.Value ?? "";
                if (!string.IsNullOrWhiteSpace(legacyLogo))
                    try
                    {
                        string expression = FileExpression(legacyLogo);
                        var values = await Prepare(expression, ThemeResourceKind.Image).ConfigureAwait(false);
                        if (values.Count != 1) throw new InvalidDataException("Legacy logo requires a single resource.");
                        await ValidateOptionalVisual(expression, values[0]).ConfigureAwait(false);
                        Native(expression, values[0]); slots["logo"] = values[0];
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or PlatformNotSupportedException)
                    { _log("Legacy built-in logo fallback: " + error.Message); }
            }
            if (slots.TryGetValue("video", out var video)) Set("appearance.video-path", video.PreparedPath ?? throw new InvalidDataException("Video has no stable prepared resource."));
            if (!Enabled("video.enabled", true)) Set("appearance.video-path", "");
            if (playlist.Count != 0) Set("music.path", playlist[0]);
            else Set("music.path", "");
            // Non-PNG visuals use the same admitted native asset map; no consumer reopens a theme path.
            string? logoId = slots.TryGetValue("logo", out var logo) ? FindNativeId(logo, native) : null;
            string? backgroundId = slots.TryGetValue("background", out var background) ? FindNativeId(background, native) : null;
            PngImage? logoImage = PreviewPng(logo);
            PngImage? backgroundImage = PreviewPng(background);
            if (logoId is not null) sources.Add("[key=\"CustomLauncherLogo\"] { ImageSource: " + logoId + "; }");
            if (backgroundId is not null && backgroundImage is null)
                sources.Add("ContentHost.layout-Shell-14 { BackgroundImage: " + backgroundId + "; BackgroundImageFit: " + Read("appearance.background-fit")
                    + "; BackgroundImageOpacity: " + (double.Parse(Read("appearance.background-opacity"), CultureInfo.InvariantCulture) / 100).ToString(CultureInfo.InvariantCulture) + "; }");

            AvaloniaUiThemeResource? Brand(string slot) => slots.TryGetValue(slot, out var payload) && FindNativeId(payload, native) is { } id ? native[id] : null;
            AvaloniaUiThemeResource? light = Brand("icon-light") ?? Brand("launcherIcon");
            AvaloniaUiThemeResource? dark = Brand("icon-dark") ?? Brand("launcherIcon");
            light ??= dark; dark ??= light;
            string iconMode = basis.Values.FirstOrDefault(value => value.Key == "appearance.launcher-icon-mode")?.Value.Value ?? "theme";
            if (iconMode == "default") light = dark = null;
            else if (iconMode == "custom")
            {
                string path = basis.Values.FirstOrDefault(value => value.Key == "appearance.launcher-icon-path")?.Value.Value ?? "";
                try
                {
                    PngImage? custom = await CustomAppearanceSession.LoadAsync(path, token).ConfigureAwait(false);
                    if (custom is not null) light = dark = new("host-selected-launcher-icon", "image/png", custom.Bytes.ToArray());
                    else light = dark = null;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                { _log("Host-selected launcher icon fallback: " + error.Message); light = dark = null; }
            }
            AvaloniaUiThemeStartupColors? colors = StartupColors(candidate, effective);
            if (!builtIn && colors is not null)
                Set("appearance.custom-theme", "{\"background\":\"" + HexColor(colors.Background) + "\",\"foreground\":\"" + HexColor(colors.Foreground) + "\",\"accent\":\"" + HexColor(colors.Accent) + "\"}");
            AvaloniaUiThemeWindowMaterial material = ResolveMaterial(candidate);
            var nativeAppearance = new AvaloniaUiThemeAppearance(activation, light, dark,
                Array.AsReadOnly(native.Values.ToArray()), colors, material, UseNativeTextLayout: !builtIn)
            {
                WindowIconLight = iconMode == "theme" ? Brand("windowIcon") : null,
                WindowIconDark = iconMode == "theme" ? Brand("windowIcon") : null,
                TrayIconLight = iconMode == "theme" ? Brand("trayIcon") : null,
                TrayIconDark = iconMode == "theme" ? Brand("trayIcon") : null,
                SplashIconLight = iconMode == "theme" ? Brand("splashIcon") : null,
                SplashIconDark = iconMode == "theme" ? Brand("splashIcon") : null,
                ResizePolicy = ResolveResizePolicy(candidate),
                SplashMotion = ResolveSplashMotion(candidate),
            };
            token.ThrowIfCancellationRequested();
            var program = NssThemeCompiler.Compile(sources.Count == 0 ? [""] : sources, inputs, activation);
            var settings = new SettingsEffectiveSnapshot(basis.Revision, Array.AsReadOnly(effective.Values.ToArray())) { Overlay = basis.Overlay };
            var appearance = new DesktopEffectiveAppearanceSnapshot(activation, settings,
                Array.AsReadOnly(playlist.Distinct(StringComparer.Ordinal).ToArray()), ThemeResources: true,
                Logo: logoImage, Background: backgroundImage, HasPreparedImages: true, LogoResourceId: logoId, BackgroundResourceId: backgroundId);
            return new(candidate, program, nativeAppearance, appearance, Array.AsReadOnly(leases.Distinct().ToArray()));
        }
        catch
        {
            foreach (Guid lease in leases.Distinct())
                await CommandAsync(ThemesContract.ReleaseResource, new ThemeResourceReleaseCommand(lease), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ReleasePreparedAsync(PreparedTheme? prepared, CancellationToken token)
    {
        if (prepared is null) return;
        foreach (Guid lease in prepared.Leases)
        {
            // Retirement is compensation: a cancelled preview/activation must still release every lease.
            var result = await CommandAsync(ThemesContract.ReleaseResource, new ThemeResourceReleaseCommand(lease), CancellationToken.None).ConfigureAwait(false);
            if (!result.IsSuccess) _log("Theme resource retirement failed: " + result.Error!.Message);
        }
    }

    private static string AppearanceLiteral(string value, ThemeCandidate candidate)
    {
        if (value.StartsWith("option(", StringComparison.Ordinal) && value.EndsWith(')'))
        {
            string id = value[7..^1].Trim().Trim('"', '\'');
            if (!candidate.Options.TryGetValue(id, out var option) || option.Kind is ThemeOptionKind.File or ThemeOptionKind.Files or ThemeOptionKind.Directory)
                throw new InvalidDataException("Appearance property requires a known scalar option.");
            return option.Value;
        }
        return value;
    }
    private static string CanonicalThemeOption(ThemeOptionValue value) => value.Kind switch
    {
        ThemeOptionKind.Boolean or ThemeOptionKind.Number or ThemeOptionKind.Range or ThemeOptionKind.Color => value.Value,
        ThemeOptionKind.File when value.Value.Length != 0 => FileExpression(value.Value),
        ThemeOptionKind.Directory when value.Value.Length != 0 => FolderExpression(value.Value),
        ThemeOptionKind.Files => FileListLiteral(value.Values ?? []),
        _ => "\"" + JsonEncodedText.Encode(value.Value).ToString() + "\"",
    };
    private static string FileListLiteral(IReadOnlyList<string> files)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray(); foreach (string file in files) writer.WriteStringValue(file); writer.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
    private static string HexColor(XsrUiColor color) => "#" + color.Red.ToString("X2", CultureInfo.InvariantCulture)
        + color.Green.ToString("X2", CultureInfo.InvariantCulture) + color.Blue.ToString("X2", CultureInfo.InvariantCulture);
    private static string FileExpression(string value) => ResourceValue(value, "file");
    private static string FolderExpression(string value) => ResourceValue(value, "folder");
    private static string ResourceValue(string value, string function) => value.StartsWith("asset(", StringComparison.Ordinal) || value.StartsWith("file(", StringComparison.Ordinal) || value.StartsWith("folder(", StringComparison.Ordinal)
        ? value : function + "(\"" + value + "\")";
    private static string CanonicalResourceExpression(string expression, ThemeCandidate candidate)
    {
        if (expression.StartsWith("option(", StringComparison.Ordinal) && expression.EndsWith(')'))
        {
            string id = expression[7..^1].Trim().Trim('"', '\'');
            if (!candidate.Options.TryGetValue(id, out var option) || option.Kind is not (ThemeOptionKind.File or ThemeOptionKind.Directory) || string.IsNullOrWhiteSpace(option.Value))
                throw new InvalidDataException("Resource expression requires a selected single file or directory option.");
            expression = option.Kind == ThemeOptionKind.Directory ? FolderExpression(option.Value) : FileExpression(option.Value);
        }
        int open = expression.IndexOf('(');
        if (open < 1 || !expression.EndsWith(')') || expression[..open] is not ("asset" or "file" or "folder")) throw new InvalidDataException("Unsupported resource expression.");
        string argument = expression[(open + 1)..^1].Trim();
        if (argument.Length >= 2 && argument[0] is '\'' or '"' && argument[^1] == argument[0]) argument = argument[1..^1];
        if (argument.Length is 0 or > 4096 || argument.Any(char.IsControl) || argument.Contains('"')) throw new InvalidDataException("Invalid resource identity.");
        return expression[..open] + "(\"" + argument + "\")";
    }
    private static string? FindNativeId(ThemeResourcePayload? payload, IReadOnlyDictionary<string, AvaloniaUiThemeResource> native) => payload is null ? null
        : native.FirstOrDefault(pair => pair.Value.MediaType == payload.ContentType && pair.Value.Content.Span.SequenceEqual(payload.Bytes)).Key;
    private static PngImage? PreviewPng(ThemeResourcePayload? payload) => payload?.ContentType == "image/png"
        ? PngImage.TryCreatePreview(payload.Bytes) ?? throw new InvalidDataException("Theme PNG exceeds its bounded preview dimensions.") : null;
    private static AvaloniaUiThemeResizePolicy ResolveResizePolicy(ThemeCandidate candidate)
    {
        string value = "auto";
        if (candidate.Manifest.Appearance.Properties is { } properties && properties.TryGetValue("resize-policy", out string? expression))
            value = AppearanceLiteral(expression, candidate);
        return value switch
        {
            "auto" => AvaloniaUiThemeResizePolicy.Auto,
            "immediate" => AvaloniaUiThemeResizePolicy.Immediate,
            "deferred" => AvaloniaUiThemeResizePolicy.Deferred,
            "animated" => AvaloniaUiThemeResizePolicy.Animated,
            "instant" => AvaloniaUiThemeResizePolicy.Instant,
            _ => throw new InvalidDataException("Invalid finite theme resize policy."),
        };
    }
    private static AvaloniaUiThemeSplashMotion ResolveSplashMotion(ThemeCandidate candidate) => candidate.Manifest.Appearance.SplashMotion switch
    {
        null => AvaloniaUiThemeSplashMotion.Default,
        "none" => AvaloniaUiThemeSplashMotion.None,
        "fade" => AvaloniaUiThemeSplashMotion.Fade,
        "breathe" => AvaloniaUiThemeSplashMotion.Breathe,
        "draw" => AvaloniaUiThemeSplashMotion.Draw,
        _ => throw new InvalidDataException("Invalid finite theme startup motion."),
    };

    private AvaloniaUiThemeWindowMaterial ResolveMaterial(ThemeCandidate candidate)
    {
        if (candidate.SourceKind == ThemeSourceKind.BuiltIn || candidate.Manifest.Appearance.WindowMaterial is null) return AvaloniaUiThemeWindowMaterial.Default;
        string material = candidate.Manifest.Appearance.WindowMaterial;
        string? capability = material is "mica" ? "window-mica" : material is "acrylic" or "blur" ? "window-acrylic" : null;
        if (capability is not null && !_capabilities.Contains(capability))
        {
            var optional = candidate.Manifest.OptionalCapabilities.FirstOrDefault(value => value.Name == capability);
            if (optional?.Fallback is null || candidate.Manifest.RequiredCapabilities.Contains(capability)) throw new PlatformNotSupportedException("Theme requires an unavailable window material.");
            if (optional.Fallback is not ("none" or "solid")) throw new PlatformNotSupportedException("Theme declared an unknown fallback for its unavailable window material.");
            _log("Theme material fallback (" + capability + "/" + optional.Fallback + ")."); return AvaloniaUiThemeWindowMaterial.Solid;
        }
        return material switch { "mica" => AvaloniaUiThemeWindowMaterial.Mica, "acrylic" or "blur" => AvaloniaUiThemeWindowMaterial.Acrylic, "transparent" => AvaloniaUiThemeWindowMaterial.Transparent, "none" or "solid" => AvaloniaUiThemeWindowMaterial.Solid, _ => AvaloniaUiThemeWindowMaterial.Default };
    }
    private AvaloniaUiThemeStartupColors? StartupColors(ThemeCandidate candidate, IReadOnlyDictionary<string, SettingsEffectiveValue> effective)
    {
        string? accent = candidate.Manifest.Appearance.AccentColor;
        XsrUiCustomPalette? palette = CustomAppearanceSession.ParsePalette(effective.GetValueOrDefault("appearance.custom-theme")?.Value.Value ?? "");
        if (accent is null && palette is null) return null;
        return palette is not null ? new(palette.Background, palette.Foreground, accent is null ? palette.Accent : NssStyleSheet.Color(accent))
            : new(IsDark ? new(15, 19, 27) : new(243, 245, 249), IsDark ? new(236, 240, 247) : new(20, 26, 36), NssStyleSheet.Color(accent!));
    }
}
