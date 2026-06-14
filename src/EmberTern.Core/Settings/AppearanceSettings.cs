namespace EmberTern.Core.Settings;

// Visual preferences. Foundation stub for a future appearance milestone — persisted
// and round-tripped, but no consumer reads it yet (the theme toggle still lives in the
// View's code-behind). Kept Avalonia-free: ThemeVariant is a plain string
// ("Dark" / "Light" / null = follow system), AccentColor a "#RRGGBB" string or null.
public sealed class AppearanceSettings
{
    public string? ThemeVariant { get; set; }
    public string? AccentColor { get; set; }
}
