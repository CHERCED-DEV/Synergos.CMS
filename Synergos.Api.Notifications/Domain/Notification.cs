using Synergos.Core;

namespace Synergos.Api.Notifications.Domain;

/// <summary>Por dónde sale un aviso.</summary>
public enum Channel
{
    Email,
    Sms,
    Push,
}

/// <summary>
/// Una plantilla de aviso.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="Key">Nombre estable con el que la pide un dominio — <c>cita.recordatorio</c>.</param>
/// <param name="Channel">Por dónde sale.</param>
/// <param name="Subject">Asunto, con marcadores <c>{nombre}</c>.</param>
/// <param name="Body">Cuerpo, con marcadores <c>{nombre}</c>.</param>
/// <remarks>
/// <b>La plantilla vive acá y el texto lo escribe el dominio.</b> Es la línea que mantiene esta
/// capacidad agnóstica: Notifications sabe rellenar marcadores y entregar, no sabe qué es una
/// cita. Si el texto viviera acá cableado por caso de uso, la primera plantilla clínica la
/// habría atado a Salud.
/// </remarks>
public sealed record Template(string Id, string Key, Channel Channel, string Subject, string Body);

/// <summary>
/// En qué va un envío.
/// </summary>
/// <remarks>
/// <para><b>Antes eran dos valores —<c>Sent</c> y <c>Failed</c>— y mentían.</b> «Sent» quería
/// decir «el transporte lo aceptó», que no es «llegó»: un correo aceptado y rebotado media hora
/// después se quedaba para siempre como enviado. Un rastro que dice que sí cuando fue que no es
/// peor que no tener rastro, porque nadie lo mira dos veces.</para>
///
/// <para><b>Las cuatro del medio llegan por webhook, minutos después del envío</b>, y pueden
/// llegar <i>fuera de orden</i>. Por eso el avance está en una regla —<see cref="NotificationRules.Advance"/>—
/// y no en una asignación suelta.</para>
/// </remarks>
public enum DeliveryStatus
{
    /// <summary>Registrado, todavía no aceptado por el proveedor. <b>Se puede reintentar.</b></summary>
    Queued = 1,

    /// <summary>
    /// El proveedor lo aceptó y dio un id. <b>No es «llegó»</b>: es «me hago cargo de intentarlo».
    /// </summary>
    Accepted = 2,

    /// <summary>El servidor del destinatario lo aceptó. Lo más cerca de «llegó» que existe.</summary>
    Delivered = 3,

    /// <summary>Rebotó. La dirección no sirve, o el destino lo rechazó.</summary>
    Bounced = 4,

    /// <summary>Lo marcaron como correo no deseado. <b>Cuesta la reputación del remitente.</b></summary>
    Complained = 5,

    /// <summary>El proveedor lo rechazó de plano y nunca hubo id. No se reintenta.</summary>
    Failed = 6,
}

/// <summary>Un envío, con su rastro.</summary>
/// <param name="Id">Identificador nuestro.</param>
/// <param name="To">A quién — opaco.</param>
/// <param name="Address">La dirección concreta en ese canal.</param>
/// <param name="Channel">Por dónde salió.</param>
/// <param name="TemplateKey">Con qué plantilla.</param>
/// <param name="Subject">Asunto ya rellenado.</param>
/// <param name="Body">Cuerpo ya rellenado.</param>
/// <param name="Status">En qué va.</param>
/// <param name="AtUtc">Cuándo se registró.</param>
/// <param name="ProviderMessageId">El id que dio el proveedor, o <c>null</c> si todavía no aceptó.</param>
/// <param name="StatusAtUtc">Cuándo cambió el estado por última vez.</param>
/// <remarks>
/// <para><b><c>ProviderMessageId</c> es lo que hace posible saber si llegó.</b> El proveedor
/// avisa del rebote citando <i>su</i> id, no el nuestro. Sin guardarlo, «entregado» y «rebotado»
/// quedan fuera de alcance <b>para siempre</b> — no es una mejora que se pueda añadir después
/// sobre los envíos viejos, porque el evento ya pasó y no vuelve.</para>
/// </remarks>
public sealed record Delivery(
    string Id,
    Ref To,
    string Address,
    Channel Channel,
    string TemplateKey,
    string Subject,
    string Body,
    DeliveryStatus Status,
    DateTimeOffset AtUtc,
    string? ProviderMessageId = null,
    DateTimeOffset? StatusAtUtc = null);

/// <summary>
/// Por dónde sale de verdad un aviso.
/// </summary>
/// <remarks>
/// <para>Es la costura hacia el mundo: un proveedor de correo, uno de SMS, una pasarela de push.
/// El servicio no sabe cuál — y por eso esta API se puede probar y desplegar sin ninguno.</para>
///
/// <para><b>Devuelve el id del proveedor, no un booleano</b>, y es asíncrona. Las dos cosas
/// vienen del mismo hecho: hablar con un proveedor real es una llamada de red que termina en un
/// identificador con el que después se correlaciona el webhook de vuelta. Un <c>bool</c>
/// síncrono no es una simplificación de eso: es una forma que no lo admite.</para>
/// </remarks>
public interface INotificationSender
{
    /// <summary>Si este transporte sabe hablar por ese canal.</summary>
    /// <remarks>
    /// Se pregunta <b>antes</b> de intentar. Sin esto, un despliegue con correo pero sin SMS
    /// descubre que no puede mandar SMS al primer intento fallido, con el envío ya registrado.
    /// </remarks>
    bool Supports(Channel channel);

    /// <summary>
    /// Entrega y devuelve el id del proveedor, o dice por qué no pudo. <b>No lanza.</b>
    /// </summary>
    /// <param name="idempotencyKey">
    /// Para que un reintento nuestro no produzca dos correos <i>del lado del proveedor</i>.
    /// Nuestra propia idempotencia cubre el caso normal; ésta cubre el que no podemos ver: la
    /// petición que sí llegó y cuya respuesta se perdió.
    /// </param>
    Task<Result<string>> SendAsync(
        Channel channel, string address, string subject, string body,
        string idempotencyKey, CancellationToken ct = default);
}
