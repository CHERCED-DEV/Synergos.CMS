using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Las fichas de estadía servidas desde el CONTENIDO que el hotelero autoró. Es el
/// <see cref="IStayContentProvider"/> que se registra cuando
/// <c>Synergos:Catalog:Sources:Booking = cms</c>.
/// </summary>
/// <remarks>
/// <b>Es la versión SIMPLE de la forma de dos capas</b> de
/// <see cref="CatalogPropertyCatalogProvider"/> y <see cref="CatalogEventCatalogProvider"/>: una
/// sola capa, la del contenido. <see cref="IStayContentProvider"/> es de SOLO LECTURA —tiene
/// <c>GetStayAsync</c> y nada más—, así que no existe un segundo autor que publique por fuera
/// del backoffice y no hay nada que fusionar. Prescindir del <c>IJsonEntityStore</c> aquí no es
/// una omisión: un overlay durable sin escritor sería exactamente el campo que promete algo que
/// nadie cumple, y el siguiente que lo viera confiaría en él.
///
/// <para><b>La resolución se calca del stub y no se reinventa</b>: primero por
/// <c>stayId</c> exacto, después por código de tipo de habitación, aceptando el
/// <c>offerId</c> del buscador ("DLX/DLX-FLEX") por su segmento inicial. Si el orden cambiara
/// según de dónde salen las estadías, sería una regresión invisible que solo aparecería al
/// mover el flag.</para>
///
/// <para>Lógica pura: cero <c>Umbraco.Cms.*</c> y cero <c>Microsoft.AspNetCore.*</c>
/// (ADR 0002).</para>
/// </remarks>
public sealed class CatalogStayContentProvider : IStayContentProvider
{
    private readonly ICatalogSource<StayDetail> _source;

    public CatalogStayContentProvider(ICatalogSource<StayDetail> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async Task<StayDetail?> GetStayAsync(string stayRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stayRef))
        {
            return null;
        }

        // Acepta el offerId del search ("DLX/DLX-FLEX") → resolver por el room type (el
        // segmento antes del '/'). También stayId o room type code directos.
        var key = stayRef.Trim();
        var slash = key.IndexOf('/', StringComparison.Ordinal);
        var head = slash > 0 ? key[..slash] : key;

        var catalog = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);

        return catalog.FirstOrDefault(s =>
                string.Equals(s.StayId, key, StringComparison.OrdinalIgnoreCase))
            ?? catalog.FirstOrDefault(s =>
                s.RoomTypeCodes.Any(c => string.Equals(c, head, StringComparison.OrdinalIgnoreCase)));
    }
}
