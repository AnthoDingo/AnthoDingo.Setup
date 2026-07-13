using AnthoDingo.Setup;
using AnthoDingo.Setup.Example;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Fichier écrit par l'assistant d'installation à la fin du wizard — prioritaire
// sur appsettings.json une fois l'installation terminée. Doit rester hors Git
// (voir .gitignore).
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services.AddFileBasedSetup<AppSetupInitializer>();

// Pour restreindre les types de base proposés par l'assistant :
// builder.Services.AddFileBasedSetup<AppSetupInitializer>(o =>
//     o.AllowedProviders = [DbProvider.Postgres, DbProvider.Sqlite]);

WebApplication app = builder.Build();

// Garde + page /setup intégrée. À appeler en tout premier dans le pipeline.
app.UseSetupMiddleware("AnthoDingo.Setup.Example");

SetupService setup = app.Services.GetRequiredService<SetupService>();

app.MapGet("/", () => setup.IsSetupComplete()
    ? Results.Text($"Application installée (base : {setup.GetConfiguredProvider()}).")
    : Results.Redirect("/setup"));

app.Run();
