using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El contrato de un alquiler hecho por la puerta (línea 005, T024; research R10): se emite al leerlo si la renta se
/// completó y es del miembro, una sola vez, y «mis alquileres» reconcilia el que se pagó con la pestaña cerrada.
/// </summary>
/// <remarks>
/// El orquestador falso contesta como <c>Bff.Alquiler</c>: la renta sólo a su dueño (por <c>X-Synergos-Sujeto</c>) y la
/// lista de los alquileres de la puerta del sujeto que pregunta.
/// </remarks>
public sealed class ArtefactoDeAlquilerTests
{
    private const string Kind = "alquiler.arrendatario";

    private static readonly Guid Ana = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Beto = Guid.Parse("99999999-8888-7777-6666-555555555555");

    internal sealed class OrquestadorFalso : HttpMessageHandler
    {
        public ConcurrentDictionary<string, (Guid Dueno, string Estado)> Rentas { get; } = new(StringComparer.Ordinal);

        public ConcurrentQueue<(string Ruta, string? Sujeto)> Llamadas { get; } = new();

        public bool Caido { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var ruta = req.RequestUri!.AbsolutePath;
            var sujeto = req.Headers.TryGetValues("X-Synergos-Sujeto", out var v) ? v.Single() : null;
            Llamadas.Enqueue((ruta, sujeto));
            if (Caido)
            {
                throw new HttpRequestException("Connection refused");
            }

            if (ruta == "/v1/alquileres")
            {
                var suyos = Rentas.Where(r => sujeto == $"{Kind}:{r.Value.Dueno:N}")
                    .OrderBy(r => r.Key, StringComparer.Ordinal)
                    .Select(r => new { id = r.Key, status = r.Value.Estado });
                return Responder(HttpStatusCode.OK, JsonSerializer.Serialize(new { items = suyos }));
            }

            var id = Uri.UnescapeDataString(ruta["/v1/flujos/alquiler.renta/".Length..]);
            if (!Rentas.TryGetValue(id, out var renta) || sujeto != $"{Kind}:{renta.Dueno:N}")
            {
                return Responder(HttpStatusCode.NotFound,
                    """{"title":"Not Found","status":404,"code":"alquiler.saga_not_found"}""");
            }

            return Responder(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                id,
                flujo = "alquiler.renta",
                numero = "ALQ-" + id.ToUpperInvariant(),
                estado = renta.Estado,
                datos = new
                {
                    total = new { amount = 420_000m, currency = "COP" },
                    garantia = new { amount = 500_000m, currency = "COP" },
                    garantiaId = "pg-garantia",
                    cobroId = "pg-cobro",
                    ventana = new { start = "2026-11-02T09:00:00+00:00", end = "2026-11-09T09:00:00+00:00" },
                    equipo = "andamio-6m",
                    cantidad = 2,
                },
                compensacionesPendientes = 0,
                error = (string?)null,
                deshaciendo = false,
            }));
        }

        public int Veces(string ruta) => Llamadas.Count(l => l.Ruta == ruta);

        private static Task<HttpResponseMessage> Responder(HttpStatusCode codigo, string cuerpo)
            => Task.FromResult(new HttpResponseMessage(codigo)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
            });
    }

    internal sealed class Fabrica(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(h, disposeHandler: false) { BaseAddress = new Uri("http://bff-alquiler.local/") };
    }

    private sealed class AlmacenEnMemoria : IJsonEntityStore
    {
        private readonly ConcurrentDictionary<string, string> _data = new(StringComparer.Ordinal);

        public Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default)
        {
            _data[resourceType + "/" + key] = json;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_data.TryGetValue(resourceType + "/" + key, out var json) ? json : null);

        public Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(_data
                .Where(kv => kv.Key.StartsWith(resourceType + "/", StringComparison.Ordinal))
                .Select(kv => kv.Value)
                .ToList());

        public Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_data.TryRemove(resourceType + "/" + key, out _));
    }

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Sesion(Guid? miembro, bool personal = false) : IMemberAccessGate
    {
        public bool IsAuthenticated => miembro is not null;
        public string? CurrentMemberDisplayName => null;
        public string? CurrentMemberEmail => null;
        public Guid? CurrentMemberKey => miembro;
        public IReadOnlyCollection<string> CurrentMemberRoles => [];
        public bool HasAnyRole(string? allowedRolesCsv) => personal;
    }

    private sealed class Arnes
    {
        public Arnes(bool hayDestino = true, bool conFlujo = true)
        {
            Ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
            Emisor = new EquipmentAgreementIssuer(
                Ledger, new SelladorLocal(new HmacAgreementSigner("llave-de-prueba-de-32-bytes-xxxx"u8.ToArray())), new RelojFijo());
            var puerta = Substitute.For<IOptionsMonitor<PuertaSettings>>();
            puerta.CurrentValue.Returns(new PuertaSettings
            {
                Flujos = conFlujo
                    ? new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal)
                    {
                        [ArtefactoDeAlquiler.Flujo] = new() { Acceso = "Miembro", SujetoKind = Kind },
                    }
                    : new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal),
            });
            Artefacto = new ArtefactoDeAlquiler(new Fabrica(Orquestador), Ledger, Emisor, new StubEquipmentCatalogProvider(),
                puerta, NullLogger<ArtefactoDeAlquiler>.Instance, hayDestino);
        }

        public OrquestadorFalso Orquestador { get; } = new();

        public EquipmentAgreementLedger Ledger { get; }

        public EquipmentAgreementIssuer Emisor { get; }

        public ArtefactoDeAlquiler Artefacto { get; }

        public AlquilerController Borde(Guid? miembro, bool personal = false)
            => new(new StubEquipmentCatalogProvider(), Substitute.For<IEquipmentRentalService>(),
                Substitute.For<IAlquilerDeLaPuerta>(), Emisor, Ledger, Artefacto, new Sesion(miembro, personal),
                NullLogger<AlquilerController>.Instance);
    }

    [Fact]
    public async Task Completada_y_mia_emite_el_contrato_sellado_por_la_puerta_con_lo_que_contesto_el_orquestador()
    {
        var a = new Arnes();
        a.Orquestador.Rentas["alq-1"] = (Ana, "Completed");

        var r = await a.Artefacto.ContratoAsync("alq-1", Ana, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, r.Estado);
        var c = r.Contrato!;
        Assert.Equal(("alq-1", Ana.ToString("N"), "andamio-6m", 2), (c.RentalId, c.RenterId, c.EquipmentId, c.Quantity));
        Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9)), (c.Start, c.End));
        Assert.Equal((420_000m, 500_000m, "COP"), (c.RentalTotal, c.DepositHeld, c.Currency));
        Assert.Equal(CaminoDeCompra.Puerta, c.Camino);
        Assert.True(await a.Emisor.ComprobarAsync(c));
        Assert.Equal(c, await a.Ledger.GetAsync("alq-1"));
        // Preguntó como el miembro, que es lo que hace que una ajena no exista.
        Assert.Contains(a.Orquestador.Llamadas, l => l.Sujeto == $"{Kind}:{Ana:N}");
    }

    [Fact]
    public async Task En_curso_no_emite_y_dice_por_que()
    {
        var a = new Arnes();
        a.Orquestador.Rentas["alq-1"] = (Ana, "Running");

        var r = await a.Artefacto.ContratoAsync("alq-1", Ana, CancellationToken.None);

        Assert.Equal((StatusCodes.Status409Conflict, "alquiler.rental_in_progress"), (r.Estado, r.Codigo));
        Assert.Null(await a.Ledger.GetAsync("alq-1"));

        a.Orquestador.Rentas["alq-1"] = (Ana, "Compensated");
        Assert.Equal("alquiler.rental_not_completed", (await a.Artefacto.ContratoAsync("alq-1", Ana, CancellationToken.None)).Codigo);
        Assert.Null(await a.Ledger.GetAsync("alq-1"));
    }

    [Fact]
    public async Task Ajena_no_existe_ni_por_el_artefacto_ni_por_el_borde()
    {
        var a = new Arnes();
        a.Orquestador.Rentas["alq-1"] = (Ana, "Completed");

        Assert.Equal(StatusCodes.Status404NotFound, (await a.Artefacto.ContratoAsync("alq-1", Beto, CancellationToken.None)).Estado);
        Assert.Null(await a.Ledger.GetAsync("alq-1"));

        // Ya emitido para Ana, Beto tampoco lo ve por el borde; el personal sí.
        Assert.IsType<OkObjectResult>((await a.Borde(Ana).Agreement("alq-1", CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await a.Borde(Beto).Agreement("alq-1", CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await a.Borde(Beto, personal: true).Agreement("alq-1", CancellationToken.None)).Result);
        Assert.Equal(StatusCodes.Status401Unauthorized,
            ((ContentResult)(await a.Borde(null).Agreement("alq-1", CancellationToken.None)).Result!).StatusCode);
    }

    [Fact]
    public async Task Repetir_y_a_la_vez_dan_el_mismo_contrato_y_se_emite_una_vez()
    {
        var a = new Arnes();
        a.Orquestador.Rentas["alq-1"] = (Ana, "Completed");

        var dos = await Task.WhenAll(
            a.Artefacto.ContratoAsync("alq-1", Ana, CancellationToken.None),
            a.Artefacto.ContratoAsync("alq-1", Ana, CancellationToken.None));
        var otra = await a.Artefacto.ContratoAsync("alq-1", Ana, CancellationToken.None);

        Assert.Equal(dos[0].Contrato, dos[1].Contrato);
        Assert.Equal(dos[0].Contrato, otra.Contrato);
        Assert.Equal(1, a.Orquestador.Veces("/v1/flujos/alquiler.renta/alq-1"));
    }

    [Fact]
    public async Task Mis_alquileres_emite_el_que_se_pago_con_la_pestana_cerrada()
    {
        var a = new Arnes();
        a.Orquestador.Rentas["alq-1"] = (Ana, "Completed");
        a.Orquestador.Rentas["alq-2"] = (Ana, "Running");
        a.Orquestador.Rentas["alq-3"] = (Beto, "Completed");

        var res = await a.Borde(Ana).Mine(CancellationToken.None);

        var lista = Assert.IsAssignableFrom<IReadOnlyList<AgreementDto>>(Assert.IsType<OkObjectResult>(res.Result).Value);
        var unico = Assert.Single(lista);
        Assert.Equal("alq-1", unico.RentalId);
        Assert.True(unico.Verified);
        Assert.Null(await a.Ledger.GetAsync("alq-3"));

        // Lo ya emitido no se vuelve a pedir: la segunda lectura sólo lista.
        await a.Borde(Ana).Mine(CancellationToken.None);
        Assert.Equal(1, a.Orquestador.Veces("/v1/flujos/alquiler.renta/alq-1"));
        Assert.Equal(0, a.Orquestador.Veces("/v1/flujos/alquiler.renta/alq-2"));
    }

    [Fact]
    public async Task Sin_puerta_no_sale_a_la_red_y_caido_la_bandeja_sale_igual()
    {
        foreach (var sinPuerta in new[] { new Arnes(hayDestino: false), new Arnes(conFlujo: false) })
        {
            sinPuerta.Orquestador.Rentas["alq-1"] = (Ana, "Completed");
            Assert.Equal(StatusCodes.Status404NotFound,
                (await sinPuerta.Artefacto.ContratoAsync("alq-1", Ana, CancellationToken.None)).Estado);
            await sinPuerta.Artefacto.ReconciliarAsync(Ana, CancellationToken.None);
            Assert.Empty(sinPuerta.Orquestador.Llamadas);
        }

        var a = new Arnes();
        a.Orquestador.Caido = true;
        var r = await a.Artefacto.ContratoAsync("alq-1", Ana, CancellationToken.None);
        Assert.Equal((StatusCodes.Status503ServiceUnavailable, true), (r.Estado, r.Transitorio));
        Assert.IsType<OkObjectResult>((await a.Borde(Ana).Mine(CancellationToken.None)).Result);
    }
}
