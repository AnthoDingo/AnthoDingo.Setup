# AnthoDingo.Setup

*[Version française](README.md)*

First-run "setup" middleware for ASP.NET Core.

Until the application is configured, every request is redirected to a `/setup`
page **provided by the library** (database form + administrator account). On
submit: connection test → migrations/seed → admin creation → writes an
`appsettings.local.json` → restart.

- **4 supported database types**: SQL Server, MySQL/MariaDB, PostgreSQL and
  SQLite (local file). The wizard shows a selector with the fields relevant to
  each type; the host application can restrict the list that is offered.
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

var app = builder.Build();

app.UseSetupMiddleware("My Application");   // built-in /setup page

var setup = app.Services.GetRequiredService<SetupService>();
if (setup.IsSetupComplete())
{
    // setup.GetConfiguredProvider() returns the DbProvider chosen at install
    // time — useful to rebuild the right DbContextOptionsBuilder on every startup.
}
```

> To provide your own setup page instead of the built-in one, use
> `app.UseSetupGate()` (gate only, no page).

## Example project

`src/AnthoDingo.Setup.Example` is a minimal ASP.NET Core application (API + EF
Core) showing the full integration: an `ISetupInitializer` implementation, an
`AppDbContext` that switches between the 4 EF Core providers, and the
middleware wired up in `Program.cs`.

```bash
dotnet run --project src/AnthoDingo.Setup.Example
```

Then open `/setup`: pick a database type, test the connection, initialize the
schema and create the administrator account.

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
| `AdminAccount(UserName, Password, DisplayName?)` | Admin account to create. |
| `DbProvider` | Enum: `SqlServer`, `MySql`, `Postgres`, `Sqlite`. |
| `SetupOptions.AllowedProviders` | Database types offered in the wizard (default: all 4). |
| `SetupOptions` | Customization (path, allowed prefixes, connection string name…). |

## Breaking change (v2.0.0)

`ISetupInitializer.InitializeDatabaseAsync` and `CreateAdminAsync` now take a
first `DbProvider provider` parameter, required to build the right
`DbContextOptionsBuilder` (`UseSqlServer`/`UseMySql`/`UseNpgsql`/`UseSqlite`) on
the application side. `SetupService.CompleteSetup` also now takes the
`provider` as a parameter.

## License

MIT
