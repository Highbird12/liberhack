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

    // The 3D bookshelf (wwwroot/js/shelf.js). Its book is built into the script, so no database access here.
    public IActionResult Shelf() => View();

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
