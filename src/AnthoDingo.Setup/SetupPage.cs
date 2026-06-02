using System.Net;
using System.Text;

namespace AnthoDingo.Setup;

/// <summary>
/// Rendu HTML de la page d'installation intégrée. Les feuilles de style
/// (Bootstrap + Bootstrap Icons) sont embarquées dans la bibliothèque et servies
/// par le middleware sous <c>/setup/_assets/</c> : aucune dépendance en ligne
/// (CDN) ni au <c>wwwroot</c> de l'application hôte. Le JavaScript est inline.
/// </summary>
internal static class SetupPage
{
    private const string Shell = """
<!DOCTYPE html>
<html lang="fr">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Installation — {{APP_NAME}}</title>
  <link rel="stylesheet" href="/setup/_assets/bootstrap.min.css" />
  <link rel="stylesheet" href="/setup/_assets/bootstrap-icons.min.css" />
  <style>
    body { min-height:100vh; display:flex; align-items:center; justify-content:center;
           background:linear-gradient(135deg,#1e1b4b 0%,#0f172a 100%); padding:24px; }
    .setup-card { width:100%; max-width:560px; border:0; border-radius:16px;
                  box-shadow:0 20px 60px rgba(0,0,0,.35); }
    .setup-logo { width:56px; height:56px; border-radius:14px; margin:0 auto 12px;
                  background:linear-gradient(135deg,#4f46e5,#4338ca); display:flex;
                  align-items:center; justify-content:center; color:#fff; font-size:26px; }
    .spinner-lg { width:3rem; height:3rem; }
  </style>
</head>
<body>
  <div class="card setup-card">
    {{BODY}}
  </div>
  <script>
    (function () {
      var win = document.getElementById('windowsAuth');
      var creds = document.getElementById('sqlCreds');
      if (win && creds) {
        var toggle = function () { creds.style.display = win.checked ? 'none' : 'block'; };
        win.addEventListener('change', toggle); toggle();
      }
    })();
  </script>
</body>
</html>
""";

    public static string RenderForm(string appName, string? error, IDictionary<string, string>? values)
    {
        string V(string key) => values is not null && values.TryGetValue(key, out string? v) ? Enc(v) : string.Empty;
        bool isPost = values is not null && values.ContainsKey("submitted");
        bool win   = !isPost || V("windowsAuth") == "on";
        bool trust = !isPost || V("trustCert") == "on";

        string server   = V("server")   is { Length: > 0 } sv ? sv : "localhost";
        string database = V("database") is { Length: > 0 } db ? db : Enc(appName);

        StringBuilder b = new StringBuilder();
        b.Append($"""
          <div class="card-body p-4 p-md-5">
            <div class="text-center mb-4">
              <div class="setup-logo"><i class="bi bi-gear-fill"></i></div>
              <h1 class="h4 mb-1">{Enc(appName)}</h1>
              <div class="text-secondary small">Assistant d'installation</div>
            </div>
            <form method="post" action="/setup">
              <input type="hidden" name="submitted" value="1" />
        """);

        if (!string.IsNullOrEmpty(error))
            b.Append($"""
              <div class="alert alert-danger d-flex align-items-center py-2">
                <i class="bi bi-exclamation-triangle-fill me-2"></i>
                <div>{Enc(error)}</div>
              </div>
            """);

        b.Append($"""
              <h2 class="text-uppercase text-secondary fw-semibold mb-3" style="font-size:.75rem;letter-spacing:.05em">
                <i class="bi bi-database me-1"></i>Base de donnees SQL Server
              </h2>
              <div class="mb-3">
                <label class="form-label">Serveur</label>
                <input type="text" class="form-control" name="server" value="{server}" placeholder="localhost\SQLEXPRESS" />
              </div>
              <div class="mb-3">
                <label class="form-label">Nom de la base</label>
                <input type="text" class="form-control" name="database" value="{database}" />
              </div>
              <div class="form-check mb-2">
                <input type="checkbox" class="form-check-input" id="windowsAuth" name="windowsAuth" {(win ? "checked" : "")} />
                <label class="form-check-label" for="windowsAuth">Authentification Windows</label>
              </div>
              <div id="sqlCreds" class="row g-2 mb-2">
                <div class="col">
                  <label class="form-label">Utilisateur SQL</label>
                  <input type="text" class="form-control" name="sqlUser" value="{V("sqlUser")}" />
                </div>
                <div class="col">
                  <label class="form-label">Mot de passe SQL</label>
                  <input type="password" class="form-control" name="sqlPassword" />
                </div>
              </div>
              <div class="form-check mb-4">
                <input type="checkbox" class="form-check-input" id="trustCert" name="trustCert" {(trust ? "checked" : "")} />
                <label class="form-check-label" for="trustCert">TrustServerCertificate (certificat auto-signe)</label>
              </div>

              <h2 class="text-uppercase text-secondary fw-semibold mb-3" style="font-size:.75rem;letter-spacing:.05em">
                <i class="bi bi-shield-lock me-1"></i>Compte administrateur
              </h2>
              <div class="mb-3">
                <label class="form-label">Email</label>
                <input type="email" class="form-control" name="adminEmail" value="{V("adminEmail")}" placeholder="admin@exemple.com" required />
              </div>
              <div class="mb-3">
                <label class="form-label">Nom affiche <span class="text-secondary">(optionnel)</span></label>
                <input type="text" class="form-control" name="adminDisplayName" value="{V("adminDisplayName")}" />
              </div>
              <div class="row g-2 mb-1">
                <div class="col">
                  <label class="form-label">Mot de passe</label>
                  <input type="password" class="form-control" name="adminPassword" required />
                </div>
                <div class="col">
                  <label class="form-label">Confirmer</label>
                  <input type="password" class="form-control" name="adminConfirm" required />
                </div>
              </div>
              <div class="form-text mb-3">8 caracteres minimum.</div>

              <button type="submit" class="btn btn-primary w-100 py-2">
                <i class="bi bi-download me-1"></i>Installer {Enc(appName)}
              </button>
            </form>
          </div>
        """);

        return Shell.Replace("{{APP_NAME}}", Enc(appName)).Replace("{{BODY}}", b.ToString());
    }

    public static string RenderSuccess(string appName)
    {
        string body = $"""
          <div class="card-body text-center p-5">
            <div class="spinner-border spinner-lg text-primary mb-3" role="status"></div>
            <h1 class="h4 text-success"><i class="bi bi-check-circle-fill me-1"></i>{Enc(appName)} installe</h1>
            <p class="text-secondary mb-0">L'application redemarre pour charger la configuration.<br/>
               Cette page se rechargera automatiquement...</p>
          </div>
        """;
        return Shell
            .Replace("{{APP_NAME}}", Enc(appName))
            .Replace("{{BODY}}", body)
            .Replace("</head>", """<meta http-equiv="refresh" content="6; url=/" /></head>""");
    }

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
