namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// Espaciado declarativo mediante tokens — nunca valores CSS hardcodeados.
/// Permite que el design system controle el ritmo visual de forma centralizada.
/// </summary>
public sealed record DomSpacingModel(
    string? Margin,
    string? Padding,
    string? Gap);
