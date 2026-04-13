using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Application.Theming;

/// <summary>
/// Static registry of built-in multi-identity brand token presets.
///
/// Each preset supplies values for the brand-specific CSS custom properties
/// (--sg-brand-*, --sg-color-action-*, --sg-color-surface-elevated, etc.).
/// Individual ThemeSettings CMS fields override any preset value — the preset
/// acts as a fully-opinionated baseline.
///
/// Adding a new identity:
///   1. Define a new static readonly BrandPreset field below.
///   2. Add its alias to the SelectIdentityPreset dropdown in PlatformInitializer.
///   3. Add a case to the Get() switch.
///   4. Bump SchemaVersion.Value.
/// </summary>
internal static class IdentityRegistry
{
    /// <summary>
    /// Returns the preset for the given alias, or null when the alias is
    /// "custom", null, or unrecognised — in which case only CMS fields are used.
    /// </summary>
    public static BrandPreset? Get(string? preset) => preset switch
    {
        "aurora"   => Aurora,
        "graphite" => Graphite,
        "solaris"  => Solaris,
        _          => null
    };

    // ─── Aurora — Etéreo · IA · Luminoso ─────────────────────────────────────
    // Paleta: Indigo #6366f1 + Violet #a855f7 + Cyan #06b6d4
    public static readonly BrandPreset Aurora = new(
        BrandGradientHero:         "linear-gradient(135deg, #6366f1 0%, #a855f7 50%, #06b6d4 100%)",
        BrandGlowColor:            "rgba(99, 102, 241, 0.4)",
        BrandSurfaceGlassBg:       "rgba(255,255,255,0.06)",
        BrandSurfaceBackdropBlur:  "12px",
        BrandParticleEnabled:      "true",
        BrandLogoFilter:           "brightness(1) drop-shadow(0 0 8px rgba(99,102,241,0.5))",
        BrandHeroOverlay:          "linear-gradient(180deg, rgba(15,23,42,0.3) 0%, rgba(15,23,42,0.8) 100%)",
        BrandHeadingWeight:        "700",
        BrandHeadingLetterSpacing: "-0.02em",
        BrandDividerGradient:      "linear-gradient(90deg, transparent, #6366f1, transparent)",
        BrandCtaGlow:              "0 0 20px rgba(99,102,241,0.4)",
        ColorSurfaceElevated:      "rgba(99,102,241,0.08)",
        ColorTextSecondary:        "#94a3b8",
        ColorBorderSubtle:         "rgba(99,102,241,0.15)",
        ColorActionPrimary:        "#6366f1",
        ColorActionPrimaryHover:   "#4f46e5",
        ColorActionPrimaryText:    "#ffffff",
        ColorInfo:                 "#06b6d4");

    // ─── Graphite — Limpio · Corporativo · Premium ───────────────────────────
    // Paleta: Slate #1e293b + #334155 + Sky #0ea5e9
    public static readonly BrandPreset Graphite = new(
        BrandGradientHero:         "linear-gradient(135deg, #1e293b 0%, #334155 100%)",
        BrandGlowColor:            "rgba(30,41,59,0.3)",
        BrandSurfaceGlassBg:       "rgba(30,41,59,0.04)",
        BrandSurfaceBackdropBlur:  "0px",
        BrandParticleEnabled:      "false",
        BrandLogoFilter:           "none",
        BrandHeroOverlay:          "linear-gradient(180deg, rgba(15,23,42,0.2) 0%, rgba(15,23,42,0.7) 100%)",
        BrandHeadingWeight:        "600",
        BrandHeadingLetterSpacing: "-0.01em",
        BrandDividerGradient:      "linear-gradient(90deg, transparent, #475569, transparent)",
        BrandCtaGlow:              "none",
        ColorSurfaceElevated:      "#f8fafc",
        ColorTextSecondary:        "#64748b",
        ColorBorderSubtle:         "#e2e8f0",
        ColorActionPrimary:        "#1e293b",
        ColorActionPrimaryHover:   "#0f172a",
        ColorActionPrimaryText:    "#ffffff",
        ColorInfo:                 "#0ea5e9");

    // ─── Solaris — Cálido · Energético · Startup ─────────────────────────────
    // Paleta: Amber #f59e0b + Red #ef4444 + Cyan #06b6d4
    public static readonly BrandPreset Solaris = new(
        BrandGradientHero:         "linear-gradient(135deg, #f59e0b 0%, #ef4444 100%)",
        BrandGlowColor:            "rgba(245,158,11,0.4)",
        BrandSurfaceGlassBg:       "rgba(245,158,11,0.04)",
        BrandSurfaceBackdropBlur:  "8px",
        BrandParticleEnabled:      "false",
        BrandLogoFilter:           "none",
        BrandHeroOverlay:          "linear-gradient(180deg, rgba(120,53,15,0.2) 0%, rgba(120,53,15,0.7) 100%)",
        BrandHeadingWeight:        "800",
        BrandHeadingLetterSpacing: "-0.03em",
        BrandDividerGradient:      "linear-gradient(90deg, transparent, #f59e0b, transparent)",
        BrandCtaGlow:              "0 0 16px rgba(245,158,11,0.35)",
        ColorSurfaceElevated:      "rgba(245,158,11,0.06)",
        ColorTextSecondary:        "#78716c",
        ColorBorderSubtle:         "rgba(245,158,11,0.2)",
        ColorActionPrimary:        "#f59e0b",
        ColorActionPrimaryHover:   "#d97706",
        ColorActionPrimaryText:    "#1c1917",
        ColorInfo:                 "#06b6d4");
}

/// <summary>
/// Immutable value object holding brand-specific CSS custom property values for one identity.
/// All fields map 1-to-1 to the brand token fields in <see cref="ThemeConfig"/>.
/// </summary>
internal sealed record BrandPreset(
    string BrandGradientHero,
    string BrandGlowColor,
    string BrandSurfaceGlassBg,
    string BrandSurfaceBackdropBlur,
    string BrandParticleEnabled,
    string BrandLogoFilter,
    string BrandHeroOverlay,
    string BrandHeadingWeight,
    string BrandHeadingLetterSpacing,
    string BrandDividerGradient,
    string BrandCtaGlow,
    string ColorSurfaceElevated,
    string ColorTextSecondary,
    string ColorBorderSubtle,
    string ColorActionPrimary,
    string ColorActionPrimaryHover,
    string ColorActionPrimaryText,
    string ColorInfo);
