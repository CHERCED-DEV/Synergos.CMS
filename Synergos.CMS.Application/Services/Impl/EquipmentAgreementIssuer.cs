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
///
/// <para><b>Sella el del interruptor y comprueba el que lo selló</b> (research R11 de la línea 005): cada contrato
/// registra <c>SelladoPor</c>, y comprobarlo con otro diría que un contrato bueno no cuadra. Si quien lo selló no está
/// registrado en este despliegue, la respuesta es «no se pudo comprobar», nunca «no cuadra».</para>
/// </remarks>
public sealed class EquipmentAgreementIssuer
{
    private readonly EquipmentAgreementLedger _ledger;
    private readonly ISelladorDeContratos? _sellador;
    private readonly IReadOnlyList<ISelladorDeContratos> _comprobadores;
    private readonly TimeProvider _clock;

    /// <summary>Construye el emisor.</summary>
    /// <param name="ledger">Dónde se anota lo emitido.</param>
    /// <param name="sellador">Quién sella, el del interruptor; null emite sin sello y lo deja visible.</param>
    /// <param name="clock">El reloj, inyectado para que un test no dependa del de pared.</param>
    /// <param name="comprobadores">
    /// Los demás selladores del despliegue, que ya no sellan pero comprueban lo que sellaron: el local con
    /// <c>Mode=Api</c>, y el de <c>Api.Signing</c> si sigue configurado con <c>Mode=Local</c>.
    /// </param>
    public EquipmentAgreementIssuer(
        EquipmentAgreementLedger ledger,
        ISelladorDeContratos? sellador,
        TimeProvider clock,
        IEnumerable<ISelladorDeContratos>? comprobadores = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _sellador = sellador;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _comprobadores = (sellador is null ? [] : new[] { sellador })
            .Concat(comprobadores ?? [])
            .ToList();
    }

    /// <summary>Emite el contrato de un alquiler y lo anota.</summary>
    /// <param name="rental">El alquiler recién reservado.</param>
    /// <param name="equipmentName">Cómo se llama el equipo, para que el papel se lea.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El contrato emitido.</returns>
    public Task<RentalAgreement> IssueAsync(
        Rental rental, string equipmentName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rental);

        return EmitirAsync(new RentalAgreement(
            RentalId: rental.RentalId,
            RenterId: rental.RenterId,
            EquipmentId: rental.EquipmentId,
            EquipmentName: equipmentName,
            Quantity: rental.Quantity,
            Start: rental.Start,
            End: rental.End,
            RentalTotal: rental.Quote.RentalTotal,
            DepositHeld: rental.DepositHeld,
            Currency: rental.Quote.Currency,
            IssuedUtc: _clock.GetUtcNow(),
            Seal: string.Empty), cancellationToken);
    }

    /// <summary>
    /// Sella y anota un contrato ya armado con lo que se pactó: el de la ruta directa, o el de un alquiler de la PUERTA
    /// que arma el artefacto con lo que contestó el orquestador (línea 005, T024).
    /// </summary>
    /// <param name="borrador">El contrato sin sello: su sello, quién lo selló y cuándo se emitió los pone este emisor.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El contrato emitido.</returns>
    public async Task<RentalAgreement> EmitirAsync(RentalAgreement borrador, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(borrador);

        var sello = _sellador is null
            ? new SelloDelContrato(string.Empty, RentalAgreement.SelladoLocal)
            : await _sellador.SellarAsync(Sujeto(borrador), cancellationToken).ConfigureAwait(false);

        var agreement = borrador with
        {
            IssuedUtc = _clock.GetUtcNow(),
            Seal = sello.Valor,
            SelladoPor = sello.SelladoPor,
            SelloPendiente = sello.Pendiente,
        };

        await _ledger.RecordAsync(agreement, cancellationToken).ConfigureAwait(false);
        return agreement;
    }

    /// <summary>
    /// Un contrato tal como se le enseña a alguien: si su sello quedó pendiente se intenta otra vez, y se comprueba.
    /// </summary>
    /// <remarks>
    /// <b>Se reintenta con quien tenía que sellarlo</b> (<c>SelladoPor</c>), nunca con otro: el servicio caído al emitirlo
    /// no autoriza a sellar con la llave local en su lugar. Si sigue sin contestar, el contrato sigue pendiente.
    /// </remarks>
    /// <param name="agreement">El contrato leído del registro.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El contrato (sellado si se pudo) y su comprobación.</returns>
    public async Task<ContratoLeido> LeerAsync(RentalAgreement agreement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agreement);

        if (agreement.SelloPendiente && QuienSello(agreement.SelladoPor) is { } quien)
        {
            var sello = await quien.SellarAsync(Sujeto(agreement), cancellationToken).ConfigureAwait(false);
            if (!sello.Pendiente)
            {
                agreement = await _ledger.AnotarSelloAsync(agreement.RentalId, sello, cancellationToken)
                    .ConfigureAwait(false) ?? agreement;
            }
        }

        if (agreement.Desenlace is { SelloPendiente: true } pendiente
            && QuienSello(pendiente.SelladoPor) is { } delDesenlace)
        {
            var sello = await delDesenlace.SellarAsync(SujetoDelDesenlace(agreement, pendiente), cancellationToken)
                .ConfigureAwait(false);
            if (!sello.Pendiente)
            {
                agreement = await _ledger.AnotarDesenlaceAsync(agreement.RentalId,
                        pendiente with { Sello = sello.Valor, SelladoPor = sello.SelladoPor, SelloPendiente = false },
                        cancellationToken)
                    .ConfigureAwait(false) ?? agreement;
            }
        }

        return new ContratoLeido(
            agreement,
            await ComprobarAsync(agreement, cancellationToken).ConfigureAwait(false),
            agreement.Desenlace is null ? null : await ComprobarDesenlaceAsync(agreement, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Sella el desenlace de la devolución que confirmó el personal y lo anota (lote 3, 005-P6; T025), con su PROPIO
    /// sello: el del contrato no se toca y sigue comprobando.
    /// </summary>
    /// <param name="agreement">El contrato del alquiler devuelto.</param>
    /// <param name="danio">Lo que se cobró de la garantía; cero sin daño.</param>
    /// <param name="liberada">Lo que se liberó; null es «no consta».</param>
    /// <param name="personal">Quién lo confirmó, por referencia.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El contrato con su desenlace sellado (o pendiente, si el servicio de firma no contestó).</returns>
    public async Task<RentalAgreement> SellarDesenlaceAsync(
        RentalAgreement agreement, decimal danio, decimal? liberada, string personal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        ArgumentException.ThrowIfNullOrWhiteSpace(personal);

        var borrador = new DesenlaceDelAlquiler(_clock.GetUtcNow(), danio, liberada, personal, string.Empty);
        var sello = _sellador is null
            ? new SelloDelContrato(string.Empty, RentalAgreement.SelladoLocal)
            : await _sellador.SellarAsync(SujetoDelDesenlace(agreement, borrador), cancellationToken).ConfigureAwait(false);
        var desenlace = borrador with { Sello = sello.Valor, SelladoPor = sello.SelladoPor, SelloPendiente = sello.Pendiente };

        return await _ledger.AnotarDesenlaceAsync(agreement.RentalId, desenlace, cancellationToken).ConfigureAwait(false)
               ?? agreement with { Desenlace = desenlace };
    }

    /// <summary>¿El sello del desenlace corresponde a lo que afirma? Con las mismas tres respuestas que el contrato.</summary>
    /// <param name="agreement">El contrato con su desenlace.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns><c>false</c> sin desenlace o sin sello o si no cuadra; <c>null</c> si no se pudo comprobar.</returns>
    public async Task<bool?> ComprobarDesenlaceAsync(RentalAgreement? agreement, CancellationToken cancellationToken = default)
    {
        if (agreement?.Desenlace is not { } desenlace)
        {
            return false;
        }

        if (desenlace.SelloPendiente)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(desenlace.Sello))
        {
            return false;
        }

        var quien = QuienSello(desenlace.SelladoPor);
        return quien is null
            ? null
            : await quien.ComprobarAsync(desenlace.Sello, SujetoDelDesenlace(agreement, desenlace), cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>¿El sello de este contrato corresponde a lo que afirma?</summary>
    /// <param name="agreement">El contrato que alguien presenta.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>
    /// <c>false</c> sin sello o si no cuadra; <c>null</c> si está pendiente o si quien lo selló no está para
    /// comprobarlo.
    /// </returns>
    public async Task<bool?> ComprobarAsync(RentalAgreement? agreement, CancellationToken cancellationToken = default)
    {
        if (agreement is null)
        {
            return false;
        }

        if (agreement.SelloPendiente)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(agreement.Seal))
        {
            // Sin sello NO se da por bueno: comprobar es lo único que impide que quien escriba en el almacén
            // fabrique un comprobante con la cifra que quiera (#45).
            return false;
        }

        var quien = QuienSello(agreement.SelladoPor);
        return quien is null
            ? null
            : await quien.ComprobarAsync(agreement.Seal, Sujeto(agreement), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>El sellador que registra el contrato: vacío es el local, el de todos los anteriores al interruptor.</summary>
    private ISelladorDeContratos? QuienSello(string? selladoPor)
    {
        var nombre = string.IsNullOrWhiteSpace(selladoPor) ? RentalAgreement.SelladoLocal : selladoPor.Trim();
        var dosPuntos = nombre.IndexOf(':', StringComparison.Ordinal);
        if (dosPuntos >= 0)
        {
            nombre = nombre[..dosPuntos];
        }

        return _comprobadores.FirstOrDefault(s => string.Equals(s.Nombre, nombre, StringComparison.Ordinal));
    }

    private static AgreementSubject Sujeto(RentalAgreement a)
        => new(a.RentalId, a.RenterId, a.EquipmentId, a.DepositHeld, a.Currency);

    private static AgreementOutcomeSubject SujetoDelDesenlace(RentalAgreement a, DesenlaceDelAlquiler d)
        => new(a.RentalId, a.RenterId, a.EquipmentId, d.Danio, d.GarantiaLiberada, a.Currency, d.Personal, d.ConfirmadoUtc);
}

/// <summary>Un contrato leído para enseñarlo, con su sello comprobado por quien lo selló.</summary>
/// <param name="Contrato">El contrato, sellado si el reintento pudo.</param>
/// <param name="Comprobado"><c>true</c> cuadra, <c>false</c> no cuadra o no tiene sello, <c>null</c> no se pudo comprobar.</param>
/// <param name="DesenlaceComprobado">Lo mismo para el sello del desenlace; null también si no hay desenlace.</param>
public sealed record ContratoLeido(RentalAgreement Contrato, bool? Comprobado, bool? DesenlaceComprobado = null);
