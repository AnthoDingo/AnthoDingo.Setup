using System.Net;
using AnthoDingo.Setup;
using Microsoft.AspNetCore.Http;

namespace AnthoDingo.Setup.Example;

/// <summary>
/// Étape supplémentaire de démonstration, ajoutée après la création du compte
/// administrateur et avant le redémarrage final (voir
/// <see cref="SetupExtensions.AddSetupStep{TStep}"/>) : demande le nom de la société et
/// l'enregistre en base avant de laisser le wizard se terminer.
/// </summary>
public sealed class CompanySetupStep : ISetupExtraStep
{
    public string Id => "company";
    public string Label => "Societe";

    public Task<string> RenderAsync(SetupExtraStepContext context, CancellationToken ct)
    {
        string value = context.Values is not null && context.Values.TryGetValue("companyName", out string? v) ? v : string.Empty;

        string html = $"""
          <form method="post" action="/setup">
            <input type="hidden" name="step" value="{Id}" />
            <input type="hidden" name="pendingState" value="{Enc(context.PendingStateToken)}" />
            <h2 class="text-uppercase text-secondary fw-semibold mb-3" style="font-size:.75rem;letter-spacing:.05em">
              <i class="bi bi-building me-1"></i>Informations sur la societe
            </h2>
            <div class="mb-3">
              <label class="form-label">Nom de la societe</label>
              <input type="text" class="form-control" name="companyName" value="{Enc(value)}" required />
            </div>
            <button type="submit" class="btn btn-primary w-100 py-2">
              <i class="bi bi-arrow-right me-1"></i>Continuer
            </button>
          </form>
          """;
        return Task.FromResult(html);
    }

    public async Task<SetupExtraStepResult> HandleAsync(SetupExtraStepContext context, IFormCollection form, CancellationToken ct)
    {
        string companyName = form["companyName"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(companyName))
            return SetupExtraStepResult.Failure("Le nom de la societe est obligatoire.");

        await using AppDbContext db = AppDbContext.Create(context.Provider, context.ConnectionString);
        db.Settings.Add(new AppSettings { CompanyName = companyName });
        await db.SaveChangesAsync(ct);

        return SetupExtraStepResult.Success();
    }

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
