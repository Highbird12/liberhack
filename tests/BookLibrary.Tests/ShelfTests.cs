using System.Net;
using System.Text.Json;
using BookLibrary.Models;
using BookLibrary.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookLibrary.Tests;

// The 3D bookshelf page and the button that opens it.
public class ShelfTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Landing_page_and_library_link_to_the_bookshelf_for_visitors()
    {
        var b = app.NewBrowser();
        Assert.Contains("href=\"/Home/Shelf\"", await b.PageAsync("/"));
        var library = await b.PageAsync("/Library");
        Assert.Contains("href=\"/Home/Shelf\"", library);
        Assert.Contains("View the bookshelf", library);
    }

    [Fact]
    public async Task Bookshelf_links_back_to_the_library()
    {
        var html = await app.NewBrowser().PageAsync("/Home/Shelf");
        Assert.Contains("href=\"/Library\"", html);
    }

    [Fact]
    public async Task Bookshelf_page_opens_without_signing_in_and_loads_its_script()
    {
        var (status, html) = await app.NewBrowser().GetAsync("/Home/Shelf");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("aria-label=\"Bookshelf\"", html);
        Assert.Contains("/js/shelf.js", html);
        Assert.Contains("/css/shelf.css", html);
        Assert.DoesNotContain("navbar", html); // full-screen page, not the site layout
    }

    [Fact]
    public async Task Bookshelf_uses_the_home_page_look()
    {
        var html = await app.NewBrowser().PageAsync("/Home/Shelf");
        Assert.Contains("hf_20260809_012548_ef22562c-c0ae-4816-ad9d-f8922af4e6a7.mp4", html); // same background video
        Assert.Contains("/landing/styles.css", html);                                            // same header and fonts
        Assert.Contains("class=\"vh-nav\"", html);
        Assert.Contains("class=\"active\" aria-current=\"page\" href=\"/Home/Shelf\"", html); // Bookshelf is the active link
        Assert.Contains("/landing/main.js", html);                                               // phone menu
        Assert.DoesNotContain("class=\"cursor\"", html);                                          // normal mouse arrow, no custom ring
    }

    // The library's books the page hands to shelf.js (the <script id="shelf-books"> JSON)
    static List<JsonElement> ShelfBooks(string html) =>
        JsonDocument.Parse(Browser.Parse(html).QuerySelector("script#shelf-books")!.TextContent)
            .RootElement.EnumerateArray().ToList();

    static JsonElement? Find(List<JsonElement> books, string title) =>
        books.Where(b => b.GetProperty("title").GetString() == title).Select(b => (JsonElement?)b).FirstOrDefault();

    [Fact]
    public async Task Library_books_with_a_cover_stand_on_the_shelf_with_a_link_to_their_page()
    {
        var books = ShelfBooks(await app.NewBrowser().PageAsync("/Home/Shelf"));
        var pride = Find(books, "Pride and Prejudice");
        Assert.NotNull(pride);
        var id = await app.DbAsync(db => db.Books.Where(b => b.Title == "Pride and Prejudice").Select(b => b.Id).SingleAsync());
        Assert.Equal("/covers/pride.svg", pride!.Value.GetProperty("cover").GetString());
        Assert.Equal($"/Books/Details/{id}", pride.Value.GetProperty("url").GetString());
        Assert.Equal(1813, pride.Value.GetProperty("year").GetInt32());
        Assert.Equal("Jane Austen", pride.Value.GetProperty("author").GetString());
    }

    [Fact]
    public async Task Shelf_is_in_publication_order()
    {
        var years = ShelfBooks(await app.NewBrowser().PageAsync("/Home/Shelf"))
            .Where(b => b.GetProperty("year").ValueKind == JsonValueKind.Number)
            .Select(b => b.GetProperty("year").GetInt32()).ToList();
        Assert.True(years.Count >= 2);
        Assert.Equal(years.OrderBy(y => y), years);
    }

    [Fact]
    public async Task Hidden_books_and_books_without_a_cover_are_not_on_the_shelf()
    {
        await app.DbAsync(async db =>
        {
            db.Books.Add(new Book { Title = "Hidden Shelf Book", Author = "A", Nationality = "X", Summary = "s", CoverUrl = "/covers/moby.svg", Hidden = true });
            await db.SaveChangesAsync();
            return 0;
        });
        await app.AddBookAsync("No Cover Shelf Book");

        var books = ShelfBooks(await app.NewBrowser().PageAsync("/Home/Shelf"));
        Assert.Null(Find(books, "Hidden Shelf Book"));
        Assert.Null(Find(books, "No Cover Shelf Book"));
    }

    [Fact]
    public async Task A_book_an_admin_adds_with_a_cover_appears_on_the_shelf_automatically()
    {
        var admin = await app.AdminAsync();
        using var res = await admin.PostFormAsync("/AdminBooks/Create", "/AdminBooks/Create",
            new()
            {
                ["Title"] = "Freshly Uploaded", ["Author"] = "New Author", ["Nationality"] = "Bulgarian", ["Summary"] = "Just added.",
                ["Year"] = "1950", ["Language"] = "Bulgarian", ["Hidden"] = "false",
            },
            ("Cover", "cover.png", "image/png", TestFiles.Png));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var cover = await app.DbAsync(db => db.Books.Where(b => b.Title == "Freshly Uploaded").Select(b => b.CoverUrl).SingleAsync());
        var onShelf = Find(ShelfBooks(await app.NewBrowser().PageAsync("/Home/Shelf")), "Freshly Uploaded");
        Assert.NotNull(onShelf);
        Assert.Equal(cover, onShelf!.Value.GetProperty("cover").GetString());   // the uploaded image is its cover
        Assert.Equal("Bulgarian", onShelf.Value.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Shelf_data_cannot_break_out_of_its_script_tag()
    {
        const string evil = "</script><script>alert(1)</script>";
        await app.DbAsync(async db =>
        {
            db.Books.Add(new Book { Title = evil, Author = "<b>x</b>", Nationality = "X", Summary = "&", CoverUrl = "/covers/anna.svg" });
            await db.SaveChangesAsync();
            return 0;
        });

        var html = await app.NewBrowser().PageAsync("/Home/Shelf");
        Assert.DoesNotContain("<script>alert(1)", html);                 // escaped in the page...
        Assert.NotNull(Find(ShelfBooks(html), evil));                       // ...and read back exactly
    }

    [Theory]
    [InlineData("/js/shelf.js", "The King in Yellow")]
    [InlineData("/css/shelf.css", ".shelf-top")]
    [InlineData("/shelf/king-in-yellow.png", null)]
    public async Task Bookshelf_files_are_served(string url, string? contains)
    {
        var response = await app.NewBrowser().Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (contains != null)
            Assert.Contains(contains, await response.Content.ReadAsStringAsync());
    }
}
