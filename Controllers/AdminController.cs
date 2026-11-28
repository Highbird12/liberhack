using BookLibrary.Data;
using BookLibrary.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookLibrary.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    public AdminController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index() =>
        View(await _db.Translations.Include(x => x.Book)
            .Where(x => x.Status == TranslationStatus.Pending)
            .OrderBy(x => x.SubmittedAt).ToListAsync());

    public async Task<IActionResult> Review(int id)
    {
        var tr = await _db.Translations.Include(x => x.Book).FirstOrDefaultAsync(x => x.Id == id);
        if (tr == null) return NotFound();
        // Warn the admin if approving this would replace a published translation.
        ViewBag.CurrentApproved = await _db.Translations
            .Where(x => x.BookId == tr.BookId && x.Status == TranslationStatus.Approved && x.Id != tr.Id)
            .OrderByDescending(x => x.DecidedAt ?? x.SubmittedAt)
            .Select(x => x.TranslatorName)
            .FirstOrDefaultAsync();
        return View(tr);
    }

    const int MaxReasonLength = 1000;

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(int id, bool approve, string? reason)
    {
        var tr = await _db.Translations.Include(x => x.Book).FirstOrDefaultAsync(x => x.Id == id);
        if (tr == null) return NotFound();

        // A translation is decided once. This stops double clicks, two admins, or the back button
        // from flipping an approved translation to rejected (or the reverse).
        if (tr.Status != TranslationStatus.Pending)
        {
            TempData["Msg"] = $"This translation was already {tr.Status.ToString().ToLowerInvariant()}. Nothing was changed.";
            return RedirectToAction(nameof(Index));
        }

        var now = DateTime.UtcNow;
        if (approve)
        {
            // Only one approved translation per book: the new one replaces the old.
            var previous = await _db.Translations
                .Where(x => x.BookId == tr.BookId && x.Status == TranslationStatus.Approved)
                .ToListAsync();
            foreach (var p in previous) p.Status = TranslationStatus.Replaced;

            tr.Status = TranslationStatus.Approved;
            TempData["Msg"] = $"Approved. This is now the Bulgarian translation of \"{tr.Book?.Title}\".";
        }
        else
        {
            reason = reason?.Trim();
            if (reason?.Length > MaxReasonLength) reason = reason[..MaxReasonLength];
            tr.Status = TranslationStatus.Rejected;
            tr.RejectionReason = string.IsNullOrEmpty(reason) ? null : reason;
            TempData["Msg"] = $"Rejected the translation of \"{tr.Book?.Title}\".";
        }
        tr.DecidedAt = now;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
