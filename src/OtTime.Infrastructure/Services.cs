using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OtTime.Infrastructure;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class IdentityBootstrapOptions
{
    public const string SectionName = "Identity:BootstrapAdministrator";

    public string? UserName { get; init; }
    public string? Email { get; init; }
    public string? TemporaryPassword { get; init; }
}

public sealed record LocalUser(string Id, string UserName, string? Email, bool IsLockedOut, IReadOnlyCollection<string> Roles);

public interface ILocalIdentityAdministration
{
    Task<IdentityResult> CreateUserAsync(string userName, string? email, string temporaryPassword, IEnumerable<string> roles, CancellationToken cancellationToken = default);
    Task<IdentityResult> DisableUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<IdentityResult> EnableUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<IdentityResult> SetRolesAsync(string userId, IEnumerable<string> roles, CancellationToken cancellationToken = default);
    Task<LocalUser?> FindUserAsync(string userId, CancellationToken cancellationToken = default);
    Task EnsureBootstrapAdministratorAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalIdentityAdministration : ILocalIdentityAdministration
{
    private static readonly string[] ValidRoles = ["User", "Reporter", "Administrator"];

    private readonly UserManager<IdentityUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly IOptions<IdentityBootstrapOptions> _bootstrap;
    private readonly IAuditWriter _audit;

    public LocalIdentityAdministration(
        UserManager<IdentityUser> users,
        RoleManager<IdentityRole> roles,
        IOptions<IdentityBootstrapOptions> bootstrap,
        IAuditWriter audit)
    {
        _users = users;
        _roles = roles;
        _bootstrap = bootstrap;
        _audit = audit;
    }

    public async Task<IdentityResult> CreateUserAsync(string userName, string? email, string temporaryPassword, IEnumerable<string> roles, CancellationToken cancellationToken = default)
    {
        var normalizedRoles = NormalizeRoles(roles);
        var roleResult = await EnsureRolesAsync(normalizedRoles);
        if (!roleResult.Succeeded)
            return roleResult;

        var user = new IdentityUser
        {
            UserName = userName.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            EmailConfirmed = false
        };

        var result = await _users.CreateAsync(user, temporaryPassword);
        if (!result.Succeeded)
            return result;

        result = await _users.AddToRolesAsync(user, normalizedRoles);
        if (result.Succeeded)
        {
            await _users.AddClaimAsync(user, new Claim("ot:must-change-password", "true"));
            await _audit.WriteAsync(new AuditEvent("identity.user.created", null, user.Id, null, new { user.UserName, user.Email, Roles = normalizedRoles }));
        }

        return result;
    }

    public async Task<IdentityResult> DisableUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
            return IdentityResult.Failed(new IdentityError { Code = "UserNotFound", Description = "The user was not found." });

        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        var result = await _users.UpdateAsync(user);
        if (result.Succeeded)
            await _audit.WriteAsync(new AuditEvent("identity.user.disabled", null, user.Id, null, new { user.UserName }));
        return result;
    }

    public async Task<IdentityResult> EnableUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
            return IdentityResult.Failed(new IdentityError { Code = "UserNotFound", Description = "The user was not found." });

        user.LockoutEnabled = true;
        user.LockoutEnd = null;
        var result = await _users.UpdateAsync(user);
        if (result.Succeeded)
            await _audit.WriteAsync(new AuditEvent("identity.user.enabled", null, user.Id, null, new { user.UserName }));
        return result;
    }

    public async Task<IdentityResult> SetRolesAsync(string userId, IEnumerable<string> roles, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
            return IdentityResult.Failed(new IdentityError { Code = "UserNotFound", Description = "The user was not found." });

        var normalizedRoles = NormalizeRoles(roles);
        var roleResult = await EnsureRolesAsync(normalizedRoles);
        if (!roleResult.Succeeded)
            return roleResult;

        var existing = await _users.GetRolesAsync(user);
        var removeResult = await _users.RemoveFromRolesAsync(user, existing);
        if (!removeResult.Succeeded)
            return removeResult;

        var result = await _users.AddToRolesAsync(user, normalizedRoles);
        if (result.Succeeded)
            await _audit.WriteAsync(new AuditEvent("identity.user.roles.changed", null, user.Id, new { Roles = existing }, new { Roles = normalizedRoles }));
        return result;
    }

    public async Task<LocalUser?> FindUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
            return null;

        var roles = await _users.GetRolesAsync(user);
        return new LocalUser(user.Id, user.UserName ?? user.Id, user.Email, user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow, roles);
    }

    public async Task EnsureBootstrapAdministratorAsync(CancellationToken cancellationToken = default)
    {
        var settings = _bootstrap.Value;
        if (string.IsNullOrWhiteSpace(settings.UserName) || string.IsNullOrWhiteSpace(settings.TemporaryPassword))
            return;

        await EnsureRolesAsync(ValidRoles);
        var administrators = await _users.GetUsersInRoleAsync("Administrator");
        if (administrators.Count > 0)
            return;

        var existing = await _users.FindByNameAsync(settings.UserName);
        if (existing is not null)
        {
            if (!await _users.IsInRoleAsync(existing, "Administrator"))
                await _users.AddToRoleAsync(existing, "Administrator");
            return;
        }

        await CreateUserAsync(settings.UserName, settings.Email, settings.TemporaryPassword, ["User", "Reporter", "Administrator"], cancellationToken);
    }

    private async Task<IdentityResult> EnsureRolesAsync(IEnumerable<string> roleNames)
    {
        foreach (var roleName in roleNames)
        {
            if (await _roles.RoleExistsAsync(roleName))
                continue;

            var result = await _roles.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
                return result;
        }

        return IdentityResult.Success;
    }

    private static string[] NormalizeRoles(IEnumerable<string> roles)
    {
        var result = roles
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (result.Any(x => !ValidRoles.Contains(x, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("One or more supplied roles are not recognized.", nameof(roles));

        return result;
    }
}

public sealed record CsvReportRow(
    DateOnly WorkDate,
    string User,
    string Category,
    string? Source,
    string? TicketReference,
    int DurationMinutes,
    bool IsAfterHours,
    string? Tags,
    string? Description);

public interface ICsvReportWriter
{
    byte[] Write(IEnumerable<CsvReportRow> rows);
}

public sealed class CsvReportWriter : ICsvReportWriter
{
    public byte[] Write(IEnumerable<CsvReportRow> rows)
    {
        var builder = new StringBuilder();
        WriteRow(builder, ["Work Date", "User", "Category", "Source", "Ticket / Reference", "Duration Minutes", "After Hours", "Tags", "Description"]);

        foreach (var row in rows)
        {
            WriteRow(builder,
            [
                row.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.User,
                row.Category,
                row.Source,
                row.TicketReference,
                row.DurationMinutes.ToString(CultureInfo.InvariantCulture),
                row.IsAfterHours ? "Yes" : "No",
                row.Tags,
                row.Description
            ]);
        }

        return new UTF8Encoding(false).GetBytes(builder.ToString());
    }

    private static void WriteRow(StringBuilder builder, IEnumerable<string?> values)
    {
        builder.AppendJoin(',', values.Select(Escape));
        builder.Append("\r\n");
    }

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
            text = "'" + text;

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

public sealed class ArtifactOptions
{
    public const string SectionName = "Artifacts";

    public string RootPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "App_Data", "artifacts");
}

public sealed record ArtifactDescriptor(string Id, string FileName, string ContentType, long Length, DateTimeOffset CreatedUtc);

public interface IArtifactStore
{
    Task<ArtifactDescriptor> SaveAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken = default);
    Task<(ArtifactDescriptor Descriptor, Stream Content)?> OpenReadAsync(string id, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class ProtectedFileArtifactStore : IArtifactStore
{
    private readonly string _root;

    public ProtectedFileArtifactStore(IOptions<ArtifactOptions> options)
    {
        _root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<ArtifactDescriptor> SaveAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "report.csv";

        var temporaryPath = GetPath(id + ".tmp");
        var contentPath = GetPath(id + ".bin");
        await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            await content.CopyToAsync(output, cancellationToken);
        }

        var descriptor = new ArtifactDescriptor(id, safeName, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, new FileInfo(temporaryPath).Length, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(GetPath(id + ".json"), JsonSerializer.Serialize(descriptor), new UTF8Encoding(false), cancellationToken);
        File.Move(temporaryPath, contentPath);
        return descriptor;
    }

    public async Task<(ArtifactDescriptor Descriptor, Stream Content)?> OpenReadAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(id))
            return null;

        var metadataPath = GetPath(id + ".json");
        var contentPath = GetPath(id + ".bin");
        if (!File.Exists(metadataPath) || !File.Exists(contentPath))
            return null;

        var metadata = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        var descriptor = JsonSerializer.Deserialize<ArtifactDescriptor>(metadata);
        if (descriptor is null)
            return null;

        Stream stream = new FileStream(contentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        return (descriptor, stream);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(id))
            return Task.CompletedTask;

        foreach (var suffix in new[] { ".bin", ".json", ".tmp" })
        {
            var path = GetPath(id + suffix);
            if (File.Exists(path))
                File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetPath(string name)
    {
        var path = Path.GetFullPath(Path.Combine(_root, name));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !string.Equals(path, _root, StringComparison.Ordinal))
            throw new InvalidOperationException("Artifact path escapes its configured root.");
        return path;
    }

    private static bool IsValidId(string id) => Guid.TryParseExact(id, "N", out _);
}

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public bool Enabled { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;
    public string From { get; init; } = string.Empty;
    public string? UserName { get; init; }
    public string? Password { get; init; }
}

public interface IReportDelivery
{
    Task DeliverAsync(IEnumerable<string> recipients, string subject, string body, ArtifactDescriptor artifact, CancellationToken cancellationToken = default);
}

public sealed class SmtpReportDelivery : IReportDelivery
{
    private readonly IOptions<SmtpOptions> _options;
    private readonly IArtifactStore _artifacts;
    private readonly ILogger<SmtpReportDelivery> _logger;

    public SmtpReportDelivery(IOptions<SmtpOptions> options, IArtifactStore artifacts, ILogger<SmtpReportDelivery> logger)
    {
        _options = options;
        _artifacts = artifacts;
        _logger = logger;
    }

    public async Task DeliverAsync(IEnumerable<string> recipients, string subject, string body, ArtifactDescriptor artifact, CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        if (!options.Enabled)
        {
            _logger.LogInformation("SMTP delivery is disabled; report artifact {ArtifactId} was retained.", artifact.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.From))
            throw new InvalidOperationException("SMTP is enabled but Host or From is not configured.");

        var addresses = recipients.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (addresses.Length == 0)
            throw new InvalidOperationException("At least one report recipient is required.");

        var opened = await _artifacts.OpenReadAsync(artifact.Id, cancellationToken)
            ?? throw new FileNotFoundException("The report artifact no longer exists.", artifact.Id);

        await using var content = opened.Value.Content;
        using var message = new MailMessage
        {
            From = new MailAddress(options.From),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };

        foreach (var address in addresses)
            message.To.Add(address);

        message.Attachments.Add(new Attachment(content, artifact.FileName, artifact.ContentType));

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            UseDefaultCredentials = string.IsNullOrWhiteSpace(options.UserName)
        };

        if (!string.IsNullOrWhiteSpace(options.UserName))
            client.Credentials = new NetworkCredential(options.UserName, options.Password);

        await client.SendMailAsync(message).WaitAsync(cancellationToken);
    }
}

public sealed record AuditEvent(string Action, string? ActorId, string? SubjectId, object? Before, object? After, DateTimeOffset? OccurredUtc = null);

public interface IAuditWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public sealed class FileAuditWriter : IAuditWriter
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly string _root;
    private readonly IClock _clock;

    public FileAuditWriter(IOptions<ArtifactOptions> options, IClock clock)
    {
        _root = Path.Combine(Path.GetFullPath(options.Value.RootPath), "audit");
        _clock = clock;
        Directory.CreateDirectory(_root);
    }

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var occurred = auditEvent.OccurredUtc ?? _clock.UtcNow;
        var record = auditEvent with { OccurredUtc = occurred };
        var path = Path.Combine(_root, $"{occurred:yyyy-MM-dd}.jsonl");
        var line = JsonSerializer.Serialize(record) + Environment.NewLine;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line, new UTF8Encoding(false), cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }
}

public sealed record TicketReference(string Source, string Reference, string? DisplayName = null);

public interface ITicketConnector
{
    string Source { get; }
    Task<TicketReference?> ResolveAsync(string reference, CancellationToken cancellationToken = default);
}

public sealed class ManualTicketConnector : ITicketConnector
{
    public const string ManualSource = "Manual";

    public string Source => ManualSource;

    public Task<TicketReference?> ResolveAsync(string reference, CancellationToken cancellationToken = default)
    {
        var value = reference?.Trim();
        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : new TicketReference(Source, value, value));
    }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOtTimeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ArtifactOptions>(configuration.GetSection(ArtifactOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<IdentityBootstrapOptions>(configuration.GetSection(IdentityBootstrapOptions.SectionName));

        services.AddIdentityCore<IdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = "__Host-OtTime";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = true;
                options.LoginPath = "/account/login";
                options.AccessDeniedPath = "/account/access-denied";
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("Reporter", policy => policy.RequireRole("Reporter", "Administrator"));
            options.AddPolicy("Administrator", policy => policy.RequireRole("Administrator"));
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICsvReportWriter, CsvReportWriter>();
        services.AddSingleton<IArtifactStore, ProtectedFileArtifactStore>();
        services.AddSingleton<IAuditWriter, FileAuditWriter>();
        services.AddScoped<IReportDelivery, SmtpReportDelivery>();
        services.AddScoped<ILocalIdentityAdministration, LocalIdentityAdministration>();
        services.AddSingleton<ITicketConnector, ManualTicketConnector>();

        return services;
    }
}