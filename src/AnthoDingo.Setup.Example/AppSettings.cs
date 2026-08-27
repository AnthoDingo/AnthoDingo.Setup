namespace AnthoDingo.Setup.Example;

/// <summary>Réglages applicatifs de démonstration, renseignés par <see cref="CompanySetupStep"/>.</summary>
public sealed class AppSettings
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
}
