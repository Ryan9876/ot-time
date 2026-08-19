using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using OtTime.Application;
using OtTime.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "OTTIME_");

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services
    .AddOptions<ScheduleWorkerOptions>()
    .Bind(builder.Configuration.GetSection(ScheduleWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWindowsService(options => options.ServiceName = "OtTime Scheduled Reports");
builder.Services.AddHostedService<ScheduledReportWorker>();

await builder.Build().RunAsync();

public sealed class ScheduledReportWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ScheduleWorkerOptions> options,
    ILogger<ScheduledReportWorker> logger) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("OtTime.Worker");
    private static readonly Meter Meter = new("OtTime.Worker");
    private static readonly Counter<long> SuccessfulRuns = Meter.CreateCounter<long>("ottime.schedule.runs.succeeded");
    private static readonly Counter<long> FailedRuns = Meter.CreateCounter<long>("ottime.schedule.runs.failed");
    private static readonly Histogram<double> RunDuration = Meter.CreateHistogram<double>("ottime.schedule.run.duration.ms");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var timeZone = ResolveTimeZone(settings.TimeZoneId);

        logger.LogInformation(
            "Scheduled report worker started. Poll interval: {PollInterval}; Time zone: {TimeZoneId}",
            settings.PollInterval,
            timeZone.Id);

        using var timer = new PeriodicTimer(settings.PollInterval);

        do
        {
            await ProcessDueSchedulesAsync(timeZone, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        logger.LogInformation("Scheduled report worker stopped.");
    }

    private async Task ProcessDueSchedulesAsync(TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            using var activity = ActivitySource.StartActivity("scheduled-reports.process");
            activity?.SetTag("schedule.timezone", timeZone.Id);

            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IScheduledReportProcessor>();

            var result = await processor.ProcessDueSchedulesAsync(
                DateTimeOffset.UtcNow,
                timeZone,
                cancellationToken);

            SuccessfulRuns.Add(result.CompletedCount);
            FailedRuns.Add(result.FailedCount);
            activity?.SetTag("schedule.completed", result.CompletedCount);
            activity?.SetTag("schedule.failed", result.FailedCount);

            logger.LogInformation(
                "Scheduled report processing completed. Claimed: {ClaimedCount}; Completed: {CompletedCount}; Failed: {FailedCount}",
                result.ClaimedCount,
                result.CompletedCount,
                result.FailedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Scheduled report processing was cancelled.");
        }
        catch (Exception exception)
        {
            FailedRuns.Add(1);
            logger.LogError(exception, "Scheduled report processing failed.");
        }
        finally
        {
            RunDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException($"Configured worker timezone '{timeZoneId}' was not found.", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new InvalidOperationException($"Configured worker timezone '{timeZoneId}' is invalid.", exception);
        }
    }
}

public sealed class ScheduleWorkerOptions
{
    public const string SectionName = "ScheduleWorker";

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(1);

    public string? TimeZoneId { get; init; } = "UTC";
}