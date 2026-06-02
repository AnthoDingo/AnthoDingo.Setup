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
}
