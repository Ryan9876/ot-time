using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using OtTime.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MemoryStore>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/access-denied";
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Reporter", policy => policy.RequireRole("Reporter", "Administrator"));
    options.AddPolicy("Administrator", policy => policy.RequireRole("Administrator"));
});

var app = builder.Build();

var pathBase = builder.Configuration["PathBase"];
if (!string.IsNullOrWhiteSpace(pathBase))
    app.UsePathBase(pathBase.StartsWith('/') ? pathBase : "/" + pathBase);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { name = "OT Time", status = "ready" }));

app.MapGet("/api/time-entries", (ClaimsPrincipal user, MemoryStore store) =>
    Results.Ok(store.ListEntries(UserId(user)))).RequireAuthorization();

app.MapGet("/api/time-entries/{id:guid}", (ClaimsPrincipal user, MemoryStore store, Guid id) =>
{
    var entry = store.GetEntry(UserId(user), IsAdministrator(user), id);
    return entry is null ? Results.NotFound() : Results.Ok(entry);
}).RequireAuthorization();

app.MapPost("/api/time-entries", (ClaimsPrincipal user, MemoryStore store, TimeEntryRequest request) =>
{
    if (!request.IsValid())
        return Results.BadRequest(new { error = "A valid work date, duration, category, and description are required." });

    return Results.Ok(store.CreateEntry(UserId(user), request));
}).RequireAuthorization();

app.MapPut("/api/time-entries/{id:guid}", (ClaimsPrincipal user, MemoryStore store, Guid id, TimeEntryRequest request) =>
{
    if (!request.IsValid())
        return Results.BadRequest(new { error = "A valid work date, duration, category, and description are required." });

    var entry = store.UpdateEntry(UserId(user), IsAdministrator(user), id, request);
    return entry is null ? Results.NotFound() : Results.Ok(entry);
}).RequireAuthorization();

app.MapDelete("/api/time-entries/{id:guid}", (ClaimsPrincipal user, MemoryStore store, Guid id) =>
{
    var deleted = store.DeleteEntry(UserId(user), IsAdministrator(user), id);
    return deleted ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

app.MapGet("/api/lookups/categories", (MemoryStore store) => Results.Ok(store.ListCategories(includeDisabled: false)))
    .RequireAuthorization();

app.MapGet("/api/admin/categories", (MemoryStore store) => Results.Ok(store.ListCategories(includeDisabled: true)))
    .RequireAuthorization("Administrator");

app.MapPost("/api/admin/categories", (ClaimsPrincipal user, MemoryStore store, CategoryRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "Category name is required." });
    return Results.Ok(store.CreateCategory(UserId(user), request));
}).RequireAuthorization("Administrator");

app.MapPut("/api/admin/categories/{id:guid}", (ClaimsPrincipal user, MemoryStore store, Guid id, CategoryRequest request) =>
{
    var category = store.UpdateCategory(UserId(user), id, request);
    return category is null ? Results.NotFound() : Results.Ok(category);
}).RequireAuthorization("Administrator");

app.MapDelete("/api/admin/categories/{id:guid}", (ClaimsPrincipal user, MemoryStore store, Guid id) =>
{
    var deleted = store.DeleteCategory(UserId(user), id);
    return deleted ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization("Administrator");

app.MapPost("/api/reports/run", (MemoryStore store, ReportFilter request) =>
{
    var rows = store.RunReport(request);
    return Results.Ok(new { rows, totalMinutes = rows.Sum(row => row.DurationMinutes) });
}).RequireAuthorization("Reporter");

app.MapPost("/api/reports/export", (MemoryStore store, ReportFilter request) =>
{
    var rows = store.RunReport(request);
    var csv = CsvWriter.Write(rows);
    return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", "ot-time-report.csv");
}).RequireAuthorization("Reporter");

app.MapGet("/api/audit", (MemoryStore store, string? entityType, string? entityId) =>
    Results.Ok(store.ListAudit(entityType, entityId))).RequireAuthorization("Administrator");

app.MapPost("/api/admin/report-schedules", (ClaimsPrincipal user, MemoryStore store, ReportScheduleRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Timezone))
        return Results.BadRequest(new { error = "Schedule name and timezone are required." });
    return Results.Ok(store.CreateSchedule(UserId(user), request));
}).RequireAuthorization("Administrator");

app.MapPost("/api/admin/report-schedules/{id:guid}/run", (MemoryStore store, Guid id) =>
    store.RunSchedule(id) ? Results.Ok() : Results.NotFound()).RequireAuthorization("Administrator");

app.MapGet("/api/admin/report-schedules/{id:guid}/executions", (MemoryStore store, Guid id) =>
{
    var executions = store.ListScheduleExecutions(id);
    return executions is null ? Results.NotFound() : Results.Ok(executions);
}).RequireAuthorization("Administrator");

app.Run();

static string UserId(ClaimsPrincipal user) =>
    user.FindFirst(ClaimTypes.NameIdentifier)?.Value
    ?? throw new InvalidOperationException("Authenticated user has no identifier.");

static bool IsAdministrator(ClaimsPrincipal user) => user.IsInRole("Administrator");

public partial class Program;
