using System.Globalization;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El motor de alquiler EN PROCESO: aparta, retiene la garantía, devuelve y cancela sin salir
/// de esta máquina.
/// </summary>
/// <remarks>
/// <para>Es el default de <c>Synergos:Alquiler:Mode</c>, para que el repo se levante entero sin
/// ningún servicio. Con <c>= Bff</c> lo reemplaza <c>HttpEquipmentRentalService</c>, que le pide
/// lo mismo a <c>Synergos.Bff.Alquiler</c>.</para>
///
/// <para><b>La garantía se AUTORIZA para no cobrarse, y ése es el estado que ningún otro
/// vertical de este repo tiene.</b> Los cuatro flujos existentes autorizan para capturar; acá el
/// desenlace normal es <i>anular</i>. Por eso <see cref="Rental.DepositHeld"/> es un campo y no
/// un derivado: mientras vale más que cero hay plata de alguien retenida, y eso tiene que poder
/// leerse sin preguntarle a nadie.</para>
///
/// <para><b>Durable desde el primer día.</b> Un alquiler dura días; un almacén en memoria lo
/// perdería en el primer reinicio y con él la única constancia de cuánto se le retuvo a quién.</para>
///
/// <para><b>La llave de idempotencia se resuelve ANTES que cualquier regla de estado</b>
/// (§0.B.16): al revés, un reintento choca con lo que él mismo creó.</para>
/// </remarks>
public sealed class StubEquipmentRentalService : IEquipmentRentalService, IDisposable
{
    internal const string RentalResourceType = "alquiler-rentals";
    internal const string IdempotencyResourceType = "alquiler-idempotency";

    /// <summary>Prefijo de los códigos de rechazo. Es contrato: alguien lo compara.</summary>
    public const string CodePrefix = "alquiler";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly IEquipmentCatalogProvider _catalog;
    private readonly IJsonEntityStore _store;
    private readonly TimeProvider _clock;
    private readonly int _maxRentalDays;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Construye el motor.</summary>
    /// <param name="catalog">De dónde salen los equipos y sus tarifas.</param>
    /// <param name="store">Dónde se guardan los alquileres, durable.</param>
    /// <param name="clock">El reloj; inyectado para que un test no dependa del de pared.</param>
    /// <param name="maxRentalDays">El tope del despliegue, de <c>AlquilerSettings</c>.</param>
    public StubEquipmentRentalService(
        IEquipmentCatalogProvider catalog,
        IJsonEntityStore store,
        TimeProvider clock,
        int maxRentalDays)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _maxRentalDays = maxRentalDays;
    }

    /// <inheritdoc />
    public async Task<RentalQuote?> QuoteAsync(
        RentalRequest request, CancellationToken cancellationToken = default)
    {
        var equipo = await _catalog.GetAsync(request.EquipmentId, cancellationToken).ConfigureAwait(false);
        if (equipo is null)
        {
            return null;
        }

        var dias = Dias(request);
        return dias <= 0 ? null : Cotizar(equipo, request.Quantity, dias);
    }

    /// <inheritdoc />
    public async Task<RentalResult> ReserveAsync(
        RentalRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Rechazo("idempotency_key_required",
                "Reservar retiene plata: sin llave, un reintento la retiene dos veces.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // La llave ANTES que el estado (§0.B.16).
            var yaHecho = await LeerPorLlaveAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (yaHecho is not null)
            {
                return new RentalResult(RentalOutcome.AlreadyDone, yaHecho, null, null);
            }

            if (string.IsNullOrWhiteSpace(request.EquipmentId))
            {
                return Rechazo("equipment_required", "No se dijo qué equipo alquilar.");
            }

            var equipo = await _catalog.GetAsync(request.EquipmentId, cancellationToken).ConfigureAwait(false);
            if (equipo is null)
            {
                return Rechazo("equipment_not_found", $"No existe el equipo '{request.EquipmentId}'.");
            }

            var dias = Dias(request);
            if (dias <= 0)
            {
                return Rechazo("bad_window",
                    "La devolución tiene que ser posterior al retiro: del 1 al 2 es un día.");
            }

            if (_maxRentalDays > 0 && dias > _maxRentalDays)
            {
                // Distinto de window_out_of_bounds a propósito: el remedio no es elegir otras
                // fechas, es que este despliegue no puede retener una garantía tanto tiempo.
                return Rechazo("window_too_long",
                    $"Este despliegue no alquila más de {_maxRentalDays} días: una autorización "
                    + "de garantía no dura más, y al devolver no quedaría nada que liberar.");
            }

            if (dias < equipo.MinDays || dias > equipo.MaxDays)
            {
                return Rechazo("window_out_of_bounds",
                    $"'{equipo.Name}' se alquila entre {equipo.MinDays} y {equipo.MaxDays} días.");
            }

            if (request.Quantity < 1)
            {
                return Rechazo("bad_quantity", "Hay que alquilar al menos una unidad.");
            }

            var libres = await LibresAsync(equipo, request, cancellationToken).ConfigureAwait(false);
            if (request.Quantity > libres)
            {
                return libres <= 0
                    ? Rechazo("no_units", $"No quedan unidades de '{equipo.Name}' en esas fechas.")
                    : Rechazo("bad_quantity",
                        $"Quedan {libres} unidades de '{equipo.Name}' en esas fechas.");
            }

            var quote = Cotizar(equipo, request.Quantity, dias);
            var alquiler = new Rental(
                RentalId: NuevoId(),
                EquipmentId: equipo.Id,
                Quantity: request.Quantity,
                Start: request.Start,
                End: request.End,
                RenterId: request.RenterId,
                State: RentalState.Reserved,
                Quote: quote,
                DepositHeld: quote.Deposit,
                DamageCharged: 0m);

            await GuardarAsync(alquiler, cancellationToken).ConfigureAwait(false);
            await _store.WriteAsync(IdempotencyResourceType, Llave(idempotencyKey),
                JsonSerializer.Serialize(alquiler.RentalId, Json), cancellationToken).ConfigureAwait(false);

            return new RentalResult(RentalOutcome.Ok, alquiler, null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<RentalResult?> ReturnAsync(
        string rentalId, decimal damageAmount, string idempotencyKey,
        CancellationToken cancellationToken = default)
        => await CerrarAsync(rentalId, damageAmount, idempotencyKey, devolucion: true, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<RentalResult?> CancelAsync(
        string rentalId, decimal penaltyAmount, string idempotencyKey,
        CancellationToken cancellationToken = default)
        => await CerrarAsync(rentalId, penaltyAmount, idempotencyKey, devolucion: false, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Devolver y cancelar son el MISMO movimiento sobre la garantía: se cobra una parte y se
    /// libera el resto. Lo que cambia es el estado destino y cuándo es legal.
    /// </summary>
    private async Task<RentalResult?> CerrarAsync(
        string rentalId, decimal monto, string idempotencyKey, bool devolucion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Rechazo("idempotency_key_required",
                "Cobrar de la garantía es un movimiento relativo: sin llave, un reintento cobra dos veces.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var yaHecho = await LeerPorLlaveAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (yaHecho is not null)
            {
                return new RentalResult(RentalOutcome.AlreadyDone, yaHecho, null, null);
            }

            var alquiler = await LeerAsync(rentalId, cancellationToken).ConfigureAwait(false);
            if (alquiler is null)
            {
                return null;
            }

            var legal = alquiler.State is RentalState.Reserved;

            if (!legal)
            {
                return devolucion
                    ? Rechazo("not_returnable",
                        $"El alquiler {rentalId} está en {alquiler.State} y ya no admite devolución.")
                    : Rechazo("not_cancellable",
                        $"El alquiler {rentalId} está en {alquiler.State} y ya no se puede cancelar.");
            }

            if (monto < 0m)
            {
                return Rechazo("bad_amount", "El monto a cobrar de la garantía no puede ser negativo.");
            }

            if (monto > alquiler.DepositHeld)
            {
                // No se captura de más ni se manda a cobrar aparte: eso sería inventar una deuda
                // que nadie autorizó. Quien reciba el equipo decide qué hacer con el exceso.
                return Rechazo("damage_exceeds_deposit",
                    $"El daño ({monto:0.##}) supera la garantía retenida ({alquiler.DepositHeld:0.##}).");
            }

            var cerrado = alquiler with
            {
                State = devolucion ? RentalState.Returned : RentalState.Cancelled,
                DepositHeld = 0m,
                DamageCharged = monto,
            };

            await GuardarAsync(cerrado, cancellationToken).ConfigureAwait(false);
            await _store.WriteAsync(IdempotencyResourceType, Llave(idempotencyKey),
                JsonSerializer.Serialize(cerrado.RentalId, Json), cancellationToken).ConfigureAwait(false);

            return new RentalResult(RentalOutcome.Ok, cerrado, null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// El valor del día que aplica para una duración: el tramo más alto que la cubre, o la
    /// tarifa base.
    /// </summary>
    /// <remarks>
    /// Los tramos llegan ya ordenados y sin repetidos de <c>EquipmentContentRules</c>, así que
    /// acá no hay desempate que tomar — que es justo lo que el #131 pide evitar.
    /// </remarks>
    internal static decimal ValorDelDia(RentalEquipment equipo, int dias)
    {
        var tramo = equipo.Rates
            .Where(r => r.MinDays <= dias)
            .OrderByDescending(r => r.MinDays)
            .FirstOrDefault();

        return tramo?.PerDay ?? equipo.DailyRate;
    }

    private static RentalQuote Cotizar(RentalEquipment equipo, int cantidad, int dias)
    {
        var porDia = ValorDelDia(equipo, dias);
        return new RentalQuote(
            EquipmentId: equipo.Id,
            Quantity: cantidad,
            Days: dias,
            PerDay: porDia,
            RentalTotal: porDia * dias * cantidad,
            Deposit: equipo.Deposit * cantidad);
    }

    private static int Dias(RentalRequest request)
        => request.End.DayNumber - request.Start.DayNumber;

    /// <summary>Cuántas unidades quedan libres en la ventana pedida.</summary>
    /// <remarks>
    /// Se cuentan los alquileres VIVOS que se solapan. Un alquiler devuelto o cancelado no
    /// ocupa: encontrar un registro no significa «esto está tomado» — la lección del #41.
    /// </remarks>
    private async Task<int> LibresAsync(
        RentalEquipment equipo, RentalRequest request, CancellationToken cancellationToken)
    {
        var vivos = 0;
        foreach (var json in await _store.ListAsync(RentalResourceType, cancellationToken).ConfigureAwait(false))
        {
            var r = Deserializar(json);
            if (r is null
                || !string.Equals(r.EquipmentId, equipo.Id, StringComparison.Ordinal)
                || r.State is RentalState.Returned or RentalState.Cancelled)
            {
                continue;
            }

            // Dos ventanas se solapan si cada una empieza antes de que termine la otra.
            if (r.Start < request.End && request.Start < r.End)
            {
                vivos += r.Quantity;
            }
        }

        return equipo.Units - vivos;
    }

    private async Task<Rental?> LeerAsync(string rentalId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rentalId))
        {
            return null;
        }

        var json = await _store.ReadAsync(RentalResourceType, rentalId, cancellationToken).ConfigureAwait(false);
        return json is null ? null : Deserializar(json);
    }

    private async Task<Rental?> LeerPorLlaveAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(IdempotencyResourceType, Llave(idempotencyKey), cancellationToken)
            .ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        var rentalId = JsonSerializer.Deserialize<string>(json, Json);
        return rentalId is null ? null : await LeerAsync(rentalId, cancellationToken).ConfigureAwait(false);
    }

    private Task GuardarAsync(Rental alquiler, CancellationToken cancellationToken)
        => _store.WriteAsync(RentalResourceType, alquiler.RentalId,
            JsonSerializer.Serialize(alquiler, Json), cancellationToken);

    private static Rental? Deserializar(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Rental>(json, Json);
        }
        catch (JsonException)
        {
            // Un documento ilegible no tumba el catálogo entero; se omite y se sigue.
            return null;
        }
    }

    private static string Llave(string idempotencyKey)
        => idempotencyKey.Trim().ToLowerInvariant();

    private string NuevoId()
        => "ALQ-" + _clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..6];

    private static RentalResult Rechazo(string code, string reason)
        => new(RentalOutcome.Rejected, null, $"{CodePrefix}.{code}", reason);

    /// <summary>Suelta el semáforo del turno de escritura.</summary>
    public void Dispose() => _gate.Dispose();
}
