using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
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
    IDataProtectionProvider     dataProtectionProvider,
    ILogger<SetupService>       logger)
{
    private readonly SetupOptions _opts = options.Value;
    private readonly ITimeLimitedDataProtector _pendingStateProtector =
        dataProtectionProvider.CreateProtector("AnthoDingo.Setup.PendingState").ToTimeLimitedDataProtector();
    private bool? _cachedComplete;
    private IReadOnlyList<(string Id, string Label)>? _cachedExtraStepDescriptors;

    private static readonly TimeSpan PendingStateLifetime = TimeSpan.FromMinutes(30);

    private sealed record PendingState(DbProvider Provider, string ConnectionString);

    /// <summary>
    /// Chiffre le provider et la chaîne de connexion validés à l'étape 1 dans un
    /// jeton opaque, à transporter d'une étape à l'autre du wizard via un champ
    /// caché du formulaire plutôt qu'en mémoire serveur — voir
    /// <see cref="TryUnprotectPendingState"/>. Contrairement à un état conservé sur
    /// ce singleton, ce jeton survit à un redémarrage/recyclage du process entre
    /// deux étapes et fonctionne même si les requêtes successives atterrissent sur
    /// des worker processes différents (IIS en Web Garden, plusieurs instances
    /// derrière un reverse proxy). Expire de lui-même après <see cref="PendingStateLifetime"/>.
    /// </summary>
    public string ProtectPendingState(DbProvider provider, string connectionString)
    {
        string json = JsonSerializer.Serialize(new PendingState(provider, connectionString));
        return _pendingStateProtector.Protect(json, PendingStateLifetime);
    }

    /// <summary>
    /// Déchiffre un jeton produit par <see cref="ProtectPendingState"/>. Retourne
    /// <c>false</c> si <paramref name="token"/> est absent, altéré ou expiré — le
    /// wizard doit alors repartir de l'étape 1.
    /// </summary>
    public bool TryUnprotectPendingState(string? token, out DbProvider provider, out string connectionString)
    {
        provider = default;
        connectionString = string.Empty;
        if (string.IsNullOrEmpty(token)) return false;

        try
        {
            string json = _pendingStateProtector.Unprotect(token);
            PendingState? state = JsonSerializer.Deserialize<PendingState>(json);
            if (state is null) return false;

            provider = state.Provider;
            connectionString = state.ConnectionString;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private readonly ITimeLimitedDataProtector _preInstallProtector =
        dataProtectionProvider.CreateProtector("AnthoDingo.Setup.PreInstall").ToTimeLimitedDataProtector();

    /// <summary>
    /// Chiffre l'étape de pré-installation atteinte (licence acceptée, tâches
    /// réussies) dans un jeton opaque, transporté comme <see cref="ProtectPendingState"/>
    /// par un champ caché : empêche de sauter la licence ou un prérequis en
    /// postant directement une étape ultérieure.
    /// </summary>
    public string ProtectPreInstallStage(int stage) =>
        _preInstallProtector.Protect(stage.ToString(System.Globalization.CultureInfo.InvariantCulture), PendingStateLifetime);

    /// <summary>Déchiffre un jeton de <see cref="ProtectPreInstallStage"/> ; <c>false</c> si absent, altéré ou expiré.</summary>
    public bool TryUnprotectPreInstallStage(string? token, out int stage)
    {
        stage = -1;
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            return int.TryParse(_preInstallProtector.Unprotect(token), System.Globalization.CultureInfo.InvariantCulture, out stage);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

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

    // ── Étapes supplémentaires (fournies par l'application hôte) ──────────────

    /// <summary>
    /// Identifiants et libellés des étapes supplémentaires enregistrées via
    /// <see cref="SetupExtensions.AddSetupStep{TStep}"/>, dans leur ordre
    /// d'enregistrement. Mis en cache après la première résolution : la liste
    /// des étapes enregistrées ne change pas en cours de vie de l'application.
    /// </summary>
    public IReadOnlyList<(string Id, string Label)> GetExtraStepDescriptors()
    {
        if (_cachedExtraStepDescriptors is not null) return _cachedExtraStepDescriptors;

        using IServiceScope scope = scopeFactory.CreateScope();
        _cachedExtraStepDescriptors = scope.ServiceProvider.GetServices<ISetupExtraStep>()
            .Select(s => (s.Id, s.Label))
            .ToList();
        return _cachedExtraStepDescriptors;
    }

    /// <summary>Résout et affiche l'étape supplémentaire <paramref name="id"/> (voir <see cref="ISetupExtraStep.RenderAsync"/>).</summary>
    public async Task<string> RenderExtraStepAsync(string id, SetupExtraStepContext context, CancellationToken ct = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        return await ResolveExtraStep(scope, id).RenderAsync(context, ct);
    }

    /// <summary>Résout et traite la soumission de l'étape supplémentaire <paramref name="id"/> (voir <see cref="ISetupExtraStep.HandleAsync"/>).</summary>
    public async Task<SetupExtraStepResult> HandleExtraStepAsync(string id, SetupExtraStepContext context, IFormCollection form, CancellationToken ct = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        return await ResolveExtraStep(scope, id).HandleAsync(context, form, ct);
    }

    private static ISetupExtraStep ResolveExtraStep(IServiceScope scope, string id) =>
        scope.ServiceProvider.GetServices<ISetupExtraStep>().FirstOrDefault(s => s.Id == id)
            ?? throw new InvalidOperationException($"Aucune etape supplementaire enregistree avec l'identifiant « {id} ».");

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
