using System.Text.Json;
using System.Diagnostics;
using BookLibrary.Data;
using BookLibrary.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookLibrary.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    public HomeController(ApplicationDbContext db) => _db = db;

    // The landing page at "/" (its own full-screen look: Views/Home/Index.cshtml and wwwroot/landing/).
    // Searches used to live at "/", so old links such as /?q=tolstoy are sent on to the library.
    public IActionResult Index(string? q, string? nationality) =>
        q != null || nationality != null ? RedirectToAction(nameof(Library), new { q, nationality }) : View();

    // The library at /Library. q searches title and author; nationality filters to one group.
    // Filtering runs in memory so matching ignores case for Cyrillic too
    // (database LIKE matching depends on the database's collation). Fine for a library of this size.
    [HttpGet("/Library")]
    public async Task<IActionResult> Library(string? q, string? nationality)
    {
        var all = await _db.Books.Where(b => !b.Hidden).OrderBy(b => b.Title).ToListAsync();
        q = q?.Trim();
        nationality = nationality?.Trim();

        ViewBag.Nationalities = all.Select(b => b.Nationality).Distinct().OrderBy(n => n).ToList();
        ViewBag.Q = q;
        ViewBag.Nationality = nationality;

        IEnumerable<Book> books = all;
        if (!string.IsNullOrEmpty(q))
            books = books.Where(b => b.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || b.Author.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(nationality))
            books = books.Where(b => string.Equals(b.Nationality, nationality, StringComparison.OrdinalIgnoreCase));

        return View(books.GroupBy(b => b.Nationality).OrderBy(g => g.Key).ToList());
    }

    // The 3D bookshelf (wwwroot/js/shelf.js). Every visible book that has a cover stands on it automatically, next to
    // the books built into the script; the script makes each one's spine from its cover. Oldest first.
    public async Task<IActionResult> Shelf()
    {
        var books = await _db.Books
            .Where(b => !b.Hidden && b.CoverUrl != null && b.CoverUrl != "")
            .OrderBy(b => b.Year == null).ThenBy(b => b.Year).ThenBy(b => b.Title)
            .Select(b => new { b.Id, b.Title, b.Author, b.Year, b.Nationality, b.Language, b.Summary, Cover = b.CoverUrl })
            .ToListAsync();

        // Written into a <script type="application/json"> tag. The default encoder escapes < > & as \u003C and so on,
        // so a title such as "</script>" cannot end the tag early.
        ViewData["ShelfBooks"] = JsonSerializer.Serialize(
            books.Select(b => new { b.Id, b.Title, b.Author, b.Year, b.Nationality, b.Language, b.Summary, b.Cover,
                                    Url = Url.Action("Details", "Books", new { id = b.Id }) }),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return View();
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
