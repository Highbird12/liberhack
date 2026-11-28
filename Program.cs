using BookLibrary.Data;
using BookLibrary.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Hosts such as Render tell the app which port to listen on with the PORT variable.
// Without it (on your own PC) the usual launch settings are used.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port)) builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseNpgsql(DatabaseConfig.GetConnectionString(builder.Configuration)));
// Keep the keys behind login cookies in the database instead of the container's disk, which is
// wiped on every deploy (that would log everyone out each time).
builder.Services.AddDataProtection()
    .SetApplicationName("BookLibrary")
    .PersistKeysToDbContext<ApplicationDbContext>();
builder.Services.AddDefaultIdentity<IdentityUser>(o =>
    {
        o.SignIn.RequireConfirmedAccount = false;
        // After 5 wrong passwords in a row an account is locked for 10 minutes (slows password guessing).
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        o.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager<LockoutSignInManager>();

// Re-check logged-in users every minute, so blocking someone or changing their admin role
// takes effect within a minute instead of the default 30.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

// Google login only turns on when keys are set (see user-secrets)
var auth = builder.Services.AddAuthentication();
var gid = builder.Configuration["Google:ClientId"];
var gsecret = builder.Configuration["Google:ClientSecret"];
if (!string.IsNullOrEmpty(gid) && !string.IsNullOrEmpty(gsecret))
    auth.AddGoogle(o => { o.ClientId = gid; o.ClientSecret = gsecret; });

builder.Services.AddControllersWithViews();

// Admin uploads: Supabase Storage when Storage:Supabase:Url and :Key are set (needed on a host,
// whose disk is wiped on every deploy), otherwise the local wwwroot/uploads folder.
if (SupabaseFileStorage.IsConfigured(builder.Configuration))
    builder.Services.AddHttpClient<IFileStorage, SupabaseFileStorage>(c => c.Timeout = TimeSpan.FromMinutes(2));
else
    builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

var app = builder.Build();
app.Logger.LogInformation("Database: {Target}", DatabaseConfig.Describe(builder.Configuration));

using (var scope = app.Services.CreateScope())
{
    await SeedData.InitAsync(scope.ServiceProvider);

    var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
    app.Logger.LogInformation("Uploads: {Storage}", storage.Description);
    if (storage is SupabaseFileStorage supabase) await supabase.EnsureBucketAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
app.Run();

// Lets the test project start the app (WebApplicationFactory<Program>).
public partial class Program { }
