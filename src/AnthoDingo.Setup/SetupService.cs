using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Npgsql;

namespace AnthoDingo.Setup;

/// <summary>
/// Service d'installation. Enregistré en <b>singleton</b> : il ne dépend
/// d'aucun DbContext et se contente de lire/écrire un fichier de configuration
/// local, ce qui rend la détection d'état très peu coûteuse (aucune requête SQL
/// sur chaque requête HTTP).
///
/// Les opérations base de données (migrations, création de l'admin) sont
/// déléguées à <see cref="ISetupInitializer"/>, résolu dans un scope dédié.
/// Quatre types de base sont pris en charge : SQL Server, MySQL/MariaDB,
/// PostgreSQL et SQLite (voir <see cref="DbProvider"/>).
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

    /// <summary>Type de base de données validé à l'étape 1, conservé entre les étapes du wizard.</summary>
    public DbProvider? PendingProvider { get; set; }

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

    /// <summary>
    /// Type de base de données choisi lors de l'installation, d'après le fichier
    /// local (<c>Setup:Provider</c>). Retourne <c>null</c> si l'installation
    /// n'est pas terminée ou si la valeur est absente/invalide.
    /// </summary>
    public DbProvider? GetConfiguredProvider()
    {
        string path = LocalConfigPath();
        if (!File.Exists(path)) return null;

        try
        {
            IConfigurationRoot cfg = new ConfigurationBuilder()
                .AddJsonFile(path, optional: false)
                .Build();
            string? raw = cfg["Setup:Provider"];
            return raw is not null && Enum.TryParse(raw, ignoreCase: true, out DbProvider provider) ? provider : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Setup] Lecture du provider dans {Path} impossible.", path);
            return null;
        }
    }

    // ── Étape 1 — tester la connexion ─────────────────────────────────────────

    /// <summary>Tente d'ouvrir une connexion pour le provider donné. Retourne <c>null</c> si succès, sinon le message d'erreur.</summary>
    public async Task<string?> TestConnectionAsync(DbProvider provider, string connectionString, CancellationToken ct = default)
    {
        try
        {
            await using DbConnection conn = CreateConnection(provider, connectionString);
            await conn.OpenAsync(ct);
            logger.LogInformation("[Setup] Test de connexion OK ({Provider}).", provider);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning("[Setup] Test de connexion échoué ({Provider}) : {Error}", provider, ex.Message);
            return ex.Message;
        }
    }

    private static DbConnection CreateConnection(DbProvider provider, string connectionString) => provider switch
    {
        DbProvider.SqlServer => new SqlConnection(connectionString),
        DbProvider.MySql     => new MySqlConnection(connectionString),
        DbProvider.Postgres  => new NpgsqlConnection(connectionString),
        DbProvider.Sqlite    => new SqliteConnection(connectionString),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Type de base de données inconnu.")
    };

    // ── Étape 2 — initialiser la base (délégué à l'application) ───────────────

    public async Task InitializeDatabaseAsync(DbProvider provider, string connectionString, CancellationToken ct = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ISetupInitializer init = scope.ServiceProvider.GetRequiredService<ISetupInitializer>();
        await init.InitializeDatabaseAsync(provider, connectionString, ct);
        logger.LogInformation("[Setup] Base initialisée ({Provider}).", provider);
    }

    // ── Étape 3 — créer l'admin (délégué à l'application) ─────────────────────

    public async Task CreateAdminAsync(DbProvider provider, string connectionString, AdminAccount admin, CancellationToken ct = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ISetupInitializer init = scope.ServiceProvider.GetRequiredService<ISetupInitializer>();
        await init.CreateAdminAsync(provider, connectionString, admin, ct);
        logger.LogInformation("[Setup] Compte admin « {User} » créé ({Provider}).", admin.UserName, provider);
    }

    // ── Finalisation — écrire appsettings.local.json ──────────────────────────

    /// <summary>
    /// Écrit le fichier local avec <c>Setup:IsComplete = true</c>, le provider
    /// choisi (<c>Setup:Provider</c>) et la chaîne de connexion. L'application
    /// doit redémarrer ensuite pour charger la nouvelle configuration (p. ex.
    /// via <c>IHostApplicationLifetime.StopApplication()</c>).
    /// </summary>
    public void CompleteSetup(DbProvider provider, string connectionString)
    {
        Dictionary<string, object> config = new Dictionary<string, object>
        {
            ["Setup"] = new Dictionary<string, object>
            {
                ["IsComplete"] = "true",
                ["Provider"]   = provider.ToString()
            },
            ["ConnectionStrings"] = new Dictionary<string, object> { [_opts.ConnectionStringName] = connectionString }
        };

        File.WriteAllText(
            LocalConfigPath(),
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        _cachedComplete = true;
        logger.LogInformation("[Setup] {File} écrit — installation terminée ({Provider}).", _opts.LocalConfigFileName, provider);
    }

    // ── Construction des chaînes de connexion ──────────────────────────────────

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

    /// <summary>Construit une chaîne de connexion MySQL/MariaDB (pilote MySqlConnector).</summary>
    public string BuildMySqlConnectionString(
        string server, uint port, string database,
        string user, string? password, bool ignoreSslErrors = true)
    {
        MySqlConnectionStringBuilder sb = new MySqlConnectionStringBuilder
        {
            Server             = server.Trim(),
            Port               = port,
            Database           = database.Trim(),
            UserID             = user.Trim(),
            Password           = password ?? string.Empty,
            SslMode            = ignoreSslErrors ? MySqlSslMode.Preferred : MySqlSslMode.Required,
            ConnectionTimeout  = 10
        };
        return sb.ConnectionString;
    }

    /// <summary>Construit une chaîne de connexion PostgreSQL (pilote Npgsql).</summary>
    public string BuildPostgresConnectionString(
        string server, int port, string database,
        string user, string? password, bool ignoreSslErrors = true)
    {
        NpgsqlConnectionStringBuilder sb = new NpgsqlConnectionStringBuilder
        {
            Host                   = server.Trim(),
            Port                   = port,
            Database               = database.Trim(),
            Username               = user.Trim(),
            Password               = password ?? string.Empty,
            Timeout                = 10,
            SslMode                = ignoreSslErrors ? SslMode.Prefer : SslMode.Require,
            TrustServerCertificate = ignoreSslErrors
        };
        return sb.ConnectionString;
    }

    /// <summary>
    /// Construit une chaîne de connexion SQLite. <paramref name="filePath"/> peut
    /// être relatif (résolu depuis le dossier de l'application) ou absolu ; le
    /// dossier parent est créé si nécessaire.
    /// </summary>
    public string BuildSqliteConnectionString(string filePath)
    {
        string trimmed  = filePath.Trim();
        string resolved = Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(env.ContentRootPath, trimmed);

        string? dir = Path.GetDirectoryName(resolved);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        SqliteConnectionStringBuilder sb = new SqliteConnectionStringBuilder
        {
            DataSource = resolved,
            Mode       = SqliteOpenMode.ReadWriteCreate
        };
        return sb.ConnectionString;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Chemin absolu du fichier de configuration local.</summary>
    public string LocalConfigPath() =>
        Path.Combine(env.ContentRootPath, _opts.LocalConfigFileName);
}
