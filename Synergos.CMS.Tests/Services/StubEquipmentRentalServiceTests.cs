using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El motor de alquiler en proceso (#147, S3): tarifas por tramo, garantía retenida,
/// idempotencia y disponibilidad por ventana.
/// </summary>
/// <remarks>
/// <para><b>Los fixtures llevan el caso que la regla RESUELVE.</b> Los tramos del equipo de
/// prueba tienen valores por día DISTINTOS entre sí y distintos de la tarifa base: con todos
/// iguales, resolver el tramo o no da el mismo número y el defecto pasaría en verde.</para>
///
/// <para><b>Lo que NO se puede probar por el seam, y queda nombrado:</b> no hay estado
/// «entregado» porque este vertical no tiene operación de entrega, así que la rama «devolver algo
/// que salió» no existe todavía. Fabricarla con un doble probaría el doble y no la regla
/// (<c>a_state_with_no_writer_cannot_be_tested_through_the_seam</c>). El disparador está escrito
/// en <see cref="RentalState"/>.</para>
/// </remarks>
public sealed class StubEquipmentRentalServiceTests
{
    private sealed class AlmacenEnMemoria : IJsonEntityStore
    {
        private readonly Dictionary<string, string> _data = new(StringComparer.Ordinal);

        public Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default)
        {
            _data[resourceType + "/" + key] = json;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_data.TryGetValue(resourceType + "/" + key, out var json) ? json : null);

        // DOCUMENTOS, no claves: es lo que devuelve FileSystemJsonEntityStore.ListAsync, que lee
        // el contenido de cada fichero. Un doble que devolviera las claves dejaría al motor
        // contando cero unidades ocupadas y la sobreventa pasaría en verde.
        public Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(_data
                .Where(kv => kv.Key.StartsWith(resourceType + "/", StringComparison.Ordinal))
                .Select(kv => kv.Value)
                .ToList());

        public Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_data.Remove(resourceType + "/" + key));
    }

    private sealed class CatalogoFalso : IEquipmentCatalogProvider
    {
        private readonly RentalEquipment _equipo;

        public CatalogoFalso(RentalEquipment equipo) => _equipo = equipo;

        public Task<IReadOnlyList<RentalEquipment>> ListAsync(string? category = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RentalEquipment>>(new[] { _equipo });

        public Task<RentalEquipment?> GetAsync(string equipmentId, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Equals(equipmentId, _equipo.Id, StringComparison.Ordinal) ? _equipo : null);
    }

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    }

    // Tarifa base 40 000; a 7 días 30 000; a 28 días 24 000. Los tres distintos a propósito.
    private static RentalEquipment Equipo(int units = 2, int minDays = 1, int maxDays = 30)
        => new(
            Id: "andamio", Name: "Andamio", Category: "Andamios", Summary: "", Description: "",
            CoverUrl: null, GalleryUrls: Array.Empty<string>(),
            Units: units, DailyRate: 40_000m, Deposit: 250_000m, MinDays: minDays, MaxDays: maxDays,
            Includes: Array.Empty<string>(), Requirements: Array.Empty<string>(),
            Rates: new[]
            {
                new EquipmentRate("semana", "Semana", 7, 30_000m, ""),
                new EquipmentRate("mes", "Mes", 28, 24_000m, ""),
            },
            Specs: Array.Empty<EquipmentSpec>());

    private static StubEquipmentRentalService Motor(
        out AlmacenEnMemoria almacen, RentalEquipment? equipo = null, int maxRentalDays = 30)
    {
        almacen = new AlmacenEnMemoria();
        return new StubEquipmentRentalService(
            new CatalogoFalso(equipo ?? Equipo()), almacen, new RelojFijo(), maxRentalDays);
    }

    private static RentalRequest Pedido(int dias = 3, int cantidad = 1, int desdeDia = 1)
    {
        // AddDays y no un día del mes: sumar sobre el día crudo revienta el constructor en
        // cuanto la ventana cruza el fin de mes, que es justo el caso largo que hay que probar.
        var inicio = new DateOnly(2026, 10, 1).AddDays(desdeDia - 1);
        return new RentalRequest("andamio", cantidad, inicio, inicio.AddDays(dias), "seudo-ana");
    }

    // ── La tarifa ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Tres_dias_cobran_la_tarifa_BASE_porque_ningun_tramo_los_cubre()
    {
        var motor = Motor(out _);

        var q = await motor.QuoteAsync(Pedido(dias: 3));

        Assert.NotNull(q);
        Assert.Equal(40_000m, q!.PerDay);
        Assert.Equal(120_000m, q.RentalTotal);
    }

    [Fact]
    public async Task Diez_dias_cobran_el_tramo_de_SEMANA_y_no_el_de_mes()
    {
        var motor = Motor(out _);

        var q = await motor.QuoteAsync(Pedido(dias: 10));

        Assert.Equal(30_000m, q!.PerDay);
        Assert.Equal(300_000m, q.RentalTotal);
    }

    [Fact]
    public async Task La_garantia_NO_se_suma_al_total_del_alquiler()
    {
        // Fundirlas diría que alguien pagó una cifra que nunca se le va a cobrar.
        var motor = Motor(out _);

        var q = await motor.QuoteAsync(Pedido(dias: 3, cantidad: 2));

        Assert.Equal(240_000m, q!.RentalTotal);
        Assert.Equal(500_000m, q.Deposit);
    }

    // ── Reservar ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reservar_RETIENE_la_garantia_y_lo_deja_legible()
    {
        var motor = Motor(out _);

        var r = await motor.ReserveAsync(Pedido(), "k1");

        Assert.Equal(RentalOutcome.Ok, r.Outcome);
        Assert.Equal(250_000m, r.Rental!.DepositHeld);
        Assert.Equal(0m, r.Rental.DamageCharged);
        Assert.Equal(RentalState.Reserved, r.Rental.State);
    }

    [Fact]
    public async Task La_misma_llave_devuelve_el_MISMO_alquiler_y_no_retiene_dos_veces()
    {
        var motor = Motor(out var almacen);

        var primera = await motor.ReserveAsync(Pedido(), "k1");
        var segunda = await motor.ReserveAsync(Pedido(), "k1");

        Assert.Equal(RentalOutcome.AlreadyDone, segunda.Outcome);
        Assert.Equal(primera.Rental!.RentalId, segunda.Rental!.RentalId);
        var guardados = await almacen.ListAsync(StubEquipmentRentalService.RentalResourceType);
        Assert.Single(guardados);
    }

    [Fact]
    public async Task Sin_llave_de_idempotencia_se_RECHAZA()
    {
        var motor = Motor(out _);

        var r = await motor.ReserveAsync(Pedido(), "  ");

        Assert.Equal(RentalOutcome.Rejected, r.Outcome);
        Assert.Equal("alquiler.idempotency_key_required", r.RejectionCode);
    }

    // ── La ventana ──────────────────────────────────────────────────────────

    [Fact]
    public async Task El_tope_del_DESPLIEGUE_y_el_del_EQUIPO_son_rechazos_DISTINTOS()
    {
        // No es cosmético: el remedio de uno es elegir otras fechas y el del otro es que este
        // despliegue no puede retener una garantía tanto tiempo.
        var largo = Motor(out _, Equipo(maxDays: 365), maxRentalDays: 30);
        var corto = Motor(out _, Equipo(maxDays: 5), maxRentalDays: 30);

        var porDespliegue = await largo.ReserveAsync(Pedido(dias: 60), "k1");
        var porEquipo = await corto.ReserveAsync(Pedido(dias: 10), "k2");

        Assert.Equal("alquiler.window_too_long", porDespliegue.RejectionCode);
        Assert.Equal("alquiler.window_out_of_bounds", porEquipo.RejectionCode);
    }

    [Fact]
    public async Task Devolver_el_mismo_dia_que_se_retira_NO_es_una_ventana()
    {
        var motor = Motor(out _);
        var mismoDia = new RentalRequest("andamio", 1, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), "x");

        var r = await motor.ReserveAsync(mismoDia, "k1");

        Assert.Equal("alquiler.bad_window", r.RejectionCode);
    }

    // ── Disponibilidad ──────────────────────────────────────────────────────

    [Fact]
    public async Task Dos_alquileres_que_se_SOLAPAN_agotan_las_unidades()
    {
        var motor = Motor(out _, Equipo(units: 2));

        await motor.ReserveAsync(Pedido(dias: 5, desdeDia: 1), "k1");
        await motor.ReserveAsync(Pedido(dias: 5, desdeDia: 3), "k2");
        var tercera = await motor.ReserveAsync(Pedido(dias: 5, desdeDia: 4), "k3");

        Assert.Equal("alquiler.no_units", tercera.RejectionCode);
    }

    [Fact]
    public async Task Una_ventana_que_EMPIEZA_donde_la_otra_termina_no_se_solapa()
    {
        var motor = Motor(out _, Equipo(units: 1));

        await motor.ReserveAsync(Pedido(dias: 3, desdeDia: 1), "k1");   // 1 → 4
        var siguiente = await motor.ReserveAsync(Pedido(dias: 3, desdeDia: 4), "k2");  // 4 → 7

        Assert.Equal(RentalOutcome.Ok, siguiente.Outcome);
    }

    [Fact]
    public async Task Un_alquiler_DEVUELTO_deja_de_ocupar_su_unidad()
    {
        // Encontrar un registro no significa «esto está tomado» — la lección del #41.
        var motor = Motor(out _, Equipo(units: 1));

        var primera = await motor.ReserveAsync(Pedido(dias: 5, desdeDia: 1), "k1");
        await motor.ReturnAsync(primera.Rental!.RentalId, 0m, "k2");
        var segunda = await motor.ReserveAsync(Pedido(dias: 5, desdeDia: 1), "k3");

        Assert.Equal(RentalOutcome.Ok, segunda.Outcome);
    }

    // ── Devolver y cancelar ─────────────────────────────────────────────────

    [Fact]
    public async Task Devolver_sin_dano_LIBERA_la_garantia_entera()
    {
        var motor = Motor(out _);
        var r = await motor.ReserveAsync(Pedido(), "k1");

        var cerrado = await motor.ReturnAsync(r.Rental!.RentalId, 0m, "k2");

        Assert.Equal(RentalOutcome.Ok, cerrado!.Outcome);
        Assert.Equal(0m, cerrado.Rental!.DepositHeld);
        Assert.Equal(0m, cerrado.Rental.DamageCharged);
        Assert.Equal(RentalState.Returned, cerrado.Rental.State);
    }

    [Fact]
    public async Task Devolver_con_dano_cobra_ESA_parte_y_suelta_el_resto()
    {
        var motor = Motor(out _);
        var r = await motor.ReserveAsync(Pedido(), "k1");

        var cerrado = await motor.ReturnAsync(r.Rental!.RentalId, 80_000m, "k2");

        Assert.Equal(80_000m, cerrado!.Rental!.DamageCharged);
        Assert.Equal(0m, cerrado.Rental.DepositHeld);
    }

    [Fact]
    public async Task Un_dano_mayor_que_la_garantia_se_RECHAZA_y_no_se_cobra_de_mas()
    {
        // Capturar por encima sería inventar una deuda que nadie autorizó.
        var motor = Motor(out _);
        var r = await motor.ReserveAsync(Pedido(), "k1");

        var cerrado = await motor.ReturnAsync(r.Rental!.RentalId, 400_000m, "k2");

        Assert.Equal("alquiler.damage_exceeds_deposit", cerrado!.RejectionCode);
        var sigueVivo = await motor.ReturnAsync(r.Rental.RentalId, 10_000m, "k3");
        Assert.Equal(250_000m - 250_000m, sigueVivo!.Rental!.DepositHeld);
        Assert.Equal(10_000m, sigueVivo.Rental.DamageCharged);
    }

    [Fact]
    public async Task Cancelar_algo_ya_DEVUELTO_se_rechaza_con_su_propio_codigo()
    {
        var motor = Motor(out _);
        var r = await motor.ReserveAsync(Pedido(), "k1");
        await motor.ReturnAsync(r.Rental!.RentalId, 0m, "k2");

        var cancelado = await motor.CancelAsync(r.Rental.RentalId, 0m, "k3");

        Assert.Equal("alquiler.not_cancellable", cancelado!.RejectionCode);
    }

    [Fact]
    public async Task Un_alquiler_que_no_existe_devuelve_NULL_y_no_un_rechazo()
    {
        // Null es «no hay tal cosa»; un rechazo diría que existe y que la regla dijo que no.
        var motor = Motor(out _);

        Assert.Null(await motor.ReturnAsync("ALQ-inexistente", 0m, "k1"));
        Assert.Null(await motor.CancelAsync("ALQ-inexistente", 0m, "k2"));
    }

    [Fact]
    public async Task Cotizar_un_equipo_que_no_existe_devuelve_null()
    {
        var motor = Motor(out _);

        Assert.Null(await motor.QuoteAsync(new RentalRequest(
            "no-existe", 1, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4), "x")));
    }
}
