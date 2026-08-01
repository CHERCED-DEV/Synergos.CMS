namespace Synergos.CMS.Interfaces;

/// <summary>
/// Un hecho de pago ya VERIFICADO, listo para que el vertical dueño actúe.
/// </summary>
/// <remarks>
/// <see cref="VerifiedStatus"/> no viene del payload: el receptor lo re-consulta
/// al PSP antes de construir este evento (anti-tampering). Un sink puede
/// confiar en él sin volver a preguntar.
/// </remarks>
/// <param name="ProviderKey">Qué PSP lo emitió.</param>
/// <param name="EventId">Id del evento en el PSP. Es la llave de deduplicación.</param>
/// <param name="OrderReference">La referencia del comercio — lo que permite
///   saber de quién es el pago.</param>
/// <param name="SessionId">Sesión de pago asociada.</param>
/// <param name="VerifiedStatus">Estado REAL, re-consultado al PSP.</param>
/// <param name="OccurredUtc">Cuándo lo emitió el PSP.</param>
public sealed record PaymentEvent(
    string ProviderKey,
    string EventId,
    string OrderReference,
    string SessionId,
    PaymentStatus VerifiedStatus,
    DateTimeOffset OccurredUtc);

/// <summary>Qué hizo el vertical con el evento.</summary>
/// <param name="Handled"><c>false</c> = "no es mío". No es un error: es la
///   respuesta normal de siete de los ocho sinks ante cualquier evento.</param>
/// <param name="Detail">Qué pasó, para el log y la respuesta al PSP.</param>
/// <param name="Terminal">El evento es mío y lo rechacé DEFINITIVAMENTE (un
///   hold vencido, por ejemplo). Corta los reintentos del PSP. Un fallo
///   transitorio NO es terminal: hay que dejar que reintente.</param>
public sealed record PaymentEventOutcome(
    bool Handled,
    string? Detail = null,
    bool Terminal = false)
{
    /// <summary>No es de este vertical.</summary>
    public static readonly PaymentEventOutcome NotMine = new(false);

    /// <summary>Procesado.</summary>
    public static PaymentEventOutcome Ok(string? detail = null) => new(true, detail);

    /// <summary>Mío, pero rechazado para siempre: que el PSP deje de reintentar.</summary>
    public static PaymentEventOutcome Rejected(string reason) => new(true, reason, Terminal: true);
}

/// <summary>
/// Recibe los hechos de pago que le pertenecen a UN vertical.
/// </summary>
/// <remarks>
/// <para>Existe porque el receptor de webhooks sólo sabía hablarle a Tienda:
/// enrutaba a <c>IShopOrderService</c> y punto. Con tarjeta se disimulaba,
/// porque el cobro parece sincrónico. Con <b>PSE</b> —que confirma por evento,
/// potencialmente minutos u horas después— los otros siete verticales no se
/// enteraban de que les habían pagado (ADR 0116).</para>
///
/// <para><b>Cada sink decide si la referencia es suya</b>, y lo hace mirando si
/// tiene ese registro. No se enruta por prefijo del identificador: las
/// referencias del repo no comparten convención —<c>ord_</c>, <c>enr_</c>, un
/// radicado, el id de una reserva— y una convención que hay que recordar es una
/// convención que alguien va a romper. El dueño del dato es la única autoridad
/// fiable sobre de quién es.</para>
///
/// <para>El coste es una consulta por sink por evento. Para el volumen de
/// webhooks de pago es irrelevante, y compra que agregar un vertical sea
/// registrar un sink más — sin tocar el receptor.</para>
/// </remarks>
public interface IPaymentEventSink
{
    /// <summary>Qué negocio atiende. Sólo para diagnóstico y logs.</summary>
    string Vertical { get; }

    /// <summary>
    /// ¿Esta referencia es de este vertical? Se responde mirando si existe el
    /// registro, sin efectos.
    /// </summary>
    /// <remarks>
    /// Está separado de <see cref="HandleAsync"/> a propósito: el receptor
    /// pregunta PRIMERO de quién es y sólo entonces consulta el estado al PSP.
    /// Al revés, cualquiera podría hacernos golpear al proveedor mandando
    /// referencias inventadas — un amplificador gratis a costa nuestra. Y para
    /// una referencia ajena, la consulta además no sirve para nada.
    /// </remarks>
    Task<bool> OwnsAsync(string orderReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atiende un evento cuya referencia este vertical ya reclamó.
    /// </summary>
    /// <remarks>
    /// Debe ser IDEMPOTENTE: el mismo evento puede llegar más de una vez. El
    /// receptor deduplica por <see cref="PaymentEvent.EventId"/>, pero esa
    /// marca se pone DESPUÉS de procesar, así que un reintento tras una caída
    /// vuelve a entrar acá.
    /// </remarks>
    Task<PaymentEventOutcome> HandleAsync(PaymentEvent paymentEvent, CancellationToken cancellationToken = default);
}
