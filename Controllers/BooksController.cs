using BookLibrary.Data;
using BookLibrary.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UglyToad.PdfPig;

namespace BookLibrary.Controllers;

public class BooksController : Controller
{
    private readonly ApplicationDbContext _db;
    public BooksController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Details(int id, bool read = false)
    {
        // Newest approved first. Approving replaces the previous one, but older databases
        // may still hold several approved translations; the newest one wins.
        var book = await _db.Books
            .Include(b => b.Translations
                .Where(x => x.Status == TranslationStatus.Approved)
                .OrderByDescending(x => x.DecidedAt ?? x.SubmittedAt))
            .FirstOrDefaultAsync(b => b.Id == id);
        if (book == null) return NotFound();
        ViewBag.Read = read;

        // The signed-in user's latest submission for this book, to show its status.
        if (User.Identity?.IsAuthenticated == true)
            ViewBag.Mine = await MyTranslations()
                .Where(t => t.BookId == id)
                .OrderByDescending(t => t.SubmittedAt)
                .Select(t => new Translation
                {
                    Id = t.Id, BookId = t.BookId, Status = t.Status,
                    SubmittedAt = t.SubmittedAt, DecidedAt = t.DecidedAt, RejectionReason = t.RejectionReason,
                })
                .FirstOrDefaultAsync();
        return View(book);
    }

    // The signed-in user's submissions, with their status and any rejection reason.
    [Authorize]
    public async Task<IActionResult> Mine() =>
        View(await MyTranslations()
            .OrderByDescending(t => t.SubmittedAt)
            .Select(t => new Translation
            {
                Id = t.Id, BookId = t.BookId, Book = t.Book, Status = t.Status,
                SubmittedAt = t.SubmittedAt, DecidedAt = t.DecidedAt, RejectionReason = t.RejectionReason,
            })
            .ToListAsync());

    [Authorize, HttpGet]
    public async Task<IActionResult> Submit(int id)
    {
        var book = await _db.Books.FindAsync(id);
        if (book == null) return NotFound();
        if (await HasPendingAsync(id)) return AlreadyPending(id);
        return View(book);
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id, IFormFile? file)
    {
        var book = await _db.Books.FindAsync(id);
        if (book == null) return NotFound();
        if (await HasPendingAsync(id)) return AlreadyPending(id);

        string text;
        try { text = file == null ? "" : ExtractText(file); } catch { text = ""; }
        if (string.IsNullOrWhiteSpace(text))
        {
            ModelState.AddModelError("", "Could not read text from that file. Use a .docx or a text-based .pdf (not a scan).");
            return View(book);
        }

        _db.Translations.Add(new Translation
        {
            BookId = id,
            TranslatorName = User.Identity?.Name ?? "",
            SubmittedByUserId = CurrentUserId(),
            Text = text,
        });
        await _db.SaveChangesAsync();
        TempData["Msg"] = "Submitted. An admin will review your translation.";
        return RedirectToAction(nameof(Details), new { id });
    }

    string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Matches by user id; translations from before that was stored are matched by name.
    IQueryable<Translation> MyTranslations()
    {
        var userId = CurrentUserId();
        var name = User.Identity?.Name;
        if (userId == null) return _db.Translations.Where(t => false);
        return _db.Translations.Where(t => t.SubmittedByUserId == userId
            || (t.SubmittedByUserId == null && t.TranslatorName == name));
    }

    // One translation waiting for review per user per book.
    Task<bool> HasPendingAsync(int bookId) =>
        MyTranslations().AnyAsync(t => t.BookId == bookId && t.Status == TranslationStatus.Pending);

    IActionResult AlreadyPending(int bookId)
    {
        TempData["Msg"] = "You already have a translation of this book waiting for review. You can send a new one after it is reviewed.";
        return RedirectToAction(nameof(Details), new { id = bookId });
    }

    static string ExtractText(IFormFile f)
    {
        using var ms = new MemoryStream();
        f.CopyTo(ms);
        ms.Position = 0;
        var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
        if (ext == ".docx")
        {
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) return "";
            return string.Join("\n\n", body.Elements<Paragraph>().Select(p => p.InnerText));
        }
        if (ext == ".pdf")
        {
            using var pdf = PdfDocument.Open(ms);
            return string.Join("\n\n", pdf.GetPages().Select(p => p.Text));
        }
        return "";
    }
}
