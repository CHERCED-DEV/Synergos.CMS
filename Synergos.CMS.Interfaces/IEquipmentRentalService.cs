namespace Synergos.CMS.Interfaces;

/// <summary>Lo que se pide al alquilar: qué equipo, cuántos, desde cuándo y para quién.</summary>
/// <param name="EquipmentId">El slug del equipo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Start">Primer día del alquiler, inclusive.</param>
/// <param name="End">Día de devolución, exclusive: alquilar del 1 al 2 es UN día.</param>
/// <param name="RenterId">
/// Quién alquila, como SEUDÓNIMO — nunca su correo. Es lo que viaja al orquestador y lo que
/// queda escrito en su disco (#47).
/// </param>
public sealed record RentalRequest(
    string EquipmentId, int Quantity, DateOnly Start, DateOnly End, string RenterId);

/// <summary>Cuánto cuesta un alquiler, desglosado como lo tiene que leer quien paga.</summary>
/// <remarks>
/// <b><see cref="Deposit"/> va aparte de <see cref="RentalTotal"/> y no se suma a la factura</b>:
/// es lo que se RETIENE, no lo que se cobra. Fundirlos en un total sería decirle a alguien que
/// pagó una cifra que nunca se le va a cobrar.
/// </remarks>
/// <param name="EquipmentId">El equipo cotizado.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Days">Cuántos días cubre.</param>
/// <param name="PerDay">El valor del día que aplicó, ya resuelto contra los tramos.</param>
/// <param name="RentalTotal">Lo que se cobra: <c>PerDay × Days × Quantity</c>.</param>
/// <param name="Deposit">Lo que se retiene y se devuelve: <c>garantía × Quantity</c>.</param>
public sealed record RentalQuote(
    string EquipmentId, int Quantity, int Days, decimal PerDay, decimal RentalTotal, decimal Deposit);

/// <summary>En qué estado está un alquiler.</summary>
/// <remarks>
/// <b>NO hay un estado «entregado», y es deliberado.</b> Sería un valor del vocabulario al que
/// ningún camino del código llega —este vertical no tiene operación de entrega— y eso es el
/// espejo de <c>no_read_without_a_write_path</c>: un estado sin camino de entrada sólo se puede
/// probar fabricándolo con un doble, que prueba el doble y no la regla
/// (<c>a_state_with_no_writer_cannot_be_tested_through_the_seam</c>).
/// <para><b>El disparador para añadirlo</b>: el día que exista una operación que registre que el
/// equipo salió del almacén. Ahí «devolver» deja de ser legal desde <c>Reserved</c>.</para>
/// </remarks>
public enum RentalState
{
    /// <summary>Ventana apartada y garantía autorizada.</summary>
    Reserved,

    /// <summary>Volvió y la garantía se resolvió.</summary>
    Returned,

    /// <summary>No llegó a salir.</summary>
    Cancelled,
}

/// <summary>Un alquiler tal como este árbol lo conoce.</summary>
/// <param name="RentalId">El identificador que emitió quien lleva la transacción.</param>
/// <param name="EquipmentId">Qué equipo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Start">Primer día, inclusive.</param>
/// <param name="End">Día de devolución, exclusive.</param>
/// <param name="RenterId">El seudónimo de quien alquila.</param>
/// <param name="State">En qué punto está.</param>
/// <param name="Quote">Lo cotizado, congelado al reservar.</param>
/// <param name="DepositHeld">Cuánto sigue retenido. Cero una vez resuelto.</param>
/// <param name="DamageCharged">Cuánto de la garantía se cobró al devolver.</param>
public sealed record Rental(
    string RentalId,
    string EquipmentId,
    int Quantity,
    DateOnly Start,
    DateOnly End,
    string RenterId,
    RentalState State,
    RentalQuote Quote,
    decimal DepositHeld,
    decimal DamageCharged);

/// <summary>Cómo terminó una operación sobre un alquiler.</summary>
public enum RentalOutcome
{
    /// <summary>Se hizo.</summary>
    Ok,

    /// <summary>La misma llave ya lo había hecho; se devuelve lo de antes.</summary>
    AlreadyDone,

    /// <summary>La regla del negocio dijo que no.</summary>
    Rejected,

    /// <summary>
    /// No se pudo AHORA. Se distingue de <see cref="Rejected"/> a propósito: quien llama puede
    /// volver a intentarlo, y confundir las dos es lo que hace que alguien deshaga algo sano
    /// (`a_lock_moves_out_of_the_process_it_does_not_get_replaced`).
    /// </summary>
    Unavailable,
}

/// <summary>El resultado de una operación, con su alquiler si lo hubo.</summary>
/// <param name="Outcome">Cómo terminó.</param>
/// <param name="Rental">El alquiler resultante, o null si no lo hubo.</param>
/// <param name="RejectionCode">El código del rechazo, o null. Es contrato: alguien lo compara.</param>
/// <param name="Reason">Qué decirle a quien está delante.</param>
public sealed record RentalResult(
    RentalOutcome Outcome, Rental? Rental, string? RejectionCode, string? Reason);

/// <summary>
/// La transacción de Alquiler: apartar la ventana, retener la garantía, devolver y cancelar.
/// </summary>
/// <remarks>
/// <para><b>Esta costura CRUZA al otro árbol</b> —es el eje 2— y lo que hay detrás lo decide
/// <c>Synergos:Alquiler:Mode</c>: el motor en proceso por defecto, o <c>Synergos.Bff.Alquiler</c>.
/// Nace `async` con su `CancellationToken` aunque la implementación por defecto no los necesite,
/// porque una costura escrita contra implementaciones en proceso no sobrevive a la primera que
/// habla por la red (`a_seam_widens_when_it_meets_the_network`).</para>
///
/// <para><b>NO hay operación de LECTURA, y es una decisión.</b> Quién alquiló qué lo guarda
/// <c>EquipmentAgreementLedger</c> de este lado, así que con el orquestador caído quien alquiló
/// sigue viendo su contrato y su garantía — lo que se para es reservar, devolver y cancelar. Es
/// la forma del timeline de pedidos (#46) y de las bandejas de Gobierno (#62), y es además lo
/// que exige la invariante del eje 3: <b>el REGISTRO nunca sale a la red</b> (doc 12 §3.2).</para>
///
/// <para><b>El monto del daño y el de la penalidad llegan CALCULADOS</b>, como la penalidad de
/// cancelación de Viajes y la devolución parcial de Tienda: quien lleva la transacción cotiza el
/// alquiler entero y no sabe cuánto vale un rayón.</para>
/// </remarks>
public interface IEquipmentRentalService
{
    /// <summary>Cuánto costaría, sin comprometer nada.</summary>
    /// <param name="request">Qué, cuántos y cuándo.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>La cotización, o null si el equipo no existe.</returns>
    Task<RentalQuote?> QuoteAsync(RentalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Aparta la ventana y retiene la garantía.</summary>
    /// <param name="request">Qué, cuántos, cuándo y quién.</param>
    /// <param name="idempotencyKey">
    /// Obligatoria: reservar mueve plata, y un reintento sin llave retiene la garantía dos veces.
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El alquiler, o el rechazo con su código.</returns>
    Task<RentalResult> ReserveAsync(
        RentalRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// El equipo volvió: libera la garantía, o cobra de ella lo que el daño valga.
    /// </summary>
    /// <remarks>
    /// <b>NO es una compensación</b>, y confundirlo sería el error que `Bff.Core` ya rechaza con
    /// todas las letras para un viaje confirmado: deshacer un alquiler cumplido no existe — lo
    /// que existe es devolverlo, con su política. Por eso vive acá y no en el compensador.
    /// </remarks>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="damageAmount">Cuánto cobrar de la garantía; 0 la libera entera.</param>
    /// <param name="idempotencyKey">Obligatoria: cobrar un daño es un movimiento relativo.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El alquiler cerrado, o el rechazo. Null si el alquiler no existe.</returns>
    Task<RentalResult?> ReturnAsync(
        string rentalId, decimal damageAmount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Cancela antes de que el equipo salga: suelta la ventana y anula la garantía.</summary>
    /// <param name="rentalId">Cuál alquiler.</param>
    /// <param name="penaltyAmount">Cuánto retener por cancelar; 0 no retiene nada.</param>
    /// <param name="idempotencyKey">Obligatoria, por lo mismo que en la devolución.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El alquiler cancelado, o el rechazo. Null si el alquiler no existe.</returns>
    Task<RentalResult?> CancelAsync(
        string rentalId, decimal penaltyAmount, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
