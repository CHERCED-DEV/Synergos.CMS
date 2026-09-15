namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Contra quién se cobra la TASA de un trámite (#27) — sección <c>Synergos:Gob:Payments</c>.
/// </summary>
/// <remarks>
/// <para><b>Va por SU PROPIO interruptor</b>, como las notificaciones de Gobierno (HU #62) y por
/// la misma razón, más una que sólo aplica acá: <c>Synergos:Payments:Mode</c> cambia el seam
/// ENTERO —los ocho consumidores del motor— y sólo se puede encender cuando Tienda, Salud,
/// Eventos y Viajes ya compran contra su orquestador. La tasa no tiene que esperar a eso:
/// <b>radicar no compone una saga</b>, y por eso es el único consumidor del motor que puede
/// hablarle a la capacidad hoy.</para>
///
/// <para><b>Y Gobierno no va a tener orquestador.</b> No es que falte: el propio motor decide
/// «no se aborta el trámite si la captura no sale» —en un servicio público, perder la radicación
/// de un ciudadano porque su banco tardó es peor que arrastrar una tasa pendiente—, y si no se
/// aborta no hay nada que deshacer. Un <c>Bff.Gob</c> sería la máquina de compensar sin
/// compensación. El mapa del cableado se equivocó con ese filtro cuatro veces.</para>
///
/// <para><b>El default es <c>Local</c>, y no es una transición.</b> Una entidad que no despliega
/// el árbol de servicios sigue radicando y sigue cobrando con el motor en proceso; lo que no
/// tiene es una pasarela de verdad detrás — que es la verdad sobre ella.</para>
/// </remarks>
public sealed class GovFeeSettings
{
    /// <summary>
    /// <c>Local</c> (default, el motor de pago en proceso) o <c>Api</c> (contra
    /// <c>Api.Payments</c>).
    /// </summary>
    /// <remarks>
    /// <c>Local</c> resuelve al <c>IPaymentProvider</c> que esté registrado, sea el que sea: si
    /// el despliegue ya puso <c>Synergos:Payments:Mode=Api</c>, la tasa va por ahí sin tener que
    /// decirlo dos veces.
    /// </remarks>
    public string Mode { get; init; } = "Local";

    /// <summary>Dónde vive la capacidad de cobros.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5204/";

    /// <summary>La llave compartida servicio↔servicio. Sin ella todo responde 401.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Con qué <c>Kind</c> se nombra el expediente que paga la tasa.
    /// </summary>
    /// <remarks>
    /// El <c>Id</c> es el RADICADO, y eso es lo que hace idempotente el cobro: se acuña antes de
    /// llamar a la capacidad, así que un reintento por timeout encuentra la misma llave y no
    /// cobra dos veces (<c>feedback_idempotency_before_state</c>).
    /// </remarks>
    public string CaseKind { get; init; } = "gov.expediente";

    /// <summary>Con qué <c>Kind</c> se nombra al ciudadano que paga.</summary>
    public string CitizenKind { get; init; } = "gov.ciudadano";

    /// <summary>
    /// Segundos de espera. Generoso a propósito: la tasa se cobra dentro de la radicación.
    /// </summary>
    /// <remarks>
    /// Y aunque se agote, el expediente se radica igual: lo que queda escrito es que la tasa no
    /// se pudo cobrar, no un trámite perdido.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 30;
}
