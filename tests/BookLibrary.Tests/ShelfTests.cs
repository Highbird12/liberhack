using System.Net;
using BookLibrary.Tests.Infrastructure;

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

    [Theory]
    [InlineData("/js/shelf.js", "The King in Yellow")]
    [InlineData("/css/shelf.css", "@font-face")]
    [InlineData("/shelf/king-in-yellow.png", null)]
    [InlineData("/shelf/fonts/newsreader.woff2", null)]
    [InlineData("/shelf/fonts/newsreader-italic.woff2", null)]
    [InlineData("/shelf/fonts/inter-tight.woff2", null)]
    public async Task Bookshelf_files_are_served(string url, string? contains)
    {
        var response = await app.NewBrowser().Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (contains != null)
            Assert.Contains(contains, await response.Content.ReadAsStringAsync());
    }
}
