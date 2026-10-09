using System.Data.Common;

namespace AnthoDingo.Setup;

/// <summary>
/// Options de configuration du middleware d'installation.
/// </summary>
public sealed class SetupOptions
{
    /// <summary>
    /// Nom du fichier de configuration local écrit à la fin de l'installation.
    /// Il est chargé au démarrage avec priorité sur appsettings.json et ne doit
    /// jamais être committé sur Git.
    /// </summary>
    public string LocalConfigFileName { get; set; } = "appsettings.local.json";

    /// <summary>Chemin de l'assistant d'installation (cible de la redirection).</summary>
    public string SetupPath { get; set; } = "/setup";

    /// <summary>
    /// Nom de la chaîne de connexion écrite dans le fichier local
    /// (section <c>ConnectionStrings</c>).
    /// </summary>
    public string ConnectionStringName { get; set; } = "DefaultConnection";

    /// <summary>
    /// Préfixes d'URL toujours autorisés, même tant que l'installation n'est pas
    /// terminée (assets du framework, API, swagger, etc.).
    /// </summary>
    public List<string> AllowedPrefixes { get; set; } =
    [
        "/setup",
        "/_framework",
        "/_blazor",
        "/_content",
        "/css",
        "/js",
        "/lib",
        "/favicon",
        "/uploads",
        "/api",
        "/swagger",
        "/signin-oidc",
        "/signin-okta"
    ];

    /// <summary>
    /// Pilotes ADO.NET des types de base proposés à l'étape 1 de l'assistant :
    /// la bibliothèque n'embarque aucun pilote et s'en sert pour tester la
    /// connexion et construire la chaîne de connexion. Au moins un pilote doit
    /// être enregistré (vérifié au démarrage). Le premier ajouté est présélectionné.
    /// <para>
    /// Soit l'application fournit le pilote qu'elle référence déjà
    /// (<c>o.Providers[DbProvider.Postgres] = NpgsqlFactory.Instance</c>), soit
    /// elle référence le package <c>AnthoDingo.Setup.Providers</c> et appelle
    /// <c>o.AddDefaultProviders()</c> pour les quatre types pris en charge.
    /// </para>
    /// </summary>
    public OrderedDictionary<DbProvider, DbProviderFactory> Providers { get; } = new();

    /// <summary>
    /// Si <c>true</c>, le compte administrateur créé à l'étape 3 peut être
    /// identifié par un nom d'utilisateur plutôt qu'une adresse email (le champ
    /// n'est alors plus validé/typé comme un email). Par défaut <c>false</c> :
    /// une adresse email est exigée.
    /// </summary>
    public bool AllowUsernameAdmin { get; set; }

    /// <summary>
    /// Texte de la licence d'utilisation. S'il est renseigné, l'assistant affiche
    /// d'abord une page « Licence » (texte brut, retours à la ligne conservés)
    /// avec un bouton « Suivant », avant les tâches de pré-installation et la
    /// connexion à la base. Par défaut <c>null</c> : pas de page licence.
    /// </summary>
    public string? LicenseText { get; set; }

    /// <summary>
    /// Si <c>true</c> (et <see cref="LicenseText"/> renseigné), la page licence
    /// affiche une case « J'accepte les termes de la licence » qui doit être
    /// cochée pour continuer. Par défaut <c>false</c>.
    /// </summary>
    public bool RequireLicenseAcceptance { get; set; }
}
