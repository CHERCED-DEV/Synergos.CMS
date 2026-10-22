using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Services.Puerta;

/// <summary>
/// Qué flujo coloca una vista alrededor de un bloque (núcleo N3, FR-033): el que elige el editor, si es de
/// la lista cerrada —lo que la puerta expone y el sitio declara en <c>Synergos:Puerta:Flujos</c>—; si no,
/// el valor por defecto que escribe la vista.
/// </summary>
/// <remarks>
/// <para><b>Nada del contenido es frontera de confianza</b>: el valor guardado puede ser de un flujo que se
/// cerró después, o de otro sitio. Se cruza con la configuración viva, como la puerta, y fuera de la lista
/// vale el por defecto (y queda en el log), no lo que diga el contenido.</para>
///
/// <para><b>El por defecto es un literal de la vista</b> y lo cruza <c>CoordinadorDelFlujoEnLasVistasTests</c>
/// con la tabla de la puerta y la configuración, igual que cruzaba el literal de antes.</para>
/// </remarks>
public sealed class FlujosColocables
{
    private readonly TablaDeLaPuerta _tabla;
    private readonly IOptionsMonitor<PuertaSettings> _ajustes;
    private readonly ILogger<FlujosColocables> _log;

    public FlujosColocables(TablaDeLaPuerta tabla, IOptionsMonitor<PuertaSettings> ajustes, ILogger<FlujosColocables> log)
    {
        _tabla = tabla;
        _ajustes = ajustes;
        _log = log;
    }

    /// <summary>Si <paramref name="flujo"/> es de la lista cerrada: lo expone la puerta y el sitio lo declara.</summary>
    public bool EsColocable(string flujo)
        => _tabla.DelFlujo(flujo).Count > 0 && _ajustes.CurrentValue.Flujos.ContainsKey(flujo);

    /// <summary>El flujo que elige el editor si es colocable; si no, o si no eligió ninguno, <paramref name="porDefecto"/>.</summary>
    public string Elegir(string? elegido, string porDefecto)
    {
        var clave = string.IsNullOrWhiteSpace(elegido) ? null : elegido.Trim();
        if (clave is null || EsColocable(clave)) return clave ?? porDefecto;

        _log.LogWarning("El bloque eligió el flujo {Flujo}, que la puerta no expone o el sitio no declara: se coloca {PorDefecto}.",
            clave, porDefecto);
        return porDefecto;
    }
}
