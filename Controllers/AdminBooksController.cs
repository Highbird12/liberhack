using BookLibrary.Data;
using BookLibrary.Models;
using BookLibrary.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookLibrary.Controllers;

// Admin pages for adding, editing and deleting books.
[Authorize(Roles = "Admin")]
public class AdminBooksController : Controller
{
    const long MaxUploadBytes = 100L * 1024 * 1024; // 100 MB per request
    static readonly string[] CoverExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

    private readonly ApplicationDbContext _db;
    private readonly IFileStorage _storage;

    public AdminBooksController(ApplicationDbContext db, IFileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<IActionResult> Index() =>
        View(await _db.Books.OrderBy(b => b.Nationality).ThenBy(b => b.Title).ToListAsync());

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadNationalitiesAsync();
        return View("Form", new BookForm());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes), RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Create(BookForm form)
    {
        ValidateFiles(form);
        if (!ModelState.IsValid)
        {
            await LoadNationalitiesAsync();
            return View("Form", form);
        }

        var (cover, pdf, error) = await UploadAsync(form);
        if (error != null)
        {
            ModelState.AddModelError("", error);
            await LoadNationalitiesAsync();
            return View("Form", form);
        }

        var book = new Book();
        Apply(form, book);
        book.CoverUrl = cover;
        book.EnglishPdf = pdf;

        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        TempData["Msg"] = $"Added \"{book.Title}\".";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var book = await _db.Books.FindAsync(id);
        if (book == null) return NotFound();
        await LoadNationalitiesAsync();
        return View("Form", new BookForm
        {
            Id = book.Id,
            Title = book.Title,
            Author = book.Author,
            Nationality = book.Nationality,
            Summary = book.Summary,
            Year = book.Year,
            Language = book.Language,
            Hidden = book.Hidden,
            CurrentCoverUrl = book.CoverUrl,
            CurrentPdf = book.EnglishPdf,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes), RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Edit(BookForm form)
    {
        var book = await _db.Books.FindAsync(form.Id);
        if (book == null) return NotFound();

        ValidateFiles(form);
        if (!ModelState.IsValid)
        {
            form.CurrentCoverUrl = book.CoverUrl;
            form.CurrentPdf = book.EnglishPdf;
            await LoadNationalitiesAsync();
            return View("Form", form);
        }

        // Upload new files first: if that fails, nothing about the book has changed yet.
        var (cover, pdf, error) = await UploadAsync(form);
        if (error != null)
        {
            ModelState.AddModelError("", error);
            form.CurrentCoverUrl = book.CoverUrl;
            form.CurrentPdf = book.EnglishPdf;
            await LoadNationalitiesAsync();
            return View("Form", form);
        }

        Apply(form, book);
        var oldFiles = new List<string?>();
        if (form.Cover != null || form.RemoveCover)
        {
            oldFiles.Add(book.CoverUrl);
            book.CoverUrl = cover;
        }
        if (form.Pdf != null || form.RemovePdf)
        {
            oldFiles.Add(book.EnglishPdf);
            book.EnglishPdf = pdf;
        }

        await _db.SaveChangesAsync();
        // Old files are removed only after the book points at the new ones.
        foreach (var url in oldFiles) await _storage.DeleteAsync(url);
        TempData["Msg"] = $"Saved \"{book.Title}\".";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var book = await _db.Books.FindAsync(id);
        if (book == null) return NotFound();
        ViewBag.TranslationCount = await _db.Translations.CountAsync(t => t.BookId == id);
        return View(book);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var book = await _db.Books.FindAsync(id);
        if (book == null) return NotFound();

        // Translations are removed with the book (cascade delete on BookId).
        _db.Books.Remove(book);
        await _db.SaveChangesAsync();
        await _storage.DeleteAsync(book.CoverUrl);
        await _storage.DeleteAsync(book.EnglishPdf);

        TempData["Msg"] = $"Deleted \"{book.Title}\".";
        return RedirectToAction(nameof(Index));
    }

    static void Apply(BookForm form, Book book)
    {
        book.Title = form.Title.Trim();
        book.Author = form.Author.Trim();
        book.Nationality = form.Nationality.Trim();
        book.Summary = form.Summary?.Trim() ?? "";
        book.Year = form.Year;
        book.Language = string.IsNullOrWhiteSpace(form.Language) ? null : form.Language.Trim();
        book.Hidden = form.Hidden;
    }

    void ValidateFiles(BookForm form)
    {
        if (form.Cover != null)
        {
            var ext = Path.GetExtension(form.Cover.FileName).ToLowerInvariant();
            if (!CoverExtensions.Contains(ext) || !form.Cover.ContentType.StartsWith("image/"))
                ModelState.AddModelError(nameof(form.Cover), "The cover must be a .png, .jpg or .webp image.");
            else if (form.Cover.Length == 0)
                ModelState.AddModelError(nameof(form.Cover), "The cover file is empty.");
        }

        if (form.Pdf != null)
        {
            var ext = Path.GetExtension(form.Pdf.FileName).ToLowerInvariant();
            if (ext != ".pdf" || !StartsWithPdfHeader(form.Pdf))
                ModelState.AddModelError(nameof(form.Pdf), "The English book must be a PDF file.");
        }
    }

    static bool StartsWithPdfHeader(IFormFile f)
    {
        using var s = f.OpenReadStream();
        var header = new byte[4];
        return s.Read(header, 0, 4) == 4 && header[0] == '%' && header[1] == 'P' && header[2] == 'D' && header[3] == 'F';
    }

    // Uploads the form's new cover and PDF, if any. If the second upload fails, the first is
    // removed again so no orphan files are left behind.
    async Task<(string? Cover, string? Pdf, string? Error)> UploadAsync(BookForm form)
    {
        string? cover = null, pdf = null;
        try
        {
            if (form.Cover != null) cover = await _storage.SaveAsync(form.Cover, "covers");
            if (form.Pdf != null) pdf = await _storage.SaveAsync(form.Pdf, "pdfs");
            return (cover, pdf, null);
        }
        catch (FileStorageException ex)
        {
            await _storage.DeleteAsync(cover);
            return (null, null, ex.Message);
        }
    }

    async Task LoadNationalitiesAsync() =>
        ViewBag.Nationalities = await _db.Books.Select(b => b.Nationality).Distinct().OrderBy(n => n).ToListAsync();
}
