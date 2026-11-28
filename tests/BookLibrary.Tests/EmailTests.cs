using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookLibrary.Services;
using BookLibrary.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookLibrary.Tests;

// Collects emails instead of sending them.
public class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<(string To, string Subject, string Html)> Sent { get; } = new();

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        Sent.Enqueue((email, subject, htmlMessage));
        return Task.CompletedTask;
    }

    public (string To, string Subject, string Html)? LastTo(string email) =>
        Sent.Where(m => m.To == email).Select(m => ((string, string, string)?)m).LastOrDefault();

    // The first link in an email, as a path the test browser can open.
    public static string LinkIn(string html)
    {
        var href = Regex.Match(html, "href=['\"]([^'\"]+)['\"]").Groups[1].Value;
        return new Uri(WebUtility.HtmlDecode(href)).PathAndQuery;
    }
}

// The app with email switched on (settings present) and a fake sender.
public class EmailAppFactory : AppFactory
{
    public FakeEmailSender Emails { get; } = new();
    protected virtual int RequestsPer15Minutes => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Email:Resend:ApiKey", "test-key");
        builder.UseSetting("Email:From", "Book Library <noreply@test.local>");
        builder.UseSetting("Email:RequestsPer15Minutes", RequestsPer15Minutes.ToString());
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(Emails);
        });
    }
}

public class EmailTests(EmailAppFactory app) : IClassFixture<EmailAppFactory>
{
    async Task<Browser> RegisterAndConfirmAsync(string email, string password = "User123!x")
    {
        var b = app.NewBrowser();
        (await b.RegisterAsync(email, password)).Dispose();
        var confirm = app.Emails.LastTo(email);
        Assert.NotNull(confirm);
        await b.PageAsync(FakeEmailSender.LinkIn(confirm.Value.Html));
        return b;
    }

    [Fact]
    public async Task New_accounts_must_confirm_their_email_before_logging_in()
    {
        var b = app.NewBrowser();
        using (var reg = await b.RegisterAsync("newbie@test.local", "User123!x"))
            Assert.Contains("RegisterConfirmation", reg.Headers.Location?.ToString() ?? "");

        var mail = app.Emails.LastTo("newbie@test.local");
        Assert.NotNull(mail);
        Assert.Contains("Confirm your email", mail.Value.Subject);

        using (var early = await app.NewBrowser().LoginAsync("newbie@test.local", "User123!x"))
            Assert.Equal(HttpStatusCode.OK, early.StatusCode); // stays on the login page: not allowed yet

        Assert.Contains("Thank you for confirming your email", await b.TextAsync(FakeEmailSender.LinkIn(mail.Value.Html)));
        using var ok = await app.NewBrowser().LoginAsync("newbie@test.local", "User123!x");
        Assert.Equal("/", ok.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Forgot_password_emails_a_working_reset_link()
    {
        await RegisterAndConfirmAsync("forgot@test.local");
        var b = app.NewBrowser();
        using (var res = await b.PostFormAsync("/Identity/Account/ForgotPassword", "/Identity/Account/ForgotPassword",
                   new() { ["Email"] = "forgot@test.local" }))
            Assert.Contains("ForgotPasswordConfirmation", res.Headers.Location?.ToString() ?? "");

        var mail = app.Emails.LastTo("forgot@test.local");
        Assert.NotNull(mail);
        Assert.Contains("Reset your Book Library password", mail.Value.Subject);

        var path = FakeEmailSender.LinkIn(mail.Value.Html);
        var code = Browser.Parse(await b.PageAsync(path)).QuerySelector("input[name='Input.Code']")?.GetAttribute("value");
        using (var reset = await b.PostFormAsync(path, "/Identity/Account/ResetPassword", new()
               {
                   ["Input.Email"] = "forgot@test.local", ["Input.Password"] = "Changed123!", ["Input.ConfirmPassword"] = "Changed123!", ["Input.Code"] = code!,
               }))
            Assert.Contains("ResetPasswordConfirmation", reset.Headers.Location?.ToString() ?? "");

        using var login = await app.NewBrowser().LoginAsync("forgot@test.local", "Changed123!");
        Assert.Equal("/", login.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Unknown_and_blocked_addresses_get_the_same_answer_but_no_email()
    {
        await RegisterAndConfirmAsync("blocked-reset@test.local");
        var userId = await app.DbAsync(db => db.Users.Where(u => u.Email == "blocked-reset@test.local").Select(u => u.Id).SingleAsync());
        var admin = await app.AdminAsync();
        (await admin.PostFormAsync("/AdminUsers", $"/AdminUsers/Block/{userId}", new())).Dispose();
        var before = app.Emails.Sent.Count;

        foreach (var address in new[] { "nobody@test.local", "blocked-reset@test.local" })
        {
            using var res = await app.NewBrowser().PostFormAsync("/Identity/Account/ForgotPassword", "/Identity/Account/ForgotPassword",
                new() { ["Email"] = address });
            Assert.Contains("ForgotPasswordConfirmation", res.Headers.Location?.ToString() ?? "");
        }
        Assert.Equal(before, app.Emails.Sent.Count);
    }

    [Fact]
    public async Task Admin_can_confirm_an_email_by_hand()
    {
        (await app.NewBrowser().RegisterAsync("no-mail@test.local", "User123!x")).Dispose();
        var admin = await app.AdminAsync();
        Assert.Contains("Email not confirmed", await admin.TextAsync("/AdminUsers?q=no-mail@"));

        var id = await app.DbAsync(db => db.Users.Where(u => u.Email == "no-mail@test.local").Select(u => u.Id).SingleAsync());
        (await admin.PostFormAsync("/AdminUsers", $"/AdminUsers/ConfirmEmail/{id}", new())).Dispose();

        using var login = await app.NewBrowser().LoginAsync("no-mail@test.local", "User123!x");
        Assert.Equal("/", login.Headers.Location?.ToString());
    }
}

// The same app with a small limit, to check that the email pages are rate limited.
public class RateLimitedEmailAppFactory : EmailAppFactory
{
    protected override int RequestsPer15Minutes => 3;
}

public class EmailRateLimitTests(RateLimitedEmailAppFactory app) : IClassFixture<RateLimitedEmailAppFactory>
{
    [Fact]
    public async Task Email_pages_refuse_too_many_requests_but_other_pages_do_not()
    {
        var b = app.NewBrowser();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/Identity/Account/ForgotPassword")).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await b.GetAsync("/Identity/Account/ForgotPassword")).Status);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/Identity/Account/Login")).Status);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/")).Status);
    }
}

// The Resend sender itself, against a fake web server.
public class ResendEmailSenderTests
{
    class FakeHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(status) { Content = new StringContent("{\"id\":\"x\"}") };
        }
    }

    class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.resend.com/") };
    }

    static ResendEmailSender Sender(HttpMessageHandler handler, bool configured = true) =>
        new(new Factory(handler),
            new ConfigurationBuilder().AddInMemoryCollection(configured
                ? new Dictionary<string, string?> { ["Email:Resend:ApiKey"] = "re_test", ["Email:From"] = "Book Library <noreply@example.com>" }
                : new Dictionary<string, string?>()).Build(),
            NullLogger<ResendEmailSender>.Instance);

    [Fact]
    public async Task Sends_the_documented_request()
    {
        var handler = new FakeHandler(HttpStatusCode.OK);
        await Sender(handler).SendEmailAsync("reader@example.com", "Hello", "<p>Hi</p>");

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.resend.com/emails", request.RequestUri!.ToString());
        Assert.Equal("Bearer re_test", request.Headers.Authorization!.ToString());
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Book Library <noreply@example.com>", json.RootElement.GetProperty("from").GetString());
        Assert.Equal("reader@example.com", json.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal("Hello", json.RootElement.GetProperty("subject").GetString());
        Assert.Equal("<p>Hi</p>", json.RootElement.GetProperty("html").GetString());
    }

    [Fact]
    public async Task A_refused_email_does_not_throw() =>
        await Sender(new FakeHandler(HttpStatusCode.Forbidden)).SendEmailAsync("x@example.com", "S", "B");

    [Fact]
    public async Task Without_settings_nothing_is_sent()
    {
        var handler = new FakeHandler(HttpStatusCode.OK);
        await Sender(handler, configured: false).SendEmailAsync("x@example.com", "S", "B");
        Assert.Empty(handler.Requests);
    }
}
