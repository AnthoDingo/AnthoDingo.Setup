using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnthoDingo.Setup;

/// <summary>
/// Service d'installation. Enregistré en <b>singleton</b> : il ne dépend
/// d'aucun DbContext et se contente de lire/écrire un fichier de configuration
/// local, ce qui rend la détection d'état très peu coûteuse (aucune requête SQL
/// sur chaque requête HTTP).
///
/// Les opérations base de données (migrations, création de l'admin) sont
/// déléguées à <see cref="ISetupInitializer"/>, résolu dans un scope dédié.
/// </summary>
public sealed class SetupService(
    IHostEnvironment            env,
    IServiceScopeFactory        scopeFactory,
    IOptions<SetupOptions>      options,
    ILogger<SetupService>       logger)
{
    private readonly SetupOptions _opts = options.Value;
    private bool? _cachedComplete;

    /// <summary>
    /// Chaîne de connexion validée à l'étape 1, conservée en mémoire entre les
    /// étapes du wizard (évite de transporter le mot de passe dans le HTML).
    /// </summary>
    public string? PendingConnectionString { get; set; }

    // ── Détection ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Indique si l'installation est terminée, d'après le fichier local
    /// (<c>Setup:IsComplete = true</c>). Résultat mis en cache en mémoire.
    /// </summary>
    public bool IsSetupComplete()
    {
        if (_cachedComplete.HasValue) return _cachedComplete.Value;

        string path = LocalConfigPath();
        if (!File.Exists(path)) { _cachedComplete = false; return false; }

        try
        {
            IConfigurationRoot cfg = new ConfigurationBuilder()
                .AddJsonFile(path, optional: false)
                .Build();
            _cachedComplete = cfg["Setup:IsComplete"] == "true";
            return _cachedComplete.Value;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Setup] Lecture de {Path} impossible.", path);
            _cachedComplete = false;
            return false;
        }
    }

    // ── Étape 1 — tester la connexion SQL ─────────────────────────────────────

    /// <summary>Tente d'ouvrir une connexion. Retourne <c>null</c> si succès, sinon le message d'erreur.</summary>
    public async Task<string?> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using SqlConnection conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            logger.LogInformation("[Setup] Test de connexion OK.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning("[Setup] Test de connexion échoué : {Error}", ex.Message);
            return ex.Message;
        }
    }

    // ── Étape 2 — initialiser la base (délégué à l'application) ───────────────

    public async Task InitializeDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ISetupInitializer init = scope.ServiceProvider.GetRequiredService<ISetupInitializer>();
        await init.InitializeDatabaseAsync(connectionString, ct);
        logger.LogInformation("[Setup] Base initialisée.");
    }

    // ── Étape 3 — créer l'admin (délégué à l'application) ─────────────────────

    public async Task CreateAdminAsync(string connectionString, AdminAccount admin, CancellationToken ct = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ISetupInitializer init = scope.ServiceProvider.GetRequiredService<ISetupInitializer>();
        await init.CreateAdminAsync(connectionString, admin, ct);
        logger.LogInformation("[Setup] Compte admin « {User} » créé.", admin.UserName);
    }

    // ── Finalisation — écrire appsettings.local.json ──────────────────────────

    /// <summary>
    /// Écrit le fichier local avec <c>Setup:IsComplete = true</c> et la chaîne de
    /// connexion. L'application doit redémarrer ensuite pour charger la nouvelle
    /// configuration (p. ex. via <c>IHostApplicationLifetime.StopApplication()</c>).
    /// </summary>
    public void CompleteSetup(string connectionString)
    {
        Dictionary<string, object> config = new Dictionary<string, object>
        {
            ["Setup"]             = new Dictionary<string, object> { ["IsComplete"] = "true" },
            ["ConnectionStrings"] = new Dictionary<string, object> { [_opts.ConnectionStringName] = connectionString }
        };

        File.WriteAllText(
            LocalConfigPath(),
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        _cachedComplete = true;
        logger.LogInformation("[Setup] {File} écrit — installation terminée.", _opts.LocalConfigFileName);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Construit une chaîne de connexion SQL Server à partir de champs de formulaire.</summary>
    public string BuildSqlConnectionString(
        string server, string database, bool windowsAuth,
        string? user, string? password, bool trustServerCertificate = true)
    {
        SqlConnectionStringBuilder sb = new SqlConnectionStringBuilder
        {
            DataSource             = server.Trim(),
            InitialCatalog         = database.Trim(),
            IntegratedSecurity     = windowsAuth,
            TrustServerCertificate = trustServerCertificate,
            ConnectTimeout         = 10
        };
        if (!windowsAuth)
        {
            sb.UserID   = user?.Trim() ?? string.Empty;
            sb.Password = password     ?? string.Empty;
        }
        return sb.ConnectionString;
    }

    /// <summary>Chemin absolu du fichier de configuration local.</summary>
    public string LocalConfigPath() =>
        Path.Combine(env.ContentRootPath, _opts.LocalConfigFileName);
}
