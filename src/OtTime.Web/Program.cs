using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;

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

    var entry = store.CreateEntry(UserId(user), request);
    return Results.Ok(entry);
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

public sealed class TimeEntryRequest
{
    public DateOnly WorkDate { get; init; }
    public int DurationMinutes { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string? SourceName { get; init; }
    public string? TicketReference { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool AfterHours { get; init; }
    public IReadOnlyCollection<string>? Tags { get; init; }

    public bool IsValid() =>
        WorkDate != default &&
        DurationMinutes is >= 1 and <= 1440 &&
        !string.IsNullOrWhiteSpace(CategoryName) &&
        !string.IsNullOrWhiteSpace(Description);
}

public sealed class CategoryRequest
{
    public string Name { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
    public bool Enabled { get; init; } = true;
}

public sealed class ReportFilter
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public IReadOnlyCollection<string>? UserIds { get; init; }
    public IReadOnlyCollection<string>? CategoryNames { get; init; }
    public IReadOnlyCollection<string>? SourceNames { get; init; }
    public bool? AfterHours { get; init; }
}

public sealed class ReportScheduleRequest
{
    public string Name { get; init; } = string.Empty;
    public string Timezone { get; init; } = "UTC";
    public string Recurrence { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string DestinationType { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public object? Report { get; init; }
}

public sealed record TimeEntryView(
    Guid Id,
    string OwnerId,
    DateOnly WorkDate,
    int DurationMinutes,
    string CategoryName,
    string? SourceName,
    string? TicketReference,
    string Description,
    bool AfterHours,
    IReadOnlyList<string> Tags);

public sealed class CategoryView
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool Enabled { get; set; }
}

public sealed record AuditView(
    Guid Id,
    string ActorId,
    string EntityType,
    string EntityId,
    string Action,
    DateTimeOffset OccurredUtc);

public sealed record ReportScheduleView(
    Guid Id,
    string Name,
    string Timezone,
    string Recurrence,
    bool Enabled,
    string DestinationType,
    string Destination);

public sealed record ScheduleExecutionView(Guid Id, DateTimeOffset StartedUtc, string Status);

public sealed class MemoryStore
{
    private readonly object _gate = new();
    private readonly List<TimeEntryView> _entries = [];
    private readonly List<CategoryView> _categories =
    [
        new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Engineering Problem", DisplayOrder = 10, Enabled = true },
        new() { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Documentation", DisplayOrder = 20, Enabled = true },
        new() { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "After Hours Support", DisplayOrder = 30, Enabled = true }
    ];
    private readonly List<AuditView> _audit = [];
    private readonly Dictionary<Guid, ReportScheduleView> _schedules = [];
    private readonly Dictionary<Guid, List<ScheduleExecutionView>> _executions = [];

    public IReadOnlyList<TimeEntryView> ListEntries(string ownerId)
    {
        lock (_gate)
            return _entries.Where(entry => entry.OwnerId == ownerId).ToArray();
    }

    public TimeEntryView? GetEntry(string ownerId, bool isAdministrator, Guid id)
    {
        lock (_gate)
            return _entries.SingleOrDefault(entry => entry.Id == id && (entry.OwnerId == ownerId || isAdministrator));
    }

    public TimeEntryView CreateEntry(string actorId, TimeEntryRequest request)
    {
        lock (_gate)
        {
            var entry = ToEntry(Guid.NewGuid(), actorId, request);
            _entries.Add(entry);
            AddAudit(actorId, "TimeEntry", entry.Id.ToString(), "Created");
            return entry;
        }
    }

    public TimeEntryView? UpdateEntry(string actorId, bool isAdministrator, Guid id, TimeEntryRequest request)
    {
        lock (_gate)
        {
            var index = _entries.FindIndex(entry => entry.Id == id && (entry.OwnerId == actorId || isAdministrator));
            if (index < 0)
                return null;
            var existing = _entries[index];
            var updated = ToEntry(id, existing.OwnerId, request);
            _entries[index] = updated;
            AddAudit(actorId, "TimeEntry", id.ToString(), "Updated");
            return updated;
        }
    }

    public bool DeleteEntry(string actorId, bool isAdministrator, Guid id)
    {
        lock (_gate)
        {
            var index = _entries.FindIndex(entry => entry.Id == id && (entry.OwnerId == actorId || isAdministrator));
            if (index < 0)
                return false;
            _entries.RemoveAt(index);
            AddAudit(actorId, "TimeEntry", id.ToString(), "Deleted");
            return true;
        }
    }

    public IReadOnlyList<CategoryView> ListCategories(bool includeDisabled)
    {
        lock (_gate)
            return _categories
                .Where(category => includeDisabled || category.Enabled)
                .OrderBy(category => category.DisplayOrder)
                .ThenBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
                .Select(Clone)
                .ToArray();
    }

    public CategoryView CreateCategory(string actorId, CategoryRequest request)
    {
        lock (_gate)
        {
            var category = new CategoryView
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                DisplayOrder = request.DisplayOrder,
                Enabled = request.Enabled
            };
            _categories.Add(category);
            AddAudit(actorId, "Category", category.Id.ToString(), "Created");
            return Clone(category);
        }
    }

    public CategoryView? UpdateCategory(string actorId, Guid id, CategoryRequest request)
    {
        lock (_gate)
        {
            var category = _categories.SingleOrDefault(item => item.Id == id);
            if (category is null)
                return null;
            category.Name = request.Name.Trim();
            category.DisplayOrder = request.DisplayOrder;
            category.Enabled = request.Enabled;
            AddAudit(actorId, "Category", id.ToString(), "Updated");
            return Clone(category);
        }
    }

    public bool DeleteCategory(string actorId, Guid id)
    {
        lock (_gate)
        {
            var category = _categories.SingleOrDefault(item => item.Id == id);
            if (category is null)
                return false;
            category.Enabled = false;
            AddAudit(actorId, "Category", id.ToString(), "Disabled");
            return true;
        }
    }

    public IReadOnlyList<TimeEntryView> RunReport(ReportFilter filter)
    {
        lock (_gate)
        {
            IEnumerable<TimeEntryView> query = _entries;
            if (filter.From is { } from)
                query = query.Where(entry => entry.WorkDate >= from);
            if (filter.To is { } to)
                query = query.Where(entry => entry.WorkDate <= to);
            if (filter.UserIds is { Count: > 0 })
                query = query.Where(entry => filter.UserIds.Contains(entry.OwnerId, StringComparer.OrdinalIgnoreCase));
            if (filter.CategoryNames is { Count: > 0 })
                query = query.Where(entry => filter.CategoryNames.Contains(entry.CategoryName, StringComparer.OrdinalIgnoreCase));
            if (filter.SourceNames is { Count: > 0 })
                query = query.Where(entry => entry.SourceName is not null && filter.SourceNames.Contains(entry.SourceName, StringComparer.OrdinalIgnoreCase));
            if (filter.AfterHours is { } afterHours)
                query = query.Where(entry => entry.AfterHours == afterHours);
            return query.OrderBy(entry => entry.WorkDate).ThenBy(entry => entry.OwnerId).ToArray();
        }
    }

    public IReadOnlyList<AuditView> ListAudit(string? entityType, string? entityId)
    {
        lock (_gate)
            return _audit
                .Where(item => string.IsNullOrWhiteSpace(entityType) || string.Equals(item.EntityType, entityType, StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(entityId) || string.Equals(item.EntityId, entityId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.OccurredUtc)
                .ToArray();
    }

    public ReportScheduleView CreateSchedule(string actorId, ReportScheduleRequest request)
    {
        lock (_gate)
        {
            var schedule = new ReportScheduleView(
                Guid.NewGuid(), request.Name.Trim(), request.Timezone.Trim(), request.Recurrence.Trim(), request.Enabled,
                request.DestinationType.Trim(), request.Destination.Trim());
            _schedules[schedule.Id] = schedule;
            _executions[schedule.Id] = [];
            AddAudit(actorId, "ReportSchedule", schedule.Id.ToString(), "Created");
            return schedule;
        }
    }

    public bool RunSchedule(Guid id)
    {
        lock (_gate)
        {
            if (!_schedules.ContainsKey(id))
                return false;
            _executions[id].Add(new ScheduleExecutionView(Guid.NewGuid(), DateTimeOffset.UtcNow, "Succeeded"));
            return true;
        }
    }

    public IReadOnlyList<ScheduleExecutionView>? ListScheduleExecutions(Guid id)
    {
        lock (_gate)
            return _executions.TryGetValue(id, out var executions) ? executions.ToArray() : null;
    }

    private static TimeEntryView ToEntry(Guid id, string ownerId, TimeEntryRequest request) =>
        new(
            id,
            ownerId,
            request.WorkDate,
            request.DurationMinutes,
            request.CategoryName.Trim(),
            string.IsNullOrWhiteSpace(request.SourceName) ? null : request.SourceName.Trim(),
            string.IsNullOrWhiteSpace(request.TicketReference) ? null : request.TicketReference.Trim(),
            request.Description.Trim(),
            request.AfterHours,
            request.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? []);

    private void AddAudit(string actorId, string entityType, string entityId, string action) =>
        _audit.Add(new AuditView(Guid.NewGuid(), actorId, entityType, entityId, action, DateTimeOffset.UtcNow));

    private static CategoryView Clone(CategoryView category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        DisplayOrder = category.DisplayOrder,
        Enabled = category.Enabled
    };
}

public static class CsvWriter
{
    public static string Write(IEnumerable<TimeEntryView> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Work Date,User,Category,Source,Ticket Reference,Description,After Hours,Duration Minutes,Tags");
        foreach (var row in rows)
        {
            var values = new[]
            {
                row.WorkDate.ToString("yyyy-MM-dd"),
                row.OwnerId,
                row.CategoryName,
                row.SourceName ?? string.Empty,
                row.TicketReference ?? string.Empty,
                row.Description,
                row.AfterHours ? "Yes" : "No",
                row.DurationMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Join("; ", row.Tags)
            };
            builder.AppendLine(string.Join(',', values.Select(Escape)));
        }
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
