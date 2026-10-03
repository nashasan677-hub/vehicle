using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using VehicleFleetMS.Data;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("PORT") is { } port)
{
    builder.WebHost.UseUrls($"http://+:{port}");
}

var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

builder.Services.AddDbContext<FleetDbContext>(options =>
{
    if (databaseUrl is not null)
    {
        options.UseNpgsql(ConnectionStringHelper.FromDatabaseUrl(databaseUrl));
    }
    else
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("FleetDb"));
    }
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/Dashboard";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "VMS.Auth";
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("System Administrator"));
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/ForgotPassword");
    options.Conventions.AllowAnonymousToPage("/ResetPassword");
    options.Conventions.AllowAnonymousToPage("/Logout");
    options.Conventions.AllowAnonymousToPage("/Error");

    options.Conventions.AuthorizePage("/Users", "AdminOnly");
    options.Conventions.AuthorizePage("/Reports", "AdminOnly");
    options.Conventions.AuthorizePage("/Maintenance", "AdminOnly");
    options.Conventions.AuthorizePage("/Tracking", "AdminOnly");
    options.Conventions.AuthorizePage("/Alerts", "AdminOnly");
    options.Conventions.AuthorizePage("/Settings", "AdminOnly");
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
    if (databaseUrl is not null)
    {
        // Postgres (Render) has no SQL-Server-generated migrations to apply; build the schema from the current model instead.
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        db.Database.Migrate();
    }
    await DbSeeder.SeedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

// Serves runtime-uploaded files (e.g. the company logo), which the build-time
// MapStaticAssets manifest below doesn't know about since they didn't exist at build time.
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
