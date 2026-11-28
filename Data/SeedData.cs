using BookLibrary.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BookLibrary.Data;

public static class SeedData
{
    static Book B(string t, string a, string n, string s, string f) =>
        new() { Title = t, Author = a, Nationality = n, Summary = s, EnglishPdf = $"/pdfs/{f}.pdf", CoverUrl = $"/covers/{f}.svg" };

    public static async Task InitAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        await BaselineOldDatabaseAsync(db);
        await db.Database.MigrateAsync();

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new IdentityRole("Admin"));

        await EnsureAdminAsync(sp);

        if (!db.Books.Any())
        {
            db.Books.AddRange(
                B("Pride and Prejudice", "Jane Austen", "English", "Elizabeth Bennet and the proud Mr. Darcy misjudge each other while her family hunts for suitable marriages.", "pride"),
                B("Frankenstein", "Mary Shelley", "English", "A young scientist builds a living creature, then abandons it and faces the consequences.", "frankenstein"),
                B("Dracula", "Bram Stoker", "Irish", "A group of friends hunts the Transylvanian count who is preying on England.", "dracula"),
                B("Crime and Punishment", "Fyodor Dostoevsky", "Russian", "A poor student commits a murder and is consumed by guilt in St. Petersburg.", "crime"),
                B("Anna Karenina", "Leo Tolstoy", "Russian", "A married noblewoman's affair unravels her life while a landowner searches for meaning.", "anna"),
                B("Les Misérables", "Victor Hugo", "French", "Ex-convict Jean Valjean tries to rebuild his life while being pursued by an inspector.", "miserables"),
                B("Around the World in 80 Days", "Jules Verne", "French", "Phileas Fogg bets he can circle the globe in eighty days.", "eighty-days"),
                B("Moby-Dick", "Herman Melville", "American", "Captain Ahab's obsessive hunt for a white whale drives his crew to disaster.", "moby"));
            await db.SaveChangesAsync();
        }
    }

    // Before migrations, the app created app.db with EnsureCreated. Those databases already have
    // every InitialCreate table but no migration history, so Migrate would fail trying to create
    // them again. Record InitialCreate as applied so their data is kept and later migrations run.
    static async Task BaselineOldDatabaseAsync(ApplicationDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            async Task<bool> TableExists(string name)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
                var p = cmd.CreateParameter();
                p.ParameterName = "$name";
                p.Value = name;
                cmd.Parameters.Add(p);
                return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
            }

            if (!await TableExists("Books") || await TableExists("__EFMigrationsHistory")) return;

            var initial = db.Database.GetMigrations().First();
            using var create = conn.CreateCommand();
            create.CommandText =
                "CREATE TABLE \"__EFMigrationsHistory\" (" +
                "\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, " +
                "\"ProductVersion\" TEXT NOT NULL); " +
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($id, $version);";
            var id = create.CreateParameter(); id.ParameterName = "$id"; id.Value = initial; create.Parameters.Add(id);
            var ver = create.CreateParameter(); ver.ParameterName = "$version"; ver.Value = ProductInfo.GetVersion(); create.Parameters.Add(ver);
            await create.ExecuteNonQueryAsync();
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // The admin account comes from configuration, never from code:
    //   Admin:Email and Admin:Password (user secrets in development, environment variables in production).
    // If the email belongs to an existing account, that account is given the Admin role.
    // The password is only used when the account is first created; changing it later has no effect.
    static async Task EnsureAdminAsync(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");
        var email = config["Admin:Email"]?.Trim();
        var password = config["Admin:Password"];

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            log.LogWarning("No admin account configured. Set Admin:Email and Admin:Password (see README) to create one.");
            return;
        }

        var users = sp.GetRequiredService<UserManager<IdentityUser>>();
        var admin = await users.FindByEmailAsync(email);
        if (admin == null)
        {
            admin = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await users.CreateAsync(admin, password);
            if (!result.Succeeded)
            {
                log.LogError("Could not create admin account {Email}: {Errors}",
                    email, string.Join(" ", result.Errors.Select(e => e.Description)));
                return;
            }
            log.LogInformation("Created admin account {Email}.", email);
        }

        if (!await users.IsInRoleAsync(admin, "Admin"))
            await users.AddToRoleAsync(admin, "Admin");
    }
}