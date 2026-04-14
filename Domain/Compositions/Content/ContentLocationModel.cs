namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Editor-authored postal address + optional geo coordinates. All strings so the
/// editor controls locale-specific formatting — no parsing, no normalization.
///
/// Coordinates (<see cref="Latitude"/>, <see cref="Longitude"/>) are strings so
/// the editor can paste "4.7110" or "4.711027" without us imposing precision.
/// Views that need numeric values parse them on render and silently fall back
/// when absent or malformed.
/// </summary>
public sealed record ContentLocationModel(
    string? AddressLine,
    string? City,
    string? Region,
    string? PostalCode,
    string? Country,
    string? Latitude,
    string? Longitude,
    string? GoogleMapsUrl);
