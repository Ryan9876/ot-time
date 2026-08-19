# IIS Deployment and Operations

## Prerequisites

- Windows Server with IIS installed.
- IIS Management Console and URL Rewrite module.
- .NET 8 Hosting Bundle matching the deployed application runtime.
- SQL Server instance reachable from both the IIS application pool identity and the report worker service account.
- TLS certificate and approved hostname, or an approved IIS child-application path.
- A dedicated Windows service account for the scheduled-report worker.
- SMTP relay and/or approved protected filesystem or network-share destination for scheduled reports.
- Confirmed organization timezone, daylight-saving policy, retention policy, backup objectives, recipient rules, and permitted export paths.

Do not deploy production secrets, connection strings, passwords, SMTP credentials, or share credentials in source control, published artifacts, or `appsettings*.json` files.

## SQL Server Setup

Create a dedicated database and least-privilege SQL login or Windows principal. SQL Server authentication is acceptable only when approved by local policy; Windows integrated authentication is preferred where service identities are managed.

Example database creation:

    CREATE DATABASE TimeLogging;
    GO

Create a login/user for the web application and worker. The application identity requires schema migration permission during deployment and normal data access afterward. Prefer a separate migration identity for production deployments:

- Migration identity: permitted to create and alter the application schema.
- Runtime web identity: normal data access only.
- Runtime worker identity: normal data access only.

The web application and worker must use the same database and compatible application version.

Recommended SQL Server settings:

- Use a supported SQL Server version and current servicing level.
- Enable regular full, differential, and transaction-log backups as applicable to the recovery model.
- Monitor database size, log growth, failed logins, blocking, deadlocks, and backup job status.
- Protect backups with encryption and restrict restore access.
- Test restoration regularly to a separate environment.

## Configuration and Secrets

Configuration is environment-specific. Production values may be supplied through IIS environment variables, service environment variables, an approved secret store, or managed configuration tooling. Development-only secrets may use .NET user secrets.

Use the deployed application's documented configuration keys. Typical production values include:

| Purpose | Example environment variable |
|---|---|
| SQL connection string | `ConnectionStrings__DefaultConnection` |
| ASP.NET Core environment | `ASPNETCORE_ENVIRONMENT=Production` |
| Application path base | `PathBase` |
| Forwarded headers enablement | `ForwardedHeaders__Enabled=true` |
| First administrator email | `BootstrapAdmin__Email` |
| First administrator temporary password | `BootstrapAdmin__Password` |
| Bootstrap enablement | `BootstrapAdmin__Enabled=true` |
| SMTP host | `Smtp__Host` |
| SMTP port | `Smtp__Port` |
| SMTP username | `Smtp__Username` |
| SMTP password | `Smtp__Password` |
| SMTP sender address | `Smtp__FromAddress` |
| Report artifact root | `Reporting__ArtifactRoot` |
| Allowed export roots | `Reporting__AllowedDestinationRoots` |
| Worker service identity/configuration | service-specific environment configuration |

Use the double-underscore (`__`) separator for nested .NET configuration keys when setting environment variables.

Requirements:

- Set `ASPNETCORE_ENVIRONMENT` to `Production`.
- Use a unique, protected ASP.NET Core Data Protection key store shared by all web instances if the site is scaled out.
- Do not use ephemeral Data Protection keys in production; cookie decryption and password-reset/security tokens may otherwise fail after restart.
- Protect machine-level, IIS, service, and secret-store access with least privilege.
- Restrict report destinations to approved local directories or approved UNC roots.
- Configure SMTP with TLS where supported and use a dedicated relay credential where required.
- Set a stable application timezone policy for report schedules. Scheduled reports must store and execute using their configured timezone rather than the server-local timezone.

## Database Migrations

Run EF Core migrations before starting a new application version. Do not rely on the web application pool to apply production schema changes automatically.

From a deployment workstation or controlled server with the migration tooling and production configuration available:

    dotnet ef database update --project src/Infrastructure --startup-project src/Web --configuration Release

If the repository provides a migration bundle, use the versioned bundle instead:

    TimeLogging.Migrations.exe --connection "<production connection string>"

Use the repository's actual project paths and migration-bundle name if they differ.

Before migration:

1. Take and verify a database backup.
2. Review the migration list and generated SQL.
3. Confirm the target application version is compatible with the migrated schema.
4. Schedule a maintenance window for migrations that may lock or rewrite large tables.
5. Confirm the web and worker are stopped or placed in maintenance mode if the migration is not backward-compatible.

After migration:

1. Verify the migration history table contains the expected version.
2. Start the web application and worker.
3. Confirm health checks, sign-in, entry creation, reporting, and worker schedule execution.
4. Retain the previous application artifact until rollback risk has passed.

## First Administrator Bootstrap

Self-registration is disabled. Bootstrap the first administrator only through one-time deployment configuration.

Before the first application start, set:

    BootstrapAdmin__Enabled=true
    BootstrapAdmin__Email=administrator@example.invalid
    BootstrapAdmin__Password=<long unique temporary password>

The temporary password must comply with the configured password policy. Pass these values through protected environment configuration or an approved secret store.

Start the application, sign in with the bootstrap account, and immediately change the temporary password. Confirm the account has the Administrator role.

After successful bootstrap:

1. Remove `BootstrapAdmin__Password`.
2. Set `BootstrapAdmin__Enabled=false` or remove it.
3. Recycle the IIS application pool.
4. Record the bootstrap completion in the deployment record.
5. Create additional administrators through the administrative user-management interface.

Never leave bootstrap credentials enabled in production. If the application detects an existing administrator, it should not create or modify one from bootstrap settings.

## Install the .NET Hosting Bundle

Install the supported .NET 8 Hosting Bundle on the IIS server. The bundle installs the .NET runtime, ASP.NET Core Module, and IIS integration components required for framework-dependent ASP.NET Core hosting.

1. Download the current supported .NET 8 Hosting Bundle from Microsoft.
2. Run the installer as an administrator.
3. Restart IIS or reboot the server:

       iisreset

4. Confirm installed runtimes:

       dotnet --list-runtimes

5. Confirm the IIS ASP.NET Core Module is present in IIS configuration.

Install servicing updates for the Hosting Bundle as part of normal Windows/.NET patching. Restart IIS after updates.

Framework-dependent deployment is preferred. Use a self-contained publish only where installation of the Hosting Bundle is not permitted; self-contained artifacts require application-managed runtime servicing.

## Publish and Deploy

Publish the Web project in Release configuration. Example:

    dotnet publish src/Web -c Release -o C:\Deploy\TimeLogging\Web

Publish the Worker separately:

    dotnet publish src/Worker -c Release -o C:\Deploy\TimeLogging\Worker

Deploy each output to a versioned directory, for example:

    D:\Apps\TimeLogging\releases\2025.01.15\web
    D:\Apps\TimeLogging\releases\2025.01.15\worker

Maintain a stable current deployment path or IIS physical-path switch process. Do not copy files over a running application when avoidable.

Recommended deployment sequence:

1. Back up the SQL database.
2. Export and securely retain current IIS and service configuration.
3. Stop the worker service.
4. Place the IIS application offline using `app_offline.htm`, or stop the application pool.
5. Deploy the new web and worker artifacts.
6. Apply database migrations.
7. Update configuration and filesystem permissions if needed.
8. Start the worker service.
9. Remove `app_offline.htm` or start the application pool.
10. Run health checks and smoke tests.
11. Monitor logs, scheduled-report execution history, and SQL errors.

Use `app_offline.htm` only during controlled maintenance. It should contain a simple maintenance response and must be removed after deployment.

## IIS: Child Application Deployment

Use this model to host the application under an existing IIS site, such as:

    https://dashboard.example.com/timelog

This model does not replace the existing dashboard.

1. In IIS Manager, select the existing parent site.
2. Create an application beneath the parent site, for example `timelog`.
3. Set the application physical path to the published Web directory.
4. Assign a dedicated application pool.
5. Configure the pool:
   - .NET CLR version: `No Managed Code`
   - Managed pipeline mode: `Integrated`
   - Identity: dedicated least-privilege service account where practical
   - Start mode: `AlwaysRunning` where approved
   - Idle Time-out: adjust or disable only if operationally required
6. Ensure the published `web.config` remains present and unmodified except for approved deployment settings.
7. Set the application's PathBase configuration to `/timelog` if the application requires explicit configuration for its selected deployment implementation.
8. Browse to `https://dashboard.example.com/timelog/`.

The application must use relative URLs or PathBase-aware URL generation. Do not hard-code root-relative asset, form, redirect, callback, or API paths that would resolve to the parent dashboard.

The parent site must not route `/timelog` requests to the dashboard application. Review parent URL Rewrite rules, reverse-proxy rules, static-file mappings, authentication settings, and error handlers.

## IIS: Separate Site and Binding Deployment

Use this model to isolate the application under its own hostname or port, for example:

    https://timelog.example.com/

1. Create a new IIS site with the published Web directory as its physical path.
2. Assign a dedicated `No Managed Code` application pool.
3. Add an HTTPS binding with the approved hostname and certificate.
4. Configure DNS for the hostname.
5. Set `PathBase` empty unless a reverse proxy adds a prefix.
6. Configure forwarded headers when IIS or an upstream reverse proxy terminates TLS or forwards host/protocol information.
7. Verify redirects use the public HTTPS hostname and do not redirect to an internal host or HTTP URL.

A separate binding is preferred when the existing dashboard has complex route ownership, incompatible authentication settings, or a need for independent TLS, lifecycle, or access controls.

## PathBase and Reverse Proxy Behavior

The public application URL, configured PathBase, IIS application path, and proxy path handling must agree.

Examples:

| Public URL | IIS model | PathBase |
|---|---|---|
| `https://dashboard.example.com/timelog/` | Child application | `/timelog` when required by the selected hosting configuration |
| `https://timelog.example.com/` | Separate site | empty |
| `https://apps.example.com/time/` behind a reverse proxy | Separate site or child application | `/time` |

Validate all of the following after deployment:

- Sign-in and sign-out redirects.
- Antiforgery-protected form posts.
- CSS, JavaScript, images, and favicon paths.
- CSV downloads.
- Error pages and access-denied redirects.
- Generated report download links.
- Any future external authentication callback URLs.

Only trust forwarded headers from known proxies. Do not enable unrestricted forwarded-header processing on an internet-facing server.

## IIS Authentication and Application Security

The initial authentication model is local ASP.NET Core Identity with secure application cookies. Anonymous IIS access is normally required so the application can present its own sign-in flow.

- Do not enable IIS Windows Authentication unless the application is explicitly configured and tested for the approved Windows/Active Directory authentication mode.
- Do not enable IIS Basic Authentication.
- Require HTTPS and redirect HTTP to HTTPS at the approved IIS/proxy layer.
- Set secure cookie behavior appropriate to HTTPS.
- Restrict access to production configuration files, logs, exports, and Data Protection keys.
- Keep the application pool identity separate from interactive administrator accounts.
- Disable directory browsing.
- Remove unused IIS modules and site features where permitted.
- Configure request-size limits appropriate to expected attachments or imports; the initial application should not require unrestricted upload limits.

Authorization is enforced by the application. IIS configuration must not grant users access to report artifact directories or private application data.

## Windows Service for Scheduled Reports

Scheduled reports run in the independent Worker project, not in an IIS application pool. The worker must remain available even when no browser session exists or the IIS site is recycled.

Create a dedicated service account:

- Deny interactive sign-in unless an approved support process requires it.
- Grant Log on as a service.
- Grant read access to worker binaries and protected configuration.
- Grant modify access only to required local artifact, log, or temporary directories.
- Grant minimum SQL Server permissions required for normal application data and schedule processing.
- Grant approved SMTP relay and network-share access only where necessary.

Install the worker after publishing it. Example using `sc.exe`:

    sc.exe create TimeLoggingWorker binPath= "\"D:\Apps\TimeLogging\current\worker\TimeLogging.Worker.exe\"" start= auto obj= "DOMAIN\svc-timelog-worker" password= "<service account password>"

If the worker is framework-dependent and launched through `dotnet`, use:

    sc.exe create TimeLoggingWorker binPath= "\"C:\Program Files\dotnet\dotnet.exe\" \"D:\Apps\TimeLogging\current\worker\TimeLogging.Worker.dll\"" start= auto

Configure service recovery:

    sc.exe failure TimeLoggingWorker reset= 86400 actions= restart/60000/restart/60000/restart/300000

Start and verify:

    sc.exe start TimeLoggingWorker
    sc.exe query TimeLoggingWorker

Configure worker secrets and connection strings using protected service configuration or an approved secret store. The worker must use the same SQL database, encryption/Data Protection requirements where applicable, timezone policy, and report-delivery configuration as the web application.

Monitor the service state, event log entries, worker logs, schedule lease failures, retry counts, and report execution history.

## Filesystem, Network Share, and SMTP Permissions

Report artifacts may contain private time-entry data. Store them outside the web root unless the application explicitly serves them through an authorized download endpoint.

Recommended local paths:

    D:\TimeLoggingData\artifacts
    D:\TimeLoggingData\logs
    D:\TimeLoggingData\keys

Permissions:

| Path | Web application pool identity | Worker service account | Administrators |
|---|---|---|---|
| Application binaries | Read/execute | Not required unless shared | Full control |
| Data Protection keys | Read/write if required | Read/write only if required | Full control |
| Report artifacts | No direct access unless serving protected downloads | Modify | Full control |
| Logs | Modify | Modify | Full control |
| Backup staging | No access | No access unless approved | Controlled access |

For UNC destinations, use explicit domain service accounts. LocalSystem and virtual application-pool identities generally cannot authenticate to remote shares reliably.

- Grant only required share and NTFS permissions.
- Use UNC paths, not mapped drive letters.
- Validate write, overwrite, retention cleanup, and failure behavior under the worker identity.
- Do not allow arbitrary administrator-entered paths unless they are validated against configured allowed roots.
- Encrypt network transport and restrict share access to approved recipients.

For SMTP:

- Permit outbound connectivity only to approved relay hosts and ports.
- Use TLS where available.
- Use a dedicated sender address and credential.
- Limit recipients according to approved reporting rules.
- Avoid logging report contents, SMTP passwords, or recipient-sensitive data.
- Test attachment size limits and relay rejection handling.

## Backup and Recovery

Back up:

- SQL Server database, including transaction logs where the recovery model requires them.
- IIS site/application configuration and bindings.
- Production environment configuration and secret references, not plaintext secrets.
- Data Protection keys.
- Report artifact storage when retention policy requires preserving generated outputs.
- Worker service configuration and service-account assignment.
- Published release artifacts or reproducible build outputs.
- Saved report definitions, schedules, execution history, audit data, and lookup configuration through the database backup.

Recommended operational targets must be agreed before production:

- Recovery point objective (RPO).
- Recovery time objective (RTO).
- Database and artifact retention periods.
- Whether report artifacts are recoverable records or disposable derived output.
- Backup encryption, offsite storage, and access controls.
- Restore-test frequency and owner.

Recovery validation must include:

1. Restoring a database backup to an isolated SQL Server instance.
2. Starting a compatible web application against the restored database.
3. Validating sign-in, owner-scoped entry access, reports, and audit history.
4. Starting the worker safely with scheduled delivery disabled or redirected in the test environment.
5. Confirming Data Protection keys permit expected cookie/token behavior where relevant.

## Upgrade Procedure

1. Review release notes, migration notes, configuration changes, and known issues.
2. Verify a supported .NET Hosting Bundle and SQL Server version.
3. Back up database, configuration, Data Protection keys, and required artifact storage.
4. Verify available disk space for release artifacts, SQL growth, logs, and exports.
5. Notify users of the maintenance window if downtime is expected.
6. Stop the worker service to prevent schedule execution during the upgrade.
7. Place the web application offline or stop its application pool.
8. Deploy the new version to a new release directory.
9. Apply migrations with the controlled migration identity.
10. Update configuration only for documented new settings.
11. Start the web application and worker.
12. Run health checks and smoke tests.
13. Verify one controlled scheduled-report execution and delivery destination.
14. Monitor logs and SQL Server for errors before declaring the deployment complete.

For zero- or low-downtime upgrades, only use migrations explicitly designed for compatibility with both old and new application versions. Otherwise use a maintenance window.

## Rollback Procedure

Application rollback is safe only when the database schema and data remain compatible with the previous version.

1. Stop the worker service.
2. Place the web application offline.
3. Record the failed release version, migration state, errors, and time.
4. Switch IIS to the previous known-good web artifact.
5. Switch the worker service binary path to the previous known-good worker artifact if it was changed.
6. Restore prior compatible configuration if configuration changed.
7. Start the web application and worker.
8. Run health checks and smoke tests.

Do not automatically roll back database migrations. Schema rollback can lose data or corrupt newer records. If a migration is incompatible:

1. Keep the application offline.
2. Assess whether a forward fix is safer.
3. Restore the database from the verified pre-upgrade backup only with explicit operational approval.
4. Restore compatible artifacts and configuration.
5. Validate data integrity, authorization, reports, schedules, and audit records before reopening access.

After rollback, inspect scheduled-report execution history to ensure no duplicate or missed deliveries. The worker's schedule leasing and idempotency controls reduce duplication risk but do not replace operational review.

## Health Checks

Expose health checks only according to the application's documented routes and access-control policy. At minimum, verify:

- Process/application health: application starts and serves an authenticated-safe health response.
- Database health: SQL connectivity and required schema version.
- Worker health: Windows service is running and recently processing schedules.
- Storage health: configured artifact directories are accessible to the worker.
- SMTP health: configuration is valid; use controlled test delivery rather than frequent live relay probes.
- Dependency health: disk space, certificate validity, DNS, and proxy behavior.

Example IIS/application checks:

    curl.exe -I https://timelog.example.com/health
    sc.exe query TimeLoggingWorker

Do not expose detailed health diagnostics publicly. Public probes should return only a minimal success/failure result; detailed dependency status belongs in authenticated administration tooling, protected monitoring, or server logs.

Smoke test after deployment:

1. Open the public application URL through the intended hostname/path.
2. Sign in as an administrator.
3. Create and edit a test time entry.
4. Confirm a standard user cannot access another user's entry.
5. Run a filtered report and export CSV.
6. Confirm CSV values and totals match the displayed report.
7. Confirm a disabled lookup item is unavailable for new entries but historical records remain reportable.
8. Run or observe a controlled scheduled report.
9. Confirm artifact/download or SMTP delivery succeeds.
10. Review audit history and worker execution history.

## Operational Troubleshooting

### Application returns 500.30 or fails to start

- Confirm the .NET 8 Hosting Bundle is installed and IIS was restarted afterward.
- Review Windows Event Viewer, IIS logs, ASP.NET Core stdout logs if temporarily enabled, and application logs.
- Confirm the application pool is `No Managed Code`.
- Confirm the application pool identity can read the published files and configuration.
- Verify required environment variables and connection strings are present.
- Verify SQL Server connectivity, firewall rules, DNS, and credentials.
- Confirm the database migrations were applied.
- Check that no invalid JSON or malformed environment configuration was introduced.

Enable ASP.NET Core stdout logging only temporarily for diagnosis and protect/clean the log directory afterward. It can expose operational detail and consume disk space.

### Application works at the root but fails under a child path

- Confirm the IIS item is an Application, not only a virtual directory.
- Confirm the public prefix and configured `PathBase` agree.
- Check parent-site URL Rewrite rules and route ownership.
- Check generated CSS, JavaScript, image, redirect, and form-action URLs.
- Verify proxy forwarding does not remove or duplicate the path prefix.
- Clear cached redirects and test in a private browser session.

### Redirect loops, HTTP redirects, or incorrect hostname

- Confirm HTTPS binding and certificate assignment.
- Confirm forwarded headers are enabled only for known proxies.
- Confirm the proxy forwards original scheme and host correctly.
- Confirm the application is not receiving conflicting PathBase or host values.
- Check IIS URL Rewrite HTTPS rules for loops with application-level redirects.

### Sign-in failures or users are unexpectedly signed out

- Verify server date/time synchronization.
- Verify Data Protection keys persist across application restarts.
- Confirm all scaled-out web instances share the same protected key store.
- Confirm cookie domain/path settings match the public hostname and PathBase.
- Check lockout status and password-policy requirements.
- Confirm the bootstrap account was not disabled or its temporary password expired before first use.

### Scheduled reports do not run

- Confirm `TimeLoggingWorker` is running and configured for automatic start.
- Review worker logs, Windows Event Viewer, and report execution history.
- Confirm worker SQL connectivity and permissions.
- Confirm schedules are enabled, due, and use the intended timezone.
- Check lease status, retry state, and prior failure details.
- Confirm the worker can write to the artifact directory or UNC share.
- Confirm SMTP connectivity, credentials, sender authorization, attachment limits, and recipient rules.
- Verify the service account has `Log on as a service` and required share permissions.
- Confirm the server clock and timezone are correct.

Do not run scheduled reporting inside IIS as a workaround. Repair the worker service or its dependencies.

### Reports or CSV exports are incomplete

- Verify report filters, date boundaries, timezone assumptions, enabled lookup values, and user permissions.
- Confirm displayed report and CSV use the same selected definition and query parameters.
- Check SQL Server performance, blocking, command timeouts, and database growth.
- Confirm export retention cleanup has not removed the requested artifact.
- Review CSV output using a non-spreadsheet text viewer if spreadsheet formatting appears incorrect.
- Treat spreadsheet-formula-prefixed values as security-sensitive; CSV exports should escape dangerous formula prefixes.

### Access denied to files or network shares

- Identify the effective application pool or worker service identity.
- Verify both share permissions and NTFS permissions.
- Use UNC paths rather than mapped drives.
- Confirm the identity has access when running non-interactively.
- Check antivirus, ransomware protection, controlled folder access, and endpoint policies.
- Ensure artifact directories are not beneath the IIS web root.

### Existing dashboard is affected

- Confirm the time-logging application is isolated as an IIS application or separate site.
- Review parent-level rewrite rules, authentication settings, error pages, MIME types, and static-file handlers.
- Ensure dashboard routes do not claim the configured application prefix.
- Prefer a separate hostname/binding if route or configuration isolation cannot be guaranteed.
- Restore the prior IIS configuration if the dashboard impact cannot be corrected immediately.

## Routine Operations

Review at least weekly:

- IIS and worker availability.
- Failed sign-ins, account lockouts, and administrator changes.
- Failed report schedules, retries, undelivered reports, and aging leases.
- SQL backup success and restore-test status.
- Disk space for SQL data/logs, report artifacts, application logs, and Data Protection keys.
- Certificate expiration.
- .NET, Windows, IIS, SQL Server, and dependency security updates.
- Audit records for time-entry and administrative changes.
- Export retention and unauthorized artifact access.

Review at least quarterly:

- Administrator and Reporter role assignments.
- Service-account permissions.
- SMTP recipients and permitted report destinations.
- Retention and backup policy compliance.
- Scheduled report definitions, timezones, and daylight-saving behavior.
- Recovery and rollback procedures through a controlled test.

All production changes should be recorded with artifact version, database migration version, configuration changes, operator, approval, start/end time, validation results, and rollback decision.