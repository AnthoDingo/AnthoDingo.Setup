# AnthoDingo.Setup

*[Version française](README.md)*

First-run "setup" middleware for ASP.NET Core.

Until the application is configured, every request is redirected to a `/setup`
page **provided by the library** (optional license and pre-install tasks,
database form, administrator account). On submit: connection test → migrations/seed → admin creation → writes an
`appsettings.local.json` → restart.

- **4 supported database types**: SQL Server, MySQL/MariaDB, PostgreSQL and
  SQLite (local file). The wizard shows a selector with the fields relevant to
  each type; the host application can restrict the list that is offered.
- **License and prerequisites** (optional): a license page, with an optional
  mandatory acceptance checkbox, and application-specific pre-install tasks
  (prerequisite checks, disclaimer…) before the connection step.
- **File-based detection**: no database call on every request.
- **Built-in offline page**: Bootstrap + Bootstrap Icons are **embedded in the
  assembly** and served under `/setup/_assets/` — no dependency on a CDN or on
  the application's `wwwroot`.
- **Agnostic** of the DbContext and user model via the `ISetupInitializer`
  interface.

Targets: `net8.0` and `net10.0`.

## Drivers used

| Database | Driver (connection test) |
|----------|---------------------------|
| SQL Server | `Microsoft.Data.SqlClient` |
| MySQL / MariaDB | `MySqlConnector` |
| PostgreSQL | `Npgsql` |
| SQLite | `Microsoft.Data.Sqlite` |

These drivers are only used to **test the connection** during setup. On the
application side, use whichever EF Core provider (or other ORM) you prefer —
see the example project, which uses `Microsoft.EntityFrameworkCore.SqlServer`,
`Pomelo.EntityFrameworkCore.MySql` (built on MySqlConnector),
`Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.Sqlite`.

## Usage

### 1. Implement `ISetupInitializer`

```csharp
public sealed class AppSetupInitializer : ISetupInitializer
{
    public async Task InitializeDatabaseAsync(DbProvider provider, string cs, CancellationToken ct = default)
    {
        await using AppDbContext db = AppDbContext.Create(provider, cs);
        await db.Database.MigrateAsync(ct);
        // … seed roles / reference data
    }

    public async Task CreateAdminAsync(DbProvider provider, string cs, AdminAccount admin, CancellationToken ct = default)
    {
        await using AppDbContext db = AppDbContext.Create(provider, cs);
        // … create the user (Identity PasswordHasher, BCrypt, etc.)
    }
}

// The DbContext picks the EF Core provider matching the database selected
// in the wizard.
public static class AppDbContextFactory
{
    public static AppDbContext Create(DbProvider provider, string cs)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        switch (provider)
        {
            case DbProvider.SqlServer: builder.UseSqlServer(cs); break;
            case DbProvider.MySql:     builder.UseMySql(cs, ServerVersion.AutoDetect(cs)); break;
            case DbProvider.Postgres:  builder.UseNpgsql(cs); break;
            case DbProvider.Sqlite:    builder.UseSqlite(cs); break;
        }
        return new AppDbContext(builder.Options);
    }
}
```

### 2. Register and wire it up

```csharp
using AnthoDingo.Setup;

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true);
builder.Services.AddFileBasedSetup<AppSetupInitializer>();

// To restrict which database types the wizard offers (default: all 4):
// builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
//     o.AllowedProviders = [DbProvider.Postgres, DbProvider.Sqlite]);

// To identify the admin account by a username instead of an email address:
// builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
//     o.AllowUsernameAdmin = true);

var app = builder.Build();

app.UseSetupMiddleware("My Application");   // built-in /setup page

var setup = app.Services.GetRequiredService<SetupService>();
if (setup.IsSetupComplete())
{
    // setup.GetConfiguredProvider() returns the DbProvider chosen at install
    // time — useful to rebuild the right DbContextOptionsBuilder on every startup.
}
```

### 3. (Optional) License and pre-install tasks

Before the database connection, the wizard can show a **license** page, then one
page per **pre-install task** (prerequisite checks, disclaimer…). Order: License →
tasks (registration order) → Connection → Database → Admin.

```csharp
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
{
    o.LicenseText = File.ReadAllText("LICENSE"); // license page + "Next" button
    o.RequireLicenseAcceptance = true;           // mandatory "I accept" checkbox
});
builder.Services.AddSetupPreInstallTask<PrerequisitesCheck>();

public sealed class PrerequisitesCheck : ISetupPreInstallTask
{
    public string Title => "Prerequisites";

    public Task<SetupTaskResult> ExecuteAsync(CancellationToken ct = default) =>
        Task.FromResult(Environment.Is64BitProcess
            ? SetupTaskResult.Ok("<p>64-bit process: OK.</p>")      // "Next" button
            : SetupTaskResult.Fail("A 64-bit process is required.")); // "Retry" button
}
```

- The task runs every time its page is shown (and on every "Retry"); an exception
  is shown as a failure. `Html` is inserted **unencoded** (application content,
  never user input); `Error` is encoded.
- A disclaimer is just a task that always returns `SetupTaskResult.Ok(html)`.
- Progress (license accepted, tasks passed) travels in an encrypted token: a forged
  POST cannot skip the license or a failing prerequisite.

> To provide your own setup page instead of the built-in one, use
> `app.UseSetupGate()` (gate only, no page). The license and pre-install tasks
> then only apply to the built-in page.

### 4. (optional) Add extra steps

A host application can extend the initial setup by adding its own steps,
inserted in the wizard **between admin account creation and the final
restart** (preferences, licensing, business-specific configuration…).

```csharp
public sealed class CompanySetupStep : ISetupExtraStep
{
    public string Id => "company";
    public string Label => "Company";

    public Task<string> RenderAsync(SetupExtraStepContext ctx, CancellationToken ct) =>
        Task.FromResult($"""
          <form method="post" action="/setup">
            <input type="hidden" name="step" value="{Id}" />
            <input type="hidden" name="pendingState" value="{ctx.PendingStateToken}" />
            <input type="text" class="form-control" name="companyName" required />
            <button type="submit" class="btn btn-primary w-100">Continue</button>
          </form>
          """);

    public async Task<SetupExtraStepResult> HandleAsync(SetupExtraStepContext ctx, IFormCollection form, CancellationToken ct)
    {
        string name = form["companyName"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(name))
            return SetupExtraStepResult.Failure("Company name is required.");

        await using AppDbContext db = AppDbContext.Create(ctx.Provider, ctx.ConnectionString);
        db.Settings.Add(new AppSettings { CompanyName = name });
        await db.SaveChangesAsync(ct);
        return SetupExtraStepResult.Success();
    }
}
```

```csharp
builder.Services.AddSetupStep<CompanySetupStep>();
// Multiple calls chain in their registration order.
```

The step provides its own complete `<form>` (fields + button); the library
only takes care of the chrome (logo, stepper, error message) and
automatically inserts the step's `Label` into the stepper. Like
`ISetupInitializer`, the step is resolved in a dedicated scope, so it can
inject a `DbContext` or any other dependency normally.

## Example project

`src/AnthoDingo.Setup.Example` is a minimal ASP.NET Core application (API + EF
Core) showing the full integration: an `ISetupInitializer` implementation, an
`AppDbContext` that switches between the 4 EF Core providers, a
`WritableContentRootCheck` pre-install task (application folder is writable), and
the middleware wired up in `Program.cs`.

```bash
dotnet run --project src/AnthoDingo.Setup.Example
```

Then open `/setup`: pass the prerequisite check, pick a database type, test the connection, initialize the
schema, create the administrator account, and fill in the company name (a
sample extra step, see `CompanySetupStep`).

## API

| Member | Role |
|--------|------|
| `AddFileBasedSetup<TInitializer>(configure?)` | Registers `SetupService` (singleton) and the initializer. |
| `UseSetupMiddleware(appName)` | Gate + built-in `/setup` page (the name is displayed). |
| `UseSetupGate()` | Gate only (page provided by the application). |
| `SetupService.IsSetupComplete()` | Reads `appsettings.local.json`. |
| `SetupService.GetConfiguredProvider()` | Reads the `DbProvider` chosen at install time. |
| `SetupService.TestConnectionAsync(provider, cs)` | Tests a connection (SQL Server, MySQL, PostgreSQL or SQLite). |
| `SetupService.BuildSqlConnectionString(...)` | Builds a SQL Server connection string. |
| `SetupService.BuildMySqlConnectionString(...)` | Builds a MySQL/MariaDB connection string. |
| `SetupService.BuildPostgresConnectionString(...)` | Builds a PostgreSQL connection string. |
| `SetupService.BuildSqliteConnectionString(...)` | Builds a SQLite (file) connection string. |
| `SetupService.CompleteSetup(provider, cs)` | Writes `appsettings.local.json` (`Setup:IsComplete`, `Setup:Provider`, connection string). |
| `ISetupInitializer` | Implemented by the app: migrations + admin creation, receives the `DbProvider`. |
| `AddSetupStep<TStep>()` | Adds an extra step (`ISetupExtraStep`) between admin creation and the restart. |
| `ISetupExtraStep` | Extra step provided by the app: `Id`, `Label`, `RenderAsync`, `HandleAsync`. |
| `SetupExtraStepContext` | Context passed to the step (provider, connection string, state token, error, posted values). |
| `SetupExtraStepResult` | Result of `HandleAsync`: `Success()` or `Failure(message)`. |
| `AdminAccount(UserName, Password, DisplayName?)` | Admin account to create. |
| `DbProvider` | Enum: `SqlServer`, `MySql`, `Postgres`, `Sqlite`. |
| `SetupOptions.AllowedProviders` | Database types offered in the wizard (default: all 4). |
| `SetupOptions.AllowUsernameAdmin` | If `true`, the step 3 admin is identified by a username instead of an email (default `false`). |
| `SetupOptions.LicenseText` | License text: when set, a "License" page with a "Next" button is shown before the connection step (default: `null`). |
| `SetupOptions.RequireLicenseAcceptance` | If `true`, the license page shows a mandatory "I accept the license terms" checkbox (default: `false`). |
| `AddSetupPreInstallTask<TTask>()` | Adds a pre-install task (prerequisites, disclaimer…) shown before the connection step. |
| `ISetupPreInstallTask` / `SetupTaskResult` | Pre-install task (`Title`, `ExecuteAsync`) and its result (`Ok(html)` / `Fail(error, html)`). |
| `SetupOptions` | Customization (path, allowed prefixes, connection string name…). |

## Breaking change (v2.0.0)

`ISetupInitializer.InitializeDatabaseAsync` and `CreateAdminAsync` now take a
first `DbProvider provider` parameter, required to build the right
`DbContextOptionsBuilder` (`UseSqlServer`/`UseMySql`/`UseNpgsql`/`UseSqlite`) on
the application side. `SetupService.CompleteSetup` also now takes the
`provider` as a parameter.

## License

MIT
