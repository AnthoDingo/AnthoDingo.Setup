using System.Net;
using AnthoDingo.Setup;

namespace AnthoDingo.Setup.Example;

/// <summary>
/// Exemple d'étape préliminaire : clé d'activation à saisir avant de pouvoir
/// continuer. Clé de démonstration : <c>DEMO-1234</c> (une vraie application
/// la vérifierait auprès de son serveur de licences et la persisterait).
/// </summary>
public sealed class ActivationKeyStep : ISetupPreStep
{
    public string Label => "Activation";

    public Task<string> RenderAsync(SetupPreStepContext ctx, CancellationToken ct)
    {
        string key = ctx.Values is not null && ctx.Values.TryGetValue("activationKey", out string? v) ? v : string.Empty;
        return Task.FromResult($"""
            <label class="form-label" for="activationKey">Cle d'activation</label>
            <input type="text" class="form-control" id="activationKey" name="activationKey"
                   value="{WebUtility.HtmlEncode(key)}" placeholder="XXXX-XXXX" required autofocus />
            <div class="form-text">Demo : DEMO-1234</div>
            """);
    }

    public Task<SetupExtraStepResult> HandleAsync(SetupPreStepContext ctx, IFormCollection form, CancellationToken ct) =>
        Task.FromResult(form["activationKey"].ToString().Trim() == "DEMO-1234"
            ? SetupExtraStepResult.Success()
            : SetupExtraStepResult.Failure("Cle d'activation invalide."));
}
