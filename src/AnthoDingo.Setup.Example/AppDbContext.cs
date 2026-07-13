using AnthoDingo.Setup;
using Microsoft.EntityFrameworkCore;

namespace AnthoDingo.Setup.Example;

/// <summary>DbContext minimal de démonstration — une seule table Users.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Email).IsRequired().HasMaxLength(256);
            e.HasIndex(u => u.Email).IsUnique();
        });
    }

    /// <summary>
    /// Construit le DbContext pour le provider choisi lors de l'installation
    /// (branche vers le bon <c>UseXxx()</c> EF Core). Utilisé aussi bien par
    /// <see cref="AppSetupInitializer"/> — avant que la configuration
    /// définitive ne soit chargée — que par le reste de l'application une
    /// fois l'installation terminée.
    /// </summary>
    public static AppDbContext Create(DbProvider provider, string connectionString)
    {
        DbContextOptionsBuilder<AppDbContext> builder = new DbContextOptionsBuilder<AppDbContext>();

        switch (provider)
        {
            case DbProvider.SqlServer:
                builder.UseSqlServer(connectionString);
                break;
            case DbProvider.MySql:
                builder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                break;
            case DbProvider.Postgres:
                builder.UseNpgsql(connectionString);
                break;
            case DbProvider.Sqlite:
                builder.UseSqlite(connectionString);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, "Type de base de données inconnu.");
        }

        return new AppDbContext(builder.Options);
    }
}
