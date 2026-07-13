namespace AnthoDingo.Setup;

/// <summary>Type de base de données pris en charge par l'assistant d'installation.</summary>
public enum DbProvider
{
    /// <summary>Microsoft SQL Server (pilote Microsoft.Data.SqlClient).</summary>
    SqlServer,

    /// <summary>MySQL / MariaDB (pilote MySqlConnector).</summary>
    MySql,

    /// <summary>PostgreSQL (pilote Npgsql).</summary>
    Postgres,

    /// <summary>SQLite — fichier local (pilote Microsoft.Data.Sqlite).</summary>
    Sqlite
}
