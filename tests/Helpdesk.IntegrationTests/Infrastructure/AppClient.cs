using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Helpdesk.IntegrationTests.Infrastructure;

/// <summary>A browser-like client: keeps cookies, does not follow redirects, handles anti-forgery tokens.</summary>
public sealed partial class AppClient : IDisposable
{
    private readonly HttpClient _http;

    public AppClient(WebApplicationFactory<Program> factory)
    {
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,

            // Over HTTPS, as in production: secure cookies are not sent over plain http.
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });
    }

    /// <summary>Sends this header with every request, for example X-Forwarded-For as a proxy would.</summary>
    public void AddHeader(string name, string value) => _http.DefaultRequestHeaders.Add(name, value);

    public Task<HttpResponseMessage> GetAsync(string url) => _http.GetAsync(url);

    /// <summary>A form post as a forged request from another site would send it: cookies, but no anti-forgery token.</summary>
    public Task<HttpResponseMessage> PostWithoutTokenAsync(string url, IDictionary<string, string>? fields = null)
        => _http.PostAsync(url, new FormUrlEncodedContent(fields ?? new Dictionary<string, string>()));

    public async Task<string> GetHtmlAsync(string url)
    {
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Loads <paramref name="formPageUrl"/> to get a fresh token, then posts the fields to <paramref name="postUrl"/>.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string formPageUrl, string postUrl, IDictionary<string, string> fields)
    {
        var html = await GetHtmlAsync(formPageUrl);
        var form = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = TokenPattern().Match(html).Groups[1].Value,
        };

        return await _http.PostAsync(postUrl, new FormUrlEncodedContent(form));
    }

    public async Task<HttpResponseMessage> FollowAsync(HttpResponseMessage redirect)
    {
        Assert.True(
            redirect.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther,
            $"Expected a redirect but got {(int)redirect.StatusCode}.");
        return await _http.GetAsync(redirect.Headers.Location);
    }

    /// <param name="captcha">The answer of the bot check, for applications that have it switched on.</param>
    public Task<HttpResponseMessage> SignInAsync(string email, string password, string? captcha = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        };

        if (captcha is not null)
        {
            fields[Helpdesk.Web.Security.CaptchaGuard.FieldName] = captcha;
        }

        return PostFormAsync("/Account/Password", "/Account/Password", fields);
    }

    /// <summary>First step of the e-mail sign-in: asks for a code. A success redirects to the page for the code.</summary>
    public Task<HttpResponseMessage> RequestCodeAsync(string email, string? returnUrl = null)
    {
        var url = returnUrl is null ? "/Account/Login" : $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        return PostFormAsync("/Account/Login", url, new Dictionary<string, string> { ["Input.Email"] = email });
    }

    /// <summary>Second step: enters a code on the code page the first step redirected to.</summary>
    public Task<HttpResponseMessage> EnterCodeAsync(Uri codePage, string code)
        => PostFormAsync(codePage.OriginalString, codePage.OriginalString, new Dictionary<string, string> { ["Code"] = code });

    /// <summary>Creates a ticket as the signed-in customer and returns its id.</summary>
    public async Task<int> CreateTicketAsync(string subject, string description = "Something is broken.", string priority = "Normal")
    {
        var response = await PostFormAsync("/Tickets/Create", "/Tickets/Create", new Dictionary<string, string>
        {
            ["Input.Subject"] = subject,
            ["Input.Description"] = description,
            ["Input.Priority"] = priority,
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return int.Parse(response.Headers.Location!.OriginalString.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Posts one of the ticket page forms using the version currently shown on the page.</summary>
    public async Task<HttpResponseMessage> PostTicketActionAsync(int ticketId, string handler, IDictionary<string, string> fields, string? rowVersion = null)
    {
        var url = $"/Tickets/Details/{ticketId}";
        var version = rowVersion ?? ExtractRowVersion(await GetHtmlAsync(url));
        var form = new Dictionary<string, string>(fields) { ["rowVersion"] = version };
        return await PostFormAsync(url, $"{url}?handler={handler}", form);
    }

    /// <summary>
    /// Uploads a file through the attachment form of the ticket page. <paramref name="postTo"/> sends the form to
    /// another ticket than the one it was taken from, as someone would who cannot open that ticket.
    /// </summary>
    public async Task<HttpResponseMessage> UploadAttachmentAsync(
        int ticketId, string fileName, byte[] content, string? rowVersion = null, int? postTo = null)
    {
        var url = $"/Tickets/Details/{ticketId}";
        var html = await GetHtmlAsync(url);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(TokenPattern().Match(html).Groups[1].Value), "__RequestVerificationToken" },
            { new StringContent(rowVersion ?? ExtractRowVersion(html)), "rowVersion" },
        };

        var file = new ByteArrayContent(content);
        form.Add(file, "file", fileName);

        return await _http.PostAsync($"/Tickets/Details/{postTo ?? ticketId}?handler=Attachment", form);
    }

    public static string ExtractRowVersion(string html)
        => RowVersionPattern().Match(html).Groups[1].Value;

    public void Dispose() => _http.Dispose();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("name=\"rowVersion\" value=\"([^\"]+)\"")]
    private static partial Regex RowVersionPattern();
}
