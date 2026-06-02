namespace AnthoDingo.Setup;

/// <summary>Compte administrateur à créer lors de l'installation.</summary>
/// <param name="UserName">Identifiant / email du compte.</param>
/// <param name="Password">Mot de passe en clair (haché par l'implémentation).</param>
/// <param name="DisplayName">Nom affiché optionnel.</param>
public sealed record AdminAccount(string UserName, string Password, string? DisplayName = null);

/// <summary>
/// Implémentée par l'application hôte. Encapsule les opérations qui dépendent
/// du DbContext et du modèle utilisateur propres à l'application :
/// l'initialisation de la base (migrations / seed) et la création du premier
/// compte administrateur.
///
/// La bibliothèque appelle ces méthodes avec la chaîne de connexion validée
/// par l'assistant — l'implémentation doit donc créer son DbContext à partir
/// de cette chaîne (et non via l'injection de dépendances), car la
/// configuration définitive n'est pas encore chargée à ce stade.
/// </summary>
public interface ISetupInitializer
{
    /// <summary>Applique les migrations (ou crée le schéma) et amorce les données de référence.</summary>
    Task InitializeDatabaseAsync(string connectionString, CancellationToken ct = default);

    /// <summary>Crée le premier compte administrateur.</summary>
    Task CreateAdminAsync(string connectionString, AdminAccount admin, CancellationToken ct = default);
}
