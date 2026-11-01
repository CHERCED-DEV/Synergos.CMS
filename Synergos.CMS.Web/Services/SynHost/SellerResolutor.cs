using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynSeller</c> → <see cref="SellerProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137) y en qué zona dice sus fechas (la de los listados, como el
/// storefront).
/// </summary>
public sealed class SellerResolutor : IResolutorSynHost<SellerProps>
{
    /// <summary>El bloque de la consola: las páginas que lo colocan son las de sus vistas.</summary>
    public const string AliasDelBloque = "elementSynSeller";

    /// <summary>La vista de un bloque sin elegir: el panel, que es donde abría la consola (014, I3).</summary>
    public const string VistaPorDefecto = "panel";

    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeSeller> _negocio;
    private readonly IRutasDeLasVistas _rutas;
    private readonly string _zona;
    private readonly ILogger<SellerResolutor> _log;

    public SellerResolutor(
        IPublishedValueFallback fallback,
        INegocioDelSitio<NegocioDeSeller> negocio,
        IRutasDeLasVistas rutas,
        IOptions<ListadosSettings> listados,
        ILogger<SellerResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _rutas = rutas;
        _zona = listados.Value.ZonaHoraria;
        _log = log;
    }

    public ElementoResuelto<SellerProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<SellerProps>(new SellerProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: _negocio.Actual().ApiBase,
            ZonaHoraria: _zona,
            Vista: editor.Opciones(RutasDeLasVistas.AliasDeLaVista)?[0],
            Rutas: _rutas.Del(AliasDelBloque, VistaPorDefecto)));
    }
}
