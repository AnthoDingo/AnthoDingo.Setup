using AnthoDingo.Setup;
using AnthoDingo.Setup.Example;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Fichier écrit par l'assistant d'installation à la fin du wizard — prioritaire
// sur appsettings.json une fois l'installation terminée. Doit rester hors Git
// (voir .gitignore).
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services.AddFileBasedSetup<AppSetupInitializer>();

// Tâches affichées avant la connexion à la base (prérequis, avertissement…),
// dans l'ordre d'enregistrement.
builder.Services.AddSetupPreInstallTask<WritableContentRootCheck>();

// Pour restreindre les types de base proposés par l'assistant :
// builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
//     o.AllowedProviders = [DbProvider.Postgres, DbProvider.Sqlite]);

// Pour identifier l'admin par un nom d'utilisateur plutôt qu'un email :
// builder.Services.AddFileBasedSetup<AppSetupInitializer>(o => o.AllowUsernameAdmin = true);

// Étape supplémentaire : exécutée après la création du compte admin et avant le
// redémarrage final. Plusieurs appels s'enchaînent dans l'ordre d'enregistrement.
builder.Services.AddSetupStep<CompanySetupStep>();

// Pour afficher une page licence (avec case à cocher obligatoire) en premier :
// builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
// {
//     o.LicenseText = File.ReadAllText("LICENSE");
//     o.RequireLicenseAcceptance = true;
// });

WebApplication app = builder.Build();

// Garde + page /setup intégrée. À appeler en tout premier dans le pipeline.
app.UseSetupMiddleware("AnthoDingo.Setup.Example");

SetupService setup = app.Services.GetRequiredService<SetupService>();

app.MapGet("/", () => setup.IsSetupComplete()
    ? Results.Text($"Application installée (base : {setup.GetConfiguredProvider()}).")
    : Results.Redirect("/setup"));

app.Run();
