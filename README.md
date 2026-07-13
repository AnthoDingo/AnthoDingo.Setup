# AnthoDingo.Setup

*[English version](README.en.md)*

Middleware d'installation « premier démarrage » pour ASP.NET Core.

Tant que l'application n'est pas configurée, toute requête est redirigée vers une
page `/setup` **fournie par la bibliothèque** (formulaire base de données + compte
administrateur). À la validation : test de connexion → migrations/seed → création
de l'admin → écriture d'un `appsettings.local.json` → redémarrage.

- **4 types de base pris en charge** : SQL Server, MySQL/MariaDB, PostgreSQL et
  SQLite (fichier local). L'assistant propose un sélecteur avec les champs adaptés
  à chaque type ; l'application hôte peut restreindre la liste proposée.
- **Détection file-based** : aucun appel base de données sur chaque requête.
- **Page intégrée hors-ligne** : Bootstrap + Bootstrap Icons sont **embarqués dans
  l'assembly** et servis sous `/setup/_assets/` — aucune dépendance à un CDN ni au
  `wwwroot` de l'application.
- **Agnostique** du DbContext et du modèle utilisateur via l'interface
  `ISetupInitializer`.

Cible : `net8.0` et `net10.0`.

## Pilotes utilisés

| Base | Pilote (test de connexion) |
|------|------------------------------|
| SQL Server | `Microsoft.Data.SqlClient` |
| MySQL / MariaDB | `MySqlConnector` |
| PostgreSQL | `Npgsql` |
| SQLite | `Microsoft.Data.Sqlite` |

Ces pilotes ne servent qu'à **tester la connexion** pendant l'installation. Côté
application, utilisez le provider EF Core (ou autre ORM) de votre choix — voir le
projet d'exemple, qui utilise `Microsoft.EntityFrameworkCore.SqlServer`,
`Pomelo.EntityFrameworkCore.MySql` (construit sur MySqlConnector),
`Npgsql.EntityFrameworkCore.PostgreSQL` et `Microsoft.EntityFrameworkCore.Sqlite`.

## Utilisation

### 1. Implémenter `ISetupInitializer`

```csharp
public sealed class AppSetupInitializer : ISetupInitializer
{
    public async Task InitializeDatabaseAsync(DbProvider provider, string cs, CancellationToken ct = default)
    {
        await using AppDbContext db = AppDbContext.Create(provider, cs);
        await db.Database.MigrateAsync(ct);
        // … seed des rôles / données de référence
    }

    public async Task CreateAdminAsync(DbProvider provider, string cs, AdminAccount admin, CancellationToken ct = default)
    {
        await using AppDbContext db = AppDbContext.Create(provider, cs);
        // … créer l'utilisateur (Identity PasswordHasher, BCrypt, etc.)
    }
}

// Le DbContext choisit le provider EF Core adapté à la base sélectionnée
// dans l'assistant.
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

### 2. Enregistrer et brancher

```csharp
using AnthoDingo.Setup;

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true);
builder.Services.AddFileBasedSetup<AppSetupInitializer>();

// Pour restreindre les types de base proposés par l'assistant (par défaut : les 4) :
// builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
//     o.AllowedProviders = [DbProvider.Postgres, DbProvider.Sqlite]);

var app = builder.Build();

app.UseSetupMiddleware("Mon Application");   // page /setup intégrée

var setup = app.Services.GetRequiredService<SetupService>();
if (setup.IsSetupComplete())
{
    // setup.GetConfiguredProvider() renvoie le DbProvider choisi à l'installation
    // — utile pour reconstruire le bon DbContextOptionsBuilder à chaque démarrage.
}
```

> Pour fournir votre propre page d'installation à la place de la page intégrée,
> utilisez `app.UseSetupGate()` (garde seule, sans page).

## Projet d'exemple

`src/AnthoDingo.Setup.Example` est une application ASP.NET Core minimale (API +
EF Core) qui montre l'intégration complète : implémentation d'`ISetupInitializer`,
`AppDbContext` qui bascule entre les 4 providers EF Core, et branchement du
middleware dans `Program.cs`.

```bash
dotnet run --project src/AnthoDingo.Setup.Example
```

Puis ouvrir `/setup` : choisir un type de base, tester la connexion, initialiser
le schéma et créer le compte administrateur.

## API

| Membre | Rôle |
|--------|------|
| `AddFileBasedSetup<TInitializer>(configure?)` | Enregistre `SetupService` (singleton) et l'initialiseur. |
| `UseSetupMiddleware(appName)` | Garde + page `/setup` intégrée (le nom est affiché). |
| `UseSetupGate()` | Garde seule (page fournie par l'application). |
| `SetupService.IsSetupComplete()` | Lit `appsettings.local.json`. |
| `SetupService.GetConfiguredProvider()` | Lit le `DbProvider` choisi à l'installation. |
| `SetupService.TestConnectionAsync(provider, cs)` | Teste une connexion (SQL Server, MySQL, PostgreSQL ou SQLite). |
| `SetupService.BuildSqlConnectionString(...)` | Construit une chaîne de connexion SQL Server. |
| `SetupService.BuildMySqlConnectionString(...)` | Construit une chaîne de connexion MySQL/MariaDB. |
| `SetupService.BuildPostgresConnectionString(...)` | Construit une chaîne de connexion PostgreSQL. |
| `SetupService.BuildSqliteConnectionString(...)` | Construit une chaîne de connexion SQLite (fichier). |
| `SetupService.CompleteSetup(provider, cs)` | Écrit `appsettings.local.json` (`Setup:IsComplete`, `Setup:Provider`, connection string). |
| `ISetupInitializer` | Implémentée par l'app : migrations + création admin, reçoit le `DbProvider`. |
| `AdminAccount(UserName, Password, DisplayName?)` | Compte admin à créer. |
| `DbProvider` | Enum : `SqlServer`, `MySql`, `Postgres`, `Sqlite`. |
| `SetupOptions.AllowedProviders` | Types de base proposés dans l'assistant (par défaut : les 4). |
| `SetupOptions` | Personnalisation (chemin, préfixes autorisés, nom de la chaîne…). |

## Breaking change (v2.0.0)

`ISetupInitializer.InitializeDatabaseAsync` et `CreateAdminAsync` reçoivent
désormais un premier paramètre `DbProvider provider`, nécessaire pour construire
le bon `DbContextOptionsBuilder` (`UseSqlServer`/`UseMySql`/`UseNpgsql`/`UseSqlite`)
côté application. `SetupService.CompleteSetup` prend également le `provider` en
paramètre.

## Licence

MIT
