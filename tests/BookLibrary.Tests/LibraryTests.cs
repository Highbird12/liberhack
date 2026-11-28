using System.Net;
using BookLibrary.Tests.Infrastructure;

namespace BookLibrary.Tests;

// Home page, search, book page and hidden books.
public class LibraryTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Home_lists_the_sample_books_with_author_and_year()
    {
        var text = await app.NewBrowser().TextAsync("/Library");
        foreach (var title in new[] { "Pride and Prejudice", "Frankenstein", "Dracula", "Crime and Punishment",
                                      "Anna Karenina", "Les Misérables", "Around the World in 80 Days", "Moby-Dick" })
            Assert.Contains(title, text);
        Assert.Contains("Jane Austen, 1813", text);
    }

    [Fact]
    public async Task Search_by_title_or_author_ignores_case()
    {
        var b = app.NewBrowser();
        var byTitle = await b.PageAsync("/Library?q=PRIDE");
        Assert.Contains("1 book found.", byTitle);
        Assert.Contains("Pride and Prejudice", byTitle);

        var byAuthor = await b.PageAsync("/Library?q=tolstoy");
        Assert.Contains("Anna Karenina", byAuthor);
        Assert.DoesNotContain("Moby-Dick", byAuthor);
    }

    [Fact]
    public async Task Search_ignores_case_in_cyrillic()
    {
        await app.AddBookAsync("Под игото", nationality: "Bulgarian");
        var text = await app.NewBrowser().TextAsync("/Library?q=" + Uri.EscapeDataString("ПОД"));
        Assert.Contains("Под игото", text);
    }

    [Fact]
    public async Task Nationality_filter_shows_only_that_group()
    {
        var html = await app.NewBrowser().PageAsync("/Library?nationality=Russian");
        Assert.Contains("Crime and Punishment", html);
        Assert.DoesNotContain("Moby-Dick", html);
    }

    [Fact]
    public async Task Book_page_shows_year_and_original_language()
    {
        var id = await app.DbAsync(db => Task.FromResult(db.Books.Single(x => x.Title == "Crime and Punishment").Id));
        var html = await app.NewBrowser().PageAsync($"/Books/Details/{id}");
        Assert.Contains("1866", html);
        Assert.Contains("Original language: Russian", html);
    }

    [Fact]
    public async Task Hidden_book_is_invisible_to_visitors_and_users_but_open_to_admins()
    {
        var book = await app.AddBookAsync("Secret Draft Book", hidden: true);

        var visitor = app.NewBrowser();
        Assert.DoesNotContain("Secret Draft Book", await visitor.PageAsync("/Library"));
        Assert.Contains("No books match your search.", await visitor.PageAsync("/Library?q=Secret%20Draft"));
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/Books/Details/{book.Id}")).Status);

        var user = await app.NewUserAsync("hidden-reader@test.local");
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/Books/Details/{book.Id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/Books/Submit/{book.Id}")).Status);

        var admin = await app.AdminAsync();
        var adminView = await admin.PageAsync($"/Books/Details/{book.Id}");
        Assert.Contains("This book is hidden", adminView);
    }

    [Fact]
    public async Task Unknown_book_returns_not_found() =>
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewBrowser().GetAsync("/Books/Details/999999")).Status);
}
