using BookLibrary.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseNpgsql(DatabaseConfig.GetConnectionString(builder.Configuration)));
builder.Services.AddDefaultIdentity<IdentityUser>(o => o.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Google login only turns on when keys are set (see user-secrets)
var auth = builder.Services.AddAuthentication();
var gid = builder.Configuration["Google:ClientId"];
var gsecret = builder.Configuration["Google:ClientSecret"];
if (!string.IsNullOrEmpty(gid) && !string.IsNullOrEmpty(gsecret))
    auth.AddGoogle(o => { o.ClientId = gid; o.ClientSecret = gsecret; });

builder.Services.AddControllersWithViews();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await SeedData.InitAsync(scope.ServiceProvider);

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
