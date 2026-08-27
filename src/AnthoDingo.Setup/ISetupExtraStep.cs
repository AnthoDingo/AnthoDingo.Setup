using Microsoft.AspNetCore.Http;

namespace AnthoDingo.Setup;

/// <summary>
/// Contexte transmis à une étape supplémentaire du wizard (<see cref="ISetupExtraStep"/>)
/// pour son rendu et son traitement. Le provider et la chaîne de connexion sont ceux
/// validés à l'étape 1 ; l'étape peut s'en servir pour ouvrir son propre DbContext, comme
/// le fait <see cref="ISetupInitializer"/>.
/// </summary>
/// <param name="AppName">Nom de l'application, tel que passé à <c>UseSetupMiddleware</c>.</param>
/// <param name="Provider">Type de base validé à l'étape 1.</param>
/// <param name="ConnectionString">Chaîne de connexion validée à l'étape 1.</param>
/// <param name="PendingStateToken">
/// Jeton chiffré à reporter dans un champ caché <c>pendingState</c> du formulaire de
/// l'étape, pour que le wizard retrouve <paramref name="Provider"/> et
/// <paramref name="ConnectionString"/> à l'étape suivante.
/// </param>
/// <param name="Error">Message d'erreur à afficher, le cas échéant (ré-affichage après échec de <see cref="ISetupExtraStep.HandleAsync"/>).</param>
/// <param name="Values">Valeurs postées par le formulaire de l'étape, pour ré-affichage en cas d'erreur.</param>
public sealed record SetupExtraStepContext(
    string AppName,
    DbProvider Provider,
    string ConnectionString,
    string PendingStateToken,
    string? Error,
    IDictionary<string, string>? Values);

/// <summary>Résultat du traitement (<see cref="ISetupExtraStep.HandleAsync"/>) d'une étape supplémentaire.</summary>
public sealed record SetupExtraStepResult(bool IsSuccess, string? Error = null)
{
    /// <summary>L'étape s'est déroulée avec succès : le wizard passe à l'étape suivante.</summary>
    public static SetupExtraStepResult Success() => new(true);

    /// <summary>L'étape a échoué : elle est ré-affichée avec <paramref name="error"/>.</summary>
    public static SetupExtraStepResult Failure(string error) => new(false, error);
}

/// <summary>
/// Étape supplémentaire du wizard d'installation, ajoutée par l'application hôte entre la
/// création du compte administrateur et le redémarrage final. Permet aux solutions qui
/// consomment cette bibliothèque d'étendre le premier paramétrage (préférences, licence,
/// configuration métier propre à l'application, etc.) sans dupliquer la page ni le
/// mécanisme de progression du wizard.
///
/// Enregistrée via <see cref="SetupExtensions.AddSetupStep{TStep}"/> ; plusieurs étapes
/// peuvent être ajoutées et s'exécutent dans leur ordre d'enregistrement, entre la création
/// de l'admin et l'écriture du fichier local (<see cref="SetupService.CompleteSetup"/>) qui
/// déclenche le redémarrage. Résolue dans un scope dédié (comme
/// <see cref="ISetupInitializer"/>), donc injectable normalement (DbContext, options, etc.).
/// </summary>
public interface ISetupExtraStep
{
    /// <summary>
    /// Identifiant unique et stable de l'étape (transporté dans le champ caché <c>step</c>
    /// du formulaire). Ne doit pas valoir "1", "2" ou "3" (réservés aux étapes intégrées).
    /// </summary>
    string Id { get; }

    /// <summary>Libellé court affiché dans le stepper.</summary>
    string Label { get; }

    /// <summary>
    /// Corps HTML de l'étape : le <c>&lt;form&gt;</c> complet, avec ses propres champs et son
    /// bouton de soumission — la bibliothèque se charge uniquement de l'habillage (logo,
    /// stepper, message d'erreur). Le formulaire doit poster vers <c>/setup</c> avec un champ
    /// caché <c>step</c> valant <see cref="Id"/> et un champ caché <c>pendingState</c> valant
    /// <see cref="SetupExtraStepContext.PendingStateToken"/>.
    /// </summary>
    Task<string> RenderAsync(SetupExtraStepContext context, CancellationToken ct);

    /// <summary>
    /// Traite la soumission du formulaire de cette étape. En cas d'échec, retourne
    /// <see cref="SetupExtraStepResult.Failure"/> : l'étape est ré-affichée avec le message
    /// d'erreur et les valeurs postées.
    /// </summary>
    Task<SetupExtraStepResult> HandleAsync(SetupExtraStepContext context, IFormCollection form, CancellationToken ct);
}
