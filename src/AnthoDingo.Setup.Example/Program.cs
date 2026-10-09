using AnthoDingo.Setup;
using AnthoDingo.Setup.Example;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Fichier écrit par l'assistant d'installation à la fin du wizard — prioritaire
// sur appsettings.json une fois l'installation terminée. Doit rester hors Git
// (voir .gitignore).
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// Pilotes proposés par l'assistant : ceux que les providers EF Core de l'application
// apportent déjà. Le premier est présélectionné. Alternative : référencer le package
// AnthoDingo.Setup.Providers et appeler o.AddDefaultProviders().
builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
{
    o.Providers[DbProvider.SqlServer] = SqlClientFactory.Instance;
    o.Providers[DbProvider.MySql]     = MySqlConnectorFactory.Instance;
    o.Providers[DbProvider.Postgres]  = NpgsqlFactory.Instance;
    o.Providers[DbProvider.Sqlite]    = SqliteFactory.Instance;

    // Pour identifier l'admin par un nom d'utilisateur plutôt qu'un email :
    // o.AllowUsernameAdmin = true;

    // Pour afficher une page licence (avec case à cocher obligatoire) en premier :
    // o.LicenseText = File.ReadAllText("LICENSE");
    // o.RequireLicenseAcceptance = true;
});

// Étapes préliminaires interactives (clé d'activation…), juste après la licence.
builder.Services.AddSetupPreStep<ActivationKeyStep>();

// Tâches affichées avant la connexion à la base (prérequis, avertissement…),
// dans l'ordre d'enregistrement.
builder.Services.AddSetupPreInstallTask<WritableContentRootCheck>();

// Étape supplémentaire : exécutée après la création du compte admin et avant le
// redémarrage final. Plusieurs appels s'enchaînent dans l'ordre d'enregistrement.
builder.Services.AddSetupStep<CompanySetupStep>();

WebApplication app = builder.Build();

// Garde + page /setup intégrée. À appeler en tout premier dans le pipeline.
app.UseSetupMiddleware("AnthoDingo.Setup.Example");

SetupService setup = app.Services.GetRequiredService<SetupService>();

app.MapGet("/", () => setup.IsSetupComplete()
    ? Results.Text($"Application installée (base : {setup.GetConfiguredProvider()}).")
    : Results.Redirect("/setup"));

app.Run();
