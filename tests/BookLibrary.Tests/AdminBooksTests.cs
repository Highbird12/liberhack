using System.Net;
using BookLibrary.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookLibrary.Tests;

// Admin book management: access rules, add, edit, delete, uploads and validation.
public class AdminBooksTests(AppFactory app) : IClassFixture<AppFactory>
{
    static Dictionary<string, string> BookFields(string title, string? year = null, string? language = null, bool hidden = false) => new()
    {
        ["Title"] = title, ["Author"] = "Ann Author", ["Nationality"] = "Bulgarian", ["Summary"] = "A summary.",
        ["Year"] = year ?? "", ["Language"] = language ?? "", ["Hidden"] = hidden ? "true" : "false",
    };

    [Fact]
    public async Task Visitors_are_sent_to_log_in_and_normal_users_are_refused()
    {
        using var visitor = await app.NewBrowser().Client.GetAsync("/AdminBooks");
        Assert.Equal(HttpStatusCode.Redirect, visitor.StatusCode);
        Assert.Contains("/Identity/Account/Login", visitor.Headers.Location!.ToString());

        var user = await app.NewUserAsync("not-admin@test.local");
        using var res = await user.Client.GetAsync("/AdminBooks");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Contains("AccessDenied", res.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Admin_adds_a_book_with_cover_pdf_year_and_language()
    {
        var admin = await app.AdminAsync();
        using var res = await admin.PostFormAsync("/AdminBooks/Create", "/AdminBooks/Create",
            BookFields("Added With Files", "1925", "Bulgarian"),
            ("Cover", "cover.png", "image/png", TestFiles.Png), ("Pdf", "book.pdf", "application/pdf", TestFiles.Pdf));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var book = await app.DbAsync(db => db.Books.SingleAsync(b => b.Title == "Added With Files"));
        Assert.Equal(1925, book.Year);
        Assert.Equal("Bulgarian", book.Language);
        Assert.False(book.Hidden);
        Assert.True(app.Storage.Files.ContainsKey(book.CoverUrl!));
        Assert.True(app.Storage.Files.ContainsKey(book.EnglishPdf!));
        Assert.Contains("Added With Files", await app.NewBrowser().PageAsync("/Library"));
    }

    [Fact]
    public async Task Invalid_input_is_refused_with_a_message()
    {
        var admin = await app.AdminAsync();

        using (var empty = await admin.PostFormAsync("/AdminBooks/Create", "/AdminBooks/Create", BookFields("")))
            Assert.Contains("The Title field is required", await empty.Content.ReadAsStringAsync());

        using (var year = await admin.PostFormAsync("/AdminBooks/Create", "/AdminBooks/Create", BookFields("Bad Year", year: "3000")))
            Assert.Contains("Enter a year between 1 and 2100", await year.Content.ReadAsStringAsync());

        using (var pdf = await admin.PostFormAsync("/AdminBooks/Create", "/AdminBooks/Create", BookFields("Bad Pdf"),
                   ("Pdf", "fake.pdf", "application/pdf", TestFiles.NotAPdf)))
            Assert.Contains("must be a PDF file", await pdf.Content.ReadAsStringAsync());

        using (var svg = await admin.PostFormAsync("/AdminBooks/Create", "/AdminBooks/Create", BookFields("Bad Cover"),
                   ("Cover", "cover.svg", "image/svg+xml", "<svg/>"u8.ToArray())))
            Assert.Contains("must be a .png, .jpg or .webp", await svg.Content.ReadAsStringAsync());

        Assert.False(await app.DbAsync(db => db.Books.AnyAsync(b => b.Title == "Bad Year" || b.Title == "Bad Pdf" || b.Title == "Bad Cover")));
    }

    [Fact]
    public async Task Editing_replaces_the_cover_hides_the_book_and_removes_the_old_file()
    {
        var admin = await app.AdminAsync();
        await admin.PostFormAsync("/AdminBooks/Create", "/AdminBooks/Create", BookFields("To Edit"),
            ("Cover", "a.png", "image/png", TestFiles.Png));
        var before = await app.DbAsync(db => db.Books.SingleAsync(b => b.Title == "To Edit"));

        var fields = BookFields("Edited Title", "1999", "English", hidden: true);
        fields["Id"] = before.Id.ToString();
        using var res = await admin.PostFormAsync($"/AdminBooks/Edit/{before.Id}", "/AdminBooks/Edit", fields,
            ("Cover", "b.png", "image/png", TestFiles.Png));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var after = await app.DbAsync(db => db.Books.SingleAsync(b => b.Id == before.Id));
        Assert.Equal("Edited Title", after.Title);
        Assert.Equal(1999, after.Year);
        Assert.True(after.Hidden);
        Assert.NotEqual(before.CoverUrl, after.CoverUrl);
        Assert.False(app.Storage.Files.ContainsKey(before.CoverUrl!));
        Assert.True(app.Storage.Files.ContainsKey(after.CoverUrl!));
        Assert.DoesNotContain("Edited Title", await app.NewBrowser().PageAsync("/Library"));
    }

    [Fact]
    public async Task Deleting_a_book_removes_its_translations_and_files()
    {
        var admin = await app.AdminAsync();
        await admin.PostFormAsync("/AdminBooks/Create", "/AdminBooks/Create", BookFields("To Delete"),
            ("Cover", "c.png", "image/png", TestFiles.Png));
        var book = await app.DbAsync(db => db.Books.SingleAsync(b => b.Title == "To Delete"));

        var user = await app.NewUserAsync("translator-del@test.local");
        await user.PostFormAsync($"/Books/Submit/{book.Id}", $"/Books/Submit/{book.Id}", new(),
            ("file", "t.docx", TestFiles.DocxType, TestFiles.Docx("Превод")));
        Assert.Equal(1, await app.DbAsync(db => db.Translations.CountAsync(t => t.BookId == book.Id)));

        Assert.Contains("1 translation", await admin.PageAsync($"/AdminBooks/Delete/{book.Id}"));
        using var res = await admin.PostFormAsync($"/AdminBooks/Delete/{book.Id}", $"/AdminBooks/Delete/{book.Id}", new());
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        Assert.False(await app.DbAsync(db => db.Books.AnyAsync(b => b.Id == book.Id)));
        Assert.Equal(0, await app.DbAsync(db => db.Translations.CountAsync(t => t.BookId == book.Id)));
        Assert.False(app.Storage.Files.ContainsKey(book.CoverUrl!));
    }
}
