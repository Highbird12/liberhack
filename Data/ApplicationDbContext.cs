using BookLibrary.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BookLibrary.Data;

public class ApplicationDbContext : IdentityDbContext, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Translation> Translations => Set<Translation>();

    // The keys that protect login cookies and form tokens. Kept in the database so that
    // users stay logged in after the app restarts or is redeployed on a host.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}
