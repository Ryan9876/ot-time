using System.Globalization;
using System.Text;

namespace OtTime.Application;

public sealed class AdministrationAndReportingService(
    IUserAdministrationStore users,
    ILookupStore lookups,
    IReportStore reports,
    IReportQuery reportQuery,
    IReportArtifactStore artifacts,
    IReportDelivery reportDelivery,
    IClock clock)
{
    public async Task<UserAdministrationResult> SaveUserAsync(UserAdministrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.DisplayName);

        var roles = command.Roles?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (roles.Any(role => !ReportAuthorization.KnownRoles.Contains(role)))
            throw new ValidationException("One or more roles are invalid.");

        var user = await users.SaveAsync(command with
        {
            Email = command.Email.Trim(),
            DisplayName = command.DisplayName.Trim(),
            Roles = roles
        }, cancellationToken);

        return new UserAdministrationResult(user.Id, user.Email, user.DisplayName, user.Enabled, user.Roles);
    }

    public async Task DisableUserAsync(Guid userId, RequestActor actor, CancellationToken cancellationToken = default)
    {
        ReportAuthorization.RequireAdministrator(actor);
        await users.SetEnabledAsync(userId, false, actor.UserId, cancellationToken);
    }

    public async Task<IReadOnlyList<LookupItem>> GetLookupsAsync(string type, bool includeDisabled, RequestActor actor, CancellationToken cancellationToken = default)
    {
        ReportAuthorization.RequireAdministrator(actor);
        return await lookups.GetAsync(NormalizeLookupType(type), includeDisabled, cancellationToken);
    }

    public async Task<LookupItem> SaveLookupAsync(LookupItemCommand command, RequestActor actor, CancellationToken cancellationToken = default)
    {
        ReportAuthorization.RequireAdministrator(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Name);

        return await lookups.SaveAsync(command with
        {
            Type = NormalizeLookupType(command.Type),
            Name = command.Name.Trim(),
            Value = command.Value?.Trim()
        }, actor.UserId, cancellationToken);
    }

    public async Task DeleteLookupAsync(Guid id, RequestActor actor, CancellationToken cancellationToken = default)
    {
        ReportAuthorization.RequireAdministrator(actor);
        await lookups.DeleteAsync(id, actor.UserId, cancellationToken);
    }

    public async Task<SavedReportDefinition> SaveReportAsync(SavedReportCommand command, RequestActor actor, CancellationToken cancellationToken = default)
    {
        ReportAuthorization.RequireReportAccess(actor);
        ValidateFilters(command.Filters);

        if (command.IsShared)
            ReportAuthorization.RequireAdministrator(actor);

        var existing = command.Id is null
            ? null
            : await reports.GetReportAsync(command.Id.Value, cancellationToken);

        if (existing is not null && existing.OwnerUserId != actor.UserId && !actor.IsAdministrator)
            throw new UnauthorizedAccessException("Only the report owner or an administrator may modify this report.");

        return await reports.SaveReportAsync(command with
        {
            Name = Required(command.Name, "Report name"),
            OwnerUserId = existing?.OwnerUserId ?? actor.UserId
        }, actor.UserId, cancellationToken);
    }

    public async Task DeleteReportAsync(Guid reportId, RequestActor actor, CancellationToken cancellationToken = default)
    {
        ReportAuthorization.RequireReportAccess(actor);
        var report = await reports.GetReportAsync(reportId, cancellationToken)
            ?? throw new KeyNotFoundException("Report was not found.");

        if (report.OwnerUserId != actor.UserId && !actor.IsAdministrator)
            throw new UnauthorizedAccessException("Only the report owner or an administrator may delete this report.");

        await reports.DeleteReportAsync(reportId, actor.UserId, cancellationToken);
    }

    public async Task<ReportResult> RunReportAsync(ReportFilter filter, RequestActor actor, CancellationToken cancellationToken = default)
    {
        ReportAuthorization.RequireReportAccess(actor);
        ValidateFilters(filter);

        var effective = actor.IsAdministrator || actor.IsReporter
            ? filter
            : filter with { UserIds = [actor.UserId] };

        if (!actor.IsAdministrator && effective.UserIds is { Count: > 0 } &&
            effective.UserIds.Any(id => id != actor.UserId))
            throw new UnauthorizedAccessException("You are not authorized to report on other users.");

        var rows = await reportQuery.QueryAsync(effective, cancellationToken);
        var totals = new ReportTotals(
            rows.Count,
            rows.Sum(x => x.DurationMinutes),
            rows.GroupBy(x => x.WorkDate).Count(),
            rows.GroupBy(x => x.UserId).Count());

        return new ReportResult(rows, totals);
    }

    public async Task<byte[]> ExportCsvAsync(ReportFilter filter, RequestActor actor, CancellationToken cancellationToken = default)
    {
        var result = await RunReportAsync(filter, actor, cancellationToken);
        return CsvReportWriter.Write(result);
    }

    public async Task<ReportSchedule> SaveScheduleAsync(ReportScheduleCommand command, RequestActor actor, CancellationToken cancellationToken = default)
    {
        ReportAuthorization.RequireAdministrator(actor);
        ValidateSchedule(command);

        var report = await reports.GetReportAsync(command.ReportId, cancellationToken)
            ?? throw new KeyNotFoundException("The selected report was not found.");

        var now = clock.UtcNow;
        var nextRun = command.Enabled
            ? ScheduleCalculator.NextRun(command.Recurrence, command.TimeZoneId, now)
            : null;

        return await reports.SaveScheduleAsync(command with
        {
            Destination = command.Destination.Trim(),
            TimeZoneId = command.TimeZoneId.Trim(),
            Recipients = NormalizeRecipients(command.Recipients),
            NextRunUtc = nextRun,
            ReportId = report.Id
        }, actor.UserId, cancellationToken);
    }

    public async Task<ScheduleExecutionSummary> ExecuteDueSchedulesAsync(string workerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        var now = clock.UtcNow;
        var schedules = await reports.ClaimDueSchedulesAsync(workerId, now, TimeSpan.FromMinutes(10), cancellationToken);
        var succeeded = 0;
        var failed = 0;

        foreach (var schedule in schedules)
        {
            try
            {
                var report = await reports.GetReportAsync(schedule.ReportId, cancellationToken)
                    ?? throw new InvalidOperationException("Scheduled report no longer exists.");

                var result = await RunScheduledReportAsync(report, cancellationToken);
                var csv = CsvReportWriter.Write(result);
                var artifact = await artifacts.SaveAsync(
                    new ReportArtifact(Guid.NewGuid(), schedule.Id, report.Name, "text/csv",
                        $"{SafeFileName(report.Name)}-{now:yyyyMMddHHmmss}.csv", csv, now),
                    cancellationToken);

                await reportDelivery.DeliverAsync(schedule, artifact, cancellationToken);

                var nextRun = ScheduleCalculator.NextRun(schedule.Recurrence, schedule.TimeZoneId, now);
                await reports.CompleteScheduleAsync(schedule.Id, workerId, true, null, now, nextRun, artifact.Id, cancellationToken);
                succeeded++;
            }
            catch (Exception exception)
            {
                await reports.CompleteScheduleAsync(
                    schedule.Id, workerId, false, exception.Message, now,
                    ScheduleCalculator.RetryAfterFailure(schedule, now), null, cancellationToken);
                failed++;
            }
        }

        return new ScheduleExecutionSummary(schedules.Count, succeeded, failed);
    }

    private async Task<ReportResult> RunScheduledReportAsync(SavedReportDefinition report, CancellationToken cancellationToken)
    {
        ValidateFilters(report.Filters);
        var rows = await reportQuery.QueryAsync(report.Filters, cancellationToken);
        return new ReportResult(rows, new ReportTotals(
            rows.Count,
            rows.Sum(x => x.DurationMinutes),
            rows.GroupBy(x => x.WorkDate).Count(),
            rows.GroupBy(x => x.UserId).Count()));
    }

    private static void ValidateFilters(ReportFilter filter)
    {
        if (filter.StartDate is not null && filter.EndDate is not null && filter.StartDate > filter.EndDate)
            throw new ValidationException("The report start date must not be after the end date.");

        if (filter.TicketReference?.Length > 200)
            throw new ValidationException("Ticket/reference filters may not exceed 200 characters.");
    }

    private static void ValidateSchedule(ReportScheduleCommand command)
    {
        if (command.ReportId == Guid.Empty)
            throw new ValidationException("A report is required.");

        if (!Enum.IsDefined(command.DestinationKind))
            throw new ValidationException("A valid report destination is required.");

        _ = TimeZoneInfo.FindSystemTimeZoneById(Required(command.TimeZoneId, "Timezone"));
        ScheduleCalculator.Validate(command.Recurrence);

        if (command.DestinationKind == ReportDestinationKind.Email && NormalizeRecipients(command.Recipients).Count == 0)
            throw new ValidationException("At least one email recipient is required.");

        if (command.DestinationKind == ReportDestinationKind.FileSystem && string.IsNullOrWhiteSpace(command.Destination))
            throw new ValidationException("A filesystem destination is required.");
    }

    private static string NormalizeLookupType(string value) =>
        Required(value, "Lookup type").Trim().ToLowerInvariant() switch
        {
            "category" or "source" or "tag" or "dimension" => value.Trim().ToLowerInvariant(),
            _ => throw new ValidationException("Lookup type must be category, source, tag, or dimension.")
        };

    private static IReadOnlyList<string> NormalizeRecipients(IReadOnlyCollection<string>? recipients) =>
        recipients?.Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static string Required(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ValidationException($"{field} is required.");

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }
}

public static class ReportAuthorization
{
    public static readonly ISet<string> KnownRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "User", "Reporter", "Administrator"
    };

    public static void RequireReportAccess(RequestActor actor)
    {
        if (!actor.IsReporter && !actor.IsAdministrator)
            throw new UnauthorizedAccessException("Reporting access is required.");
    }

    public static void RequireAdministrator(RequestActor actor)
    {
        if (!actor.IsAdministrator)
            throw new UnauthorizedAccessException("Administrator access is required.");
    }
}

public static class CsvReportWriter
{
    public static byte[] Write(ReportResult report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Work Date,User,Category,Source,Ticket Reference,Description,After Hours,Duration Minutes,Tags");

        foreach (var row in report.Rows)
        {
            Append(builder, row.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Append(builder, row.UserDisplayName);
            Append(builder, row.Category);
            Append(builder, row.Source);
            Append(builder, row.TicketReference);
            Append(builder, row.Description);
            Append(builder, row.AfterHours ? "Yes" : "No");
            Append(builder, row.DurationMinutes.ToString(CultureInfo.InvariantCulture));
            Append(builder, string.Join("; ", row.Tags));
            builder.AppendLine();
        }

        builder.Append("TOTAL,,,,,,,");
        builder.Append(report.Totals.DurationMinutes.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(",");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(builder.ToString());
    }

    private static void Append(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;

        builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append("\",");
    }
}

public static class ScheduleCalculator
{
    public static void Validate(ScheduleRecurrence recurrence)
    {
        if (recurrence.Interval < 1 || recurrence.Interval > 365)
            throw new ValidationException("Schedule interval must be between 1 and 365.");

        if (recurrence.TimeOfDay < TimeOnly.MinValue || recurrence.TimeOfDay > TimeOnly.MaxValue)
            throw new ValidationException("A valid schedule time is required.");

        if (recurrence.Kind == ScheduleRecurrenceKind.Weekly &&
            (recurrence.DaysOfWeek is null || recurrence.DaysOfWeek.Count == 0))
            throw new ValidationException("A weekly schedule requires at least one day.");
    }

    public static DateTimeOffset NextRun(ScheduleRecurrence recurrence, string timeZoneId, DateTimeOffset afterUtc)
    {
        Validate(recurrence);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(afterUtc, zone);
        var date = DateOnly.FromDateTime(local.DateTime);

        for (var offset = 0; offset <= 3660; offset++)
        {
            var candidateDate = date.AddDays(offset);
            if (!Matches(recurrence, candidateDate, date))
                continue;

            var unspecified = candidateDate.ToDateTime(recurrence.TimeOfDay, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(unspecified))
                continue;

            var candidate = new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime();
            if (candidate > afterUtc)
                return candidate;
        }

        throw new ValidationException("Unable to calculate the next scheduled run.");
    }

    public static DateTimeOffset RetryAfterFailure(ReportSchedule schedule, DateTimeOffset now) =>
        now.AddMinutes(Math.Min(60, 5 * Math.Max(1, schedule.ConsecutiveFailures + 1)));

    private static bool Matches(ScheduleRecurrence recurrence, DateOnly candidate, DateOnly anchor)
    {
        return recurrence.Kind switch
        {
            ScheduleRecurrenceKind.Daily => candidate.DayNumber % recurrence.Interval == anchor.DayNumber % recurrence.Interval,
            ScheduleRecurrenceKind.Weekly => recurrence.DaysOfWeek!.Contains(candidate.DayOfWeek) &&
                                             ((candidate.DayNumber - anchor.DayNumber) / 7) % recurrence.Interval == 0,
            _ => false
        };
    }
}

public interface IUserAdministrationStore
{
    Task<ApplicationUser> SaveAsync(UserAdministrationCommand command, CancellationToken cancellationToken);
    Task SetEnabledAsync(Guid userId, bool enabled, Guid actorUserId, CancellationToken cancellationToken);
}

public interface ILookupStore
{
    Task<IReadOnlyList<LookupItem>> GetAsync(string type, bool includeDisabled, CancellationToken cancellationToken);
    Task<LookupItem> SaveAsync(LookupItemCommand command, Guid actorUserId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken);
}

public interface IReportStore
{
    Task<SavedReportDefinition?> GetReportAsync(Guid id, CancellationToken cancellationToken);
    Task<SavedReportDefinition> SaveReportAsync(SavedReportCommand command, Guid actorUserId, CancellationToken cancellationToken);
    Task DeleteReportAsync(Guid reportId, Guid actorUserId, CancellationToken cancellationToken);
    Task<ReportSchedule> SaveScheduleAsync(ReportScheduleCommand command, Guid actorUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportSchedule>> ClaimDueSchedulesAsync(string workerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task CompleteScheduleAsync(Guid scheduleId, string workerId, bool succeeded, string? error, DateTimeOffset completedUtc, DateTimeOffset? nextRunUtc, Guid? artifactId, CancellationToken cancellationToken);
}

public interface IReportQuery
{
    Task<IReadOnlyList<ReportRow>> QueryAsync(ReportFilter filter, CancellationToken cancellationToken);
}

public interface IReportArtifactStore
{
    Task<ReportArtifact> SaveAsync(ReportArtifact artifact, CancellationToken cancellationToken);
}

public interface IReportDelivery
{
    Task DeliverAsync(ReportSchedule schedule, ReportArtifact artifact, CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed record RequestActor(Guid UserId, IReadOnlySet<string> Roles)
{
    public bool IsAdministrator => Roles.Contains("Administrator");
    public bool IsReporter => Roles.Contains("Reporter");
}

public sealed record ApplicationUser(Guid Id, string Email, string DisplayName, bool Enabled, IReadOnlyList<string> Roles);
public sealed record UserAdministrationCommand(Guid? Id, string Email, string DisplayName, bool Enabled, IReadOnlyCollection<string>? Roles);
public sealed record UserAdministrationResult(Guid Id, string Email, string DisplayName, bool Enabled, IReadOnlyList<string> Roles);

public sealed record LookupItem(Guid Id, string Type, string Name, string? Value, int DisplayOrder, bool Enabled);
public sealed record LookupItemCommand(Guid? Id, string Type, string Name, string? Value, int DisplayOrder, bool Enabled);

public sealed record ReportFilter(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    IReadOnlyCollection<Guid>? UserIds = null,
    IReadOnlyCollection<Guid>? CategoryIds = null,
    IReadOnlyCollection<Guid>? SourceIds = null,
    IReadOnlyCollection<Guid>? TagIds = null,
    string? TicketReference = null,
    bool? AfterHours = null,
    IReadOnlyDictionary<string, string>? Dimensions = null);

public sealed record SavedReportDefinition(Guid Id, string Name, Guid OwnerUserId, bool IsShared, ReportFilter Filters);
public sealed record SavedReportCommand(Guid? Id, string Name, Guid OwnerUserId, bool IsShared, ReportFilter Filters);

public sealed record ReportRow(
    Guid EntryId,
    Guid UserId,
    string UserDisplayName,
    DateOnly WorkDate,
    string Category,
    string? Source,
    string? TicketReference,
    string? Description,
    bool AfterHours,
    int DurationMinutes,
    IReadOnlyList<string> Tags);

public sealed record ReportTotals(int EntryCount, int DurationMinutes, int WorkDayCount, int UserCount);
public sealed record ReportResult(IReadOnlyList<ReportRow> Rows, ReportTotals Totals);

public enum ReportDestinationKind { FileSystem, Email }
public enum ScheduleRecurrenceKind { Daily, Weekly }

public sealed record ScheduleRecurrence(
    ScheduleRecurrenceKind Kind,
    int Interval,
    TimeOnly TimeOfDay,
    IReadOnlyCollection<DayOfWeek>? DaysOfWeek = null);

public sealed record ReportSchedule(
    Guid Id,
    Guid ReportId,
    bool Enabled,
    ReportDestinationKind DestinationKind,
    string Destination,
    IReadOnlyList<string> Recipients,
    string TimeZoneId,
    ScheduleRecurrence Recurrence,
    DateTimeOffset? NextRunUtc,
    int ConsecutiveFailures);

public sealed record ReportScheduleCommand(
    Guid? Id,
    Guid ReportId,
    bool Enabled,
    ReportDestinationKind DestinationKind,
    string Destination,
    IReadOnlyCollection<string>? Recipients,
    string TimeZoneId,
    ScheduleRecurrence Recurrence,
    DateTimeOffset? NextRunUtc = null);

public sealed record ReportArtifact(
    Guid Id,
    Guid ScheduleId,
    string ReportName,
    string ContentType,
    string FileName,
    byte[] Content,
    DateTimeOffset CreatedUtc);

public sealed record ScheduleExecutionSummary(int Claimed, int Succeeded, int Failed);

public sealed class ValidationException(string message) : Exception(message);