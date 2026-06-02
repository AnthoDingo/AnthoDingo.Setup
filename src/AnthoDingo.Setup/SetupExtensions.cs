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
        services.AddSingleton<SetupService>();
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
