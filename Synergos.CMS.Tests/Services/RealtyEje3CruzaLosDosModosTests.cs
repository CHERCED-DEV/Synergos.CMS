using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La misma visita se lee igual la haya agendado el motor en proceso o el cliente cableado (#158).
/// </summary>
/// <remarks>
/// <para><b>Este es el test que ninguna de las dos suites podía escribir</b>, y por eso el
/// defecto vivió tanto. <c>StubVisitSchedulingServiceTests</c> mira el motor en proceso;
/// <c>HttpVisitSchedulingServiceTests</c> mira el cliente. Las dos en verde, y el hueco justo en
/// medio: <i>¿qué queda de ESTE lado cuando se cambia el interruptor?</i> Es el reparto del
/// addendum #116 de <c>feedback_no_read_without_a_write_path</c> — cada mitad creía que la otra
/// lo miraba.</para>
///
/// <para><b>Y el interruptor es de DESPLIEGUE</b>, así que sin esto la regresión llega un martes
/// sin que nadie toque el vertical: alguien pone <c>Synergos:Realty:Mode=Api</c> y las visitas
/// agendadas dejan de existir para quien las agendó. No falla — la agenda se sigue pintando,
/// porque se deriva del id del listado y del reloj, así que la pantalla se ve <b>bien</b> y está
/// vacía de lo único que esa persona fue a buscar.</para>
///
/// <para><b>La mutación que lo reproduce</b> es quitarle el registro a una de las dos
/// implementaciones: el <c>RealtyVisitLedger</c> es opcional en las dos —tiene que serlo, o los
/// tests que sólo miran disponibilidad no podrían construirlas— así que olvidarlo <b>compila</b>.
/// Eso es exactamente lo que hacía el cliente cableado antes de esta HU.</para>
/// </remarks>
public sealed class RealtyEje3CruzaLosDosModosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    private static VisitContact Ana => new("Ana Ruiz", "ana@correo.co", "3001234567");

    private sealed class Reloj : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private sealed class OpcionesFalsas : IOptionsMonitor<RealtySettings>
    {
        public RealtySettings CurrentValue { get; } = new();
        public RealtySettings Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<RealtySettings, string?> listener) => null;
    }

    /// <summary>
    /// Una <c>Api.Booking</c> de mentira que dice a todo que sí. Acá no se prueba el protocolo
    /// —de eso va <c>HttpVisitSchedulingServiceTests</c>— sino qué QUEDA anotado de este lado.
    /// </summary>
    private sealed class CapacidadQueAcepta : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Las formas son las del harness de `HttpVisitSchedulingServiceTests`, que es donde
            // se prueba el protocolo de verdad. Acá sólo hacen falta para llegar al final.
            var uri = request.RequestUri!.PathAndQuery;
            var cuerpo = uri.StartsWith("/v1/resources?", StringComparison.Ordinal)
                ? """{"id":"res-7","subjectKind":"realty.listado","subjectId":"L1"}"""
                : uri.Contains("/availability", StringComparison.Ordinal)
                    ? """{"available":true,"taken":0,"capacity":1}"""
                    : uri.EndsWith("/confirm", StringComparison.Ordinal)
                        ? """{"id":"resv-99","status":"Confirmed"}"""
                        : """{"id":"hold-42"}""";

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class Fabrica : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public Fabrica(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://api-booking:8080/") };
    }

    /// <summary>El primer slot de la agenda derivada del listado, que es el que se agenda.</summary>
    private static VisitSlot PrimerSlot(string listado)
        => Synergos.CMS.Application.Services.VisitAgenda.For(listado, Ahora)[0];

    [Fact]
    public async Task Con_el_motor_EN_PROCESO_la_visita_queda_anotada_en_el_registro()
    {
        var registro = new RealtyVisitLedger(new InMemoryJsonEntityStore(), () => Ahora);
        using var svc = new StubVisitSchedulingService(
            new StubReservationService(), () => Ahora, new InMemoryJsonEntityStore(), null, registro);

        var slot = PrimerSlot("L1");
        var r = await svc.BookAsync("L1", slot.Id, Ana);

        var suyas = await registro.ForVisitorAsync("ana@correo.co");
        Assert.Single(suyas);
        Assert.Equal(r.VisitId, suyas[0].VisitId);
        Assert.Equal("L1", suyas[0].ListingId);
        Assert.Equal(slot.StartUtc, suyas[0].StartUtc);
    }

    [Fact]
    public async Task Con_el_modo_CABLEADO_la_visita_queda_anotada_en_el_MISMO_registro()
    {
        // La mitad que faltaba: hasta el #158 este camino no escribía nada de este lado, así que
        // encender el interruptor hacía desaparecer el eje 3 del vertical.
        var registro = new RealtyVisitLedger(new InMemoryJsonEntityStore(), () => Ahora);
        var svc = new HttpVisitSchedulingService(
            new Fabrica(new CapacidadQueAcepta()),
            new OpcionesFalsas(),
            new Reloj(),
            NullLogger<HttpVisitSchedulingService>.Instance,
            registro);

        var slot = PrimerSlot("L1");
        var r = await svc.BookAsync("L1", slot.Id, Ana);

        var suyas = await registro.ForVisitorAsync("ana@correo.co");
        Assert.Single(suyas);
        Assert.Equal(r.VisitId, suyas[0].VisitId);
        Assert.Equal("L1", suyas[0].ListingId);
        Assert.Equal(slot.StartUtc, suyas[0].StartUtc);
    }

    [Fact]
    public async Task Las_visitas_de_los_DOS_modos_conviven_en_la_misma_bandeja()
    {
        // La invariante del doc 12 §3.2 dicha como test: el registro es UNO. Si viviera dentro
        // de una de las dos implementaciones, cambiar el interruptor partiría la bandeja en dos
        // y quien agendó antes del cambio dejaría de ver lo suyo — sin que nada fallara.
        var registro = new RealtyVisitLedger(new InMemoryJsonEntityStore(), () => Ahora);

        using var enProceso = new StubVisitSchedulingService(
            new StubReservationService(), () => Ahora, new InMemoryJsonEntityStore(), null, registro);
        var cableado = new HttpVisitSchedulingService(
            new Fabrica(new CapacidadQueAcepta()), new OpcionesFalsas(), new Reloj(),
            NullLogger<HttpVisitSchedulingService>.Instance, registro);

        // Dos inmuebles distintos: con el mismo, el segundo agendado reusaría el id y la
        // bandeja traería una sola fila aunque el registro estuviera partido — el defecto
        // pasaría en verde (la regla 7 del repo hermano sobre el fixture).
        await enProceso.BookAsync("L1", PrimerSlot("L1").Id, Ana);
        await cableado.BookAsync("L2", PrimerSlot("L2").Id, Ana);

        var suyas = await registro.ForVisitorAsync("ana@correo.co");

        Assert.Equal(2, suyas.Count);
        Assert.Equal(new[] { "L1", "L2" }, suyas.Select(v => v.ListingId).OrderBy(x => x, StringComparer.Ordinal));
    }

    // ── La modalidad (#160) ──────────────────────────────────────────────────────
    //
    // El mismo reparto de arriba: la modalidad tiene que llegar al registro por los DOS
    // caminos. Cablearla en uno solo compila, pasa las dos suites de cada lado, y deja al
    // vertical diciendo cosas distintas según una bandera de despliegue.

    [Fact]
    public async Task La_modalidad_llega_al_registro_por_los_DOS_caminos()
    {
        var registro = new RealtyVisitLedger(new InMemoryJsonEntityStore(), () => Ahora);

        using var enProceso = new StubVisitSchedulingService(
            new StubReservationService(), () => Ahora, new InMemoryJsonEntityStore(), null, registro);
        var cableado = new HttpVisitSchedulingService(
            new Fabrica(new CapacidadQueAcepta()), new OpcionesFalsas(), new Reloj(),
            NullLogger<HttpVisitSchedulingService>.Instance, registro);

        // VIDEO y no presencial, a propósito: es el valor que NINGÚN default produce. Con
        // `in-person` el fixture no distingue «la modalidad viajó» de «alguien la rellenó», que
        // es justo la fabricación que este ticket quita.
        await enProceso.BookAsync("L1", PrimerSlot("L1").Id, Ana, VisitModes.Video);
        await cableado.BookAsync("L2", PrimerSlot("L2").Id, Ana, VisitModes.Video);

        var suyas = await registro.ForVisitorAsync("ana@correo.co");

        Assert.Equal(2, suyas.Count);
        Assert.All(suyas, v => Assert.Equal(VisitModes.Video, v.Mode));
    }

    [Fact]
    public async Task Sin_modalidad_el_registro_dice_NO_CONSTA_y_no_presencial()
    {
        var registro = new RealtyVisitLedger(new InMemoryJsonEntityStore(), () => Ahora);

        using var enProceso = new StubVisitSchedulingService(
            new StubReservationService(), () => Ahora, new InMemoryJsonEntityStore(), null, registro);
        var cableado = new HttpVisitSchedulingService(
            new Fabrica(new CapacidadQueAcepta()), new OpcionesFalsas(), new Reloj(),
            NullLogger<HttpVisitSchedulingService>.Instance, registro);

        await enProceso.BookAsync("L1", PrimerSlot("L1").Id, Ana);
        await cableado.BookAsync("L2", PrimerSlot("L2").Id, Ana);

        var suyas = await registro.ForVisitorAsync("ana@correo.co");

        // `null`, no `in-person`. Ésta es la mitad que protege a las visitas ANTERIORES al
        // #160: rellenarlas afirmaría que esa gente pidió que la recibieran en el inmueble.
        Assert.Equal(2, suyas.Count);
        Assert.All(suyas, v => Assert.Null(v.Mode));
    }
}
