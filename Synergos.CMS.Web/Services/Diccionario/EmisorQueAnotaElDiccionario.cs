using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Diccionario;

/// <summary>
/// El <see cref="ISynHostEmitter"/> de la página: anota las secciones de diccionario que pide cada
/// elemento emitido (<see cref="SynHostEmitRequest.Diccionario"/>) y delega la emisión.
/// </summary>
/// <remarks>
/// <para><b>Por qué en la emisión y no en otro sitio.</b> Lo que está en la página es exactamente
/// lo que se emitió: un bloque del contenido, una vista de vertical que monta su elemento a mano,
/// el chrome del sitio. Calcularlo de antemano recorriendo el contenido vería los bloques y no las
/// vistas, y repetiría la regla de qué vista monta qué elemento.</para>
///
/// <para><b>Un decorador y no un cambio en <c>DefaultSynHostEmitter</c></b>: el emitter vive en
/// Application y no conoce la petición (ADR 0002); éste vive en Web y sí. El cable no cambia: las
/// secciones no se escriben en el tag.</para>
/// </remarks>
public sealed class EmisorQueAnotaElDiccionario : ISynHostEmitter
{
    private readonly ISynHostEmitter _emisor;
    private readonly SeccionesDeLaPagina _secciones;

    public EmisorQueAnotaElDiccionario(ISynHostEmitter emisor, SeccionesDeLaPagina secciones)
    {
        _emisor = emisor;
        _secciones = secciones;
    }

    public Task<SynHostEmitResult> EmitAsync(SynHostEmitRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _secciones.Anotar(request.Diccionario);
        return _emisor.EmitAsync(request, ct);
    }
}
