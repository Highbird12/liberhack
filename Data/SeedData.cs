using BookLibrary.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookLibrary.Data;

public static class SeedData
{
    static Book B(string t, string a, string n, string s, string f) =>
        new() { Title = t, Author = a, Nationality = n, Summary = s, EnglishPdf = $"/pdfs/{f}.pdf", CoverUrl = $"/covers/{f}.svg" };

    public static async Task InitAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        await EnsureRowLevelSecurityAsync(sp, db);

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

    // Supabase exposes tables in the public schema through its Data API, which anyone holding the
    // project's public key can call. Turning on row-level security with no policies blocks that
    // access completely. This app connects as the table owner, which is not affected by it.
    // Runs on every start so tables added by future migrations are covered too, and only touches
    // tables that do not have it yet. A failure is logged, not fatal, so a database where this
    // user is not allowed to change the tables still starts.
    static async Task EnsureRowLevelSecurityAsync(IServiceProvider sp, ApplicationDbContext db)
    {
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");
        try
        {
            var unsecured = await db.Database
                .SqlQueryRaw<string>("SELECT tablename AS \"Value\" FROM pg_tables WHERE schemaname = 'public' AND NOT rowsecurity")
                .ToListAsync();
            if (unsecured.Count == 0) return;

            await db.Database.ExecuteSqlRawAsync(
                "DO $$ DECLARE r record; BEGIN " +
                "FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND NOT rowsecurity LOOP " +
                "EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', r.tablename); " +
                "END LOOP; END $$;");
            log.LogInformation("Turned on row-level security for: {Tables}", string.Join(", ", unsecured));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not turn on row-level security. If this database is exposed through an API " +
                               "(for example Supabase's Data API), enable it by hand; see the README.");
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