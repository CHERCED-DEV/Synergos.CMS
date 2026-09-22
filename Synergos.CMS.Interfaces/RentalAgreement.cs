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
/// <param name="IssuedUtc">Cuándo se emitió.</param>
/// <param name="Seal">El sello, o vacío si el sellador no estaba disponible.</param>
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
    DateTimeOffset IssuedUtc,
    string Seal);
