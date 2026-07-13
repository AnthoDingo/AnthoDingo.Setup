using System.Reflection;
using Microsoft.AspNetCore.Http;
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
            if (HttpMethods.IsPost(ctx.Request.Method))
                await HandleInstallAsync(ctx, setup, lifetime, logger);
            else
            {
                setup.PendingConnectionString = null;   // nouvelle session → repart à l'étape 1
                setup.PendingProvider = null;
                await WriteHtmlAsync(ctx, SetupPage.RenderStep1(appName, null, null, _opts.AllowedProviders));
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
        HttpContext ctx, SetupService setup, IHostApplicationLifetime lifetime, ILogger logger)
    {
        IFormCollection form = await ctx.Request.ReadFormAsync();
        Dictionary<string, string> values = form.ToDictionary(f => f.Key, f => f.Value.ToString());
        string step = form["step"].ToString();

        switch (step)
        {
            case "1": await Step1ConnectionAsync(ctx, setup, form, values); break;
            case "2": await Step2InitDbAsync(ctx, setup); break;
            case "3": await Step3AdminAsync(ctx, setup, lifetime, logger, form, values); break;
            default:  await WriteHtmlAsync(ctx, SetupPage.RenderStep1(appName, null, null, _opts.AllowedProviders)); break;
        }
    }

    private async Task Step1ConnectionAsync(
        HttpContext ctx, SetupService setup, IFormCollection form, Dictionary<string, string> values)
    {
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
                    await WriteHtmlAsync(ctx, SetupPage.RenderStep1(appName, "Le serveur et le nom de la base sont obligatoires.", values, _opts.AllowedProviders));
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
                    await WriteHtmlAsync(ctx, SetupPage.RenderStep1(appName, "Le serveur, la base et l'utilisateur sont obligatoires.", values, _opts.AllowedProviders));
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
                    await WriteHtmlAsync(ctx, SetupPage.RenderStep1(appName, "Le serveur, la base et l'utilisateur sont obligatoires.", values, _opts.AllowedProviders));
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
                    await WriteHtmlAsync(ctx, SetupPage.RenderStep1(appName, "Le chemin du fichier SQLite est obligatoire.", values, _opts.AllowedProviders));
                    return;
                }
                connectionString = setup.BuildSqliteConnectionString(file);
                break;
            }
            default:
                await WriteHtmlAsync(ctx, SetupPage.RenderStep1(appName, "Type de base de donnees invalide.", values, _opts.AllowedProviders));
                return;
        }

        string? err = await setup.TestConnectionAsync(provider, connectionString, ctx.RequestAborted);
        if (err is not null)
        {
            await WriteHtmlAsync(ctx, SetupPage.RenderStep1(appName, $"Connexion echouee : {err}", values, _opts.AllowedProviders));
            return;
        }

        setup.PendingProvider         = provider;                // conservés pour les étapes 2/3
        setup.PendingConnectionString = connectionString;
        await WriteHtmlAsync(ctx, SetupPage.RenderStep2(appName, null, provider));
    }

    private async Task Step2InitDbAsync(HttpContext ctx, SetupService setup)
    {
        if (setup.PendingConnectionString is null || setup.PendingProvider is null)
        {
            await WriteHtmlAsync(ctx, SetupPage.RenderStep1(appName, "Session expiree, recommencez.", null, _opts.AllowedProviders));
            return;
        }

        try
        {
            await setup.InitializeDatabaseAsync(setup.PendingProvider.Value, setup.PendingConnectionString, ctx.RequestAborted);
        }
        catch (Exception ex)
        {
            await WriteHtmlAsync(ctx, SetupPage.RenderStep2(appName, $"Initialisation echouee : {ex.Message}", setup.PendingProvider.Value));
            return;
        }

        await WriteHtmlAsync(ctx, SetupPage.RenderStep3(appName, null, null));
    }

    private async Task Step3AdminAsync(
        HttpContext ctx, SetupService setup, IHostApplicationLifetime lifetime, ILogger logger,
        IFormCollection form, Dictionary<string, string> values)
    {
        if (setup.PendingConnectionString is null || setup.PendingProvider is null)
        {
            await WriteHtmlAsync(ctx, SetupPage.RenderStep1(appName, "Session expiree, recommencez.", null, _opts.AllowedProviders));
            return;
        }

        string  email       = form["adminEmail"].ToString().Trim();
        string? displayName = form["adminDisplayName"];
        string  password    = form["adminPassword"].ToString();
        string  confirm     = form["adminConfirm"].ToString();

        if (string.IsNullOrWhiteSpace(email))
        { await WriteHtmlAsync(ctx, SetupPage.RenderStep3(appName, "L'email administrateur est obligatoire.", values)); return; }
        if (password.Length < 8)
        { await WriteHtmlAsync(ctx, SetupPage.RenderStep3(appName, "Le mot de passe doit faire au moins 8 caracteres.", values)); return; }
        if (password != confirm)
        { await WriteHtmlAsync(ctx, SetupPage.RenderStep3(appName, "Les mots de passe ne correspondent pas.", values)); return; }

        DbProvider provider = setup.PendingProvider.Value;

        try
        {
            await setup.CreateAdminAsync(provider, setup.PendingConnectionString, new AdminAccount(email, password, displayName), ctx.RequestAborted);
        }
        catch (Exception ex)
        {
            await WriteHtmlAsync(ctx, SetupPage.RenderStep3(appName, ex.Message, values));
            return;
        }

        setup.CompleteSetup(provider, setup.PendingConnectionString);
        setup.PendingConnectionString = null;
        setup.PendingProvider = null;
        await WriteHtmlAsync(ctx, SetupPage.RenderSuccess(appName));

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
