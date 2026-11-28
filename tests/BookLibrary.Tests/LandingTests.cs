using System.Net;
using BookLibrary.Tests.Infrastructure;

namespace BookLibrary.Tests;

// The landing page at "/", its shared header, and the dark look of the library.
public class LandingTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Landing_page_shows_the_headline_and_links_to_the_library()
    {
        var html = await app.NewBrowser().PageAsync("/");
        Assert.Contains("The ultimate open source library", html);
        Assert.Contains("Books Worth Reading", html);
        Assert.Contains("In Bulgarian", html);
        Assert.Contains("href=\"/Library\"", html);
        Assert.Contains("Browse the library", html);
        Assert.Contains("hf_20260809_012548_ef22562c-c0ae-4816-ad9d-f8922af4e6a7.mp4", html); // background video
        Assert.Contains("/landing/assets/logo.webp", html);
        Assert.DoesNotContain("navbar", html);           // full-screen page, not the site layout
        Assert.DoesNotContain("Pride and Prejudice", html); // the book list lives on /Library now
    }

    [Fact]
    public async Task Visitors_see_sign_in_and_no_my_translations_link()
    {
        var html = await app.NewBrowser().PageAsync("/");
        Assert.Contains("Sign in", html);
        Assert.Contains("/Identity/Account/Login", html);
        Assert.DoesNotContain("My translations", html);
        Assert.DoesNotContain("Log out", html);
    }

    [Fact]
    public async Task Signed_in_users_see_my_translations_and_a_log_out_form()
    {
        var user = await app.NewUserAsync("landing-user@test.local");
        var html = await user.PageAsync("/");
        Assert.Contains("My translations", html);
        Assert.Contains("Log out", html);
        Assert.Contains("__RequestVerificationToken", html); // log out is a POST with the anti-forgery token
        Assert.DoesNotContain(">Manage books<", html);      // admin links only for admins
    }

    [Fact]
    public async Task Admins_get_admin_links_in_the_menu()
    {
        var admin = await app.AdminAsync();
        var html = await admin.PageAsync("/");
        Assert.Contains(">Manage books<", html);
        Assert.Contains(">Users<", html);
        Assert.Contains(">Translation requests<", html);
    }

    [Fact]
    public async Task Old_search_links_on_the_home_page_go_to_the_library()
    {
        var (status, _) = await app.NewBrowser().GetAsync("/?q=tolstoy");
        Assert.Equal(HttpStatusCode.Redirect, status);
        var response = await app.NewBrowser().Client.GetAsync("/?q=tolstoy&nationality=Russian");
        Assert.Equal("/Library?q=tolstoy&nationality=Russian", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Library_uses_the_dark_look_and_other_pages_keep_the_normal_one()
    {
        var b = app.NewBrowser();
        var library = await b.PageAsync("/Library");
        Assert.Contains("<body class=\"vibe\">", library);
        Assert.Contains("data-bs-theme=\"dark\"", library);
        Assert.Contains("/css/vibe.css", library);
        Assert.Contains("class=\"active\" aria-current=\"page\" href=\"/Library\"", library);

        var id = await app.DbAsync(db => Task.FromResult(db.Books.First(x => !x.Hidden).Id));
        var book = await b.PageAsync($"/Books/Details/{id}");
        Assert.DoesNotContain("/css/vibe.css", book);
        Assert.Contains("navbar", book);
        Assert.Contains("href=\"/Library\"", book); // Back to library
    }

    [Theory]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Register")]
    [InlineData("/Identity/Account/ForgotPassword")]
    public async Task Log_in_register_and_password_pages_use_the_home_page_look_and_video(string url)
    {
        var html = await app.NewBrowser().PageAsync(url);
        Assert.Contains("<body class=\"vibe auth video\">", html);
        Assert.Contains("data-bs-theme=\"dark\"", html);
        Assert.Contains("hf_20260809_012548_ef22562c-c0ae-4816-ad9d-f8922af4e6a7.mp4", html);   // same background video
        Assert.Contains("class=\"vh-nav\"", html);                                              // same header
        Assert.DoesNotContain("navbar-brand\" href=\"/\">BookLibrary", html);                    // not Identity's own white layout
    }

    [Theory]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Register")]
    public async Task Identity_forms_load_jquery_so_client_side_validation_works(string url)
    {
        // Identity's validation scripts need jQuery, which the layout has to load (it is Identity's own static copy;
        // a published app contains it under wwwroot/Identity/lib, which the test host does not serve)
        var html = await app.NewBrowser().PageAsync(url);
        var jquery = html.IndexOf("/Identity/lib/jquery/dist/jquery.min.js", StringComparison.Ordinal);
        var validate = html.IndexOf("jquery.validate", StringComparison.Ordinal);
        Assert.True(jquery > 0, "jQuery is not loaded");
        Assert.True(validate > jquery, "the validation plugin must come after jQuery");
    }

    [Fact]
    public async Task Without_google_keys_the_login_page_has_no_provider_button()
    {
        var html = await app.NewBrowser().PageAsync("/Identity/Account/Login");
        Assert.DoesNotContain("name=\"provider\"", html);
    }

    [Fact]
    public async Task My_translations_uses_the_home_page_look_and_video()
    {
        var user = await app.NewUserAsync("mine-look@test.local");
        var html = await user.PageAsync("/Books/Mine");
        Assert.Contains("<body class=\"vibe video\">", html);
        Assert.Contains("hf_20260809_012548_ef22562c-c0ae-4816-ad9d-f8922af4e6a7.mp4", html);
        Assert.Contains("class=\"glass\"", html);
        Assert.Contains("class=\"active\" aria-current=\"page\" href=\"/Books/Mine\"", html);   // My translations is the active link
    }

    [Fact]
    public async Task Library_has_the_dark_look_but_no_video()
    {
        var html = await app.NewBrowser().PageAsync("/Library");
        Assert.Contains("<body class=\"vibe\">", html);
        Assert.DoesNotContain("<video", html);
    }

    [Fact]
    public async Task Account_settings_page_uses_the_normal_site_layout_with_working_scripts()
    {
        var user = await app.NewUserAsync("settings-user@test.local");
        var html = await user.PageAsync("/Identity/Account/Manage");
        Assert.Contains("Manage your account", html);
        Assert.DoesNotContain("vibe auth", html);                       // settings are not part of the log in flow
        Assert.Contains("navbar", html);                                 // the site's own navigation
        Assert.Contains("/Identity/lib/jquery/dist/jquery.min.js", html);
    }

    [Theory]
    [InlineData("/landing/styles.css", "Geist Pixel Circle")]
    [InlineData("/landing/main.js", "vh-menu")]
    [InlineData("/css/vibe.css", "body.vibe")]
    [InlineData("/landing/assets/logo.webp", null)]
    [InlineData("/landing/fonts/GeistPixel-Circle.woff2", null)]
    public async Task Landing_files_are_served(string url, string? contains)
    {
        var response = await app.NewBrowser().Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (contains != null)
            Assert.Contains(contains, await response.Content.ReadAsStringAsync());
    }
}
