namespace AnthoDingo.Setup.Example;

/// <summary>Entité utilisateur minimale pour la démonstration.</summary>
public sealed class AppUser
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
