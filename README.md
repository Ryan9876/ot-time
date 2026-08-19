# ot-time

A low-friction, multi-account time-logging application for engineering work, external ticket requests, general work activities, and after-hours activity.

## Status

Greenfield specification and planning stage. No application code, database schema, UI assets, or deployment scripts are included yet.

## Initial Scope

- Authenticated, administrator-managed user accounts.
- Private owner-scoped time entries with server-enforced authorization.
- Time entry fields:
  - Work date
  - Duration in minutes
  - Configurable activity category
  - Optional ticket source and reference
  - Description
  - After-hours indicator
  - Optional tags and configurable reporting dimensions
- User history with create, edit, and delete operations for owned entries.
- Administrator-managed users, categories, ticket sources, tags, custom dimensions, ordering, and enabled states.
- Organization reporting for authorized Reporter and Administrator roles.
- Shared report and CSV-export query pipeline.
- Saved report definitions and independently executed scheduled reports.
- Immutable audit history for entry mutations and administrative changes.
- Manual ticket/reference entry first, with extensible connector abstractions for approved future integrations.
- IIS-compatible deployment beside an existing dashboard through either an application path or separate hostname/site binding.

## Planned Architecture

The proposed baseline is an ASP.NET Core 8 modular monolith with SQL Server:

| Project | Responsibility |
| --- | --- |
| `Domain` | Entities, value objects, invariants, and domain rules |
| `Application` | Use cases, authorization-aware contracts, reporting, and connector abstractions |
| `Infrastructure` | EF Core, SQL Server, ASP.NET Core Identity, audit storage, CSV delivery, and schedule leasing |
| `Web` | Thin MVC controllers, Razor views, authentication UI, and themed presentation |
| `Worker` | Independent Windows Service for scheduled report execution |

See [architecture guidance](docs/architecture.md).

## Local Development Quick Start

Implementation has not started. Once the solution exists, the expected local workflow is:

1. Install the supported .NET 8 SDK and SQL Server LocalDB, SQL Server Express, or an approved SQL Server instance.
2. Clone the repository.
3. Copy the development configuration template when supplied:
   ```text
   appsettings.Development.example.json -> appsettings.Development.json
   ```
4. Configure development-only secrets with user secrets or environment variables. Do not commit credentials, connection strings, SMTP settings, or delivery credentials.
5. Set the SQL Server connection string and one-time bootstrap administrator credentials.
6. Apply EF Core migrations.
7. Run the Web project for the browser application.
8. Run the Worker project separately when testing scheduled reports.
9. Sign in with the bootstrap administrator account and immediately change the temporary password.

Expected development configuration includes:

```text
ConnectionStrings__DefaultConnection
BootstrapAdmin__Email
BootstrapAdmin__TemporaryPassword
Authentication__CookieName
Reporting__DefaultTimezone
Reporting__AllowedOutputPaths
Smtp__Host
Smtp__Port
Smtp__Username
Smtp__Password
```

Exact configuration keys and commands will be finalized with implementation.

## Authentication and Authorization

Initial authentication uses local ASP.NET Core Identity accounts:

- Self-registration is disabled.
- Administrators create and manage user accounts.
- Secure cookies, password policy, lockout, antiforgery protection, and role-based authorization are required.
- Roles: `User`, `Reporter`, and `Administrator`.
- Users can retrieve, modify, delete, and export only their own entries unless an explicit reporting or administrative policy authorizes broader access.
- Identity-provider SSO or Windows/Active Directory authentication may be added later behind an application identity abstraction.

## Reporting and Scheduling

Administrators can create on-demand and saved reports filtered by:

- User
- Date range
- Category
- Ticket source
- Ticket/reference identifier
- After-hours status
- Tags
- Custom reporting dimensions

CSV exports must use the same query pipeline as displayed reports so rows and totals remain consistent. CSV values must be safely escaped, including spreadsheet-formula prefixes.

Scheduled reports run in the independent Worker service, not in a browser session or IIS application pool. Schedules include recurrence, timezone, enabled state, recipients or output destination, next-run state, execution history, leasing, idempotency, and safe retry handling.

Initial delivery targets are protected server or network paths with downloadable artifact history, plus optional SMTP attachments where approved.

## Configuration Summary

Configuration is environment-specific and external to source control.

| Area | Required Decisions |
| --- | --- |
| Database | SQL Server version, server access, database name, backup ownership |
| Hosting | IIS site, hostname or application path, TLS certificate, reverse-proxy behavior |
| Runtime | .NET Hosting Bundle availability and framework-dependent versus self-contained publishing |
| Authentication | Local accounts initially; future SSO or Windows authentication if approved |
| Reporting | Organization timezone, daylight-saving policy, retention, recipients, allowed delivery paths |
| Notifications | SMTP service, credentials, attachment limits, sender identity |
| Security | Secret-store approach, account policy, audit access, recovery objectives |
| Styling | Target dashboard URL, reusable assets, style guide, or approved screenshots |

Use environment variables, IIS configuration, user secrets for development, or an approved secret store. Never commit secrets.

## IIS Deployment

The application must coexist with the existing dashboard and must not replace or disrupt it.

Supported hosting approaches:

1. An IIS child application under a configurable application path.
2. A separate IIS site using a distinct hostname or binding.

The Web application will use relative URLs and configurable `PathBase` and forwarded-header behavior so routes and static assets work under either hosting model. Framework-dependent deployment is preferred where the .NET Hosting Bundle is available; self-contained publishing is reserved for servers that cannot install it.

See [deployment guidance](docs/deployment.md).

## Visual Theme Dependency

The application will use tokenized CSS custom properties and a shared layout so it can adopt the existing dashboard’s visual language. Visual acceptance requires one or more of:

- Target-site URL
- Source assets
- Style guide
- Approved screenshots
- Licensed logo, icon, and typography assets

Until those references are supplied, typography, palette, spacing, navigation treatment, controls, responsive behavior, and branding remain provisional.

## Project Boundaries

This project does not initially include:

- Replacement or modification of the existing dashboard.
- Public or unauthenticated access to time data.
- Payroll, billing, invoicing, expenses, employee surveillance, or HR performance management.
- Automatic two-way synchronization with all ticket systems.
- Organization-specific legal, retention, labor-law, compliance, or data-residency policy decisions.
- Browser-dependent scheduled reporting.
- Committed secrets, credentials, or production connection strings.

## Implementation Roadmap

1. Confirm environment, authentication, SQL Server, IIS, delivery, retention, timezone, and visual-design decisions.
2. Create the five-project solution and establish domain, application, persistence, and authorization boundaries.
3. Implement local account administration, bootstrap administrator flow, and owner-scoped time-entry operations.
4. Add configurable categories, sources, tags, dimensions, audit events, and concurrency handling.
5. Implement reporting, CSV export, saved report definitions, and report authorization.
6. Implement the independent Worker service, schedule leasing, execution history, retries, and approved delivery destinations.
7. Apply the approved dashboard theme and complete responsive visual review.
8. Add IIS deployment artifacts, operational documentation, monitoring guidance, backups, and recovery validation.
9. Validate acceptance criteria, including direct-request authorization tests and non-root IIS hosting.

## Production Readiness Decisions

Production deployment requires confirmation of:

- IIS and .NET runtime availability
- SQL Server version, access, backups, and recovery objectives
- Application path or hostname, TLS, and proxy configuration
- Authentication strategy
- Dashboard design references and asset licensing
- Organization timezone and daylight-saving handling
- Data retention and audit retention policy
- SMTP or network-share availability and credentials
- Recipient authorization and report-delivery rules
