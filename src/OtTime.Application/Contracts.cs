using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OtTime.Application;

[Flags]
public enum ApplicationPermission
{
    None = 0,
    ManageOwnEntries = 1,
    ViewOrganizationReports = 2,
    ExportOrganizationReports = 4,
    ManageConfiguration = 8,
    ManageSchedules = 16,
    ManageUsers = 32
}

public sealed record CurrentUser(
    string UserId,
    string DisplayName,
    IReadOnlySet<string> Roles,
    ApplicationPermission Permissions)
{
    public bool Has(ApplicationPermission permission) => (Permissions & permission) == permission;
}

public interface ICurrentUserAccessor
{
    CurrentUser? Current { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ITimeEntryStore
{
    Task<TimeEntryDto?> FindAsync(EntryAccessRequest request, Guid entryId, CancellationToken cancellationToken = default);
    Task<PagedResult<TimeEntryDto>> SearchAsync(TimeEntrySearchRequest request, CancellationToken cancellationToken = default);
    Task<TimeEntryDto> CreateAsync(TimeEntryCreateRequest request, CancellationToken cancellationToken = default);
    Task<TimeEntryDto> UpdateAsync(TimeEntryUpdateRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(EntryDeleteRequest request, CancellationToken cancellationToken = default);
}

public interface ILookupStore
{
    Task<IReadOnlyList<LookupItemDto>> GetAsync(LookupQueryRequest request, LookupKind kind, CancellationToken cancellationToken = default);
    Task<LookupItemDto> SaveAsync(LookupSaveRequest request, CancellationToken cancellationToken = default);
    Task DisableAsync(LookupDeleteRequest request, CancellationToken cancellationToken = default);
}

public interface IReportDefinitionStore
{
    Task<ReportDefinitionDto?> FindAsync(ReportAccessRequest request, Guid reportId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportDefinitionDto>> GetAsync(ReportAccessRequest request, CancellationToken cancellationToken = default);
    Task<ReportDefinitionDto> SaveAsync(ReportDefinitionSaveRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(ReportDefinitionDeleteRequest request, CancellationToken cancellationToken = default);
}

public interface IReportQueryService
{
    Task<ReportResultDto> RunAsync(ReportRunRequest request, CancellationToken cancellationToken = default);
}

public interface IAuditWriter
{
    Task WriteAsync(AuditEventRequest request, CancellationToken cancellationToken = default);
}

public interface ICsvExporter
{
    CsvExportDto Create(ReportResultDto report);
}

public interface ICsvStorage
{
    Task<StoredArtifactDto> StoreAsync(CsvStorageRequest request, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(ArtifactAccessRequest request, Guid artifactId, CancellationToken cancellationToken = default);
}

public interface IReportDeliveryService
{
    Task<ReportDeliveryResultDto> DeliverAsync(ReportDeliveryRequest request, CancellationToken cancellationToken = default);
}

public interface IScheduleLeaseStore
{
    Task<IReadOnlyList<ScheduleLeaseDto>> AcquireDueAsync(
        DateTimeOffset now,
        int maximumCount,
        TimeSpan leaseDuration,
        string workerId,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(ScheduleCompletionRequest request, CancellationToken cancellationToken = default);
    Task FailAsync(ScheduleFailureRequest request, CancellationToken cancellationToken = default);
}

public interface ITicketConnector
{
    string SourceKey { get; }
    Task<TicketLookupResultDto?> FindAsync(TicketLookupRequest request, CancellationToken cancellationToken = default);
}

public sealed record EntryAccessRequest(CurrentUser Actor);
public sealed record EntryDeleteRequest(CurrentUser Actor, Guid EntryId, byte[] ConcurrencyToken);

public sealed record TimeEntrySearchRequest(
    CurrentUser Actor,
    DateOnly? From = null,
    DateOnly? To = null,
    IReadOnlyCollection<string>? OwnerIds = null,
    IReadOnlyCollection<Guid>? CategoryIds = null,
    IReadOnlyCollection<Guid>? SourceIds = null,
    string? TicketReference = null,
    bool? IsAfterHours = null,
    IReadOnlyCollection<Guid>? TagIds = null,
    int Page = 1,
    int PageSize = 50);

public sealed record TimeEntryCreateRequest(
    CurrentUser Actor,
    string? OwnerId,
    DateOnly WorkDate,
    int DurationMinutes,
    Guid CategoryId,
    Guid? SourceId,
    string? TicketReference,
    string Description,
    bool IsAfterHours,
    IReadOnlyCollection<Guid> TagIds,
    IReadOnlyDictionary<string, string>? DimensionValues);

public sealed record TimeEntryUpdateRequest(
    CurrentUser Actor,
    Guid EntryId,
    byte[] ConcurrencyToken,
    DateOnly WorkDate,
    int DurationMinutes,
    Guid CategoryId,
    Guid? SourceId,
    string? TicketReference,
    string Description,
    bool IsAfterHours,
    IReadOnlyCollection<Guid> TagIds,
    IReadOnlyDictionary<string, string>? DimensionValues);

public sealed record TimeEntryDto(
    Guid Id,
    string OwnerId,
    string OwnerDisplayName,
    DateOnly WorkDate,
    int DurationMinutes,
    LookupItemDto Category,
    LookupItemDto? Source,
    string? TicketReference,
    string Description,
    bool IsAfterHours,
    IReadOnlyList<LookupItemDto> Tags,
    IReadOnlyDictionary<string, string> DimensionValues,
    DateTimeOffset CreatedUtc,
    string CreatedByUserId,
    DateTimeOffset ModifiedUtc,
    string ModifiedByUserId,
    byte[] ConcurrencyToken);

public enum LookupKind
{
    ActivityCategory,
    TicketSource,
    Tag,
    ReportingDimension
}

public sealed record LookupQueryRequest(CurrentUser Actor, bool IncludeDisabled = false);

public sealed record LookupSaveRequest(
    CurrentUser Actor,
    LookupKind Kind,
    Guid? Id,
    string Name,
    string? Key,
    int DisplayOrder,
    bool IsEnabled);

public sealed record LookupDeleteRequest(CurrentUser Actor, LookupKind Kind, Guid Id, byte[] ConcurrencyToken);

public sealed record LookupItemDto(
    Guid Id,
    LookupKind Kind,
    string Name,
    string? Key,
    int DisplayOrder,
    bool IsEnabled,
    byte[] ConcurrencyToken);

public sealed record ReportAccessRequest(CurrentUser Actor);

public sealed record ReportFilterDto(
    DateOnly? From,
    DateOnly? To,
    IReadOnlyCollection<string>? OwnerIds,
    IReadOnlyCollection<Guid>? CategoryIds,
    IReadOnlyCollection<Guid>? SourceIds,
    string? TicketReference,
    bool? IsAfterHours,
    IReadOnlyCollection<Guid>? TagIds,
    IReadOnlyDictionary<string, string>? DimensionValues);

public sealed record ReportRunRequest(CurrentUser Actor, ReportFilterDto Filter);

public sealed record ReportDefinitionSaveRequest(
    CurrentUser Actor,
    Guid? Id,
    string Name,
    ReportFilterDto Filter,
    bool IsShared,
    byte[]? ConcurrencyToken);

public sealed record ReportDefinitionDeleteRequest(CurrentUser Actor, Guid Id, byte[] ConcurrencyToken);

public sealed record ReportDefinitionDto(
    Guid Id,
    string Name,
    string OwnerId,
    bool IsShared,
    ReportFilterDto Filter,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc,
    byte[] ConcurrencyToken);

public sealed record ReportRowDto(
    Guid EntryId,
    string OwnerDisplayName,
    DateOnly WorkDate,
    int DurationMinutes,
    string Category,
    string? Source,
    string? TicketReference,
    string Description,
    bool IsAfterHours,
    string Tags,
    IReadOnlyDictionary<string, string> DimensionValues);

public sealed record ReportResultDto(
    IReadOnlyList<ReportRowDto> Rows,
    int TotalDurationMinutes,
    DateTimeOffset GeneratedUtc);

public sealed record CsvExportDto(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record CsvStorageRequest(
    CurrentUser Actor,
    Guid? ScheduleId,
    CsvExportDto Export,
    DateTimeOffset ExpiresUtc);

public sealed record ArtifactAccessRequest(CurrentUser Actor);

public sealed record StoredArtifactDto(
    Guid Id,
    string FileName,
    string ContentType,
    long Length,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc);

public enum ReportDeliveryDestination
{
    FileSystem,
    EmailAttachment
}

public sealed record ReportDeliveryRequest(
    CurrentUser? Actor,
    ReportDeliveryDestination Destination,
    string Target,
    IReadOnlyCollection<string> Recipients,
    CsvExportDto Export);

public sealed record ReportDeliveryResultDto(
    bool Succeeded,
    string? ExternalReference,
    string? ErrorMessage);

public sealed record ScheduleLeaseDto(
    Guid ScheduleId,
    Guid ReportDefinitionId,
    string ScheduleName,
    string TimeZoneId,
    string Recurrence,
    ReportDeliveryDestination Destination,
    string DeliveryTarget,
    IReadOnlyCollection<string> Recipients,
    DateTimeOffset DueUtc,
    DateTimeOffset LeaseExpiresUtc,
    string LeaseToken,
    string IdempotencyKey);

public sealed record ScheduleCompletionRequest(
    Guid ScheduleId,
    string LeaseToken,
    string IdempotencyKey,
    DateTimeOffset CompletedUtc,
    DateTimeOffset NextRunUtc,
    Guid? ArtifactId,
    string? DeliveryReference);

public sealed record ScheduleFailureRequest(
    Guid ScheduleId,
    string LeaseToken,
    string IdempotencyKey,
    DateTimeOffset FailedUtc,
    DateTimeOffset NextRunUtc,
    string ErrorMessage);

public sealed record AuditEventRequest(
    CurrentUser? Actor,
    string EventType,
    string EntityType,
    string EntityId,
    string? OwnerId,
    string? BeforeJson,
    string? AfterJson,
    DateTimeOffset OccurredUtc);

public sealed record TicketLookupRequest(
    CurrentUser Actor,
    string Reference);

public sealed record TicketLookupResultDto(
    string Reference,
    string? Title,
    string? Status,
    string? Url,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);