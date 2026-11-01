namespace Nexa.Services.Accounts;

public sealed record AccountOnboardingOptions(string MicrosoftClientId, LittleSkinOAuthConfiguration? LittleSkin)
{
    public const string DeviceFlowRedirectUri = "https://open.littleskin.cn/oauth/callback";

    public static AccountOnboardingOptions FromEnvironment()
    {
        static string First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
        string microsoft = First(Environment.GetEnvironmentVariable("NEXA_MS_CLIENT_ID") ?? Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"),
            Environment.GetEnvironmentVariable("MS_CLIENT_ID"), Environment.GetEnvironmentVariable("Nexa_CLIENT_ID"), Environment.GetEnvironmentVariable("CLIENT_ID"));
        string id = First(Environment.GetEnvironmentVariable("NEXA_LITTLESKIN_CLIENT_ID") ?? Environment.GetEnvironmentVariable("PCL_LITTLESKIN_CLIENT_ID"),
            Environment.GetEnvironmentVariable("LITTLESKIN_CLIENT_ID"));
        string secret = First(Environment.GetEnvironmentVariable("NEXA_LITTLESKIN_CLIENT_SECRET") ?? Environment.GetEnvironmentVariable("PCL_LITTLESKIN_CLIENT_SECRET"),
            Environment.GetEnvironmentVariable("LITTLESKIN_CLIENT_SECRET"));
        string redirect = First(Environment.GetEnvironmentVariable("NEXA_LITTLESKIN_REDIRECT_URI") ?? Environment.GetEnvironmentVariable("PCL_LITTLESKIN_REDIRECT_URI"),
            Environment.GetEnvironmentVariable("LITTLESKIN_REDIRECT_URI"), "http://127.0.0.1:17342/oauth/littleskin/callback");
        LittleSkinOAuthConfiguration? littleSkin = !string.IsNullOrWhiteSpace(id) && Uri.TryCreate(redirect, UriKind.Absolute, out var uri)
            ? new(id, secret, uri) : null;
        return new(microsoft, littleSkin);
    }
}
