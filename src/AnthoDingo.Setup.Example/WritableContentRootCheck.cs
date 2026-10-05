using AnthoDingo.Setup;

namespace AnthoDingo.Setup.Example;

/// <summary>
/// Exemple de tâche de pré-installation : vérifie que le dossier de
/// l'application est accessible en écriture (l'assistant y écrit
/// appsettings.local.json à la fin de l'installation).
/// </summary>
public sealed class WritableContentRootCheck(IHostEnvironment env) : ISetupPreInstallTask
{
    public string Title => "Prerequis";

    public Task<SetupTaskResult> ExecuteAsync(CancellationToken ct = default)
    {
        string probe = Path.Combine(env.ContentRootPath, $".setup-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return Task.FromResult(SetupTaskResult.Ok(
                "<p class=\"text-success mb-0\"><i class=\"bi bi-check-circle-fill me-1\"></i>Dossier de l'application accessible en ecriture.</p>"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(SetupTaskResult.Fail($"Dossier de l'application non accessible en ecriture : {ex.Message}"));
        }
    }
}
