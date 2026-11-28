using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BookLibrary.Services;

// The built-in login page signs in with lockoutOnFailure: false, so wrong passwords are never
// counted. This turns counting on for every password login: after too many wrong passwords in a
// row (Identity's Lockout options in Program.cs) the account is locked for a while, which makes
// guessing passwords on the public site impractical.
public class LockoutSignInManager(
    UserManager<IdentityUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<IdentityUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<IdentityUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<IdentityUser> confirmation)
    : SignInManager<IdentityUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override Task<SignInResult> PasswordSignInAsync(string userName, string password, bool isPersistent, bool lockoutOnFailure) =>
        base.PasswordSignInAsync(userName, password, isPersistent, lockoutOnFailure: true);
}
