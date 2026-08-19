using System;
using System.Collections.Generic;
using System.Linq;

namespace OtTime.Domain;

public sealed class DomainRuleViolationException : InvalidOperationException
{
    public DomainRuleViolationException(string message) : base(message) { }
}

public static class DomainRules
{
    public static string Required(string? value, string name, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new DomainRuleViolationException($"{name} is required.");
        if (normalized.Length > maximumLength)
            throw new DomainRuleViolationException($"{name} cannot exceed {maximumLength} characters.");
        return normalized;
    }

    public static string? Optional(string? value, string name, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (normalized.Length > maximumLength)
            throw new DomainRuleViolationException($"{name} cannot exceed {maximumLength} characters.");
        return normalized;
    }

    public static DateTimeOffset Utc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new DomainRuleViolationException($"{name} must be expressed in UTC.");
        return value;
    }

    public static Guid Id(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw new DomainRuleViolationException($"{name} is required.");
        return value;
    }
}

public abstract class AuditedEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public Guid CreatedByUserId { get; protected set; }
    public DateTimeOffset CreatedAtUtc { get; protected set; }
    public Guid? ModifiedByUserId { get; protected set; }
    public DateTimeOffset? ModifiedAtUtc { get; protected set; }
    public byte[] ConcurrencyToken { get; protected set; } = Array.Empty<byte>();

    protected AuditedEntity() { }

    protected AuditedEntity(Guid actorUserId, DateTimeOffset createdAtUtc)
    {
        CreatedByUserId = DomainRules.Id(actorUserId, nameof(actorUserId));
        CreatedAtUtc = DomainRules.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    protected void Touch(Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        ModifiedByUserId = DomainRules.Id(actorUserId, nameof(actorUserId));
        ModifiedAtUtc = DomainRules.Utc(modifiedAtUtc, nameof(modifiedAtUtc));
    }
}

public abstract class ConfigurableLookup : AuditedEntity
{
    public string Name { get; protected set; } = null!;
    public int DisplayOrder { get; protected set; }
    public bool IsEnabled { get; protected set; } = true;

    protected ConfigurableLookup() { }

    protected ConfigurableLookup(string name, int displayOrder, Guid actorUserId, DateTimeOffset createdAtUtc)
        : base(actorUserId, createdAtUtc)
    {
        Name = DomainRules.Required(name, nameof(name), 120);
        if (displayOrder < 0)
            throw new DomainRuleViolationException("Display order cannot be negative.");
        DisplayOrder = displayOrder;
    }

    public void Rename(string name, Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        Name = DomainRules.Required(name, nameof(name), 120);
        Touch(actorUserId, modifiedAtUtc);
    }

    public void Reorder(int displayOrder, Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        if (displayOrder < 0)
            throw new DomainRuleViolationException("Display order cannot be negative.");
        DisplayOrder = displayOrder;
        Touch(actorUserId, modifiedAtUtc);
    }

    public void SetEnabled(bool enabled, Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        IsEnabled = enabled;
        Touch(actorUserId, modifiedAtUtc);
    }
}

public sealed class ActivityCategory : ConfigurableLookup
{
    public bool RequiresTicketReference { get; private set; }

    public ActivityCategory() { }

    public ActivityCategory(
        string name,
        int displayOrder,
        bool requiresTicketReference,
        Guid actorUserId,
        DateTimeOffset createdAtUtc)
        : base(name, displayOrder, actorUserId, createdAtUtc)
    {
        RequiresTicketReference = requiresTicketReference;
    }

    public void SetTicketRequirement(bool required, Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        RequiresTicketReference = required;
        Touch(actorUserId, modifiedAtUtc);
    }
}

public sealed class TicketSource : ConfigurableLookup
{
    public string Key { get; private set; } = null!;

    public TicketSource() { }

    public TicketSource(
        string name,
        string key,
        int displayOrder,
        Guid actorUserId,
        DateTimeOffset createdAtUtc)
        : base(name, displayOrder, actorUserId, createdAtUtc)
    {
        Key = DomainRules.Required(key, nameof(key), 64).ToUpperInvariant();
    }

    public void ChangeKey(string key, Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        Key = DomainRules.Required(key, nameof(key), 64).ToUpperInvariant();
        Touch(actorUserId, modifiedAtUtc);
    }
}

public sealed class Tag : ConfigurableLookup
{
    public string? Color { get; private set; }

    public Tag() { }

    public Tag(
        string name,
        string? color,
        int displayOrder,
        Guid actorUserId,
        DateTimeOffset createdAtUtc)
        : base(name, displayOrder, actorUserId, createdAtUtc)
    {
        Color = DomainRules.Optional(color, nameof(color), 32);
    }

    public void SetColor(string? color, Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        Color = DomainRules.Optional(color, nameof(color), 32);
        Touch(actorUserId, modifiedAtUtc);
    }
}

public enum CustomDimensionKind
{
    Text = 1,
    Select = 2
}

public sealed class CustomDimension : ConfigurableLookup
{
    private readonly List<CustomDimensionOption> _options = new();

    public CustomDimensionKind Kind { get; private set; }
    public bool IsRequired { get; private set; }
    public IReadOnlyCollection<CustomDimensionOption> Options => _options;

    public CustomDimension() { }

    public CustomDimension(
        string name,
        CustomDimensionKind kind,
        bool isRequired,
        int displayOrder,
        Guid actorUserId,
        DateTimeOffset createdAtUtc)
        : base(name, displayOrder, actorUserId, createdAtUtc)
    {
        Kind = kind;
        IsRequired = isRequired;
    }

    public CustomDimensionOption AddOption(
        string value,
        int displayOrder,
        Guid actorUserId,
        DateTimeOffset createdAtUtc)
    {
        if (Kind != CustomDimensionKind.Select)
            throw new DomainRuleViolationException("Only select dimensions can contain options.");

        var option = new CustomDimensionOption(Id, value, displayOrder, actorUserId, createdAtUtc);
        _options.Add(option);
        Touch(actorUserId, createdAtUtc);
        return option;
    }

    public void SetRequired(bool required, Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        IsRequired = required;
        Touch(actorUserId, modifiedAtUtc);
    }
}

public sealed class CustomDimensionOption : ConfigurableLookup
{
    public Guid CustomDimensionId { get; private set; }

    public CustomDimensionOption() { }

    public CustomDimensionOption(
        Guid customDimensionId,
        string value,
        int displayOrder,
        Guid actorUserId,
        DateTimeOffset createdAtUtc)
        : base(value, displayOrder, actorUserId, createdAtUtc)
    {
        CustomDimensionId = DomainRules.Id(customDimensionId, nameof(customDimensionId));
    }
}

public sealed class TimeEntry : AuditedEntity
{
    private readonly List<TimeEntryTag> _tags = new();
    private readonly List<TimeEntryDimensionValue> _dimensionValues = new();

    public Guid OwnerUserId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public int DurationMinutes { get; private set; }
    public Guid ActivityCategoryId { get; private set; }
    public Guid? TicketSourceId { get; private set; }
    public string? TicketReference { get; private set; }
    public string Description { get; private set; } = null!;
    public bool IsAfterHours { get; private set; }
    public IReadOnlyCollection<TimeEntryTag> Tags => _tags;
    public IReadOnlyCollection<TimeEntryDimensionValue> DimensionValues => _dimensionValues;

    public TimeEntry() { }

    public TimeEntry(
        Guid ownerUserId,
        DateOnly workDate,
        int durationMinutes,
        Guid activityCategoryId,
        Guid? ticketSourceId,
        string? ticketReference,
        string description,
        bool isAfterHours,
        Guid actorUserId,
        DateTimeOffset createdAtUtc)
        : base(actorUserId, createdAtUtc)
    {
        OwnerUserId = DomainRules.Id(ownerUserId, nameof(ownerUserId));
        Apply(workDate, durationMinutes, activityCategoryId, ticketSourceId, ticketReference, description, isAfterHours);
    }

    public void Update(
        DateOnly workDate,
        int durationMinutes,
        Guid activityCategoryId,
        Guid? ticketSourceId,
        string? ticketReference,
        string description,
        bool isAfterHours,
        Guid actorUserId,
        DateTimeOffset modifiedAtUtc)
    {
        Apply(workDate, durationMinutes, activityCategoryId, ticketSourceId, ticketReference, description, isAfterHours);
        Touch(actorUserId, modifiedAtUtc);
    }

    public void ReplaceTags(IEnumerable<Guid> tagIds, Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        var ids = tagIds?.Distinct().ToArray() ?? throw new DomainRuleViolationException("Tags are required.");
        if (ids.Any(x => x == Guid.Empty))
            throw new DomainRuleViolationException("A tag identifier is invalid.");

        _tags.Clear();
        _tags.AddRange(ids.Select(x => new TimeEntryTag(Id, x)));
        Touch(actorUserId, modifiedAtUtc);
    }

    public void ReplaceDimensionValues(
        IEnumerable<TimeEntryDimensionValue> values,
        Guid actorUserId,
        DateTimeOffset modifiedAtUtc)
    {
        var replacements = values?.ToArray() ?? throw new DomainRuleViolationException("Dimension values are required.");
        if (replacements.GroupBy(x => x.CustomDimensionId).Any(x => x.Count() > 1))
            throw new DomainRuleViolationException("A dimension may have only one value per time entry.");

        _dimensionValues.Clear();
        _dimensionValues.AddRange(replacements.Select(x => x.ForEntry(Id)));
        Touch(actorUserId, modifiedAtUtc);
    }

    private void Apply(
        DateOnly workDate,
        int durationMinutes,
        Guid activityCategoryId,
        Guid? ticketSourceId,
        string? ticketReference,
        string description,
        bool isAfterHours)
    {
        if (workDate == default)
            throw new DomainRuleViolationException("Work date is required.");
        if (durationMinutes is < 1 or > 1440)
            throw new DomainRuleViolationException("Duration must be between 1 and 1440 minutes.");

        ActivityCategoryId = DomainRules.Id(activityCategoryId, nameof(activityCategoryId));
        TicketReference = DomainRules.Optional(ticketReference, nameof(ticketReference), 160);
        if (TicketReference is not null && ticketSourceId is null)
            throw new DomainRuleViolationException("A ticket source is required when a ticket reference is supplied.");

        WorkDate = workDate;
        DurationMinutes = durationMinutes;
        TicketSourceId = ticketSourceId is null ? null : DomainRules.Id(ticketSourceId.Value, nameof(ticketSourceId));
        Description = DomainRules.Required(description, nameof(description), 4000);
        IsAfterHours = isAfterHours;
    }
}

public sealed class TimeEntryTag
{
    public Guid TimeEntryId { get; private set; }
    public Guid TagId { get; private set; }

    public TimeEntryTag() { }

    public TimeEntryTag(Guid timeEntryId, Guid tagId)
    {
        TimeEntryId = DomainRules.Id(timeEntryId, nameof(timeEntryId));
        TagId = DomainRules.Id(tagId, nameof(tagId));
    }
}

public sealed class TimeEntryDimensionValue
{
    public Guid TimeEntryId { get; private set; }
    public Guid CustomDimensionId { get; private set; }
    public Guid? CustomDimensionOptionId { get; private set; }
    public string? TextValue { get; private set; }

    public TimeEntryDimensionValue() { }

    public TimeEntryDimensionValue(
        Guid customDimensionId,
        Guid? customDimensionOptionId,
        string? textValue)
    {
        CustomDimensionId = DomainRules.Id(customDimensionId, nameof(customDimensionId));
        CustomDimensionOptionId = customDimensionOptionId;
        TextValue = DomainRules.Optional(textValue, nameof(textValue), 500);

        if ((CustomDimensionOptionId is null) == (TextValue is null))
            throw new DomainRuleViolationException("A dimension value must contain either an option or text.");
    }

    internal TimeEntryDimensionValue ForEntry(Guid timeEntryId)
    {
        TimeEntryId = DomainRules.Id(timeEntryId, nameof(timeEntryId));
        return this;
    }
}

public sealed class ReportDefinition : AuditedEntity
{
    public string Name { get; private set; } = null!;
    public Guid OwnerUserId { get; private set; }
    public bool IsShared { get; private set; }
    public ReportFilter Filter { get; private set; } = new();

    public ReportDefinition() { }

    public ReportDefinition(
        string name,
        Guid ownerUserId,
        bool isShared,
        ReportFilter filter,
        Guid actorUserId,
        DateTimeOffset createdAtUtc)
        : base(actorUserId, createdAtUtc)
    {
        Name = DomainRules.Required(name, nameof(name), 160);
        OwnerUserId = DomainRules.Id(ownerUserId, nameof(ownerUserId));
        IsShared = isShared;
        Filter = filter ?? throw new DomainRuleViolationException("A report filter is required.");
        Filter.Validate();
    }

    public void Update(string name, bool isShared, ReportFilter filter, Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        Name = DomainRules.Required(name, nameof(name), 160);
        IsShared = isShared;
        Filter = filter ?? throw new DomainRuleViolationException("A report filter is required.");
        Filter.Validate();
        Touch(actorUserId, modifiedAtUtc);
    }
}

public sealed class ReportFilter
{
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public IReadOnlyCollection<Guid> UserIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyCollection<Guid> ActivityCategoryIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyCollection<Guid> TicketSourceIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyCollection<Guid> TagIds { get; init; } = Array.Empty<Guid>();
    public string? TicketReferenceContains { get; init; }
    public bool? IsAfterHours { get; init; }
    public IReadOnlyCollection<ReportDimensionFilter> DimensionFilters { get; init; } = Array.Empty<ReportDimensionFilter>();

    public void Validate()
    {
        if (FromDate is not null && ToDate is not null && FromDate > ToDate)
            throw new DomainRuleViolationException("The report start date cannot be after the end date.");

        if (UserIds.Concat(ActivityCategoryIds).Concat(TicketSourceIds).Concat(TagIds).Any(x => x == Guid.Empty))
            throw new DomainRuleViolationException("A report filter contains an invalid identifier.");

        if (DimensionFilters.GroupBy(x => x.CustomDimensionId).Any(x => x.Count() > 1))
            throw new DomainRuleViolationException("A report filter may contain one value per dimension.");
    }
}

public sealed class ReportDimensionFilter
{
    public Guid CustomDimensionId { get; init; }
    public Guid? CustomDimensionOptionId { get; init; }
    public string? TextValue { get; init; }
}

public enum ScheduleRecurrenceKind
{
    Cron = 1
}

public enum ReportDestinationKind
{
    ProtectedFileSystem = 1,
    SmtpAttachment = 2
}

public sealed class ReportSchedule : AuditedEntity
{
    public Guid ReportDefinitionId { get; private set; }
    public string Name { get; private set; } = null!;
    public ScheduleRecurrenceKind RecurrenceKind { get; private set; }
    public string RecurrenceExpression { get; private set; } = null!;
    public string TimeZoneId { get; private set; } = null!;
    public ReportDestinationKind DestinationKind { get; private set; }
    public string Destination { get; private set; } = null!;
    public bool IsEnabled { get; private set; }
    public DateTimeOffset? NextRunAtUtc { get; private set; }
    public DateTimeOffset? LeaseUntilUtc { get; private set; }
    public string? LeaseOwner { get; private set; }

    public ReportSchedule() { }

    public ReportSchedule(
        Guid reportDefinitionId,
        string name,
        string recurrenceExpression,
        string timeZoneId,
        ReportDestinationKind destinationKind,
        string destination,
        DateTimeOffset? nextRunAtUtc,
        Guid actorUserId,
        DateTimeOffset createdAtUtc)
        : base(actorUserId, createdAtUtc)
    {
        ReportDefinitionId = DomainRules.Id(reportDefinitionId, nameof(reportDefinitionId));
        Name = DomainRules.Required(name, nameof(name), 160);
        RecurrenceKind = ScheduleRecurrenceKind.Cron;
        RecurrenceExpression = DomainRules.Required(recurrenceExpression, nameof(recurrenceExpression), 160);
        TimeZoneId = DomainRules.Required(timeZoneId, nameof(timeZoneId), 128);
        DestinationKind = destinationKind;
        Destination = DomainRules.Required(destination, nameof(destination), 1000);
        NextRunAtUtc = nextRunAtUtc is null ? null : DomainRules.Utc(nextRunAtUtc.Value, nameof(nextRunAtUtc));
        IsEnabled = true;
    }

    public bool TryLease(string workerId, DateTimeOffset nowUtc, TimeSpan duration)
    {
        nowUtc = DomainRules.Utc(nowUtc, nameof(nowUtc));
        if (!IsEnabled || NextRunAtUtc is null || NextRunAtUtc > nowUtc || duration <= TimeSpan.Zero)
            return false;
        if (LeaseUntilUtc is not null && LeaseUntilUtc > nowUtc)
            return false;

        LeaseOwner = DomainRules.Required(workerId, nameof(workerId), 128);
        LeaseUntilUtc = nowUtc.Add(duration);
        return true;
    }

    public void CompleteRun(DateTimeOffset nextRunAtUtc, Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        NextRunAtUtc = DomainRules.Utc(nextRunAtUtc, nameof(nextRunAtUtc));
        LeaseOwner = null;
        LeaseUntilUtc = null;
        Touch(actorUserId, modifiedAtUtc);
    }

    public void SetEnabled(bool enabled, Guid actorUserId, DateTimeOffset modifiedAtUtc)
    {
        IsEnabled = enabled;
        if (!enabled)
        {
            LeaseOwner = null;
            LeaseUntilUtc = null;
        }
        Touch(actorUserId, modifiedAtUtc);
    }
}

public enum ReportExecutionStatus
{
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

public sealed class ReportExecution
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ReportScheduleId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public ReportExecutionStatus Status { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? FailureMessage { get; private set; }
    public Guid? ReportArtifactId { get; private set; }

    public ReportExecution() { }

    public ReportExecution(Guid reportScheduleId, string idempotencyKey, DateTimeOffset startedAtUtc)
    {
        ReportScheduleId = DomainRules.Id(reportScheduleId, nameof(reportScheduleId));
        IdempotencyKey = DomainRules.Required(idempotencyKey, nameof(idempotencyKey), 160);
        StartedAtUtc = DomainRules.Utc(startedAtUtc, nameof(startedAtUtc));
        Status = ReportExecutionStatus.Running;
    }

    public void Succeed(Guid artifactId, DateTimeOffset completedAtUtc)
    {
        EnsureRunning();
        ReportArtifactId = DomainRules.Id(artifactId, nameof(artifactId));
        CompletedAtUtc = DomainRules.Utc(completedAtUtc, nameof(completedAtUtc));
        Status = ReportExecutionStatus.Succeeded;
    }

    public void Fail(string failureMessage, DateTimeOffset completedAtUtc)
    {
        EnsureRunning();
        FailureMessage = DomainRules.Required(failureMessage, nameof(failureMessage), 4000);
        CompletedAtUtc = DomainRules.Utc(completedAtUtc, nameof(completedAtUtc));
        Status = ReportExecutionStatus.Failed;
    }

    private void EnsureRunning()
    {
        if (Status != ReportExecutionStatus.Running)
            throw new DomainRuleViolationException("Only running executions may be completed.");
    }
}

public enum ReportArtifactKind
{
    Csv = 1
}

public sealed class ReportArtifact
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ReportExecutionId { get; private set; }
    public ReportArtifactKind Kind { get; private set; }
    public string FileName { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long LengthBytes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public ReportArtifact() { }

    public ReportArtifact(
        Guid reportExecutionId,
        ReportArtifactKind kind,
        string fileName,
        string storageKey,
        string contentType,
        long lengthBytes,
        DateTimeOffset createdAtUtc)
    {
        ReportExecutionId = DomainRules.Id(reportExecutionId, nameof(reportExecutionId));
        Kind = kind;
        FileName = DomainRules.Required(fileName, nameof(fileName), 255);
        StorageKey = DomainRules.Required(storageKey, nameof(storageKey), 1000);
        ContentType = DomainRules.Required(contentType, nameof(contentType), 128);
        if (lengthBytes < 0)
            throw new DomainRuleViolationException("Artifact length cannot be negative.");
        LengthBytes = lengthBytes;
        CreatedAtUtc = DomainRules.Utc(createdAtUtc, nameof(createdAtUtc));
    }
}

public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
    Enabled = 4,
    Disabled = 5,
    Executed = 6
}

public sealed class AuditEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? ActorUserId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;
    public AuditAction Action { get; private set; }
    public string? BeforeSnapshot { get; private set; }
    public string? AfterSnapshot { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public AuditEvent() { }

    public AuditEvent(
        Guid? actorUserId,
        string entityType,
        string entityId,
        AuditAction action,
        string? beforeSnapshot,
        string? afterSnapshot,
        DateTimeOffset occurredAtUtc)
    {
        ActorUserId = actorUserId is null ? null : DomainRules.Id(actorUserId.Value, nameof(actorUserId));
        EntityType = DomainRules.Required(entityType, nameof(entityType), 128);
        EntityId = DomainRules.Required(entityId, nameof(entityId), 128);
        Action = action;
        BeforeSnapshot = DomainRules.Optional(beforeSnapshot, nameof(beforeSnapshot), 32000);
        AfterSnapshot = DomainRules.Optional(afterSnapshot, nameof(afterSnapshot), 32000);
        OccurredAtUtc = DomainRules.Utc(occurredAtUtc, nameof(occurredAtUtc));
    }
}