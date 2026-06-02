# AnthoDingo.Setup

Middleware d'installation « premier démarrage » pour ASP.NET Core.

Tant que l'application n'est pas configurée, toute requête est redirigée vers une
page `/setup` **fournie par la bibliothèque** (formulaire base de données + compte
administrateur). À la validation : test de connexion → migrations/seed → création
de l'admin → écriture d'un `appsettings.local.json` → redémarrage.

- **Détection file-based** : aucun appel base de données sur chaque requête.
- **Page intégrée hors-ligne** : Bootstrap + Bootstrap Icons sont **embarqués dans
  l'assembly** et servis sous `/setup/_assets/` — aucune dépendance à un CDN ni au
  `wwwroot` de l'application.
- **Agnostique** du DbContext et du modèle utilisateur via l'interface
  `ISetupInitializer`.

Cible : `net8.0` et `net10.0`.

## Utilisation

### 1. Implémenter `ISetupInitializer`

```csharp
public sealed class AppSetupInitializer : ISetupInitializer
{
    public async Task InitializeDatabaseAsync(string cs, CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync(ct);
        // … seed des rôles / données de référence
    }

    public async Task CreateAdminAsync(string cs, AdminAccount admin, CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
        await using var db = new AppDbContext(options);
        // … créer l'utilisateur (Identity PasswordHasher, BCrypt, etc.)
    }
}
```

### 2. Enregistrer et brancher

```csharp
using AnthoDingo.Setup;

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true);
builder.Services.AddFileBasedSetup<AppSetupInitializer>();

var app = builder.Build();

app.UseSetupMiddleware("Mon Application");   // page /setup intégrée

// Migrations auto seulement si l'installation est terminée
var setup = app.Services.GetRequiredService<SetupService>();
if (setup.IsSetupComplete()) { /* db.Database.Migrate() */ }
```

> Pour fournir votre propre page d'installation à la place de la page intégrée,
> utilisez `app.UseSetupGate()` (garde seule, sans page).

## API

| Membre | Rôle |
|--------|------|
| `AddFileBasedSetup<TInitializer>(configure?)` | Enregistre `SetupService` (singleton) et l'initialiseur. |
| `UseSetupMiddleware(appName)` | Garde + page `/setup` intégrée (le nom est affiché). |
| `UseSetupGate()` | Garde seule (page fournie par l'application). |
| `SetupService.IsSetupComplete()` | Lit `appsettings.local.json`. |
| `SetupService.TestConnectionAsync(cs)` | Teste une connexion SQL Server. |
| `SetupService.BuildSqlConnectionString(...)` | Construit une chaîne de connexion. |
| `SetupService.CompleteSetup(cs)` | Écrit `appsettings.local.json`. |
| `ISetupInitializer` | Implémentée par l'app : migrations + création admin. |
| `AdminAccount(UserName, Password, DisplayName?)` | Compte admin à créer. |
| `SetupOptions` | Personnalisation (chemin, préfixes autorisés, nom de la chaîne…). |

## Licence

MIT
