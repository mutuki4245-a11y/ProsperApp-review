using System.Security.Cryptography;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace ProsperApp.Infrastructure.Security;

public static class CspNonce
{
    public const string HttpContextItemKey = "ProsperApp.CspNonce";

    public static string Create() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    public static string? Get(HttpContext context) => context.Items[HttpContextItemKey] as string;
}

[HtmlTargetElement("script")]
public sealed class ScriptNonceTagHelper(IHttpContextAccessor httpContextAccessor) : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var nonce = httpContext is null ? null : CspNonce.Get(httpContext);
        if (!string.IsNullOrWhiteSpace(nonce) && !output.Attributes.ContainsName("nonce"))
        {
            output.Attributes.SetAttribute("nonce", nonce);
        }
    }
}
