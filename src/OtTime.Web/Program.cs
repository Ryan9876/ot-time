using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OtTime.Application;
using OtTime.Infrastructure;
using OtTime.Infrastructure.Identity;
using OtTime.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

builder.Configuration.AddEnvironmentVariables();

var connectionString = builder.Configuration.GetConnectionString("OtTime")
    ?? throw new InvalidOperationException("ConnectionStrings:OtTime must be configured.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddDbContext<OtTimeDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 4;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<OtTimeDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "__Host-OtTime";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("Reporter", policy => policy.RequireRole("Reporter", "Administrator"));
    options.AddPolicy("Administrator", policy => policy.RequireRole("Administrator"));
    options.AddPolicy("TimeEntryOwner", policy => policy.RequireAuthenticatedUser());
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-OtTime.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services
    .AddControllersWithViews(options =>
        options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()))
    .AddRazorRuntimeCompilation();

builder.Services.AddRazorPages();
builder.Services.AddHealthChecks().AddDbContextCheck<OtTimeDbContext>("database");

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    options.ForwardLimit = 1;

    foreach (var value in builder.Configuration.GetSection("Hosting:TrustedProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(value, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<OtTimeDbContext>();
    await database.Database.MigrateAsync();

    await BootstrapFirstAdministratorAsync(
        scope.ServiceProvider,
        app.Configuration,
        app.Lifetime.ApplicationStopping);
}

var pathBase = app.Configuration["Hosting:PathBase"];
if (!string.IsNullOrWhiteSpace(pathBase))
{
    if (!pathBase.StartsWith('/'))
    {
        pathBase = "/" + pathBase;
    }

    app.UsePathBase(pathBase.TrimEnd('/'));
}

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var csp = app.Configuration["Security:ContentSecurityPolicy"]
        ?? "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'self'; form-action 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'";

    context.Response.Headers["Content-Security-Policy"] = csp;
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks(app.Configuration["Health:Path"] ?? "/health").AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

await app.RunAsync();

static async Task BootstrapFirstAdministratorAsync(
    IServiceProvider services,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    var email = configuration["Bootstrap:FirstAdmin:Email"];
    var password = configuration["Bootstrap:FirstAdmin:Password"];

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        return;
    }

    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    foreach (var role in new[] { "User", "Reporter", "Administrator" })
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            var result = await roleManager.CreateAsync(new IdentityRole(role));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Unable to create the '{role}' role: {string.Join("; ", result.Errors.Select(error => error.Description))}");
            }
        }
    }

    var administratorRole = await roleManager.FindByNameAsync("Administrator");
    if (administratorRole is null || await userManager.GetUsersInRoleAsync("Administrator") is { Count: > 0 })
    {
        return;
    }

    var user = await userManager.FindByEmailAsync(email);
    if (user is null)
    {
        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Unable to create the first administrator: {string.Join("; ", createResult.Errors.Select(error => error.Description))}");
        }
    }

    var roleResult = await userManager.AddToRoleAsync(user, "Administrator");
    if (!roleResult.Succeeded)
    {
        throw new InvalidOperationException(
            $"Unable to grant administrator access: {string.Join("; ", roleResult.Errors.Select(error => error.Description))}");
    }

    await userManager.AddClaimAsync(user, new System.Security.Claims.Claim("ot:password-change-required", "true"));
}

public partial class Program;