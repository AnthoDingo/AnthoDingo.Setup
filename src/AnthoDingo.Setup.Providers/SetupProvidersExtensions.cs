using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace AnthoDingo.Setup;

/// <summary>Enregistrement des pilotes fournis par le package <c>AnthoDingo.Setup.Providers</c>.</summary>
public static class SetupProvidersExtensions
{
    /// <summary>
    /// Enregistre dans <see cref="SetupOptions.Providers"/> les pilotes des quatre types
    /// de base pris en charge (SQL Server, MySQL/MariaDB, PostgreSQL, SQLite), ou
    /// seulement ceux de <paramref name="only"/> s'il est renseigné — p. ex.
    /// <c>o.AddDefaultProviders(DbProvider.Postgres, DbProvider.Sqlite)</c>.
    /// </summary>
    /// <param name="options">Options de l'assistant.</param>
    /// <param name="only">Types de base à proposer ; vide = les quatre.</param>
    /// <returns><paramref name="options"/>, pour chaîner.</returns>
    public static SetupOptions AddDefaultProviders(this SetupOptions options, params DbProvider[] only)
    {
        (DbProvider Provider, DbProviderFactory Factory)[] all =
        [
            (DbProvider.SqlServer, SqlClientFactory.Instance),
            (DbProvider.MySql,     MySqlConnectorFactory.Instance),
            (DbProvider.Postgres,  NpgsqlFactory.Instance),
            (DbProvider.Sqlite,    SqliteFactory.Instance)
        ];

        foreach ((DbProvider provider, DbProviderFactory factory) in all)
        {
            if (only.Length == 0 || only.Contains(provider))
                options.Providers[provider] = factory;
        }
        return options;
    }
}
