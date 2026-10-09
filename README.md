# AnthoDingo.Setup

*[English version](README.en.md) · [Deutsche Version](README.de.md)*

Middleware d'installation « premier démarrage » pour ASP.NET Core.

Tant que l'application n'est pas configurée, toute requête est redirigée vers une
page `/setup` **fournie par la bibliothèque** (licence et tâches de
pré-installation optionnelles, formulaire base de données, compte administrateur).
À la validation : test de connexion → migrations/seed → création
de l'admin → écriture d'un `appsettings.local.json` → redémarrage.

- **4 types de base pris en charge** : SQL Server, MySQL/MariaDB, PostgreSQL et
  SQLite (fichier local). L'assistant propose un sélecteur avec les champs adaptés
  à chaque type ; l'application hôte choisit les types proposés.
- **Aucun pilote embarqué** : l'application fournit le pilote ADO.NET qu'elle
  référence déjà, ou ajoute le package `AnthoDingo.Setup.Providers` pour les quatre.
- **Licence et prérequis** (optionnels) : page licence d'utilisation, avec case
  d'acceptation obligatoire si souhaité, et tâches de pré-installation propres à
  l'application (vérification des prérequis, avertissement…) avant la connexion.
- **Détection file-based** : aucun appel base de données sur chaque requête.
- **Page intégrée hors-ligne** : Bootstrap + Bootstrap Icons sont **embarqués dans
  l'assembly** et servis sous `/setup/_assets/` — aucune dépendance à un CDN ni au
  `wwwroot` de l'application.
- **Agnostique** du DbContext et du modèle utilisateur via l'interface
  `ISetupInitializer`.

Cible : `net10.0`.

## Installation

```bash
dotnet add package AnthoDingo.Setup
# Optionnel : les 4 pilotes (sinon, fournir les vôtres, voir « Pilotes »)
dotnet add package AnthoDingo.Setup.Providers
```

| Package | Contenu |
|---------|---------|
| `AnthoDingo.Setup` | Middleware, assistant `/setup`, `SetupService`. Aucun pilote de base de données. |
| `AnthoDingo.Setup.Providers` | `AnthoDingo.Setup` + les 4 pilotes et `o.AddDefaultProviders()`. |

## Pilotes

`AnthoDingo.Setup` ne référence aucun pilote : l'assistant teste la connexion et
construit la chaîne de connexion avec le `DbProviderFactory` (ADO.NET) enregistré
pour chaque type de base dans `SetupOptions.Providers`. Seuls les types enregistrés
sont proposés ; le premier ajouté est présélectionné. Sans aucun pilote,
l'application refuse de démarrer avec un message explicite.

| Base | `DbProvider` | Pilote | Factory |
|------|--------------|--------|---------|
| SQL Server | `SqlServer` | `Microsoft.Data.SqlClient` | `SqlClientFactory.Instance` |
| MySQL / MariaDB | `MySql` | `MySqlConnector` | `MySqlConnectorFactory.Instance` |
| PostgreSQL | `Postgres` | `Npgsql` | `NpgsqlFactory.Instance` |
| SQLite | `Sqlite` | `Microsoft.Data.Sqlite` | `SqliteFactory.Instance` |

**Option 1 — fournir ses pilotes** (recommandé) : le provider EF Core de
l'application apporte déjà le pilote (p. ex. `Npgsql.EntityFrameworkCore.PostgreSQL`
→ `Npgsql`), rien de plus n'est publié. Seul `AnthoDingo.Setup` est référencé.

```csharp
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
    o.Providers[DbProvider.Postgres] = NpgsqlFactory.Instance);
```

**Option 2 — package `AnthoDingo.Setup.Providers`** : embarque les quatre pilotes,
au prix d'une publication plus lourde (SqlClient notamment).

```csharp
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o => o.AddDefaultProviders());
// ou seulement certains types :
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
    o.AddDefaultProviders(DbProvider.Postgres, DbProvider.Sqlite));
```

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
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
{
    o.Providers[DbProvider.Postgres] = NpgsqlFactory.Instance; // voir « Pilotes »

    // Pour identifier le compte admin par un nom d'utilisateur plutôt qu'un email :
    // o.AllowUsernameAdmin = true;
});

var app = builder.Build();

app.UseSetupMiddleware("Mon Application");   // page /setup intégrée

var setup = app.Services.GetRequiredService<SetupService>();
if (setup.IsSetupComplete())
{
    // setup.GetConfiguredProvider() renvoie le DbProvider choisi à l'installation
    // — utile pour reconstruire le bon DbContextOptionsBuilder à chaque démarrage.
}
```

### 3. (Optionnel) Licence, étapes préliminaires et tâches de pré-installation

Avant la connexion à la base, l'assistant peut afficher une page **licence
d'utilisation**, puis une page par **étape préliminaire** (saisie : clé
d'activation…), puis une page par **tâche de pré-installation** (vérification des
prérequis, avertissement…). Ordre : Licence → étapes préliminaires → tâches (chacune
dans l'ordre d'enregistrement) → Connexion → Base → Admin.

```csharp
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
{
    o.AddDefaultProviders();                     // ou vos pilotes, voir « Pilotes »
    o.LicenseText = File.ReadAllText("LICENSE"); // page licence + bouton « Suivant »
    o.RequireLicenseAcceptance = true;           // case « J'accepte » obligatoire
});
builder.Services.AddSetupPreInstallTask<PrerequisCheck>();

public sealed class PrerequisCheck : ISetupPreInstallTask
{
    public string Title => "Prérequis";

    public Task<SetupTaskResult> ExecuteAsync(CancellationToken ct = default) =>
        Task.FromResult(Environment.Is64BitProcess
            ? SetupTaskResult.Ok("<p>Processus 64 bits : OK.</p>")      // bouton « Suivant »
            : SetupTaskResult.Fail("Un processus 64 bits est requis.")); // bouton « Réessayer »
}
```

- La tâche est exécutée à chaque affichage de sa page (et à chaque « Réessayer ») ;
  une exception est affichée comme un échec. `Html` est inséré **sans encodage**
  (contenu de l'application, jamais de saisie utilisateur) ; `Error` est encodé.
- Un avertissement / disclaimer = une tâche qui renvoie toujours `SetupTaskResult.Ok(html)`.
- La progression (licence acceptée, clé validée, tâches réussies) voyage dans un
  jeton chiffré : un POST forgé ne permet pas de sauter la licence, une clé refusée
  ou un prérequis en échec.

**Étape préliminaire** (l'assistant ne continue que si la saisie est validée) :

```csharp
builder.Services.AddSetupPreStep<ActivationKeyStep>();

public sealed class ActivationKeyStep(ILicenseServer server) : ISetupPreStep
{
    public string Label => "Activation";

    // Champs seulement : formulaire, champs cachés et bouton « Suivant » fournis par la lib.
    public Task<string> RenderAsync(SetupPreStepContext ctx, CancellationToken ct) =>
        Task.FromResult("""<input type="text" class="form-control" name="activationKey" required />""");

    public async Task<SetupExtraStepResult> HandleAsync(SetupPreStepContext ctx, IFormCollection form, CancellationToken ct) =>
        await server.ActivateAsync(form["activationKey"], ct)            // persistez la clé ici si besoin
            ? SetupExtraStepResult.Success()
            : SetupExtraStepResult.Failure("Clé d'activation invalide."); // page ré-affichée avec l'erreur
}
```

- Le HTML de `RenderAsync` est inséré **sans encodage** : encodez les valeurs de
  `ctx.Values` (saisie utilisateur) que vous ré-affichez après une erreur.

> Pour fournir votre propre page d'installation à la place de la page intégrée,
> utilisez `app.UseSetupGate()` (garde seule, sans page). La licence et les tâches
> de pré-installation ne concernent alors que la page intégrée.

### 4. (optionnel) Ajouter des étapes supplémentaires

Une application hôte peut étendre le premier paramétrage en ajoutant ses propres
étapes, insérées dans le wizard **entre la création du compte administrateur et
le redémarrage final** (préférences, licence, configuration métier…).

```csharp
public sealed class CompanySetupStep : ISetupExtraStep
{
    public string Id => "company";
    public string Label => "Societe";

    public Task<string> RenderAsync(SetupExtraStepContext ctx, CancellationToken ct) =>
        Task.FromResult($"""
          <form method="post" action="/setup">
            <input type="hidden" name="step" value="{Id}" />
            <input type="hidden" name="pendingState" value="{ctx.PendingStateToken}" />
            <input type="text" class="form-control" name="companyName" required />
            <button type="submit" class="btn btn-primary w-100">Continuer</button>
          </form>
          """);

    public async Task<SetupExtraStepResult> HandleAsync(SetupExtraStepContext ctx, IFormCollection form, CancellationToken ct)
    {
        string name = form["companyName"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(name))
            return SetupExtraStepResult.Failure("Le nom de la societe est obligatoire.");

        await using AppDbContext db = AppDbContext.Create(ctx.Provider, ctx.ConnectionString);
        db.Settings.Add(new AppSettings { CompanyName = name });
        await db.SaveChangesAsync(ct);
        return SetupExtraStepResult.Success();
    }
}
```

```csharp
builder.Services.AddSetupStep<CompanySetupStep>();
// Plusieurs appels s'enchainent dans leur ordre d'enregistrement.
```

L'étape fournit elle-même son `<form>` complet (champs + bouton) ; la
bibliothèque se charge uniquement de l'habillage (logo, stepper, message
d'erreur) et insère automatiquement le libellé (`Label`) dans le stepper.
Comme `ISetupInitializer`, l'étape est résolue dans un scope dédié : elle peut
donc injecter normalement un `DbContext` ou toute autre dépendance.

## Projet d'exemple

`src/AnthoDingo.Setup.Example` est une application ASP.NET Core minimale (API +
EF Core) qui montre l'intégration complète : implémentation d'`ISetupInitializer`,
`AppDbContext` qui bascule entre les 4 providers EF Core, tâche de
pré-installation `WritableContentRootCheck` (dossier de l'application accessible
en écriture), et branchement du middleware dans `Program.cs`. Il enregistre les
pilotes déjà apportés par ses providers EF Core (option 1) et ne référence donc
pas `AnthoDingo.Setup.Providers`.

```bash
dotnet run --project src/AnthoDingo.Setup.Example
```

Puis ouvrir `/setup` : saisir la clé d'activation de démonstration (`DEMO-1234`), passer la vérification des prérequis, choisir un type de base, tester la connexion, initialiser
le schéma, créer le compte administrateur, renseigner le nom de la société
(étape supplémentaire de démonstration, voir `CompanySetupStep`).

## API

| Membre | Rôle |
|--------|------|
| `AddFileBasedSetup<TInitializer>(configure?)` | Enregistre `SetupService` (singleton) et l'initialiseur ; `configure` doit enregistrer au moins un pilote. |
| `UseSetupMiddleware(appName)` | Garde + page `/setup` intégrée (le nom est affiché). |
| `UseSetupGate()` | Garde seule (page fournie par l'application). |
| `SetupService.IsSetupComplete()` | Lit `appsettings.local.json`. |
| `SetupService.GetConfiguredProvider()` | Lit le `DbProvider` choisi à l'installation. |
| `SetupService.TestConnectionAsync(provider, cs)` | Teste une connexion avec le pilote enregistré pour `provider`. |
| `SetupService.BuildSqlConnectionString(...)` | Construit une chaîne de connexion SQL Server. |
| `SetupService.BuildMySqlConnectionString(...)` | Construit une chaîne de connexion MySQL/MariaDB. |
| `SetupService.BuildPostgresConnectionString(...)` | Construit une chaîne de connexion PostgreSQL. |
| `SetupService.BuildSqliteConnectionString(...)` | Construit une chaîne de connexion SQLite (fichier). |
| `SetupService.CompleteSetup(provider, cs)` | Écrit `appsettings.local.json` (`Setup:IsComplete`, `Setup:Provider`, connection string). |
| `ISetupInitializer` | Implémentée par l'app : migrations + création admin, reçoit le `DbProvider`. |
| `AddSetupStep<TStep>()` | Ajoute une étape supplémentaire (`ISetupExtraStep`) entre la création de l'admin et le redémarrage. |
| `ISetupExtraStep` | Étape supplémentaire fournie par l'app : `Id`, `Label`, `RenderAsync`, `HandleAsync`. |
| `SetupExtraStepContext` | Contexte passé à l'étape (provider, chaîne de connexion, jeton d'état, erreur, valeurs postées). |
| `SetupExtraStepResult` | Résultat de `HandleAsync` : `Success()` ou `Failure(message)`. |
| `AdminAccount(UserName, Password, DisplayName?)` | Compte admin à créer. |
| `DbProvider` | Enum : `SqlServer`, `MySql`, `Postgres`, `Sqlite`. |
| `SetupOptions.Providers` | Pilotes (`DbProviderFactory`) des types de base proposés dans l'assistant ; au moins un requis. |
| `SetupOptions.AddDefaultProviders(params DbProvider[])` | Package `AnthoDingo.Setup.Providers` : enregistre les 4 pilotes (ou ceux passés en paramètre). |
| `SetupOptions.AllowUsernameAdmin` | Si `true`, l'admin de l'étape 3 est identifié par un nom d'utilisateur plutôt qu'un email (par défaut `false`). |
| `SetupOptions.LicenseText` | Texte de la licence : si renseigné, page « Licence » avec bouton « Suivant » avant la connexion (par défaut : `null`). |
| `SetupOptions.RequireLicenseAcceptance` | Si `true`, case « J'accepte les termes de la licence » obligatoire sur la page licence (par défaut : `false`). |
| `AddSetupPreStep<TStep>()` | Ajoute une étape préliminaire interactive (clé d'activation…) affichée juste après la licence. |
| `ISetupPreStep` / `SetupPreStepContext` | Étape préliminaire (`Label`, `RenderAsync` → champs HTML, `HandleAsync` → `SetupExtraStepResult`). |
| `AddSetupPreInstallTask<TTask>()` | Ajoute une tâche de pré-installation (prérequis, avertissement…) affichée avant la connexion. |
| `ISetupPreInstallTask` / `SetupTaskResult` | Tâche de pré-installation (`Title`, `ExecuteAsync`) et son résultat (`Ok(html)` / `Fail(error, html)`). |
| `SetupOptions` | Personnalisation (chemin, préfixes autorisés, nom de la chaîne…). |

## Breaking change (v3.0.0)

`AnthoDingo.Setup` n'embarque plus les pilotes SQL Server, MySQL, PostgreSQL et
SQLite, et `SetupOptions.AllowedProviders` est remplacé par `SetupOptions.Providers`.
Pour retrouver le comportement de la v2 : référencer `AnthoDingo.Setup.Providers` et
appeler `o.AddDefaultProviders()` (avec en paramètre les types de l'ancien
`AllowedProviders`, le cas échéant). Une application qui utilisait ces pilotes sans
les référencer (dépendance transitive) doit les référencer elle-même. La chaîne
PostgreSQL ne contient plus `Trust Server Certificate`, option sans effet depuis Npgsql 8.

```csharp
// v2
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
    o.AllowedProviders = [DbProvider.Postgres, DbProvider.Sqlite]);

// v3 — avec le package AnthoDingo.Setup.Providers
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
    o.AddDefaultProviders(DbProvider.Postgres, DbProvider.Sqlite));

// v3 — ou avec les pilotes déjà référencés par l'application
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
{
    o.Providers[DbProvider.Postgres] = NpgsqlFactory.Instance;
    o.Providers[DbProvider.Sqlite]   = SqliteFactory.Instance;
});
```

## Breaking change (v2.0.0)

`ISetupInitializer.InitializeDatabaseAsync` et `CreateAdminAsync` reçoivent
désormais un premier paramètre `DbProvider provider`, nécessaire pour construire
le bon `DbContextOptionsBuilder` (`UseSqlServer`/`UseMySql`/`UseNpgsql`/`UseSqlite`)
côté application. `SetupService.CompleteSetup` prend également le `provider` en
paramètre.

## Licence

MIT
