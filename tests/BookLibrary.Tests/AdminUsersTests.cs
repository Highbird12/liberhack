using System.Net;
using BookLibrary.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookLibrary.Tests;

// The admin Users page, account lockout and password reset links.
public class AdminUsersTests(AppFactory app) : IClassFixture<AppFactory>
{
    Task<string> UserId(string email) =>
        app.DbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

    static Task<HttpResponseMessage> Act(Browser admin, string action, string id) =>
        admin.PostFormAsync("/AdminUsers", $"/AdminUsers/{action}/{id}", new());

    [Fact]
    public async Task Only_admins_can_open_the_users_page()
    {
        var user = await app.NewUserAsync("plain@test.local");
        using var res = await user.Client.GetAsync("/AdminUsers");
        Assert.Contains("AccessDenied", res.Headers.Location!.ToString());

        var rows = Browser.Parse(await (await app.AdminAsync()).PageAsync("/AdminUsers?q=plain@"))
            .QuerySelectorAll("table tr td:first-child").Select(td => td.TextContent.Trim()).ToList();
        Assert.Equal(["plain@test.local"], rows);
    }

    [Fact]
    public async Task Making_someone_admin_gives_access_and_removing_it_takes_it_away()
    {
        await app.NewUserAsync("promoted@test.local");
        var admin = await app.AdminAsync();
        var id = await UserId("promoted@test.local");

        await Act(admin, "MakeAdmin", id);
        var promoted = app.NewBrowser();
        await promoted.LoginAsync("promoted@test.local", "User123!x");
        Assert.Equal(HttpStatusCode.OK, (await promoted.GetAsync("/AdminBooks")).Status);

        await Act(admin, "RemoveAdmin", id);
        var again = app.NewBrowser();
        await again.LoginAsync("promoted@test.local", "User123!x");
        using var res = await again.Client.GetAsync("/AdminBooks");
        Assert.Contains("AccessDenied", res.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Admins_cannot_remove_their_own_role_or_the_configured_admin()
    {
        var admin = await app.AdminAsync();
        var adminId = await UserId(AppFactory.AdminEmail);
        await Act(admin, "RemoveAdmin", adminId);
        Assert.Contains("can&#x27;t remove your own admin role", await admin.PageAsync("/AdminUsers"));

        // A second admin tries to demote the admin from the settings.
        await app.NewUserAsync("second-admin@test.local");
        await Act(admin, "MakeAdmin", await UserId("second-admin@test.local"));
        var second = app.NewBrowser();
        await second.LoginAsync("second-admin@test.local", "User123!x");
        await Act(second, "RemoveAdmin", adminId);
        Assert.Contains("set in the server settings", await second.PageAsync("/AdminUsers"));
        Assert.True(await app.DbAsync(db => db.UserRoles.AnyAsync(r => r.UserId == adminId)));
    }

    [Fact]
    public async Task Blocked_users_cannot_log_in_until_unblocked()
    {
        await app.NewUserAsync("blocked@test.local");
        var admin = await app.AdminAsync();
        var id = await UserId("blocked@test.local");

        await Act(admin, "Block", id);
        Assert.Contains("Blocked", await admin.PageAsync("/AdminUsers?q=blocked@"));
        using (var denied = await app.NewBrowser().LoginAsync("blocked@test.local", "User123!x"))
            Assert.Contains("Lockout", denied.Headers.Location?.ToString() ?? "");

        await Act(admin, "Unblock", id);
        using var ok = await app.NewBrowser().LoginAsync("blocked@test.local", "User123!x");
        Assert.Equal("/", ok.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Admins_cannot_be_blocked()
    {
        var admin = await app.AdminAsync();
        await app.NewUserAsync("admin-two@test.local");
        var id = await UserId("admin-two@test.local");
        await Act(admin, "MakeAdmin", id);
        await Act(admin, "Block", id);
        Assert.Contains("Remove the admin role before blocking", await admin.PageAsync("/AdminUsers"));
        Assert.Null(await app.DbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.LockoutEnd).SingleAsync()));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_an_admin_can_unlock_it()
    {
        await app.NewUserAsync("guessed@test.local");
        var attacker = app.NewBrowser();
        for (var i = 0; i < 5; i++)
            (await attacker.LoginAsync("guessed@test.local", "WrongPassword1!")).Dispose();

        // Even the right password is refused now.
        using (var locked = await app.NewBrowser().LoginAsync("guessed@test.local", "User123!x"))
            Assert.Contains("Lockout", locked.Headers.Location?.ToString() ?? "");

        var admin = await app.AdminAsync();
        Assert.Contains("Locked after wrong passwords", await admin.PageAsync("/AdminUsers?q=guessed@"));
        await Act(admin, "Unblock", await UserId("guessed@test.local"));
        using var ok = await app.NewBrowser().LoginAsync("guessed@test.local", "User123!x");
        Assert.Equal("/", ok.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Reset_link_lets_the_user_choose_a_new_password()
    {
        await app.NewUserAsync("forgetful@test.local");
        var admin = await app.AdminAsync();
        await Act(admin, "ResetLink", await UserId("forgetful@test.local"));

        var link = Browser.Parse(await admin.PageAsync("/AdminUsers")).QuerySelector("#reset-link")?.GetAttribute("value");
        Assert.False(string.IsNullOrEmpty(link));
        var path = new Uri(link!).PathAndQuery;

        var user = app.NewBrowser();
        var code = Browser.Parse(await user.PageAsync(path)).QuerySelector("input[name='Input.Code']")?.GetAttribute("value");
        using var reset = await user.PostFormAsync(path, "/Identity/Account/ResetPassword", new()
        {
            ["Input.Email"] = "forgetful@test.local", ["Input.Password"] = "BrandNew123!", ["Input.ConfirmPassword"] = "BrandNew123!", ["Input.Code"] = code!,
        });
        Assert.Contains("ResetPasswordConfirmation", reset.Headers.Location?.ToString() ?? "");

        using var oldPw = await app.NewBrowser().LoginAsync("forgetful@test.local", "User123!x");
        Assert.Equal(HttpStatusCode.OK, oldPw.StatusCode); // stays on the login page
        using var newPw = await app.NewBrowser().LoginAsync("forgetful@test.local", "BrandNew123!");
        Assert.Equal("/", newPw.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Forgot_password_page_tells_the_truth()
    {
        var html = await app.NewBrowser().PageAsync("/Identity/Account/ForgotPassword");
        Assert.Contains("can't send emails", html);
        Assert.Contains("Ask an admin", html);
    }
}
