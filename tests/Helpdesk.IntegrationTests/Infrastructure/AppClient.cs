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
            HandleCookies = true,
        });
    }

    public Task<HttpResponseMessage> GetAsync(string url) => _http.GetAsync(url);

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

    public async Task SignInAsDemoAsync(string role)
    {
        var response = await PostFormAsync("/Account/Login", $"/Account/Login?handler=Demo&role={role}", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public Task<HttpResponseMessage> SignInAsync(string email, string password)
        => PostFormAsync("/Account/Login", "/Account/Login", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        });

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

    /// <summary>Uploads a file through the attachment form of the ticket page.</summary>
    public async Task<HttpResponseMessage> UploadAttachmentAsync(int ticketId, string fileName, byte[] content, string? rowVersion = null)
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

        return await _http.PostAsync($"{url}?handler=Attachment", form);
    }

    public static string ExtractRowVersion(string html)
        => RowVersionPattern().Match(html).Groups[1].Value;

    public void Dispose() => _http.Dispose();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("name=\"rowVersion\" value=\"([^\"]+)\"")]
    private static partial Regex RowVersionPattern();
}
