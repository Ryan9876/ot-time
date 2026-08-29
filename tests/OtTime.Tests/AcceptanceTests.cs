using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace OtTime.Tests;

public sealed class AcceptanceTests : IClassFixture<AcceptanceTests.AppFactory>
{
    private readonly AppFactory _factory;

    public AcceptanceTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task Authenticated_user_can_create_update_delete_and_view_own_entry()
    {
        using var client = _factory.CreateClient();
        var entry = await Post(client, "alice", "/api/time-entries", new
        {
            workDate = "2025-01-15",
            durationMinutes = 90,
            categoryName = "Engineering Problem",
            sourceName = "Jira",
            ticketReference = "ENG-42",
            description = "Investigated production fault",
            afterHours = false,
            tags = new[] { "incident" }
        });

        var id = Id(entry);
        var history = await GetJson(client, "alice", "/api/time-entries");

        Assert.Contains(history.EnumerateArray(), item => Id(item) == id);

        var updated = await Put(client, "alice", $"/api/time-entries/{id}", new
        {
            workDate = "2025-01-15",
            durationMinutes = 120,
            categoryName = "Engineering Problem",
            sourceName = "Jira",
            ticketReference = "ENG-42",
            description = "Resolved production fault",
            afterHours = false,
            tags = new[] { "incident", "urgent" }
        });

        Assert.Equal(120, updated.GetProperty("durationMinutes").GetInt32());

        var deleted = await Delete(client, "alice", $"/api/time-entries/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        history = await GetJson(client, "alice", "/api/time-entries");
        Assert.DoesNotContain(history.EnumerateArray(), item => Id(item) == id);
    }

    [Fact]
    public async Task User_can_record_non_ticket_and_after_hours_activities()
    {
        using var client = _factory.CreateClient();

        var work = await Post(client, "alice", "/api/time-entries", new
        {
            workDate = "2025-01-16",
            durationMinutes = 30,
            categoryName = "Documentation",
            description = "Updated runbook",
            afterHours = false
        });

        var afterHours = await Post(client, "alice", "/api/time-entries", new
        {
            workDate = "2025-01-16",
            durationMinutes = 60,
            categoryName = "After Hours Support",
            description = "Emergency maintenance",
            afterHours = true
        });

        Assert.False(work.TryGetProperty("ticketReference", out var ticket) && ticket.ValueKind != JsonValueKind.Null);
        Assert.True(afterHours.GetProperty("afterHours").GetBoolean());
    }

    [Fact]
    public async Task Direct_requests_cannot_read_change_delete_or_export_another_users_entries()
    {
        using var client = _factory.CreateClient();
        var entry = await Post(client, "bob", "/api/time-entries", new
        {
            workDate = "2025-01-17",
            durationMinutes = 45,
            categoryName = "Engineering Problem",
            description = "Private investigation",
            afterHours = false
        });

        var id = Id(entry);

        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, "alice", HttpMethod.Get, $"/api/time-entries/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, "alice", HttpMethod.Delete, $"/api/time-entries/{id}")).StatusCode);

        var update = await Send(client, "alice", HttpMethod.Put, $"/api/time-entries/{id}", new
        {
            workDate = "2025-01-17",
            durationMinutes = 1,
            categoryName = "Engineering Problem",
            description = "Attempted overwrite",
            afterHours = false
        });

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var export = await Send(client, "alice", HttpMethod.Post, "/api/reports/export", new
        {
            userIds = new[] { "bob" },
            from = "2025-01-01",
            to = "2025-01-31"
        });

        Assert.True(export.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Administrator_can_manage_lookup_values_and_disabled_values_are_not_offered_for_new_entries()
    {
        using var client = _factory.CreateClient();
        var category = await Post(client, "admin", "/api/admin/categories", new
        {
            name = "Customer Escalation",
            displayOrder = 100,
            enabled = true
        });

        var id = Id(category);
        var renamed = await Put(client, "admin", $"/api/admin/categories/{id}", new
        {
            name = "Escalation",
            displayOrder = 5,
            enabled = false
        });

        Assert.Equal("Escalation", renamed.GetProperty("name").GetString());
        Assert.False(renamed.GetProperty("enabled").GetBoolean());

        var categories = await GetJson(client, "alice", "/api/lookups/categories");
        Assert.DoesNotContain(categories.EnumerateArray(), item => Id(item) == id);

        var adminCategories = await GetJson(client, "admin", "/api/admin/categories");
        Assert.Contains(adminCategories.EnumerateArray(), item =>
            Id(item) == id &&
            item.GetProperty("displayOrder").GetInt32() == 5 &&
            !item.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Administrator_report_filters_totals_and_csv_share_the_same_rows_and_escape_formulas()
    {
        using var client = _factory.CreateClient();

        await Post(client, "alice", "/api/time-entries", new
        {
            workDate = "2025-02-01",
            durationMinutes = 60,
            categoryName = "Engineering Problem",
            sourceName = "Jira",
            ticketReference = "OPS-1",
            description = "=SUM(A1:A2)",
            afterHours = false
        });

        await Post(client, "bob", "/api/time-entries", new
        {
            workDate = "2025-02-01",
            durationMinutes = 90,
            categoryName = "After Hours Support",
            sourceName = "ServiceNow",
            ticketReference = "INC-1",
            description = "After-hours support",
            afterHours = true
        });

        var filter = new
        {
            from = "2025-02-01",
            to = "2025-02-01",
            categoryNames = new[] { "Engineering Problem" },
            sourceNames = new[] { "Jira" },
            afterHours = false
        };

        var report = await Post(client, "admin", "/api/reports/run", filter);
        Assert.Equal(60, report.GetProperty("totalMinutes").GetInt32());
        Assert.Single(report.GetProperty("rows").EnumerateArray());

        var csvResponse = await Send(client, "admin", HttpMethod.Post, "/api/reports/export", filter);
        csvResponse.EnsureSuccessStatusCode();
        var csv = await csvResponse.Content.ReadAsStringAsync();

        Assert.Contains("OPS-1", csv);
        Assert.DoesNotContain("INC-1", csv);
        Assert.Contains("'=SUM(A1:A2)", csv);
        Assert.DoesNotContain("\n=SUM(A1:A2)", csv);
    }

    [Fact]
    public async Task Entry_and_administrative_changes_have_immutable_actor_timestamped_audit_history()
    {
        using var client = _factory.CreateClient();
        var entry = await Post(client, "alice", "/api/time-entries", new
        {
            workDate = "2025-02-02",
            durationMinutes = 15,
            categoryName = "Documentation",
            description = "Initial note",
            afterHours = false
        });

        var id = Id(entry);

        await Put(client, "alice", $"/api/time-entries/{id}", new
        {
            workDate = "2025-02-02",
            durationMinutes = 30,
            categoryName = "Documentation",
            description = "Updated note",
            afterHours = false
        });

        var audit = await GetJson(client, "admin", $"/api/audit?entityType=TimeEntry&entityId={Uri.EscapeDataString(id)}");
        Assert.Contains(audit.EnumerateArray(), item =>
            item.GetProperty("actorId").GetString() == "alice" &&
            item.TryGetProperty("occurredUtc", out _) &&
            item.TryGetProperty("action", out _));

        var category = await Post(client, "admin", "/api/admin/categories", new
        {
            name = "Audit Category",
            displayOrder = 99,
            enabled = true
        });

        var adminAudit = await GetJson(client, "admin", $"/api/audit?entityType=Category&entityId={Uri.EscapeDataString(Id(category))}");
        Assert.Contains(adminAudit.EnumerateArray(), item => item.GetProperty("actorId").GetString() == "admin");
    }

    [Fact]
    public async Task Scheduled_report_is_persisted_with_timezone_and_records_execution_history()
    {
        using var client = _factory.CreateClient();
        var schedule = await Post(client, "admin", "/api/admin/report-schedules", new
        {
            name = "Daily engineering report",
            timezone = "UTC",
            recurrence = "0 0 8 * * *",
            enabled = true,
            destinationType = "File",
            destination = "reports/daily.csv",
            report = new
            {
                from = "2025-02-01",
                to = "2025-02-28"
            }
        });

        var id = Id(schedule);
        Assert.True(schedule.GetProperty("enabled").GetBoolean());
        Assert.Equal("UTC", schedule.GetProperty("timezone").GetString());

        var execution = await Send(client, "admin", HttpMethod.Post, $"/api/admin/report-schedules/{id}/run");
        execution.EnsureSuccessStatusCode();

        var history = await GetJson(client, "admin", $"/api/admin/report-schedules/{id}/executions");
        Assert.Contains(history.EnumerateArray(), item =>
            item.TryGetProperty("startedUtc", out _) &&
            item.TryGetProperty("status", out var status) &&
            status.GetString() == "Succeeded");
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected_and_administrators_can_access_organization_reporting()
    {
        using var client = _factory.CreateClient();

        var anonymous = await client.GetAsync("/api/time-entries");
        Assert.True(anonymous.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect);

        var userReport = await Send(client, "alice", HttpMethod.Post, "/api/reports/run", new { });
        Assert.Equal(HttpStatusCode.Forbidden, userReport.StatusCode);

        var adminReport = await Send(client, "admin", HttpMethod.Post, "/api/reports/run", new { });
        adminReport.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Application_routes_remain_available_under_a_non_root_path_base()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("PathBase", "/ottime"));

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost/ottime/")
        });

        var response = await client.GetAsync("");
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<JsonElement> GetJson(HttpClient client, string user, string path)
    {
        var response = await Send(client, user, HttpMethod.Get, path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Post(HttpClient client, string user, string path, object body)
    {
        var response = await Send(client, user, HttpMethod.Post, path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Put(HttpClient client, string user, string path, object body)
    {
        var response = await Send(client, user, HttpMethod.Put, path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> Delete(HttpClient client, string user, string path) =>
        Send(client, user, HttpMethod.Delete, path);

    private static Task<HttpResponseMessage> Send(HttpClient client, string user, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-User", user);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return client.SendAsync(request);
    }

    private static string Id(JsonElement item) => item.GetProperty("id").ToString();

    public sealed class AppFactory : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
            });
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "AcceptanceTest";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var values) || string.IsNullOrWhiteSpace(values))
                return Task.FromResult(AuthenticateResult.NoResult());

            var user = values.ToString();
            var roles = user == "admin"
                ? new[] { "User", "Reporter", "Administrator" }
                : new[] { "User" };

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user),
                new(ClaimTypes.Name, user)
            };

            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}