namespace Synergos.CMS.Configuration;

/// <summary>
/// Default brand theme values written by <c>ContentSeeder</c> into ThemeSettings
/// on a fresh install. Empty values are skipped — they never overwrite editor input.
///
/// Override per environment via appsettings.json section "Synergos:Seed:Theme".
/// Editors can freely change any value in the backoffice afterwards.
/// </summary>
public sealed class SeedTheme
{
    // ── Colors (hex) ──────────────────────────────────────────────────────────
    public string ColorPrimary      { get; init; } = "#1a56db";
    public string ColorSecondary    { get; init; } = "#0e4fb5";
    public string ColorAccent       { get; init; } = "#0694a2";
    public string ColorBackground   { get; init; } = "#ffffff";
    public string ColorSurface      { get; init; } = "#f9fafb";
    public string ColorText         { get; init; } = "#111827";
    public string ColorTextInverse  { get; init; } = "#ffffff";
    public string ColorBorder       { get; init; } = "#e5e7eb";
    public string ColorSuccess      { get; init; } = "#0e9f6e";
    public string ColorWarning      { get; init; } = "#ff5a1f";
    public string ColorError        { get; init; } = "#f05252";

    // ── Typography ────────────────────────────────────────────────────────────
    public string FontFamilyHeading { get; init; } = "Manrope, sans-serif";
    public string FontFamilyBody    { get; init; } = "Manrope, sans-serif";
    public string FontBaseSize      { get; init; } = "16px";

    // ── Spacing & Layout ──────────────────────────────────────────────────────
    public string ContainerMaxWidth { get; init; } = "1280px";
    public string BorderRadius      { get; init; } = "8px";
    public string SectionPaddingY   { get; init; } = "5rem";

    // ── Component variants (DropDown.Flexible aliases) ────────────────────────
    public string ButtonStyle       { get; init; } = "rounded";
    public string CardStyle         { get; init; } = "elevated";
    public string HeaderStyle       { get; init; } = "sticky";
}
