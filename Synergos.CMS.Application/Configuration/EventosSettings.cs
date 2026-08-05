namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Contra qué se compran las entradas (HU #35) — sección <c>Synergos:Eventos</c>.
/// </summary>
/// <remarks>
/// <para><b>El default es el motor en proceso, y no es una transición.</b> Un clon limpio
/// arranca y vende entradas sin levantar cuatro servicios. Poner <see cref="Mode"/> en
/// <c>Bff</c> sin el orquestador arriba degrada —no se puede comprar, y lo dice— pero no tumba
/// el vertical: el catálogo, las fichas, «mis entradas», transferir y la puerta siguen
/// sirviendo, porque el artefacto <b>no</b> depende de por dónde se pagó.</para>
///
/// <para><b>Contra el ORQUESTADOR y no contra las capacidades</b>, al revés que Realty (#33a).
/// La diferencia es que acá sí hay algo que deshacer: si el cobro falla hay que soltar el aforo
/// apartado, y si el consumo falla después de capturar hay que devolver la plata. El CMS no
/// tiene dónde anotar una compensación pendiente. Hay gate.</para>
/// </remarks>
public sealed class EventosSettings
{
    /// <summary>
    /// <c>Stub</c> (default, el motor en proceso) o <c>Bff</c> (contra <c>Synergos.Bff.Eventos</c>).
    /// </summary>
    public string Mode { get; init; } = "Stub";

    /// <summary>Dónde vive el orquestador.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5303/";

    /// <summary>La llave compartida servicio↔servicio. Sin ella todo responde 401.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// El <c>Kind</c> con el que este vertical nombra a quien compra.
    /// </summary>
    /// <remarks>
    /// Viaja opaco: el orquestador lo guarda y lo devuelve, y ninguna capacidad ramifica sobre
    /// él (<c>CLAUDE.md</c> §13). Es configurable porque el nombre del sujeto es del negocio que
    /// despliega, no de la plataforma.
    /// </remarks>
    public string BuyerKind { get; init; } = "eventos.comprador";

    /// <summary>
    /// Segundos de espera. Generoso a propósito: comprar cruza cuatro servicios y NO es auxiliar.
    /// </summary>
    /// <remarks>
    /// Cortarlo pronto es peor que esperar. Un timeout no dice «no se cobró» — dice «no sé»; y
    /// el peor resultado posible es un cobro sin entradas. Ver
    /// <c>HttpEventTicketingService</c>: ante un timeout se consulta si la compra llegó a
    /// existir, y para eso hay que haberle dado tiempo a existir.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 30;
}
