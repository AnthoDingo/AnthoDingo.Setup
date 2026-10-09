# AnthoDingo.Setup

*[Version française](README.md) · [English version](README.en.md)*

Erststart-Middleware („Setup“) für ASP.NET Core.

Solange die Anwendung nicht konfiguriert ist, wird jede Anfrage auf eine
`/setup`-Seite umgeleitet, die **von der Bibliothek bereitgestellt wird**
(optionale Lizenz und Vorinstallationsaufgaben, Datenbankformular,
Administratorkonto). Beim Absenden: Verbindungstest → Migrationen/Seed →
Anlegen des Admins → Schreiben einer `appsettings.local.json` → Neustart.

- **4 unterstützte Datenbanktypen**: SQL Server, MySQL/MariaDB, PostgreSQL und
  SQLite (lokale Datei). Der Assistent zeigt eine Auswahl mit den passenden
  Feldern für jeden Typ; die Host-Anwendung legt die angebotenen Typen fest.
- **Kein mitgelieferter Treiber**: Die Anwendung stellt den ADO.NET-Treiber
  bereit, den sie bereits referenziert, oder fügt das Paket
  `AnthoDingo.Setup.Providers` für alle vier hinzu.
- **Lizenz und Voraussetzungen** (optional): eine Lizenzseite, auf Wunsch mit
  verpflichtendem Zustimmungs-Kontrollkästchen, sowie anwendungsspezifische
  Vorinstallationsaufgaben (Prüfung der Voraussetzungen, Hinweis…) vor dem
  Verbindungsschritt.
- **Dateibasierte Erkennung**: kein Datenbankaufruf bei jeder Anfrage.
- **Integrierte Offline-Seite**: Bootstrap + Bootstrap Icons sind **in die
  Assembly eingebettet** und werden unter `/setup/_assets/` ausgeliefert — keine
  Abhängigkeit von einem CDN oder vom `wwwroot` der Anwendung.
- **Unabhängig** vom DbContext und vom Benutzermodell über die Schnittstelle
  `ISetupInitializer`.

Zielplattform: `net10.0`.

## Installation

```bash
dotnet add package AnthoDingo.Setup
# Optional: alle 4 Treiber (sonst eigene bereitstellen, siehe „Treiber“)
dotnet add package AnthoDingo.Setup.Providers
```

| Paket | Inhalt |
|-------|--------|
| `AnthoDingo.Setup` | Middleware, Assistent `/setup`, `SetupService`. Kein Datenbanktreiber. |
| `AnthoDingo.Setup.Providers` | `AnthoDingo.Setup` + die 4 Treiber und `o.AddDefaultProviders()`. |

## Treiber

`AnthoDingo.Setup` referenziert keinen Treiber: Der Assistent testet die
Verbindung und erstellt die Verbindungszeichenfolge mit der ADO.NET-
`DbProviderFactory`, die für jeden Datenbanktyp in `SetupOptions.Providers`
registriert ist. Nur registrierte Typen werden angeboten; der zuerst hinzugefügte
ist vorausgewählt. Ohne Treiber verweigert die Anwendung den Start mit einer
eindeutigen Meldung.

| Datenbank | `DbProvider` | Treiber | Factory |
|-----------|--------------|---------|---------|
| SQL Server | `SqlServer` | `Microsoft.Data.SqlClient` | `SqlClientFactory.Instance` |
| MySQL / MariaDB | `MySql` | `MySqlConnector` | `MySqlConnectorFactory.Instance` |
| PostgreSQL | `Postgres` | `Npgsql` | `NpgsqlFactory.Instance` |
| SQLite | `Sqlite` | `Microsoft.Data.Sqlite` | `SqliteFactory.Instance` |

**Option 1 — eigene Treiber bereitstellen** (empfohlen): Der EF-Core-Provider der
Anwendung bringt den Treiber bereits mit (z. B. `Npgsql.EntityFrameworkCore.PostgreSQL`
→ `Npgsql`), es wird nichts zusätzlich veröffentlicht. Nur `AnthoDingo.Setup` wird referenziert.

```csharp
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
    o.Providers[DbProvider.Postgres] = NpgsqlFactory.Instance);
```

**Option 2 — Paket `AnthoDingo.Setup.Providers`**: enthält alle vier Treiber,
um den Preis einer größeren Veröffentlichung (insbesondere SqlClient).

```csharp
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o => o.AddDefaultProviders());
// oder nur bestimmte Typen:
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
    o.AddDefaultProviders(DbProvider.Postgres, DbProvider.Sqlite));
```

## Verwendung

### 1. `ISetupInitializer` implementieren

```csharp
public sealed class AppSetupInitializer : ISetupInitializer
{
    public async Task InitializeDatabaseAsync(DbProvider provider, string cs, CancellationToken ct = default)
    {
        await using AppDbContext db = AppDbContext.Create(provider, cs);
        await db.Database.MigrateAsync(ct);
        // … Seed der Rollen / Stammdaten
    }

    public async Task CreateAdminAsync(DbProvider provider, string cs, AdminAccount admin, CancellationToken ct = default)
    {
        await using AppDbContext db = AppDbContext.Create(provider, cs);
        // … Benutzer anlegen (Identity PasswordHasher, BCrypt usw.)
    }
}

// Der DbContext wählt den EF-Core-Provider passend zur im Assistenten
// ausgewählten Datenbank.
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

### 2. Registrieren und einbinden

```csharp
using AnthoDingo.Setup;

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true);
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
{
    o.Providers[DbProvider.Postgres] = NpgsqlFactory.Instance; // siehe „Treiber“

    // Um das Admin-Konto über einen Benutzernamen statt einer E-Mail zu identifizieren:
    // o.AllowUsernameAdmin = true;
});

var app = builder.Build();

app.UseSetupMiddleware("Meine Anwendung");   // integrierte /setup-Seite

var setup = app.Services.GetRequiredService<SetupService>();
if (setup.IsSetupComplete())
{
    // setup.GetConfiguredProvider() liefert den bei der Installation gewählten DbProvider
    // — nützlich, um bei jedem Start den richtigen DbContextOptionsBuilder aufzubauen.
}
```

### 3. (Optional) Lizenz, vorbereitende Schritte und Vorinstallationsaufgaben

Vor der Datenbankverbindung kann der Assistent eine **Lizenzseite** anzeigen,
dann eine Seite pro **vorbereitendem Schritt** (Eingabe: Aktivierungsschlüssel…),
dann eine Seite pro **Vorinstallationsaufgabe** (Prüfung der Voraussetzungen,
Hinweis…). Reihenfolge: Lizenz → vorbereitende Schritte → Aufgaben (jeweils in
Registrierungsreihenfolge) → Verbindung → Datenbank → Admin.

```csharp
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
{
    o.AddDefaultProviders();                     // oder eigene Treiber, siehe „Treiber“
    o.LicenseText = File.ReadAllText("LICENSE"); // Lizenzseite + Schaltfläche „Weiter“
    o.RequireLicenseAcceptance = true;           // verpflichtendes Kontrollkästchen „Ich stimme zu“
});
builder.Services.AddSetupPreInstallTask<PrerequisiteCheck>();

public sealed class PrerequisiteCheck : ISetupPreInstallTask
{
    public string Title => "Voraussetzungen";

    public Task<SetupTaskResult> ExecuteAsync(CancellationToken ct = default) =>
        Task.FromResult(Environment.Is64BitProcess
            ? SetupTaskResult.Ok("<p>64-Bit-Prozess: OK.</p>")             // Schaltfläche „Weiter“
            : SetupTaskResult.Fail("Ein 64-Bit-Prozess ist erforderlich.")); // Schaltfläche „Erneut versuchen“
}
```

- Die Aufgabe wird bei jeder Anzeige ihrer Seite (und bei jedem „Erneut
  versuchen“) ausgeführt; eine Ausnahme wird als Fehlschlag angezeigt. `Html`
  wird **ohne Kodierung** eingefügt (Inhalt der Anwendung, niemals
  Benutzereingaben); `Error` wird kodiert.
- Ein Hinweis / Disclaimer = eine Aufgabe, die immer `SetupTaskResult.Ok(html)` zurückgibt.
- Der Fortschritt (Lizenz akzeptiert, Schlüssel bestätigt, Aufgaben erfolgreich)
  wird in einem verschlüsselten Token übertragen: Ein gefälschter POST kann die
  Lizenz, einen abgelehnten Schlüssel oder eine fehlgeschlagene Voraussetzung
  nicht überspringen.

**Vorbereitender Schritt** (der Assistent fährt erst fort, wenn die Eingabe
bestätigt wurde):

```csharp
builder.Services.AddSetupPreStep<ActivationKeyStep>();

public sealed class ActivationKeyStep(ILicenseServer server) : ISetupPreStep
{
    public string Label => "Aktivierung";

    // Nur Felder: Formular, versteckte Felder und Schaltfläche „Weiter“ stellt die Bibliothek bereit.
    public Task<string> RenderAsync(SetupPreStepContext ctx, CancellationToken ct) =>
        Task.FromResult("""<input type="text" class="form-control" name="activationKey" required />""");

    public async Task<SetupExtraStepResult> HandleAsync(SetupPreStepContext ctx, IFormCollection form, CancellationToken ct) =>
        await server.ActivateAsync(form["activationKey"], ct)                 // Schlüssel bei Bedarf hier speichern
            ? SetupExtraStepResult.Success()
            : SetupExtraStepResult.Failure("Ungültiger Aktivierungsschlüssel."); // Seite wird mit dem Fehler erneut angezeigt
}
```

- Das HTML aus `RenderAsync` wird **ohne Kodierung** eingefügt: Kodieren Sie die
  Werte aus `ctx.Values` (Benutzereingaben), die Sie nach einem Fehler erneut
  anzeigen.

> Um statt der integrierten Seite eine eigene Installationsseite bereitzustellen,
> verwenden Sie `app.UseSetupGate()` (nur die Sperre, ohne Seite). Lizenz und
> Vorinstallationsaufgaben betreffen dann nur die integrierte Seite.

### 4. (Optional) Zusätzliche Schritte hinzufügen

Eine Host-Anwendung kann die Ersteinrichtung um eigene Schritte erweitern, die
im Assistenten **zwischen dem Anlegen des Administratorkontos und dem
abschließenden Neustart** eingefügt werden (Einstellungen, Lizenz,
fachliche Konfiguration…).

```csharp
public sealed class CompanySetupStep : ISetupExtraStep
{
    public string Id => "company";
    public string Label => "Firma";

    public Task<string> RenderAsync(SetupExtraStepContext ctx, CancellationToken ct) =>
        Task.FromResult($"""
          <form method="post" action="/setup">
            <input type="hidden" name="step" value="{Id}" />
            <input type="hidden" name="pendingState" value="{ctx.PendingStateToken}" />
            <input type="text" class="form-control" name="companyName" required />
            <button type="submit" class="btn btn-primary w-100">Weiter</button>
          </form>
          """);

    public async Task<SetupExtraStepResult> HandleAsync(SetupExtraStepContext ctx, IFormCollection form, CancellationToken ct)
    {
        string name = form["companyName"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(name))
            return SetupExtraStepResult.Failure("Der Firmenname ist erforderlich.");

        await using AppDbContext db = AppDbContext.Create(ctx.Provider, ctx.ConnectionString);
        db.Settings.Add(new AppSettings { CompanyName = name });
        await db.SaveChangesAsync(ct);
        return SetupExtraStepResult.Success();
    }
}
```

```csharp
builder.Services.AddSetupStep<CompanySetupStep>();
// Mehrere Aufrufe werden in Registrierungsreihenfolge ausgeführt.
```

Der Schritt liefert sein vollständiges `<form>` (Felder + Schaltfläche) selbst;
die Bibliothek kümmert sich nur um den Rahmen (Logo, Fortschrittsanzeige,
Fehlermeldung) und fügt die Bezeichnung (`Label`) automatisch in die
Fortschrittsanzeige ein. Wie `ISetupInitializer` wird der Schritt in einem
eigenen Scope aufgelöst: Er kann also ganz normal einen `DbContext` oder jede
andere Abhängigkeit injizieren.

## Beispielprojekt

`src/AnthoDingo.Setup.Example` ist eine minimale ASP.NET-Core-Anwendung (API +
EF Core), die die vollständige Integration zeigt: Implementierung von
`ISetupInitializer`, ein `AppDbContext`, der zwischen den 4 EF-Core-Providern
umschaltet, die Vorinstallationsaufgabe `WritableContentRootCheck`
(Anwendungsordner beschreibbar) und das Einbinden der Middleware in
`Program.cs`. Es registriert die Treiber, die seine EF-Core-Provider bereits
mitbringen (Option 1), und referenziert daher `AnthoDingo.Setup.Providers` nicht.

```bash
dotnet run --project src/AnthoDingo.Setup.Example
```

Dann `/setup` öffnen: den Demo-Aktivierungsschlüssel (`DEMO-1234`) eingeben, die
Prüfung der Voraussetzungen durchlaufen, einen Datenbanktyp wählen, die
Verbindung testen, das Schema initialisieren, das Administratorkonto anlegen und
den Firmennamen eintragen (zusätzlicher Demo-Schritt, siehe `CompanySetupStep`).

## API

| Member | Zweck |
|--------|-------|
| `AddFileBasedSetup<TInitializer>(configure?)` | Registriert `SetupService` (Singleton) und den Initializer; `configure` muss mindestens einen Treiber registrieren. |
| `UseSetupMiddleware(appName)` | Sperre + integrierte `/setup`-Seite (der Name wird angezeigt). |
| `UseSetupGate()` | Nur die Sperre (Seite wird von der Anwendung bereitgestellt). |
| `SetupService.IsSetupComplete()` | Liest `appsettings.local.json`. |
| `SetupService.GetConfiguredProvider()` | Liest den bei der Installation gewählten `DbProvider`. |
| `SetupService.TestConnectionAsync(provider, cs)` | Testet eine Verbindung mit dem für `provider` registrierten Treiber. |
| `SetupService.BuildSqlConnectionString(...)` | Erstellt einen SQL-Server-Connection-String. |
| `SetupService.BuildMySqlConnectionString(...)` | Erstellt einen MySQL/MariaDB-Connection-String. |
| `SetupService.BuildPostgresConnectionString(...)` | Erstellt einen PostgreSQL-Connection-String. |
| `SetupService.BuildSqliteConnectionString(...)` | Erstellt einen SQLite-Connection-String (Datei). |
| `SetupService.CompleteSetup(provider, cs)` | Schreibt `appsettings.local.json` (`Setup:IsComplete`, `Setup:Provider`, Connection-String). |
| `ISetupInitializer` | Von der Anwendung implementiert: Migrationen + Anlegen des Admins, erhält den `DbProvider`. |
| `AddSetupStep<TStep>()` | Fügt einen zusätzlichen Schritt (`ISetupExtraStep`) zwischen dem Anlegen des Admins und dem Neustart hinzu. |
| `ISetupExtraStep` | Zusätzlicher Schritt der Anwendung: `Id`, `Label`, `RenderAsync`, `HandleAsync`. |
| `SetupExtraStepContext` | An den Schritt übergebener Kontext (Provider, Connection-String, Status-Token, Fehler, gesendete Werte). |
| `SetupExtraStepResult` | Ergebnis von `HandleAsync`: `Success()` oder `Failure(message)`. |
| `AdminAccount(UserName, Password, DisplayName?)` | Anzulegendes Admin-Konto. |
| `DbProvider` | Enum: `SqlServer`, `MySql`, `Postgres`, `Sqlite`. |
| `SetupOptions.Providers` | Treiber (`DbProviderFactory`) der im Assistenten angebotenen Datenbanktypen; mindestens einer erforderlich. |
| `SetupOptions.AddDefaultProviders(params DbProvider[])` | Paket `AnthoDingo.Setup.Providers`: registriert die 4 Treiber (oder die übergebenen). |
| `SetupOptions.AllowUsernameAdmin` | Bei `true` wird der Admin in Schritt 3 über einen Benutzernamen statt einer E-Mail identifiziert (Standard: `false`). |
| `SetupOptions.LicenseText` | Lizenztext: wenn gesetzt, wird vor dem Verbindungsschritt eine Seite „Lizenz“ mit Schaltfläche „Weiter“ angezeigt (Standard: `null`). |
| `SetupOptions.RequireLicenseAcceptance` | Bei `true` zeigt die Lizenzseite ein verpflichtendes Kontrollkästchen „Ich stimme den Lizenzbedingungen zu“ (Standard: `false`). |
| `AddSetupPreStep<TStep>()` | Fügt einen interaktiven vorbereitenden Schritt (Aktivierungsschlüssel…) direkt nach der Lizenz hinzu. |
| `ISetupPreStep` / `SetupPreStepContext` | Vorbereitender Schritt (`Label`, `RenderAsync` → HTML-Felder, `HandleAsync` → `SetupExtraStepResult`). |
| `AddSetupPreInstallTask<TTask>()` | Fügt eine Vorinstallationsaufgabe (Voraussetzungen, Hinweis…) vor dem Verbindungsschritt hinzu. |
| `ISetupPreInstallTask` / `SetupTaskResult` | Vorinstallationsaufgabe (`Title`, `ExecuteAsync`) und ihr Ergebnis (`Ok(html)` / `Fail(error, html)`). |
| `SetupOptions` | Anpassung (Pfad, erlaubte Präfixe, Name des Connection-Strings…). |

## Breaking Change (v3.0.0)

`AnthoDingo.Setup` liefert die Treiber für SQL Server, MySQL, PostgreSQL und SQLite
nicht mehr mit, und `SetupOptions.AllowedProviders` wird durch
`SetupOptions.Providers` ersetzt. Um das Verhalten von v2 wiederherzustellen:
`AnthoDingo.Setup.Providers` referenzieren und `o.AddDefaultProviders()` aufrufen
(ggf. mit den Typen aus dem früheren `AllowedProviders`). Eine Anwendung, die diese
Treiber ohne eigene Referenz (transitive Abhängigkeit) verwendet hat, muss sie nun
selbst referenzieren. Die PostgreSQL-Verbindungszeichenfolge enthält kein
`Trust Server Certificate` mehr, eine Option ohne Wirkung seit Npgsql 8.

```csharp
// v2
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
    o.AllowedProviders = [DbProvider.Postgres, DbProvider.Sqlite]);

// v3 — mit dem Paket AnthoDingo.Setup.Providers
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
    o.AddDefaultProviders(DbProvider.Postgres, DbProvider.Sqlite));

// v3 — oder mit den Treibern, die die Anwendung bereits referenziert
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
{
    o.Providers[DbProvider.Postgres] = NpgsqlFactory.Instance;
    o.Providers[DbProvider.Sqlite]   = SqliteFactory.Instance;
});
```

## Breaking Change (v2.0.0)

`ISetupInitializer.InitializeDatabaseAsync` und `CreateAdminAsync` erhalten nun
einen ersten Parameter `DbProvider provider`, der nötig ist, um in der Anwendung
den richtigen `DbContextOptionsBuilder` aufzubauen
(`UseSqlServer`/`UseMySql`/`UseNpgsql`/`UseSqlite`). `SetupService.CompleteSetup`
erhält den `provider` ebenfalls als Parameter.

## Lizenz

MIT
