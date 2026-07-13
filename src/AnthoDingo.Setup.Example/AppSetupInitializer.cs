using AnthoDingo.Setup;
using Microsoft.AspNetCore.Identity;

namespace AnthoDingo.Setup.Example;

/// <summary>
/// Implémentation de démonstration de <see cref="ISetupInitializer"/> :
/// crée le schéma puis le premier compte administrateur, quel que soit le
/// type de base choisi dans l'assistant (<see cref="DbProvider"/>).
/// </summary>
public sealed class AppSetupInitializer : ISetupInitializer
{
    private static readonly PasswordHasher<AppUser> Hasher = new();

    public async Task InitializeDatabaseAsync(DbProvider provider, string connectionString, CancellationToken ct = default)
    {
        await using AppDbContext db = AppDbContext.Create(provider, connectionString);
        // EnsureCreated() pour l'exemple (pas de migrations à générer par provider).
        // Dans une vraie application, préférez `await db.Database.MigrateAsync(ct);`
        // avec des migrations EF Core générées pour chaque provider pris en charge.
        await db.Database.EnsureCreatedAsync(ct);
    }

    public async Task CreateAdminAsync(DbProvider provider, string connectionString, AdminAccount admin, CancellationToken ct = default)
    {
        await using AppDbContext db = AppDbContext.Create(provider, connectionString);

        AppUser user = new AppUser
        {
            Email       = admin.UserName,
            DisplayName = admin.DisplayName,
            IsAdmin     = true
        };
        user.PasswordHash = Hasher.HashPassword(user, admin.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }
}
