#nullable enable
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace OtTime.Infrastructure;

public sealed class ApplicationUser : IdentityUser
{
    [MaxLength(200)] public string DisplayName { get; set; } = "";
    public bool MustChangePassword { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public abstract class AuditedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(450)] public string CreatedById { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public string? ModifiedById { get; set; }
    public DateTimeOffset? ModifiedUtc { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class LookupItem : AuditedEntity
{
    [MaxLength(40)] public string Kind { get; set; } = "";
    [MaxLength(120)] public string Name { get; set; } = "";
    public int DisplayOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class ReportingDimension : AuditedEntity
{
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(80)] public string Key { get; set; } = "";
    public int DisplayOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class TimeEntry : AuditedEntity
{
    [MaxLength(450)] public string OwnerId { get; set; } = "";
    public DateOnly WorkDate { get; set; }
    public int DurationMinutes { get; set; }
    public Guid CategoryId { get; set; }
    public Guid? SourceId { get; set; }
    [MaxLength(200)] public string? TicketReference { get; set; }
    [MaxLength(4000)] public string Description { get; set; } = "";
    public bool IsAfterHours { get; set; }
    public LookupItem Category { get; set; } = null!;
    public LookupItem? Source { get; set; }
    public ICollection<TimeEntryTag> Tags { get; set; } = new List<TimeEntryTag>();
    public ICollection<TimeEntryDimensionValue> DimensionValues { get; set; } = new List<TimeEntryDimensionValue>();
}

public sealed class TimeEntryTag
{
    public Guid TimeEntryId { get; set; }
    public Guid TagId { get; set; }
    public TimeEntry TimeEntry { get; set; } = null!;
    public LookupItem Tag { get; set; } = null!;
}

public sealed class TimeEntryDimensionValue
{
    public Guid TimeEntryId { get; set; }
    public Guid DimensionId { get; set; }
    [MaxLength(500)] public string Value { get; set; } = "";
    public TimeEntry TimeEntry { get; set; } = null!;
    public ReportingDimension Dimension { get; set; } = null!;
}

public sealed class ReportDefinition : AuditedEntity
{
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(450)] public string OwnerId { get; set; } = "";
    public bool IsShared { get; set; }
    public string FilterJson { get; set; } = "{}";
}

public sealed class ReportSchedule : AuditedEntity
{
    public Guid ReportDefinitionId { get; set; }
    [MaxLength(100)] public string TimeZoneId { get; set; } = "UTC";
    [MaxLength(200)] public string Recurrence { get; set; } = "";
    [MaxLength(40)] public string DestinationKind { get; set; } = "";
    [MaxLength(2000)] public string Destination { get; set; } = "";
    [MaxLength(2000)] public string? Recipients { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset NextRunUtc { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    [MaxLength(100)] public string? LeaseToken { get; set; }
    public ReportDefinition ReportDefinition { get; set; } = null!;
    public ICollection<ReportScheduleRun> Runs { get; set; } = new List<ReportScheduleRun>();
}

public sealed class ReportScheduleRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScheduleId { get; set; }
    [MaxLength(200)] public string IdempotencyKey { get; set; } = "";
    [MaxLength(30)] public string Status { get; set; } = "Running";
    public int Attempt { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    [MaxLength(2000)] public string? ArtifactPath { get; set; }
    [MaxLength(4000)] public string? Error { get; set; }
    public ReportSchedule Schedule { get; set; } = null!;
}

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(450)] public string? ActorId { get; set; }
    [MaxLength(120)] public string EntityType { get; set; } = "";
    [MaxLength(100)] public string EntityId { get; set; } = "";
    [MaxLength(30)] public string Action { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}

public interface ICurrentActor
{
    string? UserId { get; }
}

public sealed class PersistenceDbContext(
    DbContextOptions<PersistenceDbContext> options,
    ICurrentActor actor) : IdentityDbContext<ApplicationUser>(options)
{
    private readonly ICurrentActor _actor = actor;

    public DbSet<LookupItem> LookupItems => Set<LookupItem>();
    public DbSet<ReportingDimension> ReportingDimensions => Set<ReportingDimension>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<TimeEntryTag> TimeEntryTags => Set<TimeEntryTag>();
    public DbSet<TimeEntryDimensionValue> TimeEntryDimensionValues => Set<TimeEntryDimensionValue>();
    public DbSet<ReportDefinition> ReportDefinitions => Set<ReportDefinition>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<ReportScheduleRun> ReportScheduleRuns => Set<ReportScheduleRun>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<LookupItem>(e =>
        {
            e.ToTable("LookupItems");
            e.HasIndex(x => new { x.Kind, x.Name }).IsUnique();
            e.HasIndex(x => new { x.Kind, x.Enabled, x.DisplayOrder });
        });

        b.Entity<ReportingDimension>(e =>
        {
            e.ToTable("ReportingDimensions");
            e.HasIndex(x => x.Key).IsUnique();
            e.HasIndex(x => new { x.Enabled, x.DisplayOrder });
        });

        b.Entity<TimeEntry>(e =>
        {
            e.ToTable("TimeEntries");
            e.HasIndex(x => new { x.OwnerId, x.WorkDate });
            e.HasIndex(x => new { x.WorkDate, x.CategoryId });
            e.HasIndex(x => new { x.SourceId, x.TicketReference });
            e.Property(x => x.WorkDate).HasColumnType("date");
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TimeEntryTag>(e =>
        {
            e.ToTable("TimeEntryTags");
            e.HasKey(x => new { x.TimeEntryId, x.TagId });
            e.HasOne(x => x.TimeEntry).WithMany(x => x.Tags).HasForeignKey(x => x.TimeEntryId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tag).WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TimeEntryDimensionValue>(e =>
        {
            e.ToTable("TimeEntryDimensionValues");
            e.HasKey(x => new { x.TimeEntryId, x.DimensionId });
            e.HasIndex(x => new { x.DimensionId, x.Value });
            e.HasOne(x => x.TimeEntry).WithMany(x => x.DimensionValues).HasForeignKey(x => x.TimeEntryId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Dimension).WithMany().HasForeignKey(x => x.DimensionId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ReportDefinition>(e =>
        {
            e.ToTable("ReportDefinitions");
            e.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
        });

        b.Entity<ReportSchedule>(e =>
        {
            e.ToTable("ReportSchedules");
            e.HasIndex(x => new { x.Enabled, x.NextRunUtc });
            e.HasIndex(x => x.LeaseUntilUtc);
            e.HasOne(x => x.ReportDefinition).WithMany().HasForeignKey(x => x.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ReportScheduleRun>(e =>
        {
            e.ToTable("ReportScheduleRuns");
            e.HasIndex(x => new { x.ScheduleId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.ScheduleId, x.StartedUtc });
            e.HasOne(x => x.Schedule).WithMany(x => x.Runs).HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditEvent>(e =>
        {
            e.ToTable("AuditEvents");
            e.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredUtc });
            e.HasIndex(x => new { x.ActorId, x.OccurredUtc });
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAudit()
    {
        var now = DateTimeOffset.UtcNow;
        var actorId = _actor.UserId;
        var events = new List<AuditEvent>();

        foreach (var entry in ChangeTracker.Entries().Where(e =>
                     e.Entity is AuditedEntity &&
                     e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var entity = (AuditedEntity)entry.Entity;
            if (entry.State == EntityState.Added)
            {
                entity.CreatedUtc = now;
                entity.CreatedById = actorId ?? entity.CreatedById;
            }
            else if (entry.State == EntityState.Modified)
            {
                entity.ModifiedUtc = now;
                entity.ModifiedById = actorId;
            }

            events.Add(new AuditEvent
            {
                ActorId = actorId,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = entity.Id.ToString("D"),
                Action = entry.State.ToString(),
                BeforeJson = entry.State == EntityState.Added ? null : Snapshot(entry.OriginalValues),
                AfterJson = entry.State == EntityState.Deleted ? null : Snapshot(entry.CurrentValues)
            });
        }

        if (events.Count != 0)
            AuditEvents.AddRange(events);
    }

    private static string Snapshot(PropertyValues values) =>
        JsonSerializer.Serialize(values.Properties.ToDictionary(
            p => p.Name,
            p => values[p] is byte[] bytes ? Convert.ToBase64String(bytes) : values[p]));
}

public sealed class TimeEntryStore(PersistenceDbContext db)
{
    public IQueryable<TimeEntry> QueryForOwner(string ownerId) =>
        db.TimeEntries
            .Where(x => x.OwnerId == ownerId)
            .Include(x => x.Category)
            .Include(x => x.Source)
            .Include(x => x.Tags).ThenInclude(x => x.Tag)
            .Include(x => x.DimensionValues).ThenInclude(x => x.Dimension);

    public IQueryable<TimeEntry> QueryForReporting(bool mayReport, string actorId) =>
        mayReport ? db.TimeEntries : QueryForOwner(actorId);

    public Task<TimeEntry?> FindOwnedAsync(Guid id, string ownerId, CancellationToken cancellationToken = default) =>
        QueryForOwner(ownerId).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task DeleteOwnedAsync(Guid id, string ownerId, CancellationToken cancellationToken = default)
    {
        var entry = await db.TimeEntries.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken)
            ?? throw new KeyNotFoundException("Time entry was not found.");
        db.TimeEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TResult> InTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

public sealed record ScheduleLease(
    Guid ScheduleId,
    Guid RunId,
    string LeaseToken,
    string IdempotencyKey,
    ReportSchedule Schedule);

public sealed class ScheduleLeaseStore(PersistenceDbContext db)
{
    public async Task<ScheduleLease?> ClaimDueAsync(
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var candidate = await db.ReportSchedules
            .AsNoTracking()
            .Where(x => x.Enabled && x.NextRunUtc <= nowUtc &&
                        (x.LeaseUntilUtc == null || x.LeaseUntilUtc < nowUtc))
            .OrderBy(x => x.NextRunUtc)
            .Select(x => new { x.Id, x.NextRunUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
            return null;

        var token = Guid.NewGuid().ToString("N");
        var leased = await db.ReportSchedules
            .Where(x => x.Id == candidate.Id && x.Enabled && x.NextRunUtc == candidate.NextRunUtc &&
                        (x.LeaseUntilUtc == null || x.LeaseUntilUtc < nowUtc))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.LeaseToken, token)
                .SetProperty(x => x.LeaseUntilUtc, nowUtc.Add(leaseDuration)), cancellationToken);

        if (leased != 1)
            return null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var schedule = await db.ReportSchedules
            .Include(x => x.ReportDefinition)
            .SingleAsync(x => x.Id == candidate.Id && x.LeaseToken == token, cancellationToken);

        var key = $"{schedule.Id:N}:{schedule.NextRunUtc.UtcTicks}";
        var run = await db.ReportScheduleRuns.SingleOrDefaultAsync(
            x => x.ScheduleId == schedule.Id && x.IdempotencyKey == key, cancellationToken);

        if (run is null)
        {
            run = new ReportScheduleRun
            {
                ScheduleId = schedule.Id,
                IdempotencyKey = key,
                Status = "Running",
                Attempt = 1,
                StartedUtc = nowUtc
            };
            db.ReportScheduleRuns.Add(run);
        }
        else
        {
            run.Status = "Running";
            run.Attempt++;
            run.StartedUtc = nowUtc;
            run.CompletedUtc = null;
            run.Error = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ScheduleLease(schedule.Id, run.Id, token, key, schedule);
    }

    public async Task CompleteAsync(
        ScheduleLease lease,
        DateTimeOffset completedUtc,
        DateTimeOffset nextRunUtc,
        string? artifactPath,
        string? error,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var schedule = await db.ReportSchedules.SingleOrDefaultAsync(
            x => x.Id == lease.ScheduleId && x.LeaseToken == lease.LeaseToken, cancellationToken);

        if (schedule is null)
            return;

        var run = await db.ReportScheduleRuns.SingleAsync(x => x.Id == lease.RunId, cancellationToken);
        run.CompletedUtc = completedUtc;
        run.ArtifactPath = artifactPath;
        run.Error = error;
        run.Status = error is null ? "Succeeded" : "Failed";

        schedule.LeaseToken = null;
        schedule.LeaseUntilUtc = null;
        schedule.NextRunUtc = nextRunUtc;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}