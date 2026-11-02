using System.Net;
using System.Security.Authentication;

namespace Nexa.Services.Accounts;

/// <summary>Trusted local guidance, never arbitrary provider response text.</summary>
internal sealed class MicrosoftAuthenticationFailure : HttpRequestException
{
    internal MicrosoftAuthenticationFailure(string operation, HttpStatusCode status)
        : base($"{operation}（HTTP {(int)status}）。" + ((int)status switch
        {
            401 or 403 => "认证服务拒绝了当前应用或会话，请重新授权；若持续发生，请联系维护者检查应用授权。",
            429 => "认证服务限制了请求，请稍后重试。",
            >= 500 => "认证服务暂时不可用，请稍后重试。",
            _ => "请检查授权状态后重试。",
        }), null, status)
    { }

    internal MicrosoftAuthenticationFailure(HttpRequestException failure)
        : base(IsCertificateFailure(failure)
            ? "连接 Minecraft 服务时 TLS 证书校验失败，请检查系统时间、系统证书和代理设置。"
            : "Microsoft 授权已完成，但连接 Minecraft 服务失败，自动重试未能恢复。请检查网络或代理后重试。", failure)
    { }

    internal static bool IsCertificateFailure(Exception failure)
    {
        for (Exception? current = failure; current is not null; current = current.InnerException)
            if (current is AuthenticationException) return true;
        return false;
    }
}
