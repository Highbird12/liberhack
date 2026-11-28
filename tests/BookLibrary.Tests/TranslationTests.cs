using System.Net;
using BookLibrary.Models;
using BookLibrary.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookLibrary.Tests;

// Submitting translations, reviewing them, and the "My translations" page.
public class TranslationTests(AppFactory app) : IClassFixture<AppFactory>
{
    // tokenPage: where to pick up the form's security token. The submit page itself redirects away
    // while the user has a translation waiting, so a repeat submission takes it from the book page.
    static Task<HttpResponseMessage> Submit(Browser b, int bookId, string text, string? tokenPage = null) =>
        b.PostFormAsync(tokenPage ?? $"/Books/Submit/{bookId}", $"/Books/Submit/{bookId}", new(),
            ("file", "translation.docx", TestFiles.DocxType, TestFiles.Docx(text)));

    static Task<HttpResponseMessage> Decide(Browser admin, int translationId, bool approve, string reason = "") =>
        admin.PostFormAsync($"/Admin/Review/{translationId}", $"/Admin/Decide/{translationId}",
            new() { ["approve"] = approve ? "true" : "false", ["reason"] = reason });

    Task<Translation> Latest(int bookId) =>
        app.DbAsync(db => db.Translations.Where(t => t.BookId == bookId).OrderByDescending(t => t.Id).FirstAsync());

    [Fact]
    public async Task Only_logged_in_users_can_submit_and_only_admins_can_review()
    {
        var book = await app.AddBookAsync("Access Book");
        using (var anon = await app.NewBrowser().Client.GetAsync($"/Books/Submit/{book.Id}"))
            Assert.Contains("/Identity/Account/Login", anon.Headers.Location!.ToString());

        var user = await app.NewUserAsync("reviewer-wannabe@test.local");
        using var res = await user.Client.GetAsync("/Admin");
        Assert.Contains("AccessDenied", res.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Unreadable_file_is_refused()
    {
        var book = await app.AddBookAsync("Unreadable Book");
        var user = await app.NewUserAsync("unreadable@test.local");
        using var res = await user.PostFormAsync($"/Books/Submit/{book.Id}", $"/Books/Submit/{book.Id}", new(),
            ("file", "notes.txt", "text/plain", "plain text"u8.ToArray()));
        Assert.Contains("Could not read text from that file", await res.Content.ReadAsStringAsync());
        Assert.False(await app.DbAsync(db => db.Translations.AnyAsync(t => t.BookId == book.Id)));
    }

    [Fact]
    public async Task Full_review_flow_with_rejection_resubmission_and_replacement()
    {
        var book = await app.AddBookAsync("Flow Book");
        var alice = await app.NewUserAsync("alice@test.local");
        var bob = await app.NewUserAsync("bob@test.local");
        var admin = await app.AdminAsync();

        // Alice submits; a second submission while the first waits is refused.
        Assert.Equal(HttpStatusCode.Redirect, (await Submit(alice, book.Id, "ALICE ONE")).StatusCode);
        Assert.Contains("waiting for review", await alice.PageAsync($"/Books/Details/{book.Id}"));
        using (var dup = await Submit(alice, book.Id, "ALICE DUPLICATE", tokenPage: $"/Books/Details/{book.Id}"))
            Assert.Equal(HttpStatusCode.Redirect, dup.StatusCode);
        Assert.Contains("already have a translation of this book waiting", await alice.PageAsync($"/Books/Details/{book.Id}"));
        using (var submitPage = await alice.Client.GetAsync($"/Books/Submit/{book.Id}"))
            Assert.Equal(HttpStatusCode.Redirect, submitPage.StatusCode);
        Assert.Equal(1, await app.DbAsync(db => db.Translations.CountAsync(t => t.BookId == book.Id)));

        // Admin rejects with a reason; deciding again changes nothing.
        var first = await Latest(book.Id);
        await Decide(admin, first.Id, approve: false, reason: "Chapter 2 is missing.");
        await Decide(admin, first.Id, approve: true);
        first = await app.DbAsync(db => db.Translations.SingleAsync(t => t.Id == first.Id));
        Assert.Equal(TranslationStatus.Rejected, first.Status);
        Assert.Equal("Chapter 2 is missing.", first.RejectionReason);

        // Alice sees the reason and sends a new version, which is approved.
        var mine = await alice.PageAsync("/Books/Mine");
        Assert.Contains("Rejected", mine);
        Assert.Contains("Chapter 2 is missing.", mine);
        await Submit(alice, book.Id, "ALICE TWO");
        var second = await Latest(book.Id);
        await Decide(admin, second.Id, approve: true);
        Assert.Contains("ALICE TWO", await app.NewBrowser().PageAsync($"/Books/Details/{book.Id}?read=true"));

        // Bob's approved translation replaces Alice's.
        await Submit(bob, book.Id, "BOB VERSION");
        var bobs = await Latest(book.Id);
        Assert.Contains("already has a published Bulgarian translation", await admin.PageAsync($"/Admin/Review/{bobs.Id}"));
        await Decide(admin, bobs.Id, approve: true);

        var page = await app.NewBrowser().PageAsync($"/Books/Details/{book.Id}?read=true");
        Assert.Contains("BOB VERSION", page);
        Assert.DoesNotContain("ALICE TWO", page);
        Assert.Equal(TranslationStatus.Replaced, (await app.DbAsync(db => db.Translations.SingleAsync(t => t.Id == second.Id))).Status);
        Assert.Contains("Replaced by a newer translation", await alice.PageAsync("/Books/Mine"));
        Assert.DoesNotContain("Chapter 2", await bob.PageAsync("/Books/Mine"));
    }
}
