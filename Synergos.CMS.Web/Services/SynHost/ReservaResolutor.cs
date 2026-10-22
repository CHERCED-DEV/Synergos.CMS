using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.Reservas;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynReserva</c> → <see cref="ReservaProps"/> (011-booking-reserva): lo que escribe el editor, el servicio
/// reservable que eligió (leído de SU ficha) y el camino del sitio de la petición (ADR 0137).
/// </summary>
/// <remarks>
/// <para><b>El servicio sale de la ficha, no del bloque</b>: el editor elige la ficha —o deja el selector vacío y vale
/// la ficha donde está el bloque— y lo reservable —duración, días, horario— es lo que la ficha declara en su composición
/// «reservable», lo MISMO que el CMS publica como oferta.
/// Una ficha no marcada reservable, o incompleta, no viaja como servicio: el elemento lo dice y no ofrece horas.</para>
///
/// <para><b>El camino lo decide acá el CMS</b> (<see cref="CaminoDeReserva.Decidir"/>): <c>apagada</c> si el sitio lo
/// declara; si no, la puerta cuando abre <see cref="CaminoDeReserva.Flujo"/>; si no, <c>no-disponible</c>.</para>
/// </remarks>
public sealed class ReservaResolutor : IResolutorSynHost<ReservaProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeReserva> _negocio;
    private readonly IPuertaDeLosFlujos _puerta;
    private readonly IOptions<ListadosSettings> _listados;
    private readonly IUmbracoContextAccessor _contexto;
    private readonly ILogger<ReservaResolutor> _log;

    public ReservaResolutor(
        IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeReserva> negocio, IPuertaDeLosFlujos puerta,
        IOptions<ListadosSettings> listados, IUmbracoContextAccessor contexto, ILogger<ReservaResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _puerta = puerta;
        _listados = listados;
        _contexto = contexto;
        _log = log;
    }

    public ElementoResuelto<ReservaProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<ReservaProps>(new ReservaProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            Servicio: Servicio(elemento.Value<IPublishedContent>(_fallback, "servicio") ?? PaginaActual()),
            EnlaceAlternativo: editor.Enlace("enlaceAlternativo")?.Url,
            Camino: CaminoDeReserva.Decidir(_negocio.Actual().Camino, _puerta.Abre(CaminoDeReserva.Flujo)),
            Zona: _listados.Value.ZonaHoraria));
    }

    /// <summary>La página de la petición: la ficha que contiene el bloque cuando el editor no eligió otra.</summary>
    private IPublishedContent? PaginaActual()
        => _contexto.TryGetUmbracoContext(out var contexto) ? contexto.PublishedRequest?.PublishedContent : null;

    /// <summary>El servicio de la ficha, o <c>null</c> si no está marcada reservable o le falta algo para reservarse.</summary>
    private ServicioReservable? Servicio(IPublishedContent? ficha)
    {
        if (ficha is null || !ficha.Value<bool>(_fallback, FichaReservable.Marca)) return null;

        var duracion = ficha.Value<int>(_fallback, "duracionMinutos");
        var abre = ficha.Value<string>(_fallback, "abre")?.Trim();
        var cierra = ficha.Value<string>(_fallback, "cierra")?.Trim();
        var dias = FichaReservable.DiasDe(ficha.Value<IEnumerable<string>>(_fallback, "diasDeAtencion"))
            .Select(d => d.ToString()).ToList();

        var horario = FichaReservable.Hora(abre) is { } desde && FichaReservable.Hora(cierra) is { } hasta && desde < hasta;
        if (duracion <= 0 || dias.Count == 0 || !horario)
        {
            _log.LogWarning("La ficha {Ficha} está marcada reservable y le falta duración, días u horario: no se ofrece para reservar.", ficha.Key);
            return null;
        }

        return new ServicioReservable(ficha.Key.ToString("D"), ficha.Name ?? string.Empty, duracion, dias, abre!, cierra!);
    }
}
