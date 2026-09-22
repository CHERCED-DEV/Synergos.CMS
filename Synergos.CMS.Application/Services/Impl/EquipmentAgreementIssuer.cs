using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El ÚNICO sitio que nombra un contrato de alquiler y arma su sello.
/// </summary>
/// <remarks>
/// <para>Vive FUERA del seam del eje 2 y lo comparten sus dos implementaciones — la invariante
/// del #153. Con el motor en proceso o con el orquestador detrás, el comprobante se emite aquí y
/// se anota en el mismo registro, así que la puerta lee un solo almacén pase lo que pase. Es
/// exactamente lo que <c>EventTicketIssuer</c> hizo para Eventos, y el cableado de aquél destapó
/// que sin ese corte la cara de organizador quedaba leyendo un almacén vacío.</para>
///
/// <para><b>Si el sellador no está disponible, el contrato se emite SIN sello y lo dice.</b> No se
/// inventa uno: un sello fabricado es peor que ninguno, porque parece prueba
/// (<c>gethashcode_is_not_a_seed</c>). Un contrato sin sello sigue sirviendo para saber qué se
/// llevó quién — lo que no sirve es para discutirlo con un tercero, y eso se ve.</para>
/// </remarks>
public sealed class EquipmentAgreementIssuer
{
    private readonly EquipmentAgreementLedger _ledger;
    private readonly IAgreementSigner? _signer;
    private readonly TimeProvider _clock;

    /// <summary>Construye el emisor.</summary>
    /// <param name="ledger">Dónde se anota lo emitido.</param>
    /// <param name="signer">Quién sella; null emite sin sello y lo deja visible.</param>
    /// <param name="clock">El reloj, inyectado para que un test no dependa del de pared.</param>
    public EquipmentAgreementIssuer(
        EquipmentAgreementLedger ledger, IAgreementSigner? signer, TimeProvider clock)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _signer = signer;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Emite el contrato de un alquiler y lo anota.</summary>
    /// <param name="rental">El alquiler recién reservado.</param>
    /// <param name="equipmentName">Cómo se llama el equipo, para que el papel se lea.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El contrato emitido.</returns>
    public async Task<RentalAgreement> IssueAsync(
        Rental rental, string equipmentName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rental);

        var subject = new AgreementSubject(
            rental.RentalId, rental.RenterId, rental.EquipmentId, rental.DepositHeld);

        var agreement = new RentalAgreement(
            RentalId: rental.RentalId,
            RenterId: rental.RenterId,
            EquipmentId: rental.EquipmentId,
            EquipmentName: equipmentName,
            Quantity: rental.Quantity,
            Start: rental.Start,
            End: rental.End,
            RentalTotal: rental.Quote.RentalTotal,
            DepositHeld: rental.DepositHeld,
            IssuedUtc: _clock.GetUtcNow(),
            Seal: _signer?.Seal(subject) ?? string.Empty);

        await _ledger.RecordAsync(agreement, cancellationToken).ConfigureAwait(false);
        return agreement;
    }

    /// <summary>¿El sello de este contrato corresponde a lo que afirma?</summary>
    /// <param name="agreement">El contrato que alguien presenta.</param>
    /// <returns><c>false</c> si no hay sellador o si el sello no cuadra.</returns>
    public bool Verify(RentalAgreement? agreement)
    {
        if (agreement is null || _signer is null || string.IsNullOrWhiteSpace(agreement.Seal))
        {
            // Sin sellador NO se da por bueno: comprobar es lo único que impide que quien
            // escriba en el almacén fabrique un comprobante con la cifra que quiera (#45).
            return false;
        }

        return _signer.Matches(agreement.Seal, new AgreementSubject(
            agreement.RentalId, agreement.RenterId, agreement.EquipmentId, agreement.DepositHeld));
    }
}
