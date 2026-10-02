using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="INegocioDelSitio{TNegocio}"/> sobre una sección <c>Synergos:Features:&lt;X&gt;</c>: los
/// valores del sitio del hostname de la petición encima de los base (ADR 0137).
/// </summary>
/// <remarks>
/// <para><b>Recarga sin reinicio.</b> Lee <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> en
/// cada llamada, así que un cambio en la configuración rige desde la petición siguiente.</para>
///
/// <para><b>Una recarga inválida no tumba la funcionalidad.</b> Al arrancar, una sección inválida no
/// deja arrancar (<see cref="ValidadorDeSeccionDeNegocio{TSeccion}"/>). En caliente, la misma
/// validación hace que el monitor deje de entregar valor; acá se sigue con el ÚLTIMO VÁLIDO y se
/// escribe el error una vez por cada fallo distinto. Medido en el piloto (#194): con el CMS corriendo,
/// un <c>FeePercnt</c> dejó la comisión de antes en la página y en el cobro, y un solo error.</para>
/// </remarks>
public sealed class NegocioDelSitio<TSeccion, TNegocio> : INegocioDelSitio<TNegocio>
    where TSeccion : class, ISeccionDeNegocio<TNegocio>
    where TNegocio : class
{
    private readonly IOptionsMonitor<TSeccion> _monitor;
    private readonly Func<Guid?> _sitio;
    private readonly string _seccion;
    private readonly ILogger _log;
    private TSeccion _vigente;
    private string? _ultimoFallo;

    /// <param name="monitor">La sección, enlazada y validada.</param>
    /// <param name="sitio">La <c>Key</c> del siteRoot de la petición en curso, o nula sin dominio.</param>
    /// <param name="seccion">El nombre de la sección, para el log.</param>
    /// <param name="log">Dónde se dice que una recarga no sirvió.</param>
    public NegocioDelSitio(IOptionsMonitor<TSeccion> monitor, Func<Guid?> sitio, string seccion, ILogger log)
    {
        _monitor = monitor;
        _sitio = sitio;
        _seccion = seccion;
        _log = log;
        _vigente = monitor.CurrentValue;
    }

    /// <inheritdoc />
    public TNegocio Actual() => Vigente().Para(_sitio());

    private TSeccion Vigente()
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
                    _seccion);
            }

            return _vigente;
        }
    }
}
