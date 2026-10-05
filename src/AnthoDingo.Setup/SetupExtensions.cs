using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AnthoDingo.Setup;

/// <summary>Méthodes d'extension pour brancher l'installation.</summary>
public static class SetupExtensions
{
    /// <summary>
    /// Enregistre <see cref="SetupService"/> (singleton) et l'implémentation
    /// <typeparamref name="TInitializer"/> de <see cref="ISetupInitializer"/> (scoped).
    /// </summary>
    public static IServiceCollection AddFileBasedSetup<TInitializer>(
        this IServiceCollection services, Action<SetupOptions>? configure = null)
        where TInitializer : class, ISetupInitializer
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<SetupOptions>();

        services.AddScoped<ISetupInitializer, TInitializer>();
        // Idempotent (TryAdd en interne) : ne duplique rien si l'application hôte
        // appelle déjà AddDataProtection() ailleurs. Nécessaire ici pour que
        // SetupService dispose d'un IDataProtectionProvider dès le premier
        // démarrage, avant même que l'hôte n'enregistre le sien (souvent gaté
        // derrière la fin de l'installation) — voir SetupService.ProtectPendingState.
        services.AddDataProtection();
        services.AddSingleton<SetupService>();
        return services;
    }

    /// <summary>
    /// Ajoute une étape supplémentaire <typeparamref name="TStep"/> au wizard, exécutée
    /// entre la création du compte administrateur et le redémarrage final. Les appels
    /// successifs s'enregistrent dans l'ordre : la première étape ajoutée est la première
    /// affichée. Enregistrée en <b>scoped</b> (comme <see cref="ISetupInitializer"/>) : elle
    /// peut donc injecter normalement un DbContext ou toute autre dépendance.
    /// </summary>
    public static IServiceCollection AddSetupStep<TStep>(this IServiceCollection services)
        where TStep : class, ISetupExtraStep
    {
        services.AddScoped<ISetupExtraStep, TStep>();
        return services;
    }

    /// <summary>
    /// Ajoute une étape préliminaire interactive (clé d'activation…) affichée par la
    /// page intégrée juste après la licence, avant les tâches de pré-installation et
    /// la connexion. Les étapes s'enchaînent dans l'ordre d'enregistrement.
    /// </summary>
    public static IServiceCollection AddSetupPreStep<TStep>(this IServiceCollection services)
        where TStep : class, ISetupPreStep
    {
        services.AddScoped<ISetupPreStep, TStep>();
        return services;
    }

    /// <summary>
    /// Ajoute une tâche de pré-installation (prérequis, avertissement…) affichée
    /// par la page intégrée avant la connexion à la base. Les tâches s'enchaînent
    /// dans l'ordre d'enregistrement.
    /// </summary>
    public static IServiceCollection AddSetupPreInstallTask<TTask>(this IServiceCollection services)
        where TTask : class, ISetupPreInstallTask
    {
        services.AddScoped<ISetupPreInstallTask, TTask>();
        return services;
    }

    /// <summary>
    /// Branche la garde d'installation <b>avec la page intégrée</b> servie par
    /// la bibliothèque. À appeler en tout premier dans le pipeline.
    ///
    /// Tant que l'installation n'est pas terminée, <c>/setup</c> affiche le
    /// formulaire (avec le nom <paramref name="appName"/>) et traite l'installation ;
    /// toute autre URL est redirigée vers <c>/setup</c>.
    /// </summary>
    public static IApplicationBuilder UseSetupMiddleware(this IApplicationBuilder app, string appName) =>
        app.UseMiddleware<SetupMiddleware>(appName, true);

    /// <summary>
    /// Branche uniquement la garde (redirection vers <c>/setup</c>) sans page
    /// intégrée : à utiliser si l'application fournit sa propre page d'installation.
    /// </summary>
    public static IApplicationBuilder UseSetupGate(this IApplicationBuilder app) =>
        app.UseMiddleware<SetupMiddleware>(string.Empty, false);
}
