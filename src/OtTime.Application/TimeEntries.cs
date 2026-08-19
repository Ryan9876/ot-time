using System.Text.Json;

namespace OtTime.Application;

public sealed class TimeEntries
{
    private readonly ITimeEntryStore _entries;
    private readonly ITimeEntryLookups _lookups;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public TimeEntries(ITimeEntryStore entries, ITimeEntryLookups lookups, IAuditWriter audit, IClock clock)
    {
        _entries = entries;
        _lookups = lookups;
        _audit = audit;
        _clock = clock;
    }

    public async Task<TimeEntryDto> CreateAsync(CurrentUser user, CreateTimeEntry request, CancellationToken cancellationToken = default)
    {
        var normalized = await ValidateAsync(request.WorkDate, request.DurationMinutes, request.CategoryId, request.SourceId,
            request.TicketReference, request.Description, request.AfterHours, request.TagIds, cancellationToken);

        var now = _clock.UtcNow;
        var entry = new TimeEntryRecord(
            Guid.NewGuid(),
            user.Id,
            normalized.WorkDate,
            normalized.DurationMinutes,
            normalized.CategoryId,
            normalized.SourceId,
            normalized.TicketReference,
            normalized.Description,
            normalized.AfterHours,
            normalized.TagIds,
            now,
            user.Id,
            now,
            user.Id,
            Array.Empty<byte>());

        await _entries.ExecuteInTransactionAsync(async ct =>
        {
            await _entries.AddAsync(entry, ct);
            await WriteAuditAsync(user, entry.Id, AuditAction.Created, null, entry, ct);
        }, cancellationToken);

        return ToDto(entry);
    }

    public async Task<IReadOnlyList<TimeEntryDto>> ListAsync(CurrentUser user, TimeEntryListRequest request, CancellationToken cancellationToken = default)
    {
        var ownerId = ResolveOwner(user, request.OwnerId);
        var entries = await _entries.ListAsync(ownerId, request.From, request.To, request.CategoryId, request.SourceId,
            request.AfterHours, request.TicketReference, cancellationToken);
        return entries.Select(ToDto).ToArray();
    }

    public async Task<TimeEntryDto> GetAsync(CurrentUser user, Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await _entries.GetAsync(id, cancellationToken);
        EnsureVisible(user, entry);
        return ToDto(entry!);
    }

    public async Task<TimeEntryDto> UpdateAsync(CurrentUser user, Guid id, UpdateTimeEntry request, CancellationToken cancellationToken = default)
    {
        var existing = await _entries.GetAsync(id, cancellationToken);
        EnsureVisible(user, existing);

        var normalized = await ValidateAsync(request.WorkDate, request.DurationMinutes, request.CategoryId, request.SourceId,
            request.TicketReference, request.Description, request.AfterHours, request.TagIds, cancellationToken);

        var now = _clock.UtcNow;
        var updated = existing! with
        {
            WorkDate = normalized.WorkDate,
            DurationMinutes = normalized.DurationMinutes,
            CategoryId = normalized.CategoryId,
            SourceId = normalized.SourceId,
            TicketReference = normalized.TicketReference,
            Description = normalized.Description,
            AfterHours = normalized.AfterHours,
            TagIds = normalized.TagIds,
            ModifiedUtc = now,
            ModifiedByUserId = user.Id
        };

        await _entries.ExecuteInTransactionAsync(async ct =>
        {
            if (!await _entries.UpdateAsync(updated, request.ConcurrencyToken, ct))
                throw new TimeEntryConcurrencyException(id);

            await WriteAuditAsync(user, id, AuditAction.Updated, existing, updated, ct);
        }, cancellationToken);

        return ToDto(updated);
    }

    public async Task DeleteAsync(CurrentUser user, Guid id, DeleteTimeEntry request, CancellationToken cancellationToken = default)
    {
        var existing = await _entries.GetAsync(id, cancellationToken);
        EnsureVisible(user, existing);

        await _entries.ExecuteInTransactionAsync(async ct =>
        {
            if (!await _entries.DeleteAsync(id, request.ConcurrencyToken, ct))
                throw new TimeEntryConcurrencyException(id);

            await WriteAuditAsync(user, id, AuditAction.Deleted, existing, null, ct);
        }, cancellationToken);
    }

    private async Task<NormalizedEntry> ValidateAsync(
        DateOnly workDate,
        int durationMinutes,
        Guid categoryId,
        Guid? sourceId,
        string? ticketReference,
        string? description,
        bool afterHours,
        IReadOnlyCollection<Guid>? tagIds,
        CancellationToken cancellationToken)
    {
        if (workDate == default)
            throw new TimeEntryValidationException("A work date is required.");
        if (durationMinutes is < 1 or > 1_440)
            throw new TimeEntryValidationException("Duration must be between 1 and 1440 minutes.");
        if (categoryId == Guid.Empty || !await _lookups.IsCategoryEnabledAsync(categoryId, cancellationToken))
            throw new TimeEntryValidationException("A valid enabled category is required.");
        if (sourceId is not null && !await _lookups.IsSourceEnabledAsync(sourceId.Value, cancellationToken))
            throw new TimeEntryValidationException("The selected ticket source is unavailable.");

        var reference = Normalize(ticketReference, 200);
        var notes = Normalize(description, 4_000);
        if (ticketReference?.Length > 200 || description?.Length > 4_000)
            throw new TimeEntryValidationException("One or more text fields exceed the allowed length.");

        var tags = (tagIds ?? Array.Empty<Guid>()).Where(x => x != Guid.Empty).Distinct().ToArray();
        if (tags.Length > 20 || !await _lookups.AreTagsEnabledAsync(tags, cancellationToken))
            throw new TimeEntryValidationException("One or more selected tags are unavailable.");

        return new NormalizedEntry(workDate, durationMinutes, categoryId, sourceId, reference, notes, afterHours, tags);
    }

    private static Guid ResolveOwner(CurrentUser user, Guid? requestedOwnerId)
    {
        if (requestedOwnerId is null || requestedOwnerId == user.Id)
            return user.Id;
        if (!user.CanManageAllTimeEntries)
            throw new TimeEntryAccessDeniedException();
        return requestedOwnerId.Value;
    }

    private static void EnsureVisible(CurrentUser user, TimeEntryRecord? entry)
    {
        if (entry is null || (entry.OwnerUserId != user.Id && !user.CanManageAllTimeEntries))
            throw new TimeEntryNotFoundException();
    }

    private async Task WriteAuditAsync(CurrentUser user, Guid entryId, AuditAction action, TimeEntryRecord? before, TimeEntryRecord? after, CancellationToken cancellationToken)
    {
        await _audit.WriteAsync(new AuditRequest(
            "TimeEntry",
            entryId.ToString(),
            action.ToString(),
            user.Id,
            _clock.UtcNow,
            before is null ? null : JsonSerializer.Serialize(ToDto(before)),
            after is null ? null : JsonSerializer.Serialize(ToDto(after))), cancellationToken);
    }

    private static TimeEntryDto ToDto(TimeEntryRecord entry) => new(
        entry.Id, entry.OwnerUserId, entry.WorkDate, entry.DurationMinutes, entry.CategoryId, entry.SourceId,
        entry.TicketReference, entry.Description, entry.AfterHours, entry.TagIds, entry.CreatedUtc,
        entry.CreatedByUserId, entry.ModifiedUtc, entry.ModifiedByUserId, entry.ConcurrencyToken);

    private static string? Normalize(string? value, int maximumLength)
    {
        var result = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return result is { Length: > 0 } && result.Length <= maximumLength ? result : result;
    }

    private sealed record NormalizedEntry(
        DateOnly WorkDate,
        int DurationMinutes,
        Guid CategoryId,
        Guid? SourceId,
        string? TicketReference,
        string? Description,
        bool AfterHours,
        IReadOnlyCollection<Guid> TagIds);
}

public sealed record CurrentUser(Guid Id, bool CanManageAllTimeEntries);

public sealed record CreateTimeEntry(
    DateOnly WorkDate,
    int DurationMinutes,
    Guid CategoryId,
    Guid? SourceId,
    string? TicketReference,
    string? Description,
    bool AfterHours,
    IReadOnlyCollection<Guid>? TagIds);

public sealed record UpdateTimeEntry(
    DateOnly WorkDate,
    int DurationMinutes,
    Guid CategoryId,
    Guid? SourceId,
    string? TicketReference,
    string? Description,
    bool AfterHours,
    IReadOnlyCollection<Guid>? TagIds,
    byte[] ConcurrencyToken);

public sealed record DeleteTimeEntry(byte[] ConcurrencyToken);

public sealed record TimeEntryListRequest(
    Guid? OwnerId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? CategoryId = null,
    Guid? SourceId = null,
    bool? AfterHours = null,
    string? TicketReference = null);

public sealed record TimeEntryDto(
    Guid Id,
    Guid OwnerUserId,
    DateOnly WorkDate,
    int DurationMinutes,
    Guid CategoryId,
    Guid? SourceId,
    string? TicketReference,
    string? Description,
    bool AfterHours,
    IReadOnlyCollection<Guid> TagIds,
    DateTimeOffset CreatedUtc,
    Guid CreatedByUserId,
    DateTimeOffset ModifiedUtc,
    Guid ModifiedByUserId,
    byte[] ConcurrencyToken);

public sealed record TimeEntryRecord(
    Guid Id,
    Guid OwnerUserId,
    DateOnly WorkDate,
    int DurationMinutes,
    Guid CategoryId,
    Guid? SourceId,
    string? TicketReference,
    string? Description,
    bool AfterHours,
    IReadOnlyCollection<Guid> TagIds,
    DateTimeOffset CreatedUtc,
    Guid CreatedByUserId,
    DateTimeOffset ModifiedUtc,
    Guid ModifiedByUserId,
    byte[] ConcurrencyToken);

public sealed record AuditRequest(
    string EntityType,
    string EntityId,
    string Action,
    Guid ActorUserId,
    DateTimeOffset OccurredUtc,
    string? BeforeJson,
    string? AfterJson);

public enum AuditAction
{
    Created,
    Updated,
    Deleted
}

public interface ITimeEntryStore
{
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    Task AddAsync(TimeEntryRecord entry, CancellationToken cancellationToken);
    Task<TimeEntryRecord?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TimeEntryRecord>> ListAsync(Guid ownerId, DateOnly? from, DateOnly? to, Guid? categoryId, Guid? sourceId, bool? afterHours, string? ticketReference, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(TimeEntryRecord entry, byte[] concurrencyToken, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, byte[] concurrencyToken, CancellationToken cancellationToken);
}

public interface ITimeEntryLookups
{
    Task<bool> IsCategoryEnabledAsync(Guid categoryId, CancellationToken cancellationToken);
    Task<bool> IsSourceEnabledAsync(Guid sourceId, CancellationToken cancellationToken);
    Task<bool> AreTagsEnabledAsync(IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);
}

public interface IAuditWriter
{
    Task WriteAsync(AuditRequest request, CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class TimeEntryValidationException : Exception
{
    public TimeEntryValidationException(string message) : base(message) { }
}

public sealed class TimeEntryAccessDeniedException : Exception
{
    public TimeEntryAccessDeniedException() : base("You are not authorized to access this time entry.") { }
}

public sealed class TimeEntryNotFoundException : Exception
{
    public TimeEntryNotFoundException() : base("The requested time entry was not found.") { }
}

public sealed class TimeEntryConcurrencyException : Exception
{
    public TimeEntryConcurrencyException(Guid id) : base($"Time entry '{id}' was changed or deleted by another request.") { }
}