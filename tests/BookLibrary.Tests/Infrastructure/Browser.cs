using System.Net;
using System.Net.Http.Headers;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BookLibrary.Tests.Infrastructure;

// A tiny "browser": keeps cookies, does not follow redirects (so tests can check them), and fills
// in the anti-forgery token that every form on the site needs.
public class Browser
{
    static readonly HtmlParser Parser = new();
    public HttpClient Client { get; }

    public Browser(AppFactory factory) =>
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task<(HttpStatusCode Status, string Html)> GetAsync(string url)
    {
        using var res = await Client.GetAsync(url);
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    // GETs a page that must load, and returns its HTML.
    public async Task<string> PageAsync(string url)
    {
        var (status, html) = await GetAsync(url);
        Assert.True(status == HttpStatusCode.OK, $"GET {url} returned {(int)status}");
        return html;
    }

    public static IHtmlDocument Parse(string html) => Parser.ParseDocument(html);

    // The page's visible text, as a browser shows it (HTML codes such as &#xE9; decoded).
    public async Task<string> TextAsync(string url) => Parse(await PageAsync(url)).Body?.TextContent ?? "";

    // Loads formPage to get its anti-forgery token, then posts the fields (and files) to action.
    public async Task<HttpResponseMessage> PostFormAsync(string formPage, string action,
        Dictionary<string, string> fields, params (string Field, string FileName, string ContentType, byte[] Bytes)[] files)
    {
        var token = Parse(await PageAsync(formPage))
            .QuerySelector("input[name=__RequestVerificationToken]")?.GetAttribute("value");
        Assert.False(string.IsNullOrEmpty(token), $"No anti-forgery token on {formPage}");

        var all = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token! };
        HttpContent content;
        if (files.Length == 0)
            content = new FormUrlEncodedContent(all);
        else
        {
            var multipart = new MultipartFormDataContent();
            foreach (var (k, v) in all) multipart.Add(new StringContent(v), k);
            foreach (var (field, name, type, bytes) in files)
            {
                var part = new ByteArrayContent(bytes);
                part.Headers.ContentType = new MediaTypeHeaderValue(type);
                multipart.Add(part, field, name);
            }
            content = multipart;
        }
        return await Client.PostAsync(action, content);
    }

    public Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        PostFormAsync("/Identity/Account/Login", "/Identity/Account/Login",
            new() { ["Input.Email"] = email, ["Input.Password"] = password, ["Input.RememberMe"] = "false" });

    public Task<HttpResponseMessage> RegisterAsync(string email, string password) =>
        PostFormAsync("/Identity/Account/Register", "/Identity/Account/Register",
            new() { ["Input.Email"] = email, ["Input.Password"] = password, ["Input.ConfirmPassword"] = password });
}
