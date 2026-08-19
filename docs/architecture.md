# Architecture

## Overview

The application is a lean ASP.NET Core 8 modular monolith for private multi-user engineering time logging, configurable reporting, and scheduled exports. It is designed for low-effort IIS deployment alongside an existing dashboard without replacing or tightly coupling to that dashboard.

Production baseline:

- ASP.NET Core 8 with server-rendered MVC/Razor UI
- SQL Server with EF Core migrations
- ASP.NET Core Identity using administrator-managed local accounts
- Independent Windows Service for scheduled report execution
- CSV export and protected filesystem delivery initially
- Tokenized visual theme pending approved dashboard references

## Solution Boundaries

The solution contains five projects.

| Project | Responsibility | Must Not Contain |
|---|---|---|
| `Domain` | Entities, value objects, domain invariants, enums, audit event models | EF Core, HTTP, Identity, file, email, or UI dependencies |
| `Application` | Use cases, authorization-aware contracts, report/query pipeline, validation, connector abstractions, scheduling contracts | MVC controllers, EF Core implementation details, direct infrastructure access |
| `Infrastructure` | EF Core/SQL Server persistence, ASP.NET Core Identity, audit persistence, CSV generation, artifact delivery, connector implementations, transactional schedule leasing | UI workflow logic |
| `Web` | MVC controllers, Razor views, authentication endpoints, request binding, antiforgery, presentation composition | Business rules, direct database queries, schedule execution |
| `Worker` | Windows Service host that claims and executes due report schedules | Browser/session-dependent behavior or UI concerns |

Dependencies flow inward:

```text
Web ───────────────┐
Worker ────────────┼──> Application ──> Domain
Infrastructure ────┘
Web and Worker ───────> Infrastructure composition/configuration
```

The Web and Worker hosts may be deployed and restarted independently. Scheduled reporting must continue when no user is signed in and when the IIS application pool is unavailable.

## Core Data Model

### Identity and Authorization

Identity is initially provided by ASP.NET Core Identity with local accounts.

- Self-registration is disabled.
- Administrators create, disable, and manage user accounts.
- The first administrator is bootstrapped using one-time environment-provided credentials.
- Bootstrap credentials must be removed or invalidated after initial setup.
- A temporary bootstrap password must be changed at first sign-in.
- Accounts support lockout, password policy, secure cookies, and security-stamp invalidation.

Roles:

| Role | Capabilities |
|---|---|
| `User` | Create, view, edit, and delete their own entries; view allowed personal history |
| `Reporter` | Run authorized organization reporting and exports; cannot administer configuration unless also an Administrator |
| `Administrator` | All Reporter capabilities plus user, lookup, dimension, report definition, schedule, and system administration |

Future identity providers, including Windows/Active Directory authentication or external SSO, must be introduced behind an application identity abstraction so entry ownership and authorization rules remain unchanged.

### Time Entry

A time entry represents logged work owned by one authenticated account.

Required fields:

- Entry identifier
- Owner user identifier
- Work date
- Duration in whole minutes
- Activity category
- Description/notes
- Created timestamp and actor
- Last modified timestamp and actor
- Concurrency token

Optional fields:

- Ticket source/system
- Ticket/reference identifier
- After-hours flag
- Tags
- Configured custom-dimension values

Rules:

- Duration must be positive and within an administrator-defined validation range.
- Categories must be enabled when selected for new or edited entries.
- A ticket/reference is optional so non-ticket work and after-hours activities can be recorded.
- A source may be selected without a connector.
- A manual ticket/reference remains valid even when no external integration is configured.
- Deleted entries are removed from active user history but retain an immutable audit event.
- Updates use optimistic concurrency to prevent silent overwrites.

### Configurable Lookup Data

Administrators manage configurable lookup entities:

- Activity categories
- Ticket systems/sources
- Tags
- Custom reporting dimensions and allowed values

Each lookup supports:

- Stable identifier
- Display name
- Enabled/disabled state
- Display order
- Audit metadata
- Administrative change history

Disabled values remain available for historical reporting but cannot be selected for new entries unless explicitly re-enabled. Renaming a lookup must not change historical ownership or report meaning because entries reference stable identifiers.

### Custom Reporting Dimensions

Custom dimensions permit evolving reporting needs without changing the time-entry schema for every new classification.

A dimension includes:

- Name
- Display label
- Data type and allowed input mode
- Required/optional state
- Enabled state
- Display order
- Allowed values where applicable

The initial implementation should favor controlled, administrator-managed enumerated values. Free-text dimensions require explicit approval because they reduce report consistency and may increase sensitive-data risk.

### Reports

A report definition stores reusable filters and presentation settings. It may include:

- Name and description
- Owner/creator and visibility scope
- Selected users
- Date range or relative date rule
- Categories
- Ticket sources
- Ticket/reference matching criteria
- After-hours status
- Tags
- Custom-dimension filters
- Grouping and sort selections
- Export format and selected fields
- Created and modified metadata

The report query pipeline is shared by on-screen reports, aggregate totals, CSV exports, and scheduled executions. A report must never use different filtering logic for display and export.

### Report Schedules

A schedule references a saved report definition and contains:

- Schedule identifier
- Enabled state
- Timezone
- Recurrence definition
- Delivery destination
- Recipient or protected path configuration reference
- Next-run timestamp
- Last-run timestamp
- Lease state
- Retry and execution state
- Created and modified metadata

Each execution stores:

- Execution identifier
- Idempotency key
- Claimed timestamp and worker identity
- Start and completion timestamps
- Outcome and failure details
- Generated artifact metadata
- Delivery result
- Row count and aggregate duration

Schedules must be evaluated and executed by the Worker, not by the Web application or browser.

## Authorization Model

Authorization is enforced server-side at application and persistence-query boundaries. User-interface filtering is not a security boundary.

### Entry Access

| Operation | User | Reporter | Administrator |
|---|---:|---:|---:|
| Create own entry | Yes | Yes | Yes |
| View own entries | Yes | Yes | Yes |
| Edit/delete own entries | Yes | Yes | Yes |
| View another user’s entry | No | Only through authorized reporting | Yes |
| Edit/delete another user’s entry | No | No | Only if explicitly approved by policy |
| Export own entries | Yes, if enabled | Yes | Yes |
| Export organization data | No | Yes | Yes |

All entry retrieval and mutation operations must include the authenticated owner identifier unless an explicit reporting or administration policy authorizes broader scope. Direct application requests must receive denial responses when attempting to access another user’s private entry without the required policy.

### Administrative Access

Only Administrators may:

- Create, disable, or reset users
- Manage roles
- Manage categories, ticket sources, tags, and dimensions
- Create or change organization-wide report definitions
- Configure schedules and delivery destinations
- View audit records
- Manage system-level reporting configuration

Reporter access may be narrowed further by organization policy, including user-group or report-definition restrictions, before production rollout.

## Audit Strategy

Audit records are immutable operational events. They are not replaced by normal row timestamps.

Audited actions include:

- Time-entry creation, modification, and deletion
- Category, source, tag, and dimension changes
- User activation, disablement, and role changes
- Report-definition changes
- Schedule creation, enablement, disablement, modification, and execution changes
- Delivery configuration changes where safe to record without exposing secrets
- Administrative security-sensitive actions

Each audit event includes:

- Event identifier
- Event type
- Subject type and identifier
- Actor user identifier or service identity
- Timestamp in UTC
- Correlation/request identifier where available
- Before and after operational snapshots or structured change details
- Source context, such as Web or Worker

Audit snapshots must exclude passwords, connection strings, API credentials, SMTP credentials, and other secrets. Audit retention, archival, and access rules require stakeholder approval.

## Reporting and Scheduling Flow

### On-Demand Reporting

1. A Reporter or Administrator selects a saved definition or supplies authorized filters.
2. The Application layer validates filters and authorization scope.
3. A single report query pipeline retrieves qualifying entries.
4. The same result set logic produces rows, totals, groupings, and CSV output.
5. CSV values are escaped according to CSV rules.
6. Values beginning with spreadsheet formula prefixes such as `=`, `+`, `-`, or `@` are neutralized to reduce spreadsheet formula injection risk.
7. Export downloads require an authenticated, authorized request.

### Scheduled Reporting

1. An Administrator saves a report definition and schedule with timezone and destination.
2. The Worker periodically queries for due schedules.
3. The Worker transactionally claims one schedule using a lease and unique execution/idempotency key.
4. The Worker resolves relative recurrence in the schedule timezone using the approved daylight-saving policy.
5. The shared report pipeline generates results and CSV output.
6. The Worker writes the artifact to a protected configured destination and optionally sends an approved SMTP attachment.
7. The execution outcome, artifact metadata, and next run are persisted transactionally where possible.
8. Lease expiration permits safe recovery after a service interruption.
9. Retries must not create duplicate deliveries where the destination supports idempotency; otherwise duplicate-risk behavior must be visible in execution history.

Initial delivery options:

- Protected server or network filesystem destination
- Downloadable artifact history within the application
- Optional SMTP attachment delivery

SMTP configuration, network-share credentials, recipient authorization, allowed paths, retention, and failure escalation are external deployment configuration and require stakeholder decisions.

## Connector Extension Point

Manual ticket/reference entry is the initial release behavior. Ticket sources are configurable independently of integrations.

Future external ticket-system support uses an `ITicketConnector` abstraction owned by the Application layer. Connector implementations reside in Infrastructure and may support approved capabilities such as:

- Ticket lookup
- Search/autocomplete
- Metadata enrichment
- Link generation
- Controlled import
- Optional synchronization

Connectors must not alter the core time-entry model, ownership model, or report-definition format. A connector is enabled only after approval of:

- API availability and licensing
- Authentication method and credential storage
- Rate limits and failure behavior
- Data fields to import or display
- Privacy and data-retention implications
- Support ownership and operational monitoring

No initial-release feature requires automatic synchronization or two-way ticket updates.

## Persistence and Transaction Boundaries

SQL Server is the production baseline.

EF Core migrations create and evolve:

- Application data tables
- ASP.NET Core Identity tables
- Time entries and lookup data
- Custom dimension definitions and values
- Audit events
- Report definitions
- Schedules and execution history
- Artifact metadata
- Connector configuration metadata, excluding secrets

Important database controls:

- Foreign keys and indexes for owner-scoped entry access and reporting filters
- Unique constraints where display-name uniqueness is required by scope
- Rowversion or equivalent concurrency token on mutable records
- UTC timestamps for persisted events and schedule execution state
- Transactional schedule leasing and completion updates
- No secrets stored in ordinary application tables unless an approved secret-store integration is used

Database backup, restore testing, retention, recovery objectives, and access administration are operational responsibilities requiring production approval.

## Security Controls

The application must implement the following controls.

### Authentication and Sessions

- Secure, HTTP-only, same-site cookies
- TLS required in production
- Password strength policy and account lockout
- Antiforgery protection on state-changing browser requests
- Session invalidation after password reset, disablement, or security-stamp change
- No public unauthenticated time-data or report access
- Login, logout, lockout, reset, and bootstrap events auditable where appropriate

### Authorization and Data Isolation

- Owner-scoped queries and mutations enforced server-side
- Policy-based authorization for reporting and administration
- No client-provided owner identifier trusted for access decisions
- Report filters constrained by caller authorization
- Artifact downloads authorized against report/schedule ownership and role policy
- Administrative endpoints protected from non-administrators

### Input, Export, and Web Protection

- Server-side validation for all entry, lookup, report, and schedule input
- Output encoding in Razor views
- Parameterized database access through EF Core or equivalent safe APIs
- CSRF/antiforgery protection
- Controlled file names and protected artifact paths
- CSV formula-injection mitigation
- Request logging without descriptions, credentials, tokens, or sensitive export contents
- Security headers appropriate to the approved IIS and reverse-proxy configuration

### Secret Management

Secrets must not be committed to source control, audit records, or ordinary application logs.

Supported configuration sources include:

- Environment variables
- IIS application configuration
- Development user-secrets
- Approved external secret store

Secrets include:

- Database connection strings
- Bootstrap administrator credentials
- SMTP credentials
- Network-share credentials
- Ticket-system API credentials
- Identity-provider keys and certificates

## IIS and Hosting Model

The Web project is deployed behind IIS as either:

- An IIS child application under a configurable application path, or
- A separate IIS site with its own hostname or binding

The application must use relative URLs and configurable path-base behavior so routes, form actions, static assets, and redirects work under a non-root application path.

Hosting requirements:

- Supported .NET 8 Hosting Bundle, unless a self-contained deployment is explicitly required
- Framework-dependent deployment preferred for smaller updates
- TLS termination and forwarded-header behavior configured for the actual proxy topology
- Application pool identity granted only required filesystem and network-share permissions
- Separate Worker service identity with least-privilege access to database, artifacts, and delivery systems
- No dashboard route, port, asset, or application-pool assumptions without approved co-hosting configuration

The Worker is installed and monitored as a Windows Service. It must not depend on an active browser, IIS application pool, or Web application memory state.

## Theme Contract

The visual implementation uses a shared layout and CSS custom-property design tokens. Tokens cover:

- Typography
- Color palette
- Spacing scale
- Navigation treatment
- Buttons and form controls
- Tables and reporting views
- Responsive breakpoints
- Focus, hover, disabled, and error states
- Logo and icon usage

Visual parity is not accepted until the requester provides at least one approved source of truth:

- Existing dashboard URL with access
- Source CSS/assets
- Style guide
- Approved screenshots and responsive references
- Explicit asset licensing approval

Until then, implementation uses neutral, accessible tokens that can be replaced without restructuring views or application behavior.

## Operational Observability

Operational logging should include:

- Authentication and authorization failures
- Unhandled exceptions
- Schedule claim, execution, retry, and delivery outcomes
- Connector failures and rate-limit conditions
- Deployment version and configuration validation failures

Logs must avoid entry descriptions, ticket content, passwords, connection strings, API keys, recipient secrets, and full report contents unless explicitly approved for secure diagnostics.

Health checks should distinguish:

- Web process availability
- Database connectivity
- Worker availability
- Last successful schedule execution
- Delivery subsystem availability where practical

## Required Stakeholder Decisions

Production implementation and deployment require confirmation of:

1. IIS version, .NET Hosting Bundle availability, and server ownership.
2. SQL Server version, database provisioning process, backup ownership, and access model.
3. Deployment mode: child application path, separate hostname, or separate IIS binding.
4. TLS certificate ownership, proxy behavior, forwarded headers, and external access requirements.
5. Authentication choice: local accounts, Windows/Active Directory, SSO, or another provider.
6. Initial administrator bootstrap process and password-reset ownership.
7. Approved dashboard URL, source assets, style guide, or screenshots for visual acceptance.
8. Organization timezone and daylight-saving behavior for schedules and date-based reports.
9. Data retention, audit retention, archival, backup, restore-test, and recovery objectives.
10. Whether deleted entries remain recoverable, and for how long.
11. SMTP availability, approved sender identity, recipient rules, and attachment restrictions.
12. Network-share availability, allowed paths, service-account permissions, and artifact retention.
13. Which users may receive organization-wide reports and whether Reporter scope requires further restriction.
14. Approved ticket systems, API credentials, connector scope, and synchronization expectations.
15. Allowed custom reporting dimensions and whether free-text values are permitted.
16. Maximum export size, scheduled-report frequency, and acceptable execution windows.
17. Monitoring, alerting, support ownership, and incident response expectations.

## Architecture Acceptance Conditions

The architecture is accepted when implementation demonstrates that:

- Users can manage only their own time entries unless explicitly authorized.
- Administrators can manage lookup data, users, reports, schedules, and organization-wide reporting.
- Reports and CSV exports use identical filtering and total calculations.
- Scheduled reports execute independently of browser sessions and IIS application-pool activity.
- Audit history records entry and administrative changes with actor and timestamp data.
- Manual ticket references work without a connector.
- A new connector can be added without changing the core entry or report model.
- The application deploys under an IIS child path or separate binding without disrupting the existing dashboard.
- Secrets remain external to source control.
- Visual implementation is validated against approved dashboard references.