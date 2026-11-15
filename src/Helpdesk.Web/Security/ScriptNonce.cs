using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Helpdesk.Web.Security;

/// <summary>A random value for one response that marks its script tags as the site's own.</summary>
public static class ScriptNonce
{
    private static readonly object ItemKey = new();

    public static string Issue(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Base64url: nothing in it is changed by HTML encoding, so the attribute matches the header exactly.
        var nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        context.Items[ItemKey] = nonce;
        return nonce;
    }

    /// <summary>The nonce of this response, or null when its policy does not use one.</summary>
    public static string? Find(HttpContext context)
        => context?.Items.TryGetValue(ItemKey, out var nonce) == true ? nonce as string : null;
}

/// <summary>Adds the response's nonce to every script tag, so a nonce-based policy lets the page's scripts run.</summary>
[HtmlTargetElement("script")]
public sealed class ScriptNonceTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (ScriptNonce.Find(ViewContext.HttpContext) is { } nonce)
        {
            output.Attributes.SetAttribute("nonce", nonce);
        }
    }
}
