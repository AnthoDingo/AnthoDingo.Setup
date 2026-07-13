using System.Net;
using System.Text;

namespace AnthoDingo.Setup;

/// <summary>
/// Rendu HTML de l'assistant d'installation multi-étapes
/// (Connexion → Initialisation → Compte admin → Terminé). Bootstrap et
/// Bootstrap Icons sont embarqués dans la bibliothèque et servis hors-ligne
/// sous <c>/setup/_assets/</c>. Le JavaScript est inline.
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
    .stepper { display:flex; gap:4px; margin:0 0 1.75rem; }
    .stepper .step { flex:1; text-align:center; }
    .stepper .dot { width:34px; height:34px; border-radius:50%; margin:0 auto 6px; display:flex;
                    align-items:center; justify-content:center; font-weight:600; font-size:14px;
                    background:#e2e8f0; color:#64748b; transition:all .2s; }
    .stepper .step.active .dot { background:#4f46e5; color:#fff; }
    .stepper .step.done .dot { background:#16a34a; color:#fff; }
    .stepper .lbl { font-size:11px; color:#94a3b8; }
    .stepper .step.active .lbl { color:#4f46e5; font-weight:600; }
    .stepper .step.done .lbl { color:#16a34a; }
    .spinner-lg { width:3rem; height:3rem; }
  </style>
</head>
<body>
  <div class="card setup-card">
    <div class="card-body p-4 p-md-5">
      {{BODY}}
    </div>
  </div>
</body>
</html>
""";

    // ── Étape 1 : connexion base de données ────────────────────────────────────

    public static string RenderStep1(
        string appName, string? error, IDictionary<string, string>? values, IReadOnlyList<DbProvider> allowedProviders)
    {
        if (allowedProviders.Count == 0) allowedProviders = [DbProvider.SqlServer];

        string V(string key) => values is not null && values.TryGetValue(key, out string? v) ? Enc(v) : string.Empty;
        bool isPost = values is not null;

        string selected = V("dbProvider") is { Length: > 0 } sp && Enum.TryParse(sp, ignoreCase: true, out DbProvider parsed) && allowedProviders.Contains(parsed)
            ? parsed.ToString()
            : allowedProviders[0].ToString();

        // SQL Server
        bool   ssWin   = !isPost || V("ss_windowsAuth") == "on";
        bool   ssTrust = !isPost || V("ss_trustCert") == "on";
        string ssServer   = V("ss_server")   is { Length: > 0 } ssv ? ssv : "localhost";
        string ssDatabase = V("ss_database") is { Length: > 0 } ssd ? ssd : Enc(appName);

        // MySQL / MariaDB
        bool   myTrust = !isPost || V("my_trustCert") == "on";
        string myServer   = V("my_server")   is { Length: > 0 } msv ? msv : "localhost";
        string myPort     = V("my_port")     is { Length: > 0 } mpv ? mpv : "3306";
        string myDatabase = V("my_database") is { Length: > 0 } mdv ? mdv : Enc(appName);

        // PostgreSQL
        bool   pgTrust = !isPost || V("pg_trustCert") == "on";
        string pgServer   = V("pg_server")   is { Length: > 0 } psv ? psv : "localhost";
        string pgPort     = V("pg_port")     is { Length: > 0 } ppv ? ppv : "5432";
        string pgDatabase = V("pg_database") is { Length: > 0 } pdv ? pdv : Enc(appName);

        // SQLite
        string sqDefaultFile = "App_Data/" + Enc(appName).ToLowerInvariant().Replace(' ', '-') + ".db";
        string sqFile = V("sq_file") is { Length: > 0 } sqv ? sqv : sqDefaultFile;

        StringBuilder b = new StringBuilder();
        b.Append(StepHeader(appName, 1));
        b.Append(ErrorBlock(error));
        b.Append($"""
          <form method="post" action="/setup">
            <input type="hidden" name="step" value="1" />
            <div class="mb-3">
              <label class="form-label text-uppercase text-secondary fw-semibold" style="font-size:.75rem;letter-spacing:.05em">
                <i class="bi bi-database me-1"></i>Type de base de donnees
              </label>
              <select class="form-select" id="dbProvider" name="dbProvider">
                {ProviderOptions(allowedProviders, selected)}
              </select>
            </div>
        """);

        if (allowedProviders.Contains(DbProvider.SqlServer))
        {
            b.Append($"""
              <div id="panel-SqlServer" class="db-panel">
                <div class="mb-3">
                  <label class="form-label">Serveur</label>
                  <input type="text" class="form-control" name="ss_server" value="{ssServer}" placeholder="localhost\SQLEXPRESS" />
                </div>
                <div class="mb-3">
                  <label class="form-label">Nom de la base</label>
                  <input type="text" class="form-control" name="ss_database" value="{ssDatabase}" />
                </div>
                <div class="form-check mb-2">
                  <input type="checkbox" class="form-check-input" id="ss_windowsAuth" name="ss_windowsAuth" {(ssWin ? "checked" : "")} />
                  <label class="form-check-label" for="ss_windowsAuth">Authentification Windows</label>
                </div>
                <div id="sqlCreds" class="row g-2 mb-2">
                  <div class="col">
                    <label class="form-label">Utilisateur SQL</label>
                    <input type="text" class="form-control" name="ss_user" value="{V("ss_user")}" />
                  </div>
                  <div class="col">
                    <label class="form-label">Mot de passe SQL</label>
                    <input type="password" class="form-control" name="ss_password" />
                  </div>
                </div>
                <div class="form-check mb-2">
                  <input type="checkbox" class="form-check-input" id="ss_trustCert" name="ss_trustCert" {(ssTrust ? "checked" : "")} />
                  <label class="form-check-label" for="ss_trustCert">TrustServerCertificate (certificat auto-signe)</label>
                </div>
              </div>
            """);
        }

        if (allowedProviders.Contains(DbProvider.MySql))
        {
            b.Append($"""
              <div id="panel-MySql" class="db-panel">
                <div class="row g-2 mb-3">
                  <div class="col-8">
                    <label class="form-label">Serveur</label>
                    <input type="text" class="form-control" name="my_server" value="{myServer}" placeholder="localhost" />
                  </div>
                  <div class="col-4">
                    <label class="form-label">Port</label>
                    <input type="number" class="form-control" name="my_port" value="{myPort}" />
                  </div>
                </div>
                <div class="mb-3">
                  <label class="form-label">Nom de la base</label>
                  <input type="text" class="form-control" name="my_database" value="{myDatabase}" />
                </div>
                <div class="row g-2 mb-2">
                  <div class="col">
                    <label class="form-label">Utilisateur</label>
                    <input type="text" class="form-control" name="my_user" value="{V("my_user")}" />
                  </div>
                  <div class="col">
                    <label class="form-label">Mot de passe</label>
                    <input type="password" class="form-control" name="my_password" />
                  </div>
                </div>
                <div class="form-check mb-2">
                  <input type="checkbox" class="form-check-input" id="my_trustCert" name="my_trustCert" {(myTrust ? "checked" : "")} />
                  <label class="form-check-label" for="my_trustCert">Ignorer la verification du certificat SSL</label>
                </div>
              </div>
            """);
        }

        if (allowedProviders.Contains(DbProvider.Postgres))
        {
            b.Append($"""
              <div id="panel-Postgres" class="db-panel">
                <div class="row g-2 mb-3">
                  <div class="col-8">
                    <label class="form-label">Serveur</label>
                    <input type="text" class="form-control" name="pg_server" value="{pgServer}" placeholder="localhost" />
                  </div>
                  <div class="col-4">
                    <label class="form-label">Port</label>
                    <input type="number" class="form-control" name="pg_port" value="{pgPort}" />
                  </div>
                </div>
                <div class="mb-3">
                  <label class="form-label">Nom de la base</label>
                  <input type="text" class="form-control" name="pg_database" value="{pgDatabase}" />
                </div>
                <div class="row g-2 mb-2">
                  <div class="col">
                    <label class="form-label">Utilisateur</label>
                    <input type="text" class="form-control" name="pg_user" value="{V("pg_user")}" />
                  </div>
                  <div class="col">
                    <label class="form-label">Mot de passe</label>
                    <input type="password" class="form-control" name="pg_password" />
                  </div>
                </div>
                <div class="form-check mb-2">
                  <input type="checkbox" class="form-check-input" id="pg_trustCert" name="pg_trustCert" {(pgTrust ? "checked" : "")} />
                  <label class="form-check-label" for="pg_trustCert">Ignorer la verification du certificat SSL</label>
                </div>
              </div>
            """);
        }

        if (allowedProviders.Contains(DbProvider.Sqlite))
        {
            b.Append($"""
              <div id="panel-Sqlite" class="db-panel">
                <div class="mb-3">
                  <label class="form-label">Fichier de base de donnees</label>
                  <input type="text" class="form-control" name="sq_file" value="{sqFile}" placeholder="App_Data/app.db" />
                  <div class="form-text">Chemin relatif au dossier de l'application, ou chemin absolu. Le fichier et son dossier sont crees automatiquement.</div>
                </div>
              </div>
            """);
        }

        b.Append("""
            <button type="submit" class="btn btn-primary w-100 py-2 mt-2">
              Tester la connexion <i class="bi bi-arrow-right ms-1"></i>
            </button>
          </form>
        """);
        // Bloc separe, non interpole (pas de "$" devant """) : les accolades JS
        // ci-dessous restent du texte litteral au lieu d'etre lues comme des
        // trous d'interpolation C# (ce qui provoquait "Unexpected token 'win'"
        // et des erreurs "Too many characters in character literal" sur les
        // chaines JS entre quotes simples).
        b.Append("""
          <script>
            (function () {
              var sel = document.getElementById('dbProvider'), panels = document.querySelectorAll('.db-panel');
              function syncPanels() {
                for (var i = 0; i < panels.length; i++) {
                  panels[i].style.display = (panels[i].id === 'panel-' + sel.value) ? '' : 'none';
                }
              }
              if (sel) { sel.addEventListener('change', syncPanels); syncPanels(); }

              var win = document.getElementById('ss_windowsAuth'), creds = document.getElementById('sqlCreds');
              if (win && creds) { var t = function(){ creds.style.display = win.checked ? 'none':'block'; };
                win.addEventListener('change', t); t(); }
            })();
          </script>
        """);
        return Wrap(appName, b.ToString());
    }

    private static string ProviderOptions(IReadOnlyList<DbProvider> allowed, string selected)
    {
        (DbProvider Provider, string Label)[] all =
        [
            (DbProvider.SqlServer, "SQL Server"),
            (DbProvider.MySql,     "MySQL / MariaDB"),
            (DbProvider.Postgres,  "PostgreSQL"),
            (DbProvider.Sqlite,    "SQLite (fichier local)")
        ];

        StringBuilder s = new StringBuilder();
        foreach ((DbProvider provider, string label) in all)
        {
            if (!allowed.Contains(provider)) continue;
            string value = provider.ToString();
            string sel   = value == selected ? " selected" : "";
            s.Append($"""<option value="{value}"{sel}>{label}</option>""");
        }
        return s.ToString();
    }

    // ── Étape 2 : initialisation de la base ───────────────────────────────────

    public static string RenderStep2(string appName, string? error, DbProvider provider)
    {
        StringBuilder b = new StringBuilder();
        b.Append(StepHeader(appName, 2));
        b.Append(ErrorBlock(error));
        b.Append($"""
          <div class="alert alert-success d-flex align-items-center py-2">
            <i class="bi bi-check-circle-fill me-2"></i><div>Connexion {Enc(ProviderLabel(provider))} reussie.</div>
          </div>
          <p class="text-secondary">Les migrations et les données de référence vont être appliquées à la base.</p>
          <form method="post" action="/setup">
            <input type="hidden" name="step" value="2" />
            <button type="submit" class="btn btn-primary w-100 py-2">
              <i class="bi bi-gear-wide-connected me-1"></i>Initialiser la base de donnees
            </button>
          </form>
        """);
        return Wrap(appName, b.ToString());
    }

    private static string ProviderLabel(DbProvider provider) => provider switch
    {
        DbProvider.SqlServer => "SQL Server",
        DbProvider.MySql     => "MySQL/MariaDB",
        DbProvider.Postgres  => "PostgreSQL",
        DbProvider.Sqlite    => "SQLite",
        _ => provider.ToString()
    };

    // ── Étape 3 : compte administrateur ───────────────────────────────────────

    public static string RenderStep3(string appName, string? error, IDictionary<string, string>? values)
    {
        string V(string key) => values is not null && values.TryGetValue(key, out string? v) ? Enc(v) : string.Empty;
        StringBuilder b = new StringBuilder();
        b.Append(StepHeader(appName, 3));
        b.Append(ErrorBlock(error));
        b.Append($"""
          <form method="post" action="/setup">
            <input type="hidden" name="step" value="3" />
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
              <i class="bi bi-check2-circle me-1"></i>Creer le compte et terminer
            </button>
          </form>
        """);
        return Wrap(appName, b.ToString());
    }

    // ── Étape 4 : terminé ─────────────────────────────────────────────────────

    public static string RenderSuccess(string appName)
    {
        StringBuilder b = new StringBuilder();
        b.Append(StepHeader(appName, 4));
        b.Append($"""
          <div class="text-center py-3">
            <div class="spinner-border spinner-lg text-primary mb-3" role="status"></div>
            <h1 class="h5 text-success"><i class="bi bi-check-circle-fill me-1"></i>{Enc(appName)} installe</h1>
            <p class="text-secondary mb-0">L'application redemarre pour charger la configuration.<br/>
               Cette page se rechargera automatiquement...</p>
          </div>
        """);
        return Wrap(appName, b.ToString())
            .Replace("</head>", """<meta http-equiv="refresh" content="6; url=/" /></head>""");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string StepHeader(string appName, int current)
    {
        string[] labels = ["Connexion", "Base", "Admin", "Termine"];
        StringBuilder s = new StringBuilder();
        s.Append($"""
          <div class="text-center mb-4">
            <div class="setup-logo"><i class="bi bi-gear-fill"></i></div>
            <h1 class="h4 mb-1">{Enc(appName)}</h1>
            <div class="text-secondary small">Assistant d'installation</div>
          </div>
          <div class="stepper">
        """);
        for (int i = 1; i <= 4; i++)
        {
            string cls = i == current ? "step active" : i < current ? "step done" : "step";
            string dot = i < current ? "<i class=\"bi bi-check-lg\"></i>" : i.ToString();
            s.Append($"""<div class="{cls}"><div class="dot">{dot}</div><div class="lbl">{labels[i - 1]}</div></div>""");
        }
        s.Append("</div>");
        return s.ToString();
    }

    private static string ErrorBlock(string? error) =>
        string.IsNullOrEmpty(error)
            ? string.Empty
            : $"""
              <div class="alert alert-danger d-flex align-items-center py-2">
                <i class="bi bi-exclamation-triangle-fill me-2"></i><div>{Enc(error)}</div>
              </div>
              """;

    private static string Wrap(string appName, string body) =>
        Shell.Replace("{{APP_NAME}}", Enc(appName)).Replace("{{BODY}}", body);

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
