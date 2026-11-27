using BookLibrary.Data;
using BookLibrary.Models;
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
    private readonly IWebHostEnvironment _env;

    public AdminBooksController(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
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

        var book = new Book();
        Apply(form, book);
        if (form.Cover != null) book.CoverUrl = await SaveUploadAsync(form.Cover, "covers");
        if (form.Pdf != null) book.EnglishPdf = await SaveUploadAsync(form.Pdf, "pdfs");

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

        Apply(form, book);

        if (form.Cover != null || form.RemoveCover)
        {
            DeleteUpload(book.CoverUrl);
            book.CoverUrl = form.Cover != null ? await SaveUploadAsync(form.Cover, "covers") : null;
        }
        if (form.Pdf != null || form.RemovePdf)
        {
            DeleteUpload(book.EnglishPdf);
            book.EnglishPdf = form.Pdf != null ? await SaveUploadAsync(form.Pdf, "pdfs") : null;
        }

        await _db.SaveChangesAsync();
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
        DeleteUpload(book.CoverUrl);
        DeleteUpload(book.EnglishPdf);

        TempData["Msg"] = $"Deleted \"{book.Title}\".";
        return RedirectToAction(nameof(Index));
    }

    static void Apply(BookForm form, Book book)
    {
        book.Title = form.Title.Trim();
        book.Author = form.Author.Trim();
        book.Nationality = form.Nationality.Trim();
        book.Summary = form.Summary?.Trim() ?? "";
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

    // Uploads go to wwwroot/uploads/<folder>/ under a random name, so user file names
    // never become paths and two uploads can't overwrite each other.
    async Task<string> SaveUploadAsync(IFormFile file, string folder)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var dir = Path.Combine(_env.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{ext}";
        await using (var fs = System.IO.File.Create(Path.Combine(dir, name)))
            await file.CopyToAsync(fs);
        return $"/uploads/{folder}/{name}";
    }

    // Only deletes files this controller uploaded; seeded files in /covers and /pdfs are left alone.
    void DeleteUpload(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("/uploads/")) return;
        var root = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads")) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(_env.WebRootPath, url.TrimStart('/')));
        if (path.StartsWith(root) && System.IO.File.Exists(path)) System.IO.File.Delete(path);
    }

    async Task LoadNationalitiesAsync() =>
        ViewBag.Nationalities = await _db.Books.Select(b => b.Nationality).Distinct().OrderBy(n => n).ToListAsync();
}
