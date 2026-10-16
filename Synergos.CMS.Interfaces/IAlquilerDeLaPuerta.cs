namespace Synergos.CMS.Interfaces;

/// <summary>
/// Lo que pasa después del cierre con un alquiler que salió por la PUERTA (ADR 0140; línea 005, T018): declarar la
/// devolución y confirmarla con el daño, en el orquestador por el que salió.
/// </summary>
/// <remarks>
/// <para><b>Es la otra mitad del <see cref="RentalAgreement.Camino"/></b>: un alquiler de la puerta no existe en el
/// motor de <see cref="IEquipmentRentalService"/> —vive en la saga genérica del orquestador—, así que lo que pasa
/// después va adonde salió y no adonde el sitio declare hoy.</para>
///
/// <para><b>Fuera de la puerta</b> (lote 3, 005-P9) hasta que el núcleo declare las operaciones posteriores al cierre
/// (T051): las rutas son del orquestador, sin marca de la puerta, y las pide el CMS con su llave compartida.</para>
/// </remarks>
public interface IAlquilerDeLaPuerta
{
    /// <summary>El inquilino declara que devolvió el equipo. No mueve plata.</summary>
    /// <param name="agreement">El contrato del alquiler: de ahí salen su id y su dueño.</param>
    /// <param name="idempotencyKey">La llave de la operación.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El alquiler declarado, o el rechazo. Null si el orquestador no lo conoce.</returns>
    Task<RentalResult?> DeclareReturnAsync(
        RentalAgreement agreement, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>El personal confirma la devolución con el daño: libera la garantía o cobra de ella.</summary>
    /// <param name="agreement">El contrato del alquiler.</param>
    /// <param name="damageAmount">Lo que se cobra de la garantía; cero la libera entera.</param>
    /// <param name="staffId">El seudónimo de quien confirma.</param>
    /// <param name="idempotencyKey">La llave de la operación.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El alquiler devuelto, o el rechazo. Null si el orquestador no lo conoce.</returns>
    Task<RentalResult?> ConfirmReturnAsync(
        RentalAgreement agreement, decimal damageAmount, string staffId, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
