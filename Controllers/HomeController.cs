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

    // q searches title and author; nationality filters to one group.
    // Filtering runs in memory so matching ignores case for Cyrillic too
    // (database LIKE matching depends on the database's collation). Fine for a library of this size.
    public async Task<IActionResult> Index(string? q, string? nationality)
    {
        var all = await _db.Books.OrderBy(b => b.Title).ToListAsync();
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

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
