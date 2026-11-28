using System.Net;
using System.Security.Claims;
using BookLibrary.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookLibrary.Tests;

// "Continue with Google". The real Google cannot be reached from a test, so one test checks that the button sends the
// browser to Google correctly, and the rest start from the moment Google sends the visitor back: a test-only page
// (/test/google-sign-in) puts the same cookie Google's redirect would leave, then the real callback page runs.
//
// Email confirmation is switched on here (like the live site with Resend set up), because that is the case where the
// built-in page would leave new Google users stuck waiting for a confirmation email.
public class GoogleAppFactory : EmailAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Google:ClientId", "test-client-id.apps.googleusercontent.com");
        builder.UseSetting("Google:ClientSecret", "test-secret");
        builder.ConfigureTestServices(s => s.AddTransient<IStartupFilter, FakeGoogleCallbackFilter>());
    }

    // /test/google-sign-in?sub=...&email=...&verified=true leaves the external-login cookie of a Google visitor.
    class FakeGoogleCallbackFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (ctx, nextMiddleware) =>
            {
                if (ctx.Request.Path != "/test/google-sign-in") { await nextMiddleware(); return; }

                var q = ctx.Request.Query;
                var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, q["sub"].ToString()) };
                if (q.ContainsKey("email")) claims.Add(new(ClaimTypes.Email, q["email"].ToString()));
                if (q.ContainsKey("verified")) claims.Add(new("email_verified", q["verified"].ToString()));
                var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Google"));
                var properties = new AuthenticationProperties { Items = { ["LoginProvider"] = "Google" } };
                await ctx.SignInAsync(IdentityConstants.ExternalScheme, principal, properties);
                ctx.Response.StatusCode = 204;
            });
            next(app);
        };
    }
}

public class GoogleLoginTests(GoogleAppFactory app) : IClassFixture<GoogleAppFactory>
{
    const string Callback = "/Identity/Account/ExternalLogin?handler=Callback&returnUrl=%2FLibrary";

    // A visitor coming back from Google: the cookie, then the real callback page.
    static async Task<(Browser Browser, HttpResponseMessage Response)> ComeBackFromGoogleAsync(
        GoogleAppFactory app, string sub, string? email, string? verified = "true", string callback = Callback)
    {
        var b = app.NewBrowser();
        var url = $"/test/google-sign-in?sub={Uri.EscapeDataString(sub)}"
                  + (email != null ? $"&email={Uri.EscapeDataString(email)}" : "")
                  + (verified != null ? $"&verified={verified}" : "");
        Assert.Equal(HttpStatusCode.NoContent, (await b.Client.GetAsync(url)).StatusCode);
        return (b, await b.Client.GetAsync(callback));
    }

    static string Where(HttpResponseMessage r) => r.Headers.Location?.OriginalString ?? "";

    Task<IdentityUser?> FindUserAsync(string email) =>
        app.DbAsync(db => db.Users.Where(u => u.Email == email).SingleOrDefaultAsync())!;

    [Fact]
    public async Task Log_in_and_register_pages_offer_continue_with_google()
    {
        var b = app.NewBrowser();
        foreach (var page in new[] { "/Identity/Account/Login", "/Identity/Account/Register" })
        {
            var html = await b.PageAsync(page);
            Assert.Contains("name=\"provider\"", html);
            Assert.Contains("value=\"Google\"", html);
        }
    }

    [Fact]
    public async Task Google_button_sends_the_visitor_to_google_with_this_sites_client_id_and_return_address()
    {
        var b = app.NewBrowser();
        var response = await b.PostFormAsync("/Identity/Account/Login", "/Identity/Account/ExternalLogin?returnUrl=%2FLibrary",
            new() { ["provider"] = "Google" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var to = Where(response);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", to);
        Assert.Contains("client_id=test-client-id.apps.googleusercontent.com", to);
        Assert.Contains("signin-google", Uri.UnescapeDataString(to));   // where Google sends the visitor back
        Assert.Contains("email", Uri.UnescapeDataString(to));           // asks for the email address
    }

    [Fact]
    public async Task First_google_sign_in_creates_a_confirmed_account_and_signs_in_even_though_email_confirmation_is_required()
    {
        var (b, response) = await ComeBackFromGoogleAsync(app, "sub-new-1", "new-google-user@test.local");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Library", Where(response));

        var user = await FindUserAsync("new-google-user@test.local");
        Assert.NotNull(user);
        Assert.True(user!.EmailConfirmed);                                  // no confirmation email needed
        Assert.Equal(1, await app.DbAsync(db => db.UserLogins.CountAsync(l => l.UserId == user.Id && l.LoginProvider == "Google")));
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/Books/Mine")).Status);  // signed in: this page needs a login
        Assert.Null(app.Emails.LastTo("new-google-user@test.local"));        // and no confirmation email was sent to them
    }

    [Fact]
    public async Task Same_google_account_signs_in_again_without_a_second_account()
    {
        await ComeBackFromGoogleAsync(app, "sub-returning", "returning@test.local");
        var (b, again) = await ComeBackFromGoogleAsync(app, "sub-returning", "returning@test.local");

        Assert.Equal("/Library", Where(again));
        Assert.Equal(1, await app.DbAsync(db => db.Users.CountAsync(u => u.Email == "returning@test.local")));
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/Books/Mine")).Status);
    }

    [Fact]
    public async Task Google_does_not_take_over_an_existing_account_with_the_same_email()
    {
        await app.NewBrowser().RegisterAsync("taken@test.local", "User123!x");   // registered earlier, with a password
        var (b, response) = await ComeBackFromGoogleAsync(app, "sub-attacker", "taken@test.local");

        Assert.StartsWith("/Identity/Account/Login", Where(response));
        var login = await b.PageAsync(Where(response));
        Assert.Contains("already exists", login);                           // the visitor is told why
        var user = await FindUserAsync("taken@test.local");
        Assert.Equal(0, await app.DbAsync(db => db.UserLogins.CountAsync(l => l.UserId == user!.Id)));   // nothing was linked
        Assert.Equal(HttpStatusCode.Redirect, (await b.GetAsync("/Books/Mine")).Status);                 // and not signed in
    }

    [Fact]
    public async Task Google_email_that_google_has_not_verified_is_refused()
    {
        var (b, response) = await ComeBackFromGoogleAsync(app, "sub-unverified", "unverified@test.local", verified: "false");

        Assert.StartsWith("/Identity/Account/Login", Where(response));
        Assert.Contains("not verified", await b.PageAsync(Where(response)));
        Assert.Null(await FindUserAsync("unverified@test.local"));
    }

    [Fact]
    public async Task Google_without_an_email_address_is_refused()
    {
        var (b, response) = await ComeBackFromGoogleAsync(app, "sub-no-email", email: null, verified: null);

        Assert.StartsWith("/Identity/Account/Login", Where(response));
        Assert.Contains("did not share an email", await b.PageAsync(Where(response)));
    }

    [Fact]
    public async Task Blocked_user_cannot_sign_in_with_google()
    {
        await ComeBackFromGoogleAsync(app, "sub-blocked", "blocked-google@test.local");
        var id = (await FindUserAsync("blocked-google@test.local"))!.Id;
        var admin = await app.AdminAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostFormAsync("/AdminUsers", $"/AdminUsers/Block/{id}", new())).StatusCode);

        var (b, response) = await ComeBackFromGoogleAsync(app, "sub-blocked", "blocked-google@test.local");

        Assert.Contains("Lockout", Where(response));
        Assert.Equal(HttpStatusCode.Redirect, (await b.GetAsync("/Books/Mine")).Status);   // sent to log in, not let through
    }

    [Fact]
    public async Task Error_from_google_goes_back_to_log_in_with_a_message()
    {
        var b = app.NewBrowser();
        var response = await b.Client.GetAsync("/Identity/Account/ExternalLogin?handler=Callback&returnUrl=%2FLibrary&remoteError=access_denied");

        Assert.StartsWith("/Identity/Account/Login", Where(response));
        Assert.Contains("access_denied", await b.PageAsync(Where(response)));
    }

    [Fact]
    public async Task Callback_without_a_google_cookie_goes_back_to_log_in()
    {
        var response = await app.NewBrowser().Client.GetAsync(Callback);
        Assert.StartsWith("/Identity/Account/Login", Where(response));
    }

    [Fact]
    public async Task Return_address_must_stay_on_this_site()
    {
        var (_, response) = await ComeBackFromGoogleAsync(app, "sub-redirect", "redirect@test.local",
            callback: "/Identity/Account/ExternalLogin?handler=Callback&returnUrl=https%3A%2F%2Fevil.example%2F");
        Assert.Equal("/", Where(response));   // an outside address is replaced by the home page
    }
}
