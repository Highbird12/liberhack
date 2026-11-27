using BookLibrary.Data;
using BookLibrary.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BookLibrary.Controllers;

public class BooksController : Controller
{
    private readonly ApplicationDbContext _db;
    public BooksController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Details(int id, bool read = false)
    {
        var book = await _db.Books
            .Include(b => b.Translations.Where(x => x.Status == TranslationStatus.Approved))
            .FirstOrDefaultAsync(b => b.Id == id);
        if (book == null) return NotFound();
        ViewBag.Read = read;
        return View(book);
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Submit(int id)
    {
        var book = await _db.Books.FindAsync(id);
        return book == null ? NotFound() : View(book);
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id, IFormFile? file)
    {
        var book = await _db.Books.FindAsync(id);
        if (book == null) return NotFound();

        string text;
        try { text = file == null ? "" : ExtractText(file); } catch { text = ""; }
        if (string.IsNullOrWhiteSpace(text))
        {
            ModelState.AddModelError("", "Could not read text from that file. Use a .docx or a text-based .pdf (not a scan).");
            return View(book);
        }

        _db.Translations.Add(new Translation { BookId = id, TranslatorName = User.Identity?.Name ?? "", Text = text });
        await _db.SaveChangesAsync();
        TempData["Msg"] = "Submitted. An admin will review your translation.";
        return RedirectToAction(nameof(Details), new { id });
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
            return string.Join("\n\n", doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().Select(p => p.InnerText));
        }
        if (ext == ".pdf")
        {
            using var pdf = PdfDocument.Open(ms);
            return string.Join("\n\n", pdf.GetPages().Select(p => p.Text));
        }
        return "";
    }
}
