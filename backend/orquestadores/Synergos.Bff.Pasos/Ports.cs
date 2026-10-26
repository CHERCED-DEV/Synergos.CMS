using Synergos.Core;

namespace Synergos.Bff.Pasos;

// ── Los puertos: lo que un paso necesita de una capacidad, y nada más ───────
// Un paso no habla HTTP: pide por su puerto y el orquestador lo cumple con su cliente. Así el
// mismo paso de autorizar le puede servir a cualquier orquestador —hoy lo usa sólo Eventos, el
// piloto de la ADR 0140— sin conocer la URL, la llave ni el DTO de ninguno, y el orquestador sigue
// siendo el único que sabe con qué capacidad habla.
//
// Cada puerto trae SOLO lo que hoy ejecuta un flujo declarado. Lo que deshace (soltar, reponer,
// anular, devolver) sigue en el `*CompensationExecutor` de cada orquestador: un método de puerto
// sin quien lo llame es una promesa que nadie prueba.

/// <summary>Lo que vale algo según <c>Api.Pricing</c>.</summary>
/// <param name="Subtotal">Antes del descuento y de los impuestos: la base de una comisión de servicio.</param>
/// <param name="Descuento">Lo que descuenta la promoción, si se cotizó con una; si no, cero. Lo
/// calcula la capacidad sobre el subtotal, antes del impuesto.</param>
/// <param name="Impuesto">El impuesto, sobre lo ya descontado.</param>
/// <param name="Total">Lo que se cobra por las líneas cotizadas: el subtotal menos el descuento más el impuesto.</param>
/// <param name="Lineas">El precio unitario de cada línea cotizada, por sujeto (núcleo N2, N-1 de Tienda): el
/// pedido congela el de cada renglón (#122), y la parte de cada ítem sale de aquí (006-P6). Vacío si quien
/// cotizó no lo dio: <c>EventosPorts</c> no lo da y Eventos no lo usa.</param>
public sealed record QuoteView(
    Money Subtotal, Money Descuento, Money Impuesto, Money Total,
    IReadOnlyList<(Ref Sujeto, Money PrecioUnitario)>? Lineas = null)
{
    /// <summary>El precio unitario de cada línea cotizada; vacío si no se dio.</summary>
    public IReadOnlyList<(Ref Sujeto, Money PrecioUnitario)> Lineas { get; init; } = Lineas ?? [];
}

/// <summary>Cotizar contra <c>Api.Pricing</c>.</summary>
public interface IPricingPort
{
    /// <summary>
    /// Cuánto valen esas líneas, con la promoción <paramref name="promocion"/> si la hay. El precio y el
    /// descuento salen de la capacidad, nunca del llamador: del llamador sólo llega el CÓDIGO.
    /// </summary>
    /// <param name="lineas">Qué y cuántas.</param>
    /// <param name="promocion">El código de la promoción, o nulo. Uno que no existe o que venció lo
    /// rechaza la capacidad con su código, antes de que nadie aparte nada.</param>
    /// <param name="ct">Cancelación.</param>
    Task<Result<QuoteView>> CotizarAsync(
        IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas, string? promocion, CancellationToken ct);
}

/// <summary>Lo que hace una oferta publicada en las capacidades (P12; NM-1 de 010, NN-16 de 005, NE-7 de 007).</summary>
/// <remarks>Hoy, sólo el precio: el pozo y el recurso de la agenda entran cuando una oferta declarada los pida (001-P11).</remarks>
public interface IOfertaPort
{
    /// <summary>
    /// Fija el precio de <paramref name="sujeto"/> en <c>Api.Pricing</c>, sin impuesto, vigente hasta
    /// <paramref name="hasta"/> (nulo: sin fin). Devuelve el identificador del precio.
    /// </summary>
    /// <remarks>Retirar un precio es fijarlo con <paramref name="hasta"/> en el instante de retirar: la cotización
    /// lo rechaza desde entonces y el precio sigue ahí, con su historia.</remarks>
    Task<Result<string>> FijarPrecioAsync(Ref sujeto, Money precio, DateTimeOffset? hasta, IdempotencyKey llave, CancellationToken ct);
}

/// <summary>Apartar y consumir existencias de un pozo contable de <c>Api.Inventory</c>.</summary>
public interface IInventoryPort
{
    /// <summary>El identificador del pozo de <paramref name="sujeto"/>. Lo genera la capacidad: no se adivina.</summary>
    Task<Result<string>> HallarAsync(Ref sujeto, CancellationToken ct);

    /// <summary>Aparta unidades del pozo y devuelve el identificador del apartado.</summary>
    Task<Result<string>> ApartarAsync(
        string itemId, int cantidad, Ref paraQue, IdempotencyKey llave, CancellationToken ct);

    /// <summary>Consume el apartado: las unidades salen del pozo de verdad.</summary>
    Task<Result<string>> ConsumirAsync(string holdId, CancellationToken ct);
}

/// <summary>Autorizar y capturar un cobro en <c>Api.Payments</c>.</summary>
public interface IPaymentsPort
{
    /// <summary>Reserva cupo en el medio de pago sin mover plata. Devuelve el identificador del cobro.</summary>
    Task<Result<string>> AutorizarAsync(
        Ref paraQue, Ref pagador, Money monto, IdempotencyKey llave, CancellationToken ct);

    /// <summary>Mueve la plata autorizada.</summary>
    Task<Result<string>> CapturarAsync(string cobroId, IdempotencyKey llave, CancellationToken ct);
}

/// <summary>Adónde y a nombre de quién va un aviso: lo que la saga no puede llevar.</summary>
/// <param name="Correo">La dirección. Sin ella no se avisa.</param>
/// <param name="Nombre">Cómo saludar.</param>
/// <param name="Enlace">Dónde ver lo avisado.</param>
/// <param name="Sitio">Desde qué sitio se hizo.</param>
/// <remarks>
/// <b>Es efímero y no se guarda en ningún sitio</b> (ADR 0140 F3): llega en la entrada de la fase que
/// avisa, sirve para esa llamada y muere con ella. La saga no lleva datos personales a propósito, y
/// un aviso no es razón para cambiar eso.
/// </remarks>
public sealed record Contacto(string Correo, string? Nombre, string? Enlace, string? Sitio);

/// <summary>Mandar un aviso por <c>Api.Notifications</c>.</summary>
public interface INotificationsPort
{
    /// <summary>
    /// Pide el aviso con la plantilla <paramref name="plantilla"/> y esos valores. Devuelve el id de
    /// la entrega. El TEXTO no viaja: la capacidad rellena la plantilla, y la plantilla la escribe el
    /// dominio.
    /// </summary>
    Task<Result<string>> AvisarAsync(
        Ref destinatario, string direccion, string plantilla, IReadOnlyDictionary<string, string> valores,
        IdempotencyKey llave, CancellationToken ct);
}

/// <summary>Un apartado en la agenda: cuál es y hasta cuándo vale si nadie lo confirma.</summary>
/// <param name="Id">El apartado.</param>
/// <param name="Vence">Cuándo deja de valer (<c>HoldResponse.ExpiresAt</c>): lo que la pantalla puede decirle a quien
/// reserva, porque lo dice el servidor (núcleo, NB-5 de 011 y NV-12 de 006).</param>
public sealed record Apartado(string Id, DateTimeOffset Vence);

/// <summary>Una franja del recurso y cuánto cupo le queda (núcleo, NB-3 de 011, sobre NB-2).</summary>
/// <param name="Inicio">Cuándo empieza.</param>
/// <param name="Fin">Cuándo termina.</param>
/// <param name="Libres">Cuántas reservas más admite; cero: llena (la pantalla decide si la enseña).</param>
public sealed record FranjaLibre(DateTimeOffset Inicio, DateTimeOffset Fin, int Libres);

/// <summary>Hallar la agenda de un sujeto, apartar una ventana y confirmarla en <c>Api.Booking</c>.</summary>
public interface IBookingPort
{
    /// <summary>
    /// Las franjas del recurso entre <paramref name="desde"/> y <paramref name="hasta"/>, con lo libre de cada una:
    /// lo que se ve antes de reservar (NB-3 de 011, sobre NB-2). Leer: no aparta nada.
    /// </summary>
    Task<Result<IReadOnlyList<FranjaLibre>>> FranjasAsync(string recurso, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct);

    /// <summary>El identificador del recurso (la agenda) de <paramref name="sujeto"/>. Lo genera la capacidad: no se adivina.</summary>
    Task<Result<string>> HallarAsync(Ref sujeto, CancellationToken ct);

    /// <summary>Aparta la ventana en el recurso para <paramref name="paraQuien"/> y devuelve el apartado con su vencimiento.</summary>
    Task<Result<Apartado>> ApartarAsync(string recurso, TimeWindow ventana, Ref paraQuien, IdempotencyKey llave, CancellationToken ct);

    /// <summary>Confirma el apartado: devuelve la reserva, que es sobre lo que se cancela después.</summary>
    Task<Result<string>> ConfirmarAsync(string apartado, IdempotencyKey llave, CancellationToken ct);
}

/// <summary>Comprobar en <c>Api.Consent</c> que hay un permiso vigente (núcleo N2).</summary>
public interface IConsentPort
{
    /// <summary>
    /// El permiso vigente de <paramref name="sujeto"/> para <paramref name="proposito"/>. Si no lo hay —nunca
    /// se dio, se retiró o venció—, el rechazo de la capacidad tal cual (<c>consent.not_granted</c>…).
    /// </summary>
    Task<Result<string>> VerificarAsync(Ref sujeto, string proposito, CancellationToken ct);
}

/// <summary>Una canasta como la ve quien la compra: de quién es, qué trae y si se puede cerrar.</summary>
/// <param name="Dueno">Quién la abrió.</param>
/// <param name="Lineas">Qué y cuántas: lo que se cotiza y se aparta.</param>
/// <param name="Cerrada">Si ya se convirtió en pedido.</param>
/// <param name="Abierta">Si todavía admite cambios (no cerrada ni vencida).</param>
public sealed record CanastaView(Ref Dueno, IReadOnlyList<(Ref Sujeto, int Cantidad)> Lineas, bool Cerrada, bool Abierta);

/// <summary>Leer y cerrar una canasta de <c>Api.Cart</c> (núcleo N2).</summary>
public interface ICartPort
{
    /// <summary>La canasta: la única fuente de qué se compra.</summary>
    Task<Result<CanastaView>> LeerAsync(string canasta, CancellationToken ct);

    /// <summary>
    /// La cierra: deja de admitir cambios. Sin llave, porque la capacidad no la lee: cerrarla otra vez se
    /// rechaza (<c>cart.already_checked_out</c>), así que repetir no duplica nada.
    /// </summary>
    Task<Result<string>> CerrarAsync(string canasta, CancellationToken ct);
}

/// <summary>Una línea del pedido con el precio que cotizó <c>Api.Pricing</c>: el pedido congela el de cada renglón.</summary>
public sealed record LineaDePedido(Ref Sujeto, int Cantidad, Money PrecioUnitario);

/// <summary>Registrar y cumplir un pedido en <c>Api.Orders</c> (núcleo N2).</summary>
public interface IOrdersPort
{
    /// <summary>Registra el pedido de <paramref name="comprador"/> y devuelve su identificador.</summary>
    Task<Result<string>> CrearAsync(
        Ref comprador, IReadOnlyList<LineaDePedido> lineas, Money total, IdempotencyKey llave, CancellationToken ct);

    /// <summary>Lo da por cumplido. Desde ahí la capacidad ya no deja anularlo.</summary>
    Task<Result<string>> CumplirAsync(string pedido, CancellationToken ct);
}

/// <summary>Adónde se despacha. Es un dato personal: entra efímero y no se guarda en la saga.</summary>
public sealed record DireccionDeEnvio(
    string Linea1, string? Linea2, string Ciudad, string? Region, string? CodigoPostal, string Pais, string? Contacto);

/// <summary>Despachar en <c>Api.Fulfillment</c> (núcleo N2).</summary>
public interface IFulfillmentPort
{
    /// <summary>Registra el envío de <paramref name="paraQue"/> y devuelve su identificador.</summary>
    Task<Result<string>> DespacharAsync(
        Ref paraQue, DireccionDeEnvio direccion, string transportadora, IdempotencyKey llave, CancellationToken ct);
}

/// <summary>Un paso de la historia de una instancia: qué transición, de dónde a dónde, quién actuó y cuándo.</summary>
public sealed record PasoDeLaHistoria(string Transicion, string Desde, string Hacia, Ref Por, string? Nota, DateTimeOffset En);

/// <summary>Una instancia de un proceso de <c>Api.Workflow</c> (núcleo, lote 7): cuál es, de qué proceso, en qué estado
/// está y cómo llegó ahí.</summary>
/// <param name="Id">La instancia.</param>
/// <param name="Proceso">La clave de la definición que sigue: la de cuando nació, aunque después se publique otra.</param>
/// <param name="Estado">Dónde está ahora.</param>
/// <param name="Historia">Cada transición disparada, en orden.</param>
public sealed record InstanciaDeProceso(string Id, string Proceso, string Estado, IReadOnlyList<PasoDeLaHistoria> Historia);

/// <summary>
/// Arrancar, disparar y leer una instancia de un proceso en <c>Api.Workflow</c> (núcleo, lote 7; la regla de forma de la
/// ADR 0140).
/// </summary>
public interface IWorkflowPort
{
    /// <summary>Arranca una instancia de <paramref name="proceso"/> para <paramref name="sujeto"/>. La misma llave devuelve la misma.</summary>
    Task<Result<InstanciaDeProceso>> ArrancarAsync(string proceso, Ref sujeto, IdempotencyKey llave, CancellationToken ct);

    /// <summary>
    /// Dispara <paramref name="transicion"/> sobre la instancia, actuando <paramref name="actor"/> con los
    /// <paramref name="roles"/> DECLARADOS (009-P6 = A: la guarda es la puerta). La misma llave devuelve la instancia sin
    /// volver a dispararla.
    /// </summary>
    Task<Result<InstanciaDeProceso>> DispararAsync(
        string instancia, string transicion, Ref actor, IReadOnlyList<string> roles, string? nota, IdempotencyKey llave, CancellationToken ct);

    /// <summary>La instancia tal como está.</summary>
    Task<Result<InstanciaDeProceso>> LeerAsync(string instancia, CancellationToken ct);
}

// ── Los puertos de deshacer ─────────────────────────────────────────────────
// Separados de los de avance a propósito: los de avance los cumple también `EventosPorts`, cuyo
// oráculo los construye a mano, y su deshacer sigue yendo por `EventosCompensationExecutor`
// (supuesto P5 del núcleo). Los cumple `PuertosHttp` y los llama `DeshacerComun`: un método con
// quien lo llama, no una promesa.

/// <summary>Deshacer en <c>Api.Inventory</c>.</summary>
public interface IDeshacerExistencias
{
    /// <summary>Suelta un apartado que no se consumió.</summary>
    Task<Result<string>> SoltarAsync(string apartado, CancellationToken ct);

    /// <summary>Devuelve al pozo unidades ya consumidas: un ajuste RELATIVO con llave (#30).</summary>
    Task<Result<string>> ReponerAsync(string pozo, int cantidad, IdempotencyKey llave, CancellationToken ct);
}

/// <summary>Deshacer en <c>Api.Payments</c>.</summary>
public interface IDeshacerCobros
{
    /// <summary>Anula una autorización que no se capturó.</summary>
    Task<Result<string>> AnularAsync(string cobro, CancellationToken ct);

    /// <summary>Lo que todavía se puede devolver de un cobro (cero si ya se devolvió todo).</summary>
    Task<Result<Money>> DevolvibleAsync(string cobro, CancellationToken ct);

    /// <summary>Devuelve <paramref name="monto"/> de un cobro capturado, con llave.</summary>
    Task<Result<string>> DevolverAsync(string cobro, Money monto, string motivo, IdempotencyKey llave, CancellationToken ct);
}

/// <summary>Deshacer en <c>Api.Booking</c>.</summary>
public interface IDeshacerAgenda
{
    /// <summary>Suelta un apartado que no se confirmó.</summary>
    Task<Result<string>> LiberarAsync(string apartado, CancellationToken ct);

    /// <summary>Cancela una reserva ya confirmada.</summary>
    Task<Result<string>> CancelarAsync(string reserva, CancellationToken ct);

    /// <summary>
    /// Cancela una reserva ya confirmada CON llave (núcleo T052): repetir la misma cancelación devuelve lo que hizo la
    /// primera, en vez de <c>booking.already_cancelled</c>.
    /// </summary>
    /// <remarks>Por defecto, sin ella: el puerto propio de una línea que no la manda cancela como siempre.</remarks>
    Task<Result<string>> CancelarAsync(string reserva, IdempotencyKey llave, CancellationToken ct) => CancelarAsync(reserva, ct);
}

/// <summary>Deshacer en <c>Api.Orders</c>.</summary>
public interface IDeshacerPedidos
{
    /// <summary>Anula un pedido que no se cumplió.</summary>
    Task<Result<string>> CancelarAsync(string pedido, CancellationToken ct);
}
