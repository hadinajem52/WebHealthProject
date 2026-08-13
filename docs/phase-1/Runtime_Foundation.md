# Phase 1 Runtime Foundation

## Configuration

The web application loads committed defaults from `appsettings.json`, environment-specific overrides, environment variables, and ASP.NET Core user-secrets in Development. Secrets are never stored in committed settings files.

Set the local PostgreSQL connection string from the repository root:

```powershell
dotnet user-secrets set "ConnectionStrings:WebHealth" "Host=localhost;Port=5432;Database=webhealth;Username=webhealth;Password=<local-password>" --project src/WebHealth.Web
```

For non-development environments, supply the same key through protected configuration, for example `ConnectionStrings__WebHealth`.

Missing database configuration does not prevent startup. It makes readiness unhealthy until configuration and connectivity are available.

## Health endpoints

| Endpoint | Purpose | Dependencies |
|---|---|---|
| `/health/live` | Confirms the web process can handle requests | None |
| `/health/ready` | Confirms the web process and PostgreSQL are available | PostgreSQL |

Both endpoints return only `Healthy` or `Unhealthy`; they do not expose connection details or exception messages.

## Logging and errors

- Serilog writes structured console logs for local development and CI.
- Each response includes a server-generated `X-Correlation-ID`, also attached to logs as `CorrelationId`.
- Request logs include method and path but exclude query strings, headers, and bodies.
- Error pages expose only a safe message and correlation reference. Framework exception diagnostics are suppressed after the application logs the bounded exception type.
- Database readiness failures log only a bounded category or exception type.

## Database startup behavior

EF Core and Npgsql are registered in Infrastructure. Resolving the application database context requires `ConnectionStrings:WebHealth`. The application never creates or migrates the database automatically at startup; explicit migration conventions and commands are implemented in the database-foundation work.
