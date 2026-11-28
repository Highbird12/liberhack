using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using BookLibrary.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace BookLibrary.Areas.Identity.Pages.Account;

// Replaces the built-in "Forgot password" page.
// With email configured it emails a reset link; without it, it tells the user to ask an admin
// (admins create reset links on the Users page).
[AllowAnonymous]
public class ForgotPasswordModel(UserManager<IdentityUser> users, IEmailSender email, IConfiguration config) : PageModel
{
    public bool EmailEnabled => EmailSettings.IsConfigured(config);

    [BindProperty, Required, EmailAddress, Display(Name = "Email")]
    public string Email { get; set; } = "";

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!EmailEnabled || !ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Email.Trim());
        // Blocked accounts get no link. Either way the same page follows, so this form can't be
        // used to find out which addresses have an account.
        if (user?.Email != null && user.LockoutEnd != DateTimeOffset.MaxValue)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = Url.Page("/Account/ResetPassword", pageHandler: null, values: new { area = "Identity", code }, protocol: Request.Scheme)!;
            await email.SendEmailAsync(user.Email, "Reset your Book Library password",
                $"<p>Someone asked to reset the password for your Book Library account.</p>" +
                $"<p><a href=\"{HtmlEncoder.Default.Encode(link)}\">Choose a new password</a></p>" +
                $"<p>The link works once and expires after a day. If you didn't ask for this, you can ignore this email.</p>");
        }
        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
