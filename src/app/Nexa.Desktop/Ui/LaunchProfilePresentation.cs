using Nexa.Core.Media;
using Nexa.Services.Accounts;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

/// <summary>Pure, credential-free projection of the original profile card's display contract.</summary>
internal static class LaunchProfilePresentation
{
    public static XsrUiRasterImage Head(PngImage image)
    {
        double scale = image.Width / 64d;
        return new(image, [
            new(new(8 * scale, 8 * scale, 8 * scale, 8 * scale), new(.125, .125, .75, .75)),
            new(new(40 * scale, 8 * scale, 8 * scale, 8 * scale), new(.0625, .0625, .875, .875)),
        ]);
    }
    public static string Avatar(string uuid)
    {
        if (!Guid.TryParse(uuid, out Guid id)) return "pcl/avatar/steve";
        string hex = id.ToString("N");
        int parity = 0;
        foreach (int index in new[] { 7, 15, 23, 31 })
            parity ^= Convert.ToInt32(hex[index].ToString(), 16);
        return (parity & 1) == 0 ? "pcl/avatar/steve" : "pcl/avatar/alex";
    }

    public static string Description(LaunchProfileView profile)
    {
        if (profile.Kind == LaunchProfileKind.ThirdParty)
        {
            string server = profile.AuthServer.Trim();
            if (!server.Contains("://", StringComparison.Ordinal)) server = "https://" + server;
            if (Uri.TryCreate(server, UriKind.Absolute, out Uri? uri) && !string.IsNullOrEmpty(uri.Host))
                return ProductPresentationText.Format("ProfileThirdParty", null, ("host", uri.Host));
        }
        if (!string.IsNullOrWhiteSpace(profile.Info)) return profile.Info;
        return profile.Kind switch
        {
            LaunchProfileKind.Microsoft => ProductPresentationText.Raw("profile.microsoft"),
            LaunchProfileKind.LittleSkin => ProductPresentationText.Raw("profile.littleskin"),
            LaunchProfileKind.ThirdParty => ProductPresentationText.Raw("profile.thirdparty"),
            LaunchProfileKind.NCloud => ProductPresentationText.Raw("profile.ncloud"),
            _ => ProductPresentationText.Raw("profile.offline"),
        };
    }
}
