using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BookLibrary.Areas.Identity.Pages.Account;

// Replaces the built-in external login page ("Continue with Google").
//
// Why not the built-in one: for a first-time visitor it shows a form asking them to confirm their email, then, because
// this site requires confirmed emails once email is set up, it emails a link. Google has already verified the address,
// and the site's email sending is limited, so that step would leave new Google users stuck.
//
// What this page does instead:
//  - a Google account that was used here before signs straight in (lockouts and blocks still apply);
//  - a first-time Google account whose email Google reports as verified gets a new account, already confirmed;
//  - if an account with that email already exists (for example one registered with a password) it is NOT linked
//    automatically. Linking a provider login to an existing account without the owner proving access is how accounts
//    get taken over (someone registers your address with their password, then you sign in with Google).
//    The visitor is asked to log in with the password instead; signed-in users can link Google on Manage > External logins.
[AllowAnonymous]
public class ExternalLoginModel(
    SignInManager<IdentityUser> signInManager,
    UserManager<IdentityUser> userManager,
    ILogger<ExternalLoginModel> logger) : PageModel
{
    // The Log in page shows this (same TempData key as its own ErrorMessage property).
    [TempData]
    public string? ErrorMessage { get; set; }

    public IActionResult OnGet() => RedirectToPage("./Login");

    // The "Continue with Google" button posts here: send the visitor to the provider.
    public IActionResult OnPost(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    // The provider sends the visitor back here.
    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");

        if (remoteError != null)
            return Fail("Signing in with Google did not work: " + remoteError, returnUrl);

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info == null)
            return Fail("Could not read the sign-in details from Google. Please try again.", returnUrl);

        // Signed in with this Google account before?
        var result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (result.Succeeded)
        {
            logger.LogInformation("Signed in with {Provider}.", info.LoginProvider);
            return LocalRedirect(returnUrl);
        }
        if (result.IsLockedOut) return RedirectToPage("./Lockout");

        // First time with this Google account.
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
            return Fail("Google did not share an email address, so an account cannot be created.", returnUrl);

        var verified = string.Equals(info.Principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        if (!verified)
            return Fail("Google has not verified this email address, so an account cannot be created with it.", returnUrl);

        if (await userManager.FindByEmailAsync(email) != null)
            return Fail($"An account with the email {email} already exists. Log in with its password instead. " +
                        "Once you are logged in you can connect Google under Manage your account > External logins.", returnUrl);

        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
            return Fail("The account could not be created: " + string.Join(" ", created.Errors.Select(e => e.Description)), returnUrl);

        var linked = await userManager.AddLoginAsync(user, info);
        if (!linked.Succeeded)
        {
            await userManager.DeleteAsync(user); // do not leave an account behind that Google cannot sign in to
            return Fail("The account could not be linked to Google: " + string.Join(" ", linked.Errors.Select(e => e.Description)), returnUrl);
        }

        logger.LogInformation("Created an account from {Provider}.", info.LoginProvider);
        await signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
        return LocalRedirect(returnUrl);
    }

    IActionResult Fail(string message, string? returnUrl)
    {
        ErrorMessage = message;
        return RedirectToPage("./Login", new { returnUrl });
    }
}
