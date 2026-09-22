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

        public PuertaDeMiembro(Guid? key) => _key = key;

        public bool IsAuthenticated => _key is not null;

        public string? CurrentMemberDisplayName => _key is null ? null : "Ana Torres";

        public string? CurrentMemberEmail => _key is null ? null : "ana@ejemplo.co";

        public Guid? CurrentMemberKey => _key;

        public IReadOnlyCollection<string> CurrentMemberRoles => Array.Empty<string>();

        public bool HasAnyRole(string? allowedRolesCsv) => false;
    }

    private sealed class RentalServiceFalso : IEquipmentRentalService
    {
        private readonly RentalResult _resultado;

        public RentalServiceFalso(RentalResult resultado) => _resultado = resultado;

        public Task<RentalQuote?> QuoteAsync(RentalRequest r, CancellationToken c = default)
            => Task.FromResult<RentalQuote?>(new RentalQuote(r.EquipmentId, 1, 3, 40_000m, 120_000m, 250_000m));

        public Task<RentalResult> ReserveAsync(RentalRequest r, string k, CancellationToken c = default)
            => Task.FromResult(_resultado);

        public Task<RentalResult?> ReturnAsync(string id, decimal m, string k, CancellationToken c = default)
            => Task.FromResult<RentalResult?>(_resultado);

        public Task<RentalResult?> CancelAsync(string id, decimal m, string k, CancellationToken c = default)
            => Task.FromResult<RentalResult?>(_resultado);
    }

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly Guid Miembro = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static Rental Alquiler() => new(
        "ALQ-1", "andamio", 1, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4), Miembro.ToString("N"),
        RentalState.Reserved, new RentalQuote("andamio", 1, 3, 40_000m, 120_000m, 250_000m),
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
            new EquipmentAgreementIssuer(ledger, firmante, new RelojFijo()),
            ledger,
            new PuertaDeMiembro(conSesion ? Miembro : null),
            NullLogger<AlquilerController>.Instance);
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

        var obj = Assert.IsType<ObjectResult>(res.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);
    }

    [Fact]
    public async Task Un_rechazo_FIRME_sale_409_con_su_codigo()
    {
        var borde = Borde(
            new RentalResult(RentalOutcome.Rejected, null, "alquiler.no_units", "No quedan unidades."),
            out _);

        var res = await borde.Reserve(Pedido(), CancellationToken.None);

        var dto = Assert.IsType<RejectionDto>(Assert.IsType<ConflictObjectResult>(res.Result).Value);
        Assert.Equal("alquiler.no_units", dto.Code);
    }

    [Fact]
    public async Task Sin_sesion_la_bandeja_NO_devuelve_lista_vacia()
    {
        // `[]` diría «todavía ninguno» sobre alguien que ni siquiera se identificó — la
        // distinción de `an_empty_list_is_honest_when_something_could_have_filled_it`.
        var borde = Borde(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null), out _, conSesion: false);

        var res = await borde.Mine(CancellationToken.None);

        var dto = Assert.IsType<RejectionDto>(Assert.IsType<UnauthorizedObjectResult>(res.Result).Value);
        Assert.Equal("alquiler.session_required", dto.Code);
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
}
