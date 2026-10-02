using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="INegocioDeEventos"/> sobre la sección <c>Synergos:Features:Eventos</c>: los valores
/// del sitio del hostname de la petición encima de los base (ADR 0137).
/// </summary>
/// <remarks>
/// <para><b>Recarga sin reinicio.</b> Lee <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> en
/// cada llamada, así que un cambio en la configuración rige desde la petición siguiente.</para>
///
/// <para><b>Una recarga inválida no tumba la venta.</b> Al arrancar, una sección inválida no deja
/// arrancar (<see cref="ValidadorDeNegocioDeEventos"/>). En caliente, la misma validación hace que
/// el monitor deje de entregar valor; acá se sigue con el ÚLTIMO VÁLIDO y se escribe el error una
/// vez por cada fallo distinto. Lo contrario —una excepción en cada página y en cada checkout por un
/// dedazo— sería peor que seguir con la comisión de antes.</para>
/// </remarks>
public sealed class NegocioDeEventosDelSitio : INegocioDeEventos
{
    private readonly IOptionsMonitor<EventosFeatureSettings> _monitor;
    private readonly Func<Guid?> _sitio;
    private readonly ILogger<NegocioDeEventosDelSitio> _log;
    private EventosFeatureSettings _vigente;
    private string? _ultimoFallo;

    public NegocioDeEventosDelSitio(
        IOptionsMonitor<EventosFeatureSettings> monitor,
        SitioDeLaPeticion sitio,
        ILogger<NegocioDeEventosDelSitio> log)
        : this(monitor, () => sitio.Resolver().SiteRoot?.Key, log)
    {
    }

    /// <param name="sitio">La <c>Key</c> del siteRoot de la petición en curso, o nula sin dominio.</param>
    internal NegocioDeEventosDelSitio(
        IOptionsMonitor<EventosFeatureSettings> monitor,
        Func<Guid?> sitio,
        ILogger<NegocioDeEventosDelSitio> log)
    {
        _monitor = monitor;
        _sitio = sitio;
        _log = log;
        _vigente = monitor.CurrentValue;
    }

    public NegocioDeEventos Actual() => Vigente().Para(_sitio());

    private EventosFeatureSettings Vigente()
    {
        try
        {
            var actual = _monitor.CurrentValue;
            _vigente = actual;
            _ultimoFallo = null;
            return actual;
        }
        catch (Exception ex) when (ex is OptionsValidationException or InvalidOperationException)
        {
            if (!string.Equals(_ultimoFallo, ex.Message, StringComparison.Ordinal))
            {
                _ultimoFallo = ex.Message;
                _log.LogError(ex,
                    "{Seccion} cambió a un valor inválido; sigue rigiendo el último válido hasta que se corrija.",
                    EventosFeatureSettings.Seccion);
            }

            return _vigente;
        }
    }
}
