namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Contra qué se valida el avance de un pedido (HU #46) — sección <c>Synergos:Tracking</c>.
/// </summary>
/// <remarks>
/// <para><b>Por dónde pasa un pedido está escrito en C#, cuatro veces.</b> Meter «en aduana»
/// entre <c>shipped</c> y <c>delivered</c> es hoy un cambio de código y un despliegue del
/// portal. <c>Api.Workflow</c> tiene las transiciones como DATO — el mismo problema que la
/// HU #44 le quitó a Gobierno, en el sitio donde estaba copiado cuatro veces.</para>
///
/// <para><b>Una definición POR PIPELINE, y no es precaución teórica.</b> Los nombres de estado
/// se repiten entre dominios: <c>paid</c> está en tres, <c>confirmed</c> en dos,
/// <c>completed</c> en dos. Con una definición única, la etapa de un dominio se leería contra
/// el pipeline de otro y «enviado» sería «matriculado» sin que nada fallara.</para>
///
/// <para><b>Leer NO sale a la red, y es la diferencia con #44.</b> El timeline se pinta en cada
/// vista de pedido, así que el CMS conserva su almacén como modelo de lectura y la capacidad
/// sólo valida el avance. Con <c>Api.Workflow</c> caída, quien compró <b>sigue viendo dónde va
/// su pedido</b>; lo único que se para es avanzarlo. Es deliberadamente lo contrario que en
/// Gobierno: allá el riesgo era <i>decidir</i> con un proceso que quizá ya no es el vigente,
/// acá es <i>mostrar</i> lo que ya pasó, que no decide nada.</para>
/// </remarks>
public sealed class TrackingSettings
{
    /// <summary>
    /// <c>Stub</c> (default, el pipeline en proceso) o <c>Api</c> (contra <c>Synergos.Api.Workflow</c>).
    /// </summary>
    public string Mode { get; init; } = "Stub";

    /// <summary>Dónde vive la capacidad.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5215/";

    /// <summary>La llave compartida servicio↔servicio. Sin ella todo responde 401.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Prefijo de la clave de definición y del <c>Kind</c> del sujeto.
    /// </summary>
    /// <remarks>
    /// El dominio se le pega detrás: <c>tracking.shop</c>, <c>tracking.travel</c>… Configurable
    /// porque <b>versionar es publicar otra clave</b>: la capacidad se niega a reescribir una
    /// definición viva, ya que cambiarle las transiciones a instancias en marcha las dejaría en
    /// estados imposibles.
    /// </remarks>
    public string DefinitionPrefix { get; init; } = "tracking";

    /// <summary>
    /// Segundos de espera. Corto: avanzar una etapa no mueve plata ni deja nada a medias.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 10;
}
