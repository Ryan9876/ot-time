using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OtTime.Web;

public static class OtTimePolicies
{
    public const string User = "User";
    public const string Reporting = "Reporting";
    public const string Administration = "Administration";
}

[AllowAnonymous]
[Route("account")]
public sealed class AccountController(IAccountUseCases accounts) : Controller
{
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginInput { ReturnUrl = Local(returnUrl) });

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(input);

        var result = await accounts.AuthenticateAsync(input.UserName, input.Password, cancellationToken);
        if (!result.Succeeded || result.Principal is null)
        {
            ModelState.AddModelError(string.Empty, result.IsLockedOut
                ? "This account is temporarily locked."
                : "The user name or password is incorrect.");
            return View(input);
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            result.Principal,
            new AuthenticationProperties
            {
                AllowRefresh = true,
                IsPersistent = input.RememberMe,
                ExpiresUtc = input.RememberMe ? DateTimeOffset.UtcNow.AddDays(14) : null
            });

        return Redirect(Local(input.ReturnUrl) ?? Url.Content("~/"));
    }

    [Authorize]
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    private string? Local(string? value) => !string.IsNullOrWhiteSpace(value) && Url.IsLocalUrl(value) ? value : null;
}

[Authorize(Policy = OtTimePolicies.User)]
[Route("entries")]
public sealed class TimeEntriesController(ITimeEntryUseCases entries) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] EntryListQuery query, CancellationToken cancellationToken) =>
        View(await entries.ListAsync(ActorId(), query, cancellationToken));

    [HttpGet("create")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View(new TimeEntryInput { WorkDate = DateOnly.FromDateTime(DateTime.Today), Lookup = await entries.GetLookupAsync(cancellationToken) });

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TimeEntryInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            input.Lookup = await entries.GetLookupAsync(cancellationToken);
            return View(input);
        }

        await entries.CreateAsync(ActorId(), input, cancellationToken);
        TempData["Success"] = "Time entry created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var entry = await entries.GetForEditAsync(ActorId(), id, cancellationToken);
        return entry is null ? NotFound() : View(entry);
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, TimeEntryInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            input.Lookup = await entries.GetLookupAsync(cancellationToken);
            return View(input);
        }

        var result = await entries.UpdateAsync(ActorId(), id, input, cancellationToken);
        if (result == EntryMutationResult.NotFound)
            return NotFound();
        if (result == EntryMutationResult.Conflict)
        {
            ModelState.AddModelError(string.Empty, "This entry was changed by another user. Reload and try again.");
            input.Lookup = await entries.GetLookupAsync(cancellationToken);
            return View(input);
        }

        TempData["Success"] = "Time entry updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? concurrencyToken, CancellationToken cancellationToken)
    {
        var result = await entries.DeleteAsync(ActorId(), id, concurrencyToken, cancellationToken);
        return result switch
        {
            EntryMutationResult.NotFound => NotFound(),
            EntryMutationResult.Conflict => Conflict(),
            _ => RedirectToAction(nameof(Index))
        };
    }

    private string ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated user has no identifier.");
}

[Authorize(Policy = OtTimePolicies.Administration)]
[Route("admin")]
public sealed class AdministrationController(IAdministrationUseCases administration) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await administration.GetDashboardAsync(cancellationToken));

    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken) =>
        View(await administration.ListUsersAsync(cancellationToken));

    [HttpPost("users")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUser(AdminUserInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Users", await administration.ListUsersAsync(cancellationToken));

        await administration.SaveUserAsync(ActorId(), input, cancellationToken);
        TempData["Success"] = "User saved.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet("lookups/{kind}")]
    public async Task<IActionResult> Lookup(string kind, CancellationToken cancellationToken)
    {
        var model = await administration.GetLookupAsync(kind, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("lookups/{kind}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLookup(string kind, LookupItemInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return RedirectToAction(nameof(Lookup), new { kind });

        var saved = await administration.SaveLookupAsync(ActorId(), kind, input, cancellationToken);
        return saved ? RedirectToAction(nameof(Lookup), new { kind }) : NotFound();
    }

    [HttpPost("lookups/{kind}/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLookup(string kind, Guid id, CancellationToken cancellationToken)
    {
        var deleted = await administration.DeleteLookupAsync(ActorId(), kind, id, cancellationToken);
        return deleted ? RedirectToAction(nameof(Lookup), new { kind }) : NotFound();
    }

    private string ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated user has no identifier.");
}

[Authorize(Policy = OtTimePolicies.Reporting)]
[Route("reports")]
public sealed class ReportsController(IReportUseCases reports) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ReportFilter filter, CancellationToken cancellationToken) =>
        View(await reports.RunAsync(ActorId(), filter, cancellationToken));

    [HttpGet("csv")]
    public async Task<IActionResult> Csv([FromQuery] ReportFilter filter, CancellationToken cancellationToken)
    {
        var export = await reports.ExportCsvAsync(ActorId(), filter, cancellationToken);
        return File(export.Content, "text/csv; charset=utf-8", export.FileName);
    }

    [HttpGet("definitions")]
    public async Task<IActionResult> Definitions(CancellationToken cancellationToken) =>
        View(await reports.ListDefinitionsAsync(ActorId(), cancellationToken));

    [HttpPost("definitions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDefinition(ReportDefinitionInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Definitions", await reports.ListDefinitionsAsync(ActorId(), cancellationToken));

        await reports.SaveDefinitionAsync(ActorId(), input, cancellationToken);
        TempData["Success"] = "Report definition saved.";
        return RedirectToAction(nameof(Definitions));
    }

    [Authorize(Policy = OtTimePolicies.Administration)]
    [HttpGet("schedules")]
    public async Task<IActionResult> Schedules(CancellationToken cancellationToken) =>
        View(await reports.ListSchedulesAsync(cancellationToken));

    [Authorize(Policy = OtTimePolicies.Administration)]
    [HttpPost("schedules")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSchedule(ReportScheduleInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Schedules", await reports.ListSchedulesAsync(cancellationToken));

        await reports.SaveScheduleAsync(ActorId(), input, cancellationToken);
        TempData["Success"] = "Report schedule saved.";
        return RedirectToAction(nameof(Schedules));
    }

    private string ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated user has no identifier.");
}

[Authorize(Policy = OtTimePolicies.Reporting)]
[Route("artifacts")]
public sealed class ArtifactsController(IReportArtifactUseCases artifacts) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await artifacts.ListAsync(ActorId(), cancellationToken));

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var artifact = await artifacts.OpenAsync(ActorId(), id, cancellationToken);
        return artifact is null
            ? NotFound()
            : File(artifact.Content, artifact.ContentType, artifact.FileName);
    }

    private string ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated user has no identifier.");
}

[AllowAnonymous]
[Route("error")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View(new SafeErrorViewModel(HttpContext.TraceIdentifier));

    [HttpGet("{statusCode:int}")]
    public IActionResult Status(int statusCode)
    {
        Response.StatusCode = statusCode;
        return View("Index", new SafeErrorViewModel(HttpContext.TraceIdentifier));
    }
}

public sealed class LoginInput
{
    [Required, Display(Name = "User name")]
    public string UserName { get; init; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }
    public string? ReturnUrl { get; init; }
}

public sealed class TimeEntryInput
{
    public Guid? Id { get; init; }

    [Required]
    public DateOnly WorkDate { get; init; }

    [Range(1, 1_440)]
    public int DurationMinutes { get; init; }

    [Required]
    public Guid CategoryId { get; init; }

    public Guid? SourceId { get; init; }

    [StringLength(200)]
    public string? TicketReference { get; init; }

    [Required, StringLength(4_000)]
    public string Description { get; init; } = string.Empty;

    public bool IsAfterHours { get; init; }
    public IReadOnlyCollection<Guid> TagIds { get; init; } = [];
    public IReadOnlyDictionary<string, string> Dimensions { get; init; } = new Dictionary<string, string>();
    public string? ConcurrencyToken { get; init; }
    public EntryLookup? Lookup { get; set; }
}

public sealed record EntryListQuery(DateOnly? From = null, DateOnly? To = null);
public sealed record EntryLookup;
public sealed record SafeErrorViewModel(string RequestId);
public sealed record AuthenticationResult(bool Succeeded, bool IsLockedOut, ClaimsPrincipal? Principal);
public sealed record CsvExport(byte[] Content, string FileName);
public sealed record ReportArtifact(Stream Content, string ContentType, string FileName);
public enum EntryMutationResult { Succeeded, NotFound, Conflict }

public sealed record AdminUserInput;
public sealed record LookupItemInput;
public sealed record ReportFilter;
public sealed record ReportDefinitionInput;
public sealed record ReportScheduleInput;

public interface IAccountUseCases
{
    Task<AuthenticationResult> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);
}

public interface ITimeEntryUseCases
{
    Task<object> ListAsync(string actorId, EntryListQuery query, CancellationToken cancellationToken);
    Task<EntryLookup> GetLookupAsync(CancellationToken cancellationToken);
    Task<TimeEntryInput?> GetForEditAsync(string actorId, Guid id, CancellationToken cancellationToken);
    Task CreateAsync(string actorId, TimeEntryInput input, CancellationToken cancellationToken);
    Task<EntryMutationResult> UpdateAsync(string actorId, Guid id, TimeEntryInput input, CancellationToken cancellationToken);
    Task<EntryMutationResult> DeleteAsync(string actorId, Guid id, string? concurrencyToken, CancellationToken cancellationToken);
}

public interface IAdministrationUseCases
{
    Task<object> GetDashboardAsync(CancellationToken cancellationToken);
    Task<object> ListUsersAsync(CancellationToken cancellationToken);
    Task SaveUserAsync(string actorId, AdminUserInput input, CancellationToken cancellationToken);
    Task<object?> GetLookupAsync(string kind, CancellationToken cancellationToken);
    Task<bool> SaveLookupAsync(string actorId, string kind, LookupItemInput input, CancellationToken cancellationToken);
    Task<bool> DeleteLookupAsync(string actorId, string kind, Guid id, CancellationToken cancellationToken);
}

public interface IReportUseCases
{
    Task<object> RunAsync(string actorId, ReportFilter filter, CancellationToken cancellationToken);
    Task<CsvExport> ExportCsvAsync(string actorId, ReportFilter filter, CancellationToken cancellationToken);
    Task<object> ListDefinitionsAsync(string actorId, CancellationToken cancellationToken);
    Task SaveDefinitionAsync(string actorId, ReportDefinitionInput input, CancellationToken cancellationToken);
    Task<object> ListSchedulesAsync(CancellationToken cancellationToken);
    Task SaveScheduleAsync(string actorId, ReportScheduleInput input, CancellationToken cancellationToken);
}

public interface IReportArtifactUseCases
{
    Task<object> ListAsync(string actorId, CancellationToken cancellationToken);
    Task<ReportArtifact?> OpenAsync(string actorId, Guid artifactId, CancellationToken cancellationToken);
}