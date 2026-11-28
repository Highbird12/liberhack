using BookLibrary.Data;
using BookLibrary.Models;
using BookLibrary.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace BookLibrary.Tests.Infrastructure;

// Starts the real app in memory against a brand-new, empty PostgreSQL database that is deleted
// afterwards, so tests never touch real data. Uploads go to an in-memory FakeFileStorage.
//
// The PostgreSQL server comes from the TEST_DATABASE variable (a connection string without a
// database name), or localhost:5432 with user and password "postgres" (what the GitHub workflow uses).
public class AppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin123!x";

    readonly string _dbName = "booklibrary_test_" + Guid.NewGuid().ToString("N");
    public FakeFileStorage Storage { get; } = new();

    static string ServerConnectionString =>
        Environment.GetEnvironmentVariable("TEST_DATABASE") is { Length: > 0 } cs
            ? cs
            : "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    string DatabaseConnectionString =>
        new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = _dbName, Pooling = false }.ConnectionString;

    public async Task InitializeAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(ServerConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_dbName}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException)
        {
            throw new InvalidOperationException(
                "The tests need a PostgreSQL server. Start one on localhost:5432 (user and password 'postgres'), " +
                "or set TEST_DATABASE to a connection string for your server. See the README.", ex);
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(ServerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development", so a developer's own user secrets are never used by the tests.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", DatabaseConnectionString);
        builder.UseSetting("Admin:Email", AdminEmail);
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage>(Storage);
        });
    }

    public Browser NewBrowser() => new(this);

    public async Task<Browser> AdminAsync()
    {
        var b = NewBrowser();
        await b.LoginAsync(AdminEmail, AdminPassword);
        return b;
    }

    // Registers a new user (which also logs them in) and returns their browser.
    public async Task<Browser> NewUserAsync(string email, string password = "User123!x")
    {
        var b = NewBrowser();
        var res = await b.RegisterAsync(email, password);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, res.StatusCode);
        return b;
    }

    // Direct database access for setting up and checking test data.
    public async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public Task<Book> AddBookAsync(string title, bool hidden = false, string nationality = "Testland") =>
        DbAsync(async db =>
        {
            var book = new Book { Title = title, Author = "Test Author", Nationality = nationality, Summary = "A test book.", Hidden = hidden };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            return book;
        });
}
