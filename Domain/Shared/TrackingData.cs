namespace Synergos.CMS.Domain.Shared;

/// <summary>
/// Metadatos de analítica que el editor asigna a una sección.
/// No contiene lógica de tracking; solo transporta los valores
/// que el Web Component Angular usará para disparar eventos.
/// </summary>
public sealed record TrackingData(string? Label, string? Category, string? Action);
