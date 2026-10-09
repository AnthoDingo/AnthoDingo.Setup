---
_layout: landing
---

# AnthoDingo.Setup

Middleware d'installation « premier démarrage » pour ASP.NET Core : page `/setup`
intégrée (licence et prérequis optionnels, base de données, compte administrateur),
détection file-based, assets embarqués hors-ligne. SQL Server, MySQL/MariaDB,
PostgreSQL et SQLite.

| Package | Contenu |
|---------|---------|
| `AnthoDingo.Setup` | Middleware et assistant `/setup`, sans pilote de base de données : l'application fournit le sien (`SetupOptions.Providers`). |
| `AnthoDingo.Setup.Providers` | Les 4 pilotes et `o.AddDefaultProviders()`. |

- [Guide d'utilisation](../README.md) · [English guide](../README.en.md) · [Deutsche Anleitung](../README.de.md)
- [Référence de l'API](api/AnthoDingo.Setup.yml)
- [Code source sur GitHub](https://github.com/AnthoDingo/AnthoDingo.Setup)
