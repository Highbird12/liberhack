using BookLibrary.Models;
using Microsoft.AspNetCore.Identity;

namespace BookLibrary.Data;

public static class SeedData
{
    static Book B(string t, string a, string n, string s, string f) =>
        new() { Title = t, Author = a, Nationality = n, Summary = s, EnglishPdf = $"/pdfs/{f}.pdf", CoverUrl = $"/covers/{f}.svg" };

    public static async Task InitAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new IdentityRole("Admin"));

        // Your admin account
        var adminEmail = "mimimam2015@email.com";               // <-- CHANGE
        var adminPassword = "12345678M@x";      // <-- CHANGE (upper + lower + digit + symbol)
        var users = sp.GetRequiredService<UserManager<IdentityUser>>();
        if (await users.FindByEmailAsync(adminEmail) == null)
        {
            var admin = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            await users.CreateAsync(admin, adminPassword);
            await users.AddToRoleAsync(admin, "Admin");
        }

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
}