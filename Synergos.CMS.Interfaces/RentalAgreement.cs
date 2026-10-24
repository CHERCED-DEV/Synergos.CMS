namespace Synergos.CMS.Interfaces;

/// <summary>
/// El contrato de un alquiler: el ARTEFACTO del vertical (eje 3 del doc 12).
/// </summary>
/// <remarks>
/// <para><b>Vive FUERA del seam de la transacción y lo comparten sus dos implementaciones</b>
/// (doc 12 §3.2, #153): un emisor metido dentro del motor en proceso no lo podría usar el
/// cliente cableado, y copiarlo sería «un comprobante con dos definiciones».</para>
///
/// <para><b>Guarda la garantía retenida al emitirlo y NO la relee</b>: es un comprobante, o sea
/// una foto de lo que se acordó. Que la cifra viva del alquiler cambie después es precisamente
/// lo que alguien puede querer discutir.</para>
/// </remarks>
/// <param name="RentalId">A qué alquiler corresponde.</param>
/// <param name="RenterId">El seudónimo de quien firmó.</param>
/// <param name="EquipmentId">Qué equipo salió.</param>
/// <param name="EquipmentName">Cómo se llama, para que el papel se lea.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Start">Desde cuándo.</param>
/// <param name="End">Hasta cuándo.</param>
/// <param name="RentalTotal">Lo que se cobró por el alquiler.</param>
/// <param name="DepositHeld">Lo que se retuvo. Es la cifra que alguien va a discutir.</param>
/// <param name="Currency">La moneda de los importes (ISO 4217). Va dentro de lo sellado: una cifra sin
/// moneda no se puede discutir.</param>
/// <param name="IssuedUtc">Cuándo se emitió.</param>
/// <param name="Seal">El sello, o vacío si el sellador no estaba disponible.</param>
/// <param name="Camino">
/// Por dónde salió el alquiler (<see cref="CaminoDeCompra"/>): lo que pasa después —declarar, devolver,
/// cancelar— va al motor por el que salió, no al que el sitio declare hoy. Los contratos guardados antes
/// de la puerta no lo traen y son de la ruta directa, que era la única.
/// </param>
/// <param name="SelladoPor">
/// Quién lo selló: <c>local</c> (la llave del CMS) o <c>api:{llave}</c> (<c>Api.Signing</c>). Un contrato se
/// comprueba siempre con quien lo selló; los de antes del interruptor los selló la llave local.
/// </param>
/// <param name="SelloPendiente">
/// El servicio de firma no contestó al emitirlo: se emitió sin sello y se sella al volver. Nunca se sella
/// con la llave local en su lugar.
/// </param>
/// <param name="Estado">
/// En qué punto está el alquiler, tal como lo supo este CMS en su última operación: el registro es el modelo
/// de LECTURA del vertical y la bandeja lo pinta sin preguntarle a nadie.
/// </param>
/// <param name="Desenlace">Cómo se cerró la devolución, con su propio sello; null mientras no se cierre.</param>
/// <param name="DeclaradaUtc">
/// Cuándo declaró la devolución quien alquiló, tal como lo anotó este CMS: lo que la cara del personal mide como «lleva
/// esperando». Null mientras no se declare, y en lo declarado antes de anotarlo.
/// </param>
public sealed record RentalAgreement(
    string RentalId,
    string RenterId,
    string EquipmentId,
    string EquipmentName,
    int Quantity,
    DateOnly Start,
    DateOnly End,
    decimal RentalTotal,
    decimal DepositHeld,
    string Currency,
    DateTimeOffset IssuedUtc,
    string Seal,
    string Camino = CaminoDeCompra.Directa,
    string SelladoPor = RentalAgreement.SelladoLocal,
    bool SelloPendiente = false,
    RentalState Estado = RentalState.Reserved,
    DesenlaceDelAlquiler? Desenlace = null,
    DateTimeOffset? DeclaradaUtc = null)
{
    /// <summary>El sellador de la llave local del CMS.</summary>
    public const string SelladoLocal = "local";
}

/// <summary>
/// Cómo se cerró la devolución de un alquiler: lo que confirmó el personal (decisión 5 del lote 1).
/// </summary>
/// <remarks>
/// Lleva su PROPIO sello y no se mete en el del contrato: el sello del contrato afirma lo que se acordó al
/// salir, y cambiarlo dejaría sin comprobar todo contrato ya emitido (lote 3, 005-P6).
/// </remarks>
/// <param name="ConfirmadoUtc">Cuándo lo confirmó el personal.</param>
/// <param name="Danio">Cuánto se cobró de la garantía; cero sin daño.</param>
/// <param name="GarantiaLiberada">Cuánto se liberó; null es «no consta», nunca cero.</param>
/// <param name="Personal">Quién lo confirmó, por referencia: nunca su nombre ni su correo.</param>
/// <param name="Sello">El sello del desenlace, o vacío si quedó pendiente.</param>
/// <param name="SelladoPor">Quién lo selló.</param>
/// <param name="SelloPendiente">Si el servicio de firma no contestó al sellarlo.</param>
public sealed record DesenlaceDelAlquiler(
    DateTimeOffset ConfirmadoUtc,
    decimal Danio,
    decimal? GarantiaLiberada,
    string Personal,
    string Sello,
    string SelladoPor = RentalAgreement.SelladoLocal,
    bool SelloPendiente = false);
