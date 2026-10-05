using Microsoft.AspNetCore.Http;

namespace AnthoDingo.Setup;

/// <summary>
/// Contexte transmis à une étape préliminaire (<see cref="ISetupPreStep"/>) pour son
/// rendu. Aucune base n'est encore configurée à ce stade.
/// </summary>
/// <param name="AppName">Nom de l'application, tel que passé à <c>UseSetupMiddleware</c>.</param>
/// <param name="Error">Message d'erreur à afficher, le cas échéant (ré-affichage après échec de <see cref="ISetupPreStep.HandleAsync"/>).</param>
/// <param name="Values">Valeurs postées par le formulaire de l'étape, pour ré-affichage en cas d'erreur (saisie utilisateur : à encoder).</param>
public sealed record SetupPreStepContext(string AppName, string? Error, IDictionary<string, string>? Values);

/// <summary>
/// Étape préliminaire <b>interactive</b> du wizard (clé d'activation, numéro de licence,
/// code d'accès…), affichée juste après la licence et avant les tâches de
/// pré-installation et la connexion à la base. L'assistant ne passe à l'étape suivante
/// que si <see cref="HandleAsync"/> réussit ; la progression voyage dans un jeton chiffré,
/// un POST forgé ne permet donc pas de sauter l'étape.
///
/// Enregistrée via <see cref="SetupExtensions.AddSetupPreStep{TStep}"/> ; plusieurs étapes
/// s'enchaînent dans leur ordre d'enregistrement. Résolue depuis le conteneur (scoped) :
/// injectable normalement, hors DbContext (la base n'est pas encore configurée). Pour
/// conserver la saisie (ex. la clé), l'étape la persiste elle-même dans <see cref="HandleAsync"/>.
/// </summary>
public interface ISetupPreStep
{
    /// <summary>Titre de la page et libellé dans l'indicateur d'étapes.</summary>
    string Label { get; }

    /// <summary>
    /// Champs HTML de l'étape (sans <c>&lt;form&gt;</c> ni bouton : la bibliothèque fournit
    /// le formulaire, ses champs cachés et le bouton « Suivant »). HTML inséré sans encodage :
    /// encodez les valeurs de <see cref="SetupPreStepContext.Values"/> que vous ré-affichez.
    /// </summary>
    Task<string> RenderAsync(SetupPreStepContext context, CancellationToken ct);

    /// <summary>
    /// Valide la saisie. <see cref="SetupExtraStepResult.Failure"/> : l'étape est ré-affichée
    /// avec le message d'erreur et les valeurs postées.
    /// </summary>
    Task<SetupExtraStepResult> HandleAsync(SetupPreStepContext context, IFormCollection form, CancellationToken ct);
}
