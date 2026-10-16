using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El borde de Alquiler (#147, S9): qué claves cruzan y qué NO se fabrica.
/// </summary>
/// <remarks>
/// <b>El fixture lleva un sellador DE VERDAD</b>, y hace falta: con el emisor sin sellador,
/// <c>Verified</c> saldría <c>false</c> por las dos razones —el sello no cuadra, y no hay con qué
/// comprobarlo— y el test no distinguiría «se comprobó y no cuadra» de «nadie miró». Con sello
/// bueno, escribir <c>false</c> a mano en el DTO se ve.
/// </remarks>
public sealed class AlquilerControllerTests
{
    private sealed class AlmacenEnMemoria : IJsonEntityStore
    {
        private readonly Dictionary<string, string> _d = new(StringComparer.Ordinal);

        public Task WriteAsync(string t, string k, string j, CancellationToken c = default)
        {
            _d[t + "/" + k] = j;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string t, string k, CancellationToken c = default)
            => Task.FromResult(_d.TryGetValue(t + "/" + k, out var j) ? j : null);

        public Task<IReadOnlyList<string>> ListAsync(string t, CancellationToken c = default)
            => Task.FromResult<IReadOnlyList<string>>(_d
                .Where(kv => kv.Key.StartsWith(t + "/", StringComparison.Ordinal))
                .Select(kv => kv.Value).ToList());

        public Task<bool> DeleteAsync(string t, string k, CancellationToken c = default)
            => Task.FromResult(_d.Remove(t + "/" + k));
    }

    private sealed class PuertaDeMiembro : IMemberAccessGate
    {
        private readonly Guid? _key;
        private readonly bool _personal;

        public PuertaDeMiembro(Guid? key, bool personal = false)
        {
            _key = key;
            _personal = personal;
        }

        public bool IsAuthenticated => _key is not null;

        public string? CurrentMemberDisplayName => _key is null ? null : "Ana Torres";

        public string? CurrentMemberEmail => _key is null ? null : "ana@ejemplo.co";

        public Guid? CurrentMemberKey => _key;

        public IReadOnlyCollection<string> CurrentMemberRoles => Array.Empty<string>();

        public bool HasAnyRole(string? allowedRolesCsv) => _personal;
    }

    private sealed class RentalServiceFalso : IEquipmentRentalService
    {
        private readonly RentalResult _resultado;

        public RentalServiceFalso(RentalResult resultado) => _resultado = resultado;

        /// <summary>Cuántas veces se intentó cerrar: una puerta que niega no llega al seam.</summary>
        public int Cierres { get; private set; }

        public Task<RentalQuote?> QuoteAsync(RentalRequest r, CancellationToken c = default)
            => Task.FromResult<RentalQuote?>(new RentalQuote(r.EquipmentId, 1, 3, 40_000m, 120_000m, 250_000m, "COP"));

        public Task<RentalResult> ReserveAsync(RentalRequest r, string k, CancellationToken c = default)
            => Task.FromResult(_resultado);

        public Task<RentalResult?> ReturnAsync(string id, decimal m, string k, CancellationToken c = default)
        {
            Cierres++;
            return Task.FromResult<RentalResult?>(_resultado);
        }

        /// <summary>Con quién y con qué penalidad se canceló por la ruta directa.</summary>
        public List<(RentalCanceller Quien, decimal Penalidad)> Cancelaciones { get; } = [];

        public Task<RentalResult?> CancelAsync(string id, decimal m, RentalCanceller quien, string k, CancellationToken c = default)
        {
            Cierres++;
            Cancelaciones.Add((quien, m));
            return Task.FromResult<RentalResult?>(_resultado);
        }

        /// <summary>Cuántas veces se declaró por la ruta directa.</summary>
        public int Declaraciones { get; private set; }

        public Task<RentalResult?> DeclareReturnAsync(string id, string k, CancellationToken c = default)
        {
            Declaraciones++;
            return Task.FromResult<RentalResult?>(_resultado);
        }
    }

    /// <summary>Lo de la puerta después del cierre: anota a quién se llamó y con qué.</summary>
    private sealed class PuertaFalsa : IAlquilerDeLaPuerta
    {
        private readonly RentalResult _resultado;

        public PuertaFalsa(RentalResult resultado) => _resultado = resultado;

        public int Declaraciones { get; private set; }

        public List<string> ConfirmadoPor { get; } = [];

        public Task<RentalResult?> DeclareReturnAsync(RentalAgreement a, string k, CancellationToken c = default)
        {
            Declaraciones++;
            return Task.FromResult<RentalResult?>(_resultado);
        }

        public Task<RentalResult?> ConfirmReturnAsync(RentalAgreement a, decimal d, string staffId, string k, CancellationToken c = default)
        {
            ConfirmadoPor.Add(staffId);
            return Task.FromResult<RentalResult?>(_resultado);
        }

        public List<(RentalCanceller Quien, decimal Penalidad, string? Personal)> Cancelaciones { get; } = [];

        public Task<RentalResult?> CancelAsync(RentalAgreement a, RentalCanceller quien, decimal p, string? staffId, string k, CancellationToken c = default)
        {
            Cancelaciones.Add((quien, p, staffId));
            return Task.FromResult<RentalResult?>(_resultado);
        }
    }

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly Guid Miembro = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static Rental Alquiler() => new(
        "ALQ-1", "andamio", 1, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4), Miembro.ToString("N"),
        RentalState.Reserved, new RentalQuote("andamio", 1, 3, 40_000m, 120_000m, 250_000m, "COP"),
        250_000m, 0m);

    private static AlquilerController Borde(
        RentalResult resultado, out EquipmentAgreementLedger ledger, bool conSesion = true, bool conSello = true)
    {
        var almacen = new AlmacenEnMemoria();
        ledger = new EquipmentAgreementLedger(almacen);
        var firmante = conSello
            ? new HmacAgreementSigner("llave-de-prueba-de-32-bytes-xxxx"u8.ToArray())
            : null;

        return new AlquilerController(
            new StubEquipmentCatalogProvider(),
            new RentalServiceFalso(resultado),
            new PuertaFalsa(resultado),
            new EquipmentAgreementIssuer(ledger, firmante is null ? null : new SelladorLocal(firmante), new RelojFijo()),
            ledger,
            new PuertaDeMiembro(conSesion ? Miembro : null),
            NullLogger<AlquilerController>.Instance);
    }

    private static readonly Guid Otro = Guid.Parse("99999999-8888-7777-6666-555555555555");

    /// <summary>
    /// Un borde que comparte el registro con otro: así el contrato lo emite el DUEÑO y el cierre
    /// lo intenta quien sea. Con un registro por borde, «no consta» y «no es tuyo» dan lo mismo.
    /// </summary>
    private static AlquilerController BordeSobre(
        EquipmentAgreementLedger ledger, RentalServiceFalso seam, IMemberAccessGate puerta, PuertaFalsa? deLaPuerta = null)
        => new(
            new StubEquipmentCatalogProvider(),
            seam,
            deLaPuerta ?? new PuertaFalsa(new RentalResult(RentalOutcome.Rejected, null, "test.no_deberia", "")),
            new EquipmentAgreementIssuer(ledger, null, new RelojFijo()),
            ledger,
            puerta,
            NullLogger<AlquilerController>.Instance);

    /// <summary>El dueño reserva (y así queda su contrato en el registro); devuelve el registro.</summary>
    private static async Task<EquipmentAgreementLedger> ReservadoPorElDueno(RentalServiceFalso seam)
    {
        var ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
        await BordeSobre(ledger, seam, new PuertaDeMiembro(Miembro)).Reserve(Pedido(), CancellationToken.None);
        Assert.NotNull(await ledger.GetAsync("ALQ-1"));
        return ledger;
    }

    private static SettleRequest TodaLaGarantia() => new(250_000m, "cierre-1");

    /// <summary>Lee el problem+json que el borde emite: estado, código y si es transitorio.</summary>
    private static (int Estado, string? Codigo, bool Transitorio) Problema(IActionResult? resultado)
    {
        var contenido = Assert.IsType<ContentResult>(resultado);
        Assert.Equal("application/problem+json", contenido.ContentType);
        using var json = System.Text.Json.JsonDocument.Parse(contenido.Content!);
        var raiz = json.RootElement;
        return (contenido.StatusCode ?? 0,
            raiz.TryGetProperty("code", out var c) ? c.GetString() : null,
            raiz.TryGetProperty("transient", out var t) && t.GetBoolean());
    }

    private static ReserveRequest Pedido()
        => new("andamio-multidireccional-2m", 1, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4), "k1");

    [Fact]
    public async Task Reservar_EMITE_el_contrato_y_lo_anota()
    {
        var borde = Borde(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null), out var ledger);

        var res = await borde.Reserve(Pedido(), CancellationToken.None);

        var dto = Assert.IsType<RentalDto>(Assert.IsType<OkObjectResult>(res.Result).Value);
        Assert.NotNull(dto.Agreement);
        Assert.Equal(250_000m, dto.Agreement!.DepositHeld);
        Assert.NotNull(await ledger.GetAsync("ALQ-1"));
    }

    [Fact]
    public async Task El_sello_del_contrato_se_COMPRUEBA_y_no_se_afirma()
    {
        // Con sellador de verdad tiene que salir true. Escribir `false` a mano —que es lo que el
        // DTO hacía antes de pasarlo— afirmaría que el sello no cuadra sin haberlo mirado.
        var borde = Borde(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null), out _);

        var res = await borde.Reserve(Pedido(), CancellationToken.None);

        var dto = (RentalDto)((OkObjectResult)res.Result!).Value!;
        Assert.True(dto.Agreement!.Verified);
        Assert.NotEqual(string.Empty, dto.Agreement.Seal);
    }

    [Fact]
    public async Task Sin_sellador_el_contrato_sale_SIN_sello_y_sin_comprobar()
    {
        // No se inventa uno: un sello fabricado es peor que ninguno porque parece prueba.
        var borde = Borde(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null), out _, conSello: false);

        var res = await borde.Reserve(Pedido(), CancellationToken.None);

        var dto = (RentalDto)((OkObjectResult)res.Result!).Value!;
        Assert.Equal(string.Empty, dto.Agreement!.Seal);
        Assert.False(dto.Agreement.Verified);
    }

    [Fact]
    public async Task Un_rechazo_TRANSITORIO_sale_503_y_no_409()
    {
        // Con 409 un consumidor deshace algo sano por quince milisegundos de cola (#112).
        var borde = Borde(
            new RentalResult(RentalOutcome.Unavailable, null, "alquiler.store_busy", "Otro está escribiendo."),
            out _);

        var res = await borde.Reserve(Pedido(), CancellationToken.None);

        var p = Problema(res.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, p.Estado);
        Assert.Equal("alquiler.store_busy", p.Codigo);
        Assert.True(p.Transitorio);
    }

    [Fact]
    public async Task Un_rechazo_FIRME_sale_409_con_su_codigo()
    {
        var borde = Borde(
            new RentalResult(RentalOutcome.Rejected, null, "alquiler.no_units", "No quedan unidades."),
            out _);

        var res = await borde.Reserve(Pedido(), CancellationToken.None);

        var p = Problema(res.Result);
        Assert.Equal(StatusCodes.Status409Conflict, p.Estado);
        Assert.Equal("alquiler.no_units", p.Codigo);
        Assert.False(p.Transitorio);
    }

    [Fact]
    public async Task Sin_sesion_la_bandeja_NO_devuelve_lista_vacia()
    {
        // `[]` diría «todavía ninguno» sobre alguien que ni siquiera se identificó — la
        // distinción de `an_empty_list_is_honest_when_something_could_have_filled_it`.
        var borde = Borde(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null), out _, conSesion: false);

        var res = await borde.Mine(CancellationToken.None);

        var p = Problema(res.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, p.Estado);
        Assert.Equal("alquiler.session_required", p.Codigo);
    }

    [Fact]
    public async Task La_cotizacion_NO_suma_la_garantia_al_total()
    {
        var borde = Borde(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null), out _);

        var res = await borde.Quote(
            new QuoteRequest("andamio", 1, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4)),
            CancellationToken.None);

        var dto = Assert.IsType<QuoteDto>(Assert.IsType<OkObjectResult>(res.Result).Value);
        Assert.Equal(120_000m, dto.RentalTotal);
        Assert.Equal(250_000m, dto.Deposit);
    }

    [Fact]
    public async Task Un_equipo_que_no_existe_da_404()
    {
        var borde = Borde(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null), out _);

        var res = await borde.EquipmentDetail("no-existe", CancellationToken.None);

        Assert.IsType<NotFoundResult>(res.Result);
    }

    // ── Quién puede cerrar un alquiler (IDOR) ───────────────────────────────

    [Fact]
    public async Task Sin_sesion_NO_se_cierra_un_alquiler()
    {
        var seam = new RentalServiceFalso(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null));
        var ledger = await ReservadoPorElDueno(seam);

        var res = await BordeSobre(ledger, seam, new PuertaDeMiembro(null))
            .Return("ALQ-1", TodaLaGarantia(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Problema(res.Result).Estado);
        Assert.Equal(0, seam.Cierres);
    }

    [Fact]
    public async Task Otro_miembro_NO_devuelve_el_alquiler_ajeno_ni_cobra_su_garantia()
    {
        // El caso entero: devolver con el monto de la garantía es capturarla. Lo intenta un
        // miembro CON sesión, que es lo que un guard de «¿está logueado?» dejaba pasar.
        var seam = new RentalServiceFalso(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null));
        var ledger = await ReservadoPorElDueno(seam);

        var devolver = await BordeSobre(ledger, seam, new PuertaDeMiembro(Otro))
            .Return("ALQ-1", TodaLaGarantia(), CancellationToken.None);
        var cancelar = await BordeSobre(ledger, seam, new PuertaDeMiembro(Otro))
            .Cancel("ALQ-1", TodaLaGarantia(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Problema(devolver.Result).Estado);
        Assert.Equal(StatusCodes.Status403Forbidden, Problema(cancelar.Result).Estado);
        Assert.Equal(0, seam.Cierres);
    }

    [Fact]
    public async Task El_dueno_NO_confirma_su_devolucion_ni_diciendo_cero()
    {
        // Decisión 5 del lote 1 (#203): quien alquila DECLARA; confirmar con el daño es del personal. Con cero,
        // el dueño liberaba su propia garantía sin que nadie mirara el equipo.
        var seam = new RentalServiceFalso(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null));
        var ledger = await ReservadoPorElDueno(seam);

        var res = await BordeSobre(ledger, seam, new PuertaDeMiembro(Miembro))
            .Return("ALQ-1", new SettleRequest(0m, "cierre-1"), CancellationToken.None);

        var p = Problema(res.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, p.Estado);
        Assert.Equal("alquiler.staff_required", p.Codigo);
        Assert.Equal(0, seam.Cierres);
    }

    // ── Declarar y confirmar (decisión 5 del lote 1; línea 005, T018) ──────

    private static Rental En(RentalState estado) => Alquiler() with { State = estado };

    [Fact]
    public async Task El_dueno_declara_y_el_registro_anota_el_estado()
    {
        var seam = new RentalServiceFalso(new RentalResult(RentalOutcome.Ok, En(RentalState.Declared), null, null));
        var ledger = await ReservadoPorElDueno(seam);

        var res = await BordeSobre(ledger, seam, new PuertaDeMiembro(Miembro))
            .DeclareReturn("ALQ-1", new DeclareReturnRequest("declarar-1"), CancellationToken.None);

        var dto = Assert.IsType<RentalDto>(Assert.IsType<OkObjectResult>(res.Result).Value);
        Assert.Equal("declared", dto.State);
        Assert.Equal(250_000m, dto.DepositHeld);
        Assert.Equal(1, seam.Declaraciones);
        Assert.Equal(RentalState.Declared, (await ledger.GetAsync("ALQ-1"))!.Estado);
    }

    [Fact]
    public async Task Declarar_un_alquiler_ajeno_no_existe()
    {
        var seam = new RentalServiceFalso(new RentalResult(RentalOutcome.Ok, En(RentalState.Declared), null, null));
        var ledger = await ReservadoPorElDueno(seam);

        var res = await BordeSobre(ledger, seam, new PuertaDeMiembro(Otro))
            .DeclareReturn("ALQ-1", new DeclareReturnRequest("declarar-1"), CancellationToken.None);

        Assert.IsType<NotFoundResult>(res.Result);
        Assert.Equal(0, seam.Declaraciones);
        Assert.Equal(RentalState.Reserved, (await ledger.GetAsync("ALQ-1"))!.Estado);
    }

    [Fact]
    public async Task El_personal_confirma_y_el_registro_anota_el_estado()
    {
        var seam = new RentalServiceFalso(new RentalResult(RentalOutcome.Ok, En(RentalState.Returned) with { DepositHeld = 0m }, null, null));
        var ledger = await ReservadoPorElDueno(seam);

        var res = await BordeSobre(ledger, seam, new PuertaDeMiembro(Otro, personal: true))
            .Return("ALQ-1", new SettleRequest(0m, "cierre-1"), CancellationToken.None);

        Assert.Equal("returned", Assert.IsType<RentalDto>(Assert.IsType<OkObjectResult>(res.Result).Value).State);
        Assert.Equal(1, seam.Cierres);
        Assert.Equal(RentalState.Returned, (await ledger.GetAsync("ALQ-1"))!.Estado);
    }

    [Fact]
    public async Task El_camino_anotado_en_el_contrato_gana_sobre_el_motor_de_la_directa()
    {
        // El contrato lo SIEMBRA el test con Camino = puerta: los emite el artefacto de T024. Lo que se prueba es
        // que lo que pasa después va adonde salió el alquiler y no al motor del Mode.
        var seam = new RentalServiceFalso(new RentalResult(RentalOutcome.Ok, En(RentalState.Declared), null, null));
        var ledger = await ReservadoPorElDueno(seam);
        await ledger.RecordAsync((await ledger.GetAsync("ALQ-1"))! with { Camino = CaminoDeCompra.Puerta });
        var deLaPuerta = new PuertaFalsa(new RentalResult(RentalOutcome.Ok, En(RentalState.Declared), null, null));

        await BordeSobre(ledger, seam, new PuertaDeMiembro(Miembro), deLaPuerta)
            .DeclareReturn("ALQ-1", new DeclareReturnRequest("declarar-1"), CancellationToken.None);
        await BordeSobre(ledger, seam, new PuertaDeMiembro(Otro, personal: true), deLaPuerta)
            .Return("ALQ-1", new SettleRequest(40_000m, "cierre-1"), CancellationToken.None);

        Assert.Equal(1, deLaPuerta.Declaraciones);
        Assert.Equal([Otro.ToString("N")], deLaPuerta.ConfirmadoPor);
        Assert.Equal(0, seam.Declaraciones);
        Assert.Equal(0, seam.Cierres);
    }

    // ── Cancelar lo confirmado (research R7; línea 005, T020) ──────────────

    [Fact]
    public async Task El_dueno_cancela_sin_penalidad_aunque_la_mande_y_el_personal_con_ella()
    {
        var seam = new RentalServiceFalso(new RentalResult(RentalOutcome.Ok, En(RentalState.Cancelled) with { DepositHeld = 0m }, null, null));
        var ledger = await ReservadoPorElDueno(seam);

        await BordeSobre(ledger, seam, new PuertaDeMiembro(Miembro))
            .Cancel("ALQ-1", new SettleRequest(99_000m, "cancelar-1"), CancellationToken.None);
        await BordeSobre(ledger, seam, new PuertaDeMiembro(Otro, personal: true))
            .Cancel("ALQ-1", new SettleRequest(50_000m, "cancelar-2"), CancellationToken.None);

        Assert.Equal([(RentalCanceller.Renter, 0m), (RentalCanceller.Staff, 50_000m)], seam.Cancelaciones);
        Assert.Equal(RentalState.Cancelled, (await ledger.GetAsync("ALQ-1"))!.Estado);
    }

    [Fact]
    public async Task Cancelar_un_alquiler_de_la_puerta_va_al_orquestador_por_el_que_salio()
    {
        var seam = new RentalServiceFalso(new RentalResult(RentalOutcome.Ok, En(RentalState.Cancelled), null, null));
        var ledger = await ReservadoPorElDueno(seam);
        await ledger.RecordAsync((await ledger.GetAsync("ALQ-1"))! with { Camino = CaminoDeCompra.Puerta });
        var deLaPuerta = new PuertaFalsa(new RentalResult(RentalOutcome.Ok, En(RentalState.Cancelled), null, null));

        await BordeSobre(ledger, seam, new PuertaDeMiembro(Miembro), deLaPuerta)
            .Cancel("ALQ-1", new SettleRequest(99_000m, "cancelar-1"), CancellationToken.None);
        await BordeSobre(ledger, seam, new PuertaDeMiembro(Otro, personal: true), deLaPuerta)
            .Cancel("ALQ-1", new SettleRequest(50_000m, "cancelar-2"), CancellationToken.None);

        Assert.Equal(
            [(RentalCanceller.Renter, 0m, (string?)null), (RentalCanceller.Staff, 50_000m, Otro.ToString("N"))],
            deLaPuerta.Cancelaciones);
        Assert.Empty(seam.Cancelaciones);
    }

    [Fact]
    public async Task La_bandeja_del_personal_trae_las_declaradas_de_la_mas_vieja_y_solo_a_el()
    {
        var ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
        var seam = new RentalServiceFalso(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null));
        var base0 = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        RentalAgreement Contrato(string id, int dias, RentalState estado) => new(
            id, Miembro.ToString("N"), "andamio", "Andamio", 1, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4),
            120_000m, 250_000m, "COP", base0.AddDays(dias), string.Empty, Estado: estado);
        await ledger.RecordAsync(Contrato("ALQ-NUEVA", 5, RentalState.Declared));
        await ledger.RecordAsync(Contrato("ALQ-VIVA", 1, RentalState.Reserved));
        await ledger.RecordAsync(Contrato("ALQ-VIEJA", 2, RentalState.Declared));

        var personal = await BordeSobre(ledger, seam, new PuertaDeMiembro(Otro, personal: true)).StaffReturns(CancellationToken.None);
        var inquilino = await BordeSobre(ledger, seam, new PuertaDeMiembro(Miembro)).StaffReturns(CancellationToken.None);
        var anonimo = await BordeSobre(ledger, seam, new PuertaDeMiembro(null)).StaffReturns(CancellationToken.None);

        var lista = Assert.IsAssignableFrom<IReadOnlyList<AgreementDto>>(Assert.IsType<OkObjectResult>(personal.Result).Value);
        Assert.Equal(["ALQ-VIEJA", "ALQ-NUEVA"], lista.Select(a => a.RentalId));
        Assert.Equal(StatusCodes.Status403Forbidden, Problema(inquilino.Result).Estado);
        Assert.Equal(StatusCodes.Status401Unauthorized, Problema(anonimo.Result).Estado);
    }

    [Fact]
    public async Task El_personal_SI_cierra_un_alquiler_ajeno()
    {
        // Quien recibe el equipo es el personal, y es quien calcula el daño (spec §«lo que no
        // hace»): negarle el cierre por no ser el dueño dejaría la devolución sin nadie que la haga.
        var seam = new RentalServiceFalso(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null));
        var ledger = await ReservadoPorElDueno(seam);

        var res = await BordeSobre(ledger, seam, new PuertaDeMiembro(Otro, personal: true))
            .Return("ALQ-1", new SettleRequest(40_000m, "cierre-1"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(res.Result);
        Assert.Equal(1, seam.Cierres);
    }
}
