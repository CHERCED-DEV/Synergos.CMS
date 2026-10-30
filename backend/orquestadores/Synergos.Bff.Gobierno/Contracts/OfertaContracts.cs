namespace Synergos.Bff.Gobierno.Contracts;

// Lo que cruza el cable de la oferta de un trámite (009-gobierno-tramite, data-model §7). Todo anulable con `= null`
// en lo que llega (molde F2 de la ADR 0140): el dominio dice qué falta, con su código.

/// <summary>Publicar la oferta de un trámite (la llama el CMS al publicar el <c>tramitePage</c>).</summary>
/// <param name="Tramite">La clave estable del nodo del trámite: no su slug ni su nombre.</param>
/// <param name="Tasa">La tasa; cero si el trámite es gratuito (se publica igual: la ausencia dice «no radicable»).</param>
/// <param name="Moneda">La moneda (ISO 4217).</param>
public sealed record PublicarOfertaDeTramiteRequest(string? Tramite = null, decimal? Tasa = null, string? Moneda = null);

/// <summary>Cómo quedó publicada la oferta de un trámite.</summary>
/// <param name="Retirada">Si se retiró: su tasa dejó de valer y el trámite no se radica por la puerta.</param>
public sealed record OfertaDeTramiteResponse(string Tramite, decimal Tasa, string Moneda, bool Retirada);
