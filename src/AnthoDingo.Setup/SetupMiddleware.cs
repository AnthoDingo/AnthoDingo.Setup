using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnthoDingo.Setup;

/// <summary>
/// Garde d'installation. Tant que l'application n'est pas configurée :
/// <list type="bullet">
///   <item>Si <c>serveBuiltInPage = true</c>, sert lui-même la page <c>/setup</c>
///         (formulaire + traitement de l'installation) — aucune dépendance au
///         Razor de l'application.</item>
///   <item>Sinon, redirige simplement vers <see cref="SetupOptions.SetupPath"/>
///         (l'application fournit sa propre page).</item>
/// </list>
/// </summary>
public sealed class SetupMiddleware(
    RequestDelegate         next,
    IOptions<SetupOptions>  options,
    string                  appName,
    bool                    serveBuiltInPage)
{
    private readonly SetupOptions _opts = options.Value;

    public async Task InvokeAsync(
        HttpContext ctx, SetupService setup, IHostApplicationLifetime lifetime, ILogger<SetupMiddleware> logger)
    {
        string path = ctx.Request.Path.Value ?? "/";

        // Assets embarqués (Bootstrap, icônes) — toujours servis, hors-ligne
        if (serveBuiltInPage && path.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await ServeAssetAsync(ctx, path.Substring(AssetPrefix.Length));
            return;
        }

        bool isSetupPath = path.Equals(_opts.SetupPath, StringComparison.OrdinalIgnoreCase);

        // Installation déjà terminée
        if (setup.IsSetupComplete())
        {
            if (isSetupPath) { ctx.Response.Redirect("/"); return; }
            await next(ctx);
            return;
        }

        // ── Page intégrée fournie par la lib ──────────────────────────────────
        if (serveBuiltInPage && isSetupPath)
        {
            List<ISetupPreInstallTask> tasks = ctx.RequestServices.GetServices<ISetupPreInstallTask>().ToList();
            List<string> preSteps = [];
            if (_opts.LicenseText is not null) preSteps.Add("Licence");
            preSteps.AddRange(tasks.Select(t => t.Title));
            SetupPage.Wizard w = new(appName, preSteps, setup.GetExtraStepDescriptors().Select(d => d.Label).ToList());

            if (HttpMethods.IsPost(ctx.Request.Method))
                await HandleInstallAsync(ctx, w, tasks, setup, lifetime, logger);
            else
            {
                // Un GET vers /setup ne remet plus à zéro d'état serveur : depuis
                // que la connexion validée à l'étape 1 voyage dans un champ caché
                // chiffré (voir SetupService.ProtectPendingState), il n'y a plus
                // rien à réinitialiser ici. Un simple rechargement de la page /
                // une sonde de monitoring / plusieurs workers ne cassent plus le
                // wizard en cours (voir historique : "Session expiree" causé par
                // un état conservé sur ce singleton et effacé par n'importe quel GET).
                await StartAsync(ctx, w, tasks, setup, null);
            }
            return;
        }

        // Laisser passer les préfixes autorisés (assets, API…)
        if (IsAllowed(path))
        {
            await next(ctx);
            return;
        }

        // Sinon : rediriger vers la page d'installation
        ctx.Response.Redirect(_opts.SetupPath);
    }

    // ── Traitement du wizard multi-étapes (page intégrée) ─────────────────────

    private async Task HandleInstallAsync(
        HttpContext ctx, SetupPage.Wizard w, List<ISetupPreInstallTask> tasks,
        SetupService setup, IHostApplicationLifetime lifetime, ILogger logger)
    {
        IFormCollection form = await ctx.Request.ReadFormAsync();
        Dictionary<string, string> values = form.ToDictionary(f => f.Key, f => f.Value.ToString());
        string step = form["step"].ToString();

        switch (step)
        {
            case "license": await LicenseAsync(ctx, w, tasks, setup, form); break;
            case "pre":
                if (setup.TryUnprotectPreInstallStage(form["preInstall"], out int stage) && stage >= 0 && stage <= tasks.Count)
                    await RunPreStageAsync(ctx, w, tasks, setup, stage);
                else
                    await StartAsync(ctx, w, tasks, setup, SessionExpired);
                break;
            case "1": await Step1ConnectionAsync(ctx, w, tasks, setup, form, values); break;
            case "2": await Step2InitDbAsync(ctx, w, tasks, setup, form); break;
            case "3": await Step3AdminAsync(ctx, w, tasks, setup, lifetime, logger, form, values); break;
            default:
                int extraIndex = IndexOfExtraStep(setup.GetExtraStepDescriptors(), step);
                if (extraIndex >= 0)
                    await StepExtraAsync(ctx, w, tasks, setup, lifetime, logger, form, values, extraIndex);
                else
                    await StartAsync(ctx, w, tasks, setup, null);
                break;
        }
    }

    private static int IndexOfExtraStep(IReadOnlyList<(string Id, string Label)> extraSteps, string id)
    {
        for (int i = 0; i < extraSteps.Count; i++)
        {
            if (extraSteps[i].Id == id) return i;
        }
        return -1;
    }

    private const string SessionExpired = "Session expiree, recommencez.";

    // ── Préalables : licence puis tâches de pré-installation ──────────────────
    // Étape de pré-installation « stage » : 0..tasks.Count-1 = tâche à exécuter,
    // tasks.Count = préalables terminés (étape 1 autorisée). Elle voyage dans un
    // jeton chiffré (SetupService.ProtectPreInstallStage) : un POST forgé ne peut
    // pas sauter la licence ou un prérequis en échec.

    /// <summary>Début du wizard : licence si configurée, sinon première tâche (ou étape 1).</summary>
    private Task StartAsync(HttpContext ctx, SetupPage.Wizard w, List<ISetupPreInstallTask> tasks, SetupService setup, string? error) =>
        _opts.LicenseText is not null
            ? WriteHtmlAsync(ctx, SetupPage.RenderLicense(w, error, _opts.LicenseText, _opts.RequireLicenseAcceptance))
            : RunPreStageAsync(ctx, w, tasks, setup, 0, error);

    private async Task LicenseAsync(
        HttpContext ctx, SetupPage.Wizard w, List<ISetupPreInstallTask> tasks, SetupService setup, IFormCollection form)
    {
        if (_opts.LicenseText is not null && _opts.RequireLicenseAcceptance && form["acceptLicense"] != "on")
        {
            await WriteHtmlAsync(ctx, SetupPage.RenderLicense(w, "Vous devez accepter la licence pour continuer.", _opts.LicenseText, true));
            return;
        }
        await RunPreStageAsync(ctx, w, tasks, setup, 0);
    }

    private async Task RunPreStageAsync(
        HttpContext ctx, SetupPage.Wizard w, List<ISetupPreInstallTask> tasks, SetupService setup, int stage, string? error = null)
    {
        if (stage >= tasks.Count)
        {
            await WriteHtmlAsync(ctx, SetupPage.RenderStep1(w, error, null, _opts.AllowedProviders, setup.ProtectPreInstallStage(tasks.Count)));
            return;
        }

        ISetupPreInstallTask task = tasks[stage];
        SetupTaskResult result;
        try
        {
            result = await task.ExecuteAsync(ctx.RequestAborted);
        }
        catch (Exception ex)
        {
            result = SetupTaskResult.Fail(ex.Message);
        }
        int    current = (_opts.LicenseText is not null ? 1 : 0) + stage + 1;
        string token   = setup.ProtectPreInstallStage(result.Success ? stage + 1 : stage);
        await WriteHtmlAsync(ctx, SetupPage.RenderTask(w, current, task.Title, result, token, error));
    }

    private async Task Step1ConnectionAsync(
        HttpContext ctx, SetupPage.Wizard w, List<ISetupPreInstallTask> tasks,
        SetupService setup, IFormCollection form, Dictionary<string, string> values)
    {
        string? preToken = form["preInstall"];
        if (!setup.TryUnprotectPreInstallStage(preToken, out int stage) || stage != tasks.Count)
        {
            await StartAsync(ctx, w, tasks, setup, SessionExpired);
            return;
        }

        if (!Enum.TryParse(form["dbProvider"], ignoreCase: true, out DbProvider provider) || !_opts.AllowedProviders.Contains(provider))
            provider = _opts.AllowedProviders.Count > 0 ? _opts.AllowedProviders[0] : DbProvider.SqlServer;

        string connectionString;
        switch (provider)
        {
            case DbProvider.SqlServer:
            {
                string  server      = form["ss_server"].ToString().Trim();
                string  database    = form["ss_database"].ToString().Trim();
                bool    windowsAuth = form["ss_windowsAuth"] == "on";
                string? user        = form["ss_user"];
                string? password    = form["ss_password"];
                bool    trustCert   = form["ss_trustCert"] == "on";

                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
                {
                    await WriteHtmlAsync(ctx, SetupPage.RenderStep1(w, "Le serveur et le nom de la base sont obligatoires.", values, _opts.AllowedProviders, preToken!));
                    return;
                }
                connectionString = setup.BuildSqlConnectionString(server, database, windowsAuth, user, password, trustCert);
                break;
            }
            case DbProvider.MySql:
            {
                string  server    = form["my_server"].ToString().Trim();
                string  database  = form["my_database"].ToString().Trim();
                string? user      = form["my_user"];
                string? password  = form["my_password"];
                bool    trustCert = form["my_trustCert"] == "on";
                if (!uint.TryParse(form["my_port"], out uint port) || port == 0) port = 3306;

                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(user))
                {
                    await WriteHtmlAsync(ctx, SetupPage.RenderStep1(w, "Le serveur, la base et l'utilisateur sont obligatoires.", values, _opts.AllowedProviders, preToken!));
                    return;
                }
                connectionString = setup.BuildMySqlConnectionString(server, port, database, user!, password, trustCert);
                break;
            }
            case DbProvider.Postgres:
            {
                string  server    = form["pg_server"].ToString().Trim();
                string  database  = form["pg_database"].ToString().Trim();
                string? user      = form["pg_user"];
                string? password  = form["pg_password"];
                bool    trustCert = form["pg_trustCert"] == "on";
                if (!int.TryParse(form["pg_port"], out int port) || port <= 0) port = 5432;

                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(user))
                {
                    await WriteHtmlAsync(ctx, SetupPage.RenderStep1(w, "Le serveur, la base et l'utilisateur sont obligatoires.", values, _opts.AllowedProviders, preToken!));
                    return;
                }
                connectionString = setup.BuildPostgresConnectionString(server, port, database, user!, password, trustCert);
                break;
            }
            case DbProvider.Sqlite:
            {
                string file = form["sq_file"].ToString().Trim();
                if (string.IsNullOrWhiteSpace(file))
                {
                    await WriteHtmlAsync(ctx, SetupPage.RenderStep1(w, "Le chemin du fichier SQLite est obligatoire.", values, _opts.AllowedProviders, preToken!));
                    return;
                }
                connectionString = setup.BuildSqliteConnectionString(file);
                break;
            }
            default:
                await WriteHtmlAsync(ctx, SetupPage.RenderStep1(w, "Type de base de donnees invalide.", values, _opts.AllowedProviders, preToken!));
                return;
        }

        string? err = await setup.TestConnectionAsync(provider, connectionString, ctx.RequestAborted);
        if (err is not null)
        {
            await WriteHtmlAsync(ctx, SetupPage.RenderStep1(w, $"Connexion echouee : {err}", values, _opts.AllowedProviders, preToken!));
            return;
        }

        // Chiffré dans un champ caché plutôt que conservé sur ce singleton : voir
        // SetupService.ProtectPendingState — survit à un redémarrage/recyclage du
        // process et fonctionne même si l'étape suivante atterrit sur un autre
        // worker process (IIS Web Garden, plusieurs instances derrière un proxy).
        string pendingStateToken = setup.ProtectPendingState(provider, connectionString);
        await WriteHtmlAsync(ctx, SetupPage.RenderStep2(w, null, provider, pendingStateToken));
    }

    private async Task Step2InitDbAsync(
        HttpContext ctx, SetupPage.Wizard w, List<ISetupPreInstallTask> tasks, SetupService setup, IFormCollection form)
    {
        string? pendingStateToken = form["pendingState"];
        if (!setup.TryUnprotectPendingState(pendingStateToken, out DbProvider provider, out string connectionString))
        {
            await StartAsync(ctx, w, tasks, setup, SessionExpired);
            return;
        }

        try
        {
            await setup.InitializeDatabaseAsync(provider, connectionString, ctx.RequestAborted);
        }
        catch (Exception ex)
        {
            await WriteHtmlAsync(ctx, SetupPage.RenderStep2(w, $"Initialisation echouee : {ex.Message}", provider, pendingStateToken!));
            return;
        }

        await WriteHtmlAsync(ctx, SetupPage.RenderStep3(w, null, null, pendingStateToken!, _opts.AllowUsernameAdmin));
    }

    private async Task Step3AdminAsync(
        HttpContext ctx, SetupPage.Wizard w, List<ISetupPreInstallTask> tasks, SetupService setup, IHostApplicationLifetime lifetime, ILogger logger,
        IFormCollection form, Dictionary<string, string> values)
    {
        string? pendingStateToken = form["pendingState"];
        if (!setup.TryUnprotectPendingState(pendingStateToken, out DbProvider provider, out string connectionString))
        {
            await StartAsync(ctx, w, tasks, setup, SessionExpired);
            return;
        }

        string  email       = form["adminEmail"].ToString().Trim();
        string? displayName = form["adminDisplayName"];
        string  password    = form["adminPassword"].ToString();
        string  confirm     = form["adminConfirm"].ToString();

        if (string.IsNullOrWhiteSpace(email))
        {
            string msg = _opts.AllowUsernameAdmin ? "Le nom d'utilisateur administrateur est obligatoire." : "L'email administrateur est obligatoire.";
            await WriteHtmlAsync(ctx, SetupPage.RenderStep3(w, msg, values, pendingStateToken!, _opts.AllowUsernameAdmin));
            return;
        }
        if (!_opts.AllowUsernameAdmin && !email.Contains('@'))
        { await WriteHtmlAsync(ctx, SetupPage.RenderStep3(w, "Adresse email invalide.", values, pendingStateToken!, _opts.AllowUsernameAdmin)); return; }
        if (password.Length < 8)
        { await WriteHtmlAsync(ctx, SetupPage.RenderStep3(w, "Le mot de passe doit faire au moins 8 caracteres.", values, pendingStateToken!, _opts.AllowUsernameAdmin)); return; }
        if (password != confirm)
        { await WriteHtmlAsync(ctx, SetupPage.RenderStep3(w, "Les mots de passe ne correspondent pas.", values, pendingStateToken!, _opts.AllowUsernameAdmin)); return; }

        try
        {
            await setup.CreateAdminAsync(provider, connectionString, new AdminAccount(email, password, displayName), ctx.RequestAborted);
        }
        catch (Exception ex)
        {
            await WriteHtmlAsync(ctx, SetupPage.RenderStep3(w, ex.Message, values, pendingStateToken!, _opts.AllowUsernameAdmin));
            return;
        }

        IReadOnlyList<(string Id, string Label)> extraSteps = setup.GetExtraStepDescriptors();
        if (extraSteps.Count > 0)
        {
            SetupExtraStepContext stepCtx = new(appName, provider, connectionString, pendingStateToken!, null, null);
            string body = await setup.RenderExtraStepAsync(extraSteps[0].Id, stepCtx, ctx.RequestAborted);
            await WriteHtmlAsync(ctx, SetupPage.RenderExtraStep(w, null, body, 1));
            return;
        }

        setup.CompleteSetup(provider, connectionString);
        await WriteHtmlAsync(ctx, SetupPage.RenderSuccess(w));
        ScheduleRestart(lifetime, logger);
    }

    private async Task StepExtraAsync(
        HttpContext ctx, SetupPage.Wizard w, List<ISetupPreInstallTask> tasks, SetupService setup,
        IHostApplicationLifetime lifetime, ILogger logger,
        IFormCollection form, Dictionary<string, string> values, int extraIndex)
    {
        IReadOnlyList<(string Id, string Label)> extraSteps = setup.GetExtraStepDescriptors();
        string? pendingStateToken = form["pendingState"];

        if (!setup.TryUnprotectPendingState(pendingStateToken, out DbProvider provider, out string connectionString))
        {
            await StartAsync(ctx, w, tasks, setup, SessionExpired);
            return;
        }

        SetupExtraStepContext handleCtx = new(appName, provider, connectionString, pendingStateToken!, null, values);
        SetupExtraStepResult result = await setup.HandleExtraStepAsync(extraSteps[extraIndex].Id, handleCtx, form, ctx.RequestAborted);

        if (!result.IsSuccess)
        {
            SetupExtraStepContext errorCtx = handleCtx with { Error = result.Error };
            string body = await setup.RenderExtraStepAsync(extraSteps[extraIndex].Id, errorCtx, ctx.RequestAborted);
            await WriteHtmlAsync(ctx, SetupPage.RenderExtraStep(w, result.Error, body, extraIndex + 1));
            return;
        }

        int nextIndex = extraIndex + 1;
        if (nextIndex < extraSteps.Count)
        {
            SetupExtraStepContext nextCtx = new(appName, provider, connectionString, pendingStateToken!, null, null);
            string body = await setup.RenderExtraStepAsync(extraSteps[nextIndex].Id, nextCtx, ctx.RequestAborted);
            await WriteHtmlAsync(ctx, SetupPage.RenderExtraStep(w, null, body, nextIndex + 1));
            return;
        }

        setup.CompleteSetup(provider, connectionString);
        await WriteHtmlAsync(ctx, SetupPage.RenderSuccess(w));
        ScheduleRestart(lifetime, logger);
    }

    private static void ScheduleRestart(IHostApplicationLifetime lifetime, ILogger logger)
    {
        logger.LogInformation("[Setup] Installation terminee — redemarrage de l'application.");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            lifetime.StopApplication();
        });
    }

    private static async Task WriteHtmlAsync(HttpContext ctx, string html)
    {
        ctx.Response.StatusCode  = StatusCodes.Status200OK;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(html);
    }

    // ── Assets embarqués (servis hors-ligne depuis l'assembly) ────────────────

    private const string AssetPrefix = "/setup/_assets/";

    private static readonly Assembly Asm = typeof(SetupPage).Assembly;

    private async Task ServeAssetAsync(HttpContext ctx, string resourceName)
    {
        // resourceName = chemin relatif (ex. "bootstrap.min.css", "fonts/bootstrap-icons.woff2")
        await using Stream? stream = Asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.Response.StatusCode  = StatusCodes.Status200OK;
        ctx.Response.ContentType = ContentTypeFor(resourceName);
        ctx.Response.Headers.CacheControl = "public, max-age=604800"; // 7 jours
        await stream.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
    }

    private static string ContentTypeFor(string name)
    {
        if (name.EndsWith(".css",   StringComparison.OrdinalIgnoreCase)) return "text/css; charset=utf-8";
        if (name.EndsWith(".js",    StringComparison.OrdinalIgnoreCase)) return "text/javascript; charset=utf-8";
        if (name.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)) return "font/woff2";
        if (name.EndsWith(".woff",  StringComparison.OrdinalIgnoreCase)) return "font/woff";
        return "application/octet-stream";
    }

    private bool IsAllowed(string path) =>
        _opts.AllowedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
