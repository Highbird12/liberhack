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
        return tr == null ? NotFound() : View(tr);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(int id, bool approve)
    {
        var tr = await _db.Translations.FindAsync(id);
        if (tr == null) return NotFound();
        tr.Status = approve ? TranslationStatus.Approved : TranslationStatus.Rejected;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
