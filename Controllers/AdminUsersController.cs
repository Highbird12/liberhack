using System.Security.Claims;
using System.Text;
using BookLibrary.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BookLibrary.Controllers;

// Admin page for managing user accounts: admin role, blocking, password reset links.
[Authorize(Roles = "Admin")]
public class AdminUsersController : Controller
{
    const string AdminRole = "Admin";
    // A block is a lockout that never ends on its own; failed-login lockouts last minutes.
    static readonly DateTimeOffset BlockedUntil = DateTimeOffset.MaxValue;

    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _users;
    private readonly IConfiguration _config;

    public AdminUsersController(ApplicationDbContext db, UserManager<IdentityUser> users, IConfiguration config)
    {
        _db = db;
        _users = users;
        _config = config;
    }

    public record UserRow(string Id, string Email, bool IsAdmin, bool IsBlocked, DateTimeOffset? LockedUntil,
                          int Translations, bool IsSelf, bool IsConfiguredAdmin);

    public async Task<IActionResult> Index(string? q)
    {
        q = q?.Trim();
        var query = _db.Users.AsQueryable();
        if (!string.IsNullOrEmpty(q))
            query = query.Where(u => u.Email != null && u.NormalizedEmail!.Contains(q.ToUpperInvariant()));
        var users = await query.OrderBy(u => u.Email).ToListAsync();

        var adminIds = (await _users.GetUsersInRoleAsync(AdminRole)).Select(u => u.Id).ToHashSet();
        var counts = await _db.Translations.Where(t => t.SubmittedByUserId != null)
            .GroupBy(t => t.SubmittedByUserId!).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var me = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var now = DateTimeOffset.UtcNow;

        ViewBag.Q = q;
        return View(users.Select(u => new UserRow(
            u.Id,
            u.Email ?? u.UserName ?? "",
            adminIds.Contains(u.Id),
            u.LockoutEnd == BlockedUntil,
            u.LockoutEnd is { } end && end > now && end != BlockedUntil ? end : null,
            counts.GetValueOrDefault(u.Id),
            u.Id == me,
            IsConfiguredAdmin(u))).ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MakeAdmin(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (!await _users.IsInRoleAsync(user, AdminRole)) await _users.AddToRoleAsync(user, AdminRole);
        await _users.UpdateSecurityStampAsync(user); // their new rights apply within a minute
        return Done($"{user.Email} is now an admin.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAdmin(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (IsSelf(user)) return Done("You can't remove your own admin role. Ask another admin.", error: true);
        if (IsConfiguredAdmin(user))
            return Done($"{user.Email} is the admin set in the server settings (Admin:Email) and gets the role back on every restart. Change that setting first.", error: true);
        if ((await _users.GetUsersInRoleAsync(AdminRole)).Count <= 1)
            return Done("There must always be at least one admin.", error: true);

        await _users.RemoveFromRoleAsync(user, AdminRole);
        await _users.UpdateSecurityStampAsync(user);
        return Done($"{user.Email} is no longer an admin.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Block(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (IsSelf(user)) return Done("You can't block yourself.", error: true);
        if (await _users.IsInRoleAsync(user, AdminRole))
            return Done($"{user.Email} is an admin. Remove the admin role before blocking the account.", error: true);

        await _users.SetLockoutEnabledAsync(user, true);
        await _users.SetLockoutEndDateAsync(user, BlockedUntil);
        await _users.UpdateSecurityStampAsync(user); // signs them out within a minute
        return Done($"{user.Email} is blocked and can no longer log in.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unblock(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);
        return Done($"{user.Email} can log in again.");
    }

    // Creates a one-time link for choosing a new password, for users who forgot theirs.
    // The admin sends it to the user personally; the site does not send email.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetLink(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = Url.Page("/Account/ResetPassword", pageHandler: null, values: new { area = "Identity", code }, protocol: Request.Scheme);
        TempData["ResetEmail"] = user.Email;
        TempData["ResetLink"] = link;
        return RedirectToAction(nameof(Index));
    }

    bool IsSelf(IdentityUser user) => user.Id == User.FindFirstValue(ClaimTypes.NameIdentifier);

    bool IsConfiguredAdmin(IdentityUser user) =>
        _config["Admin:Email"] is { Length: > 0 } email &&
        string.Equals(user.Email, email.Trim(), StringComparison.OrdinalIgnoreCase);

    IActionResult Done(string message, bool error = false)
    {
        TempData[error ? "Error" : "Msg"] = message;
        return RedirectToAction(nameof(Index));
    }
}
