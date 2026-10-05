namespace AnthoDingo.Setup;

/// <summary>Résultat d'une <see cref="ISetupPreInstallTask"/>.</summary>
/// <param name="Success">Si <c>false</c>, l'assistant bloque sur cette tâche et propose « Réessayer ».</param>
/// <param name="Html">Contenu affiché dans la page de la tâche (HTML de confiance, non encodé).</param>
/// <param name="Error">Message d'erreur affiché si <paramref name="Success"/> vaut <c>false</c> (encodé).</param>
public sealed record SetupTaskResult(bool Success, string? Html = null, string? Error = null)
{
    /// <summary>Tâche réussie : bouton « Suivant ».</summary>
    public static SetupTaskResult Ok(string? html = null) => new(true, html);
    /// <summary>Tâche en échec : message d'erreur + bouton « Réessayer ».</summary>
    public static SetupTaskResult Fail(string error, string? html = null) => new(false, html, error);
}

/// <summary>
/// Tâche exécutée par l'assistant <b>avant</b> l'étape de connexion à la base
/// (vérification des prérequis, avertissement, etc.). Chaque tâche a sa propre
/// page ; elles s'enchaînent dans l'ordre d'enregistrement
/// (voir <see cref="SetupExtensions.AddSetupPreInstallTask{TTask}"/>).
/// Résolue depuis le conteneur (scoped) : l'implémentation peut injecter ce
/// dont elle a besoin, hors DbContext (la base n'est pas encore configurée).
/// </summary>
public interface ISetupPreInstallTask
{
    /// <summary>Titre de la page et libellé dans l'indicateur d'étapes.</summary>
    string Title { get; }

    /// <summary>Exécutée à chaque affichage de la page de la tâche (et à chaque « Réessayer »).</summary>
    Task<SetupTaskResult> ExecuteAsync(CancellationToken ct = default);
}
