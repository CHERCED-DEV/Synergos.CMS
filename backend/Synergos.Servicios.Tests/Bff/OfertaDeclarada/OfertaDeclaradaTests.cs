using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La oferta declarada del núcleo (P12; NM-1 de 010, 001-P11 supuesto A con N1): un orquestador declarado la monta con
/// <c>{Origen}:Oferta:Clase</c>, publica el precio de la pieza y el de cada variante contra <c>Api.Pricing</c> real, y su
/// flujo cotiza con <c>forma.una-linea-de-oferta</c> el MISMO nombre que publicó.
/// </summary>
public sealed class OfertaDeclaradaTests
{
    private const string Llave = "llave-de-la-muestra";
    private const string Clase = "muestra.curso";

    private static object Oferta(
        string pieza = "mate-1", decimal precio = 900_000m, string moneda = "COP", params (string Codigo, decimal Total)[] variantes)
        => new { pieza, precio, moneda, variantes = variantes.Select(v => new { codigo = v.Codigo, total = v.Total }).ToList() };

    private static readonly (string, decimal)[] Planes = [("contado", 850_000m), ("premium", 1_200_000m), ("beca", 0m)];

    /// <summary>La pieza con su agenda: los lunes de 08:00 a 12:00 en Bogotá, por franjas de 30 minutos.</summary>
    private static object ConAgenda(
        decimal precio = 0m, int cupos = 1, int franja = 30, string zona = "America/Bogota", string desde = "08:00", string hasta = "12:00",
        string dia = "monday", int? aviso = 120)
        => new
        {
            pieza = "mate-1",
            precio,
            moneda = "COP",
            agenda = new { cupos, franjaMinutos = franja, horario = new[] { new { dia, desde, hasta } }, zona, avisoMinutos = aviso },
        };

    /// <summary>Un lunes de dentro de tres semanas o más, a medianoche en Bogotá.</summary>
    private static DateTimeOffset UnLunes()
    {
        var hoy = DateTimeOffset.UtcNow.Date.AddDays(21);
        while (hoy.DayOfWeek != DayOfWeek.Monday) hoy = hoy.AddDays(1);
        return new DateTimeOffset(hoy, TimeSpan.Zero).AddHours(5);
    }

    [Fact]
    public async Task Publicar_con_agenda_registra_el_recurso_de_la_pieza_con_su_horario_y_su_franja()
    {
        await using var a = await Academia.Arrancar();

        var (estado, publicada) = await a.Publicar(ConAgenda());

        Assert.True(estado == HttpStatusCode.OK, publicada.ToString());
        var (hallado, recurso) = await a.Recurso();
        Assert.True(hallado == HttpStatusCode.OK, recurso.ToString());
        Assert.Equal(recurso.GetProperty("id").GetString(), publicada.GetProperty("recursoId").GetString());
        Assert.Equal(1, recurso.GetProperty("capacity").GetInt32());
        Assert.Equal(30, recurso.GetProperty("slotMinutes").GetInt32());
        Assert.Equal(120, recurso.GetProperty("cancellationNoticeMinutes").GetInt32());
        Assert.Equal("America/Bogota", recurso.GetProperty("timeZoneId").GetString());
        var bloque = Assert.Single(recurso.GetProperty("opening").EnumerateArray());
        Assert.Equal(("Monday", "08:00", "12:00"),
            (bloque.GetProperty("day").GetString(), bloque.GetProperty("from").GetString(), bloque.GetProperty("to").GetString()));
        Assert.Equal(8, await a.Franjas(recurso.GetProperty("id").GetString()!, UnLunes()));
    }

    [Fact]
    public async Task Republicar_la_misma_agenda_no_registra_otro_recurso_y_otra_agenda_lo_revisa_sin_cancelar_reservas()
    {
        await using var a = await Academia.Arrancar();
        var (_, primera) = await a.Publicar(ConAgenda());
        var recurso = primera.GetProperty("recursoId").GetString()!;
        var reserva = await a.Reservar(recurso, UnLunes().AddHours(11), 30);

        var (_, igual) = await a.Publicar(ConAgenda(), "oferta-2");
        var (estado, revisada) = await a.Publicar(ConAgenda(cupos: 2, hasta: "10:00"), "oferta-3");

        Assert.Equal(recurso, igual.GetProperty("recursoId").GetString());
        Assert.Equal(0, igual.GetProperty("reservasFuera").GetInt32());
        Assert.True(estado == HttpStatusCode.OK, revisada.ToString());
        Assert.Equal(recurso, revisada.GetProperty("recursoId").GetString());
        Assert.Equal(1, revisada.GetProperty("reservasFuera").GetInt32());
        var (_, enLaAgenda) = await a.Recurso();
        Assert.Equal(recurso, enLaAgenda.GetProperty("id").GetString());
        Assert.Equal(2, enLaAgenda.GetProperty("capacity").GetInt32());
        Assert.Equal(4, await a.Franjas(recurso, UnLunes()));
        Assert.Equal("Confirmed", (await a.Reserva(reserva)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Otra_zona_se_rechaza_sin_tocar_la_agenda_ni_el_precio()
    {
        await using var a = await Academia.Arrancar();
        Assert.Equal(HttpStatusCode.OK, (await a.Publicar(ConAgenda(precio: 50_000m))).Estado);

        var (estado, cuerpo) = await a.Publicar(ConAgenda(precio: 70_000m, cupos: 3, zona: "Europe/Madrid"), "oferta-2");

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("muestra.oferta_no_actualizable", cuerpo.GetProperty("code").GetString());
        Assert.Equal(50_000m, await a.Total(null));
        var (_, recurso) = await a.Recurso();
        Assert.Equal("America/Bogota", recurso.GetProperty("timeZoneId").GetString());
        Assert.Equal(1, recurso.GetProperty("capacity").GetInt32());
    }

    [Fact]
    public async Task Republicar_sin_agenda_deja_la_agenda_como_estaba()
    {
        await using var a = await Academia.Arrancar();
        var (_, primera) = await a.Publicar(ConAgenda());

        var (estado, sinAgenda) = await a.Publicar(Oferta(precio: 10_000m), "oferta-2");

        Assert.True(estado == HttpStatusCode.OK, sinAgenda.ToString());
        Assert.Equal(primera.GetProperty("recursoId").GetString(), sinAgenda.GetProperty("recursoId").GetString());
        var (_, recurso) = await a.Recurso();
        Assert.Equal(1, recurso.GetProperty("capacity").GetInt32());
        Assert.Equal(10_000m, await a.Total(null));
    }

    [Fact]
    public async Task Una_oferta_sin_agenda_no_registra_recurso()
    {
        await using var a = await Academia.Arrancar();

        var (_, publicada) = await a.Publicar(Oferta());

        Assert.Equal(JsonValueKind.Null, publicada.GetProperty("recursoId").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Recurso()).Estado);
    }

    [Fact]
    public async Task Publicar_fija_el_precio_de_la_pieza_y_el_de_cada_variante_y_el_flujo_cotiza_ese_mismo_nombre()
    {
        await using var a = await Academia.Arrancar();

        var (estado, publicada) = await a.Publicar(Oferta(variantes: Planes));

        Assert.True(estado == HttpStatusCode.OK, publicada.ToString());
        Assert.Equal(4, publicada.GetProperty("precios").GetInt32());
        Assert.Equal(new[] { "contado", "premium", "beca" }, publicada.GetProperty("variantes").EnumerateArray().Select(v => v.GetString()!));
        Assert.Equal(900_000m, await a.Total(null));
        Assert.Equal(1_200_000m, await a.Total("premium"));
        Assert.Equal(850_000m, await a.Total("contado"));
        Assert.Equal(0m, await a.Total("beca"));
    }

    [Fact]
    public async Task Republicar_sin_una_variante_la_deja_de_cotizar_y_las_demas_siguen()
    {
        await using var a = await Academia.Arrancar();
        Assert.Equal(HttpStatusCode.OK, (await a.Publicar(Oferta(variantes: Planes))).Estado);

        var (estado, cuerpo) = await a.Publicar(Oferta(precio: 950_000m, variantes: [("contado", 800_000m), ("beca", 0m)]), "oferta-2");

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Equal(3, cuerpo.GetProperty("precios").GetInt32());
        Assert.Equal("pricing.price_not_in_effect", (await a.Abrir("premium", "m-premium")).Cuerpo.GetProperty("code").GetString());
        Assert.Equal(950_000m, await a.Total(null));
        Assert.Equal(800_000m, await a.Total("contado"));
    }

    [Fact]
    public async Task Retirar_vence_la_pieza_y_sus_variantes_juntas_y_repetir_da_lo_mismo()
    {
        await using var a = await Academia.Arrancar();
        Assert.Equal(HttpStatusCode.OK, (await a.Publicar(Oferta(variantes: Planes))).Estado);

        var (estado, retirada) = await a.Retirar("mate-1");
        var (otra, repetida) = await a.Retirar("mate-1", "retiro-2");

        Assert.True(estado == HttpStatusCode.OK, retirada.ToString());
        Assert.True(retirada.GetProperty("retirada").GetBoolean());
        Assert.Equal(0, retirada.GetProperty("precios").GetInt32());
        Assert.Equal(HttpStatusCode.OK, otra);
        Assert.True(repetida.GetProperty("retirada").GetBoolean());
        foreach (var plan in new string?[] { null, "contado", "premium", "beca" })
        {
            Assert.Equal("pricing.price_not_in_effect", (await a.Abrir(plan, $"m-{plan ?? "pieza"}")).Cuerpo.GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, (await a.Publicar(Oferta(variantes: [("contado", 700_000m)]), "oferta-3")).Estado);
        Assert.Equal(700_000m, await a.Total("contado"));
    }

    [Fact]
    public async Task Repetir_la_publicacion_con_su_llave_da_la_misma_oferta()
    {
        await using var a = await Academia.Arrancar();

        var (_, una) = await a.Publicar(Oferta(variantes: Planes));
        var (estado, otra) = await a.Publicar(Oferta(variantes: Planes));

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(una.ToString(), otra.ToString());
        Assert.Equal(1_200_000m, await a.Total("premium"));
    }

    [Fact]
    public async Task Retirar_lo_que_nunca_se_publico_es_que_no_existe()
    {
        await using var a = await Academia.Arrancar();

        foreach (var pieza in new[] { "mate-1", "..%2Fotra" })
        {
            var (estado, cuerpo) = await a.Retirar(pieza);

            Assert.Equal(HttpStatusCode.NotFound, estado);
            Assert.Equal("muestra.oferta_no_existe", cuerpo.GetProperty("code").GetString());
        }
    }

    public static TheoryData<string, object> Invalidas() => new()
    {
        { "pieza", Oferta(pieza: "mate/1") },
        { "precio", Oferta(precio: -1m) },
        { "moneda", Oferta(moneda: "PESOS") },
        { "variantes.codigo", Oferta(variantes: [("pre/mium", 1m)]) },
        { "variantes.total", Oferta(variantes: [("premium", -1m)]) },
        { "variantes.codigo", Oferta(variantes: [("premium", 1m), ("premium", 2m)]) },
        { "agenda.cupos", ConAgenda(cupos: 0) },
        { "agenda.franjaMinutos", ConAgenda(franja: 0) },
        { "agenda.franjaMinutos", ConAgenda(franja: 300) },
        { "agenda.avisoMinutos", ConAgenda(aviso: -1) },
        { "agenda.zona", ConAgenda(zona: "Bogota/Centro") },
        { "agenda.horario.dia", ConAgenda(dia: "lunes") },
        { "agenda.horario.dia", ConAgenda(dia: "1") },
        { "agenda.horario", ConAgenda(desde: "12:00", hasta: "08:00") },
        { "agenda.horario", ConAgenda(desde: "8am") },
    };

    [Theory]
    [MemberData(nameof(Invalidas))]
    public async Task Una_oferta_invalida_nombra_el_campo_y_no_publica_nada(string campo, object oferta)
    {
        await using var a = await Academia.Arrancar();

        var (estado, cuerpo) = await a.Publicar(oferta);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("muestra.oferta_invalida", cuerpo.GetProperty("code").GetString());
        Assert.Contains($"«{campo}»", cuerpo.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("pricing.price_not_found", (await a.Abrir(null, "m-nada")).Cuerpo.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await a.Recurso()).Estado);
    }

    [Fact]
    public async Task Sin_llave_no_se_publica()
    {
        await using var a = await Academia.Arrancar();

        var (estado, _) = await a.Publicar(Oferta(), llave: null);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("pricing.price_not_found", (await a.Abrir(null, "m-nada")).Cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Montar_la_oferta_sin_su_clase_no_arranca_y_nombra_la_clave()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Academia.Arrancar(clase: null));

        Assert.Contains("«muestra:Oferta:Clase»", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_clase_que_no_es_una_clase_no_arranca()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Academia.Arrancar(clase: "Muestra Curso"));
    }

    // ── El orquestador de la muestra con la oferta montada ──────────────────

    private sealed class Academia : IAsyncDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "oferta-declarada-" + Guid.NewGuid().ToString("N"));
        private WebApplication? _app;

        public ArnesDeCapacidades Capacidades { get; } = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking");

        public HttpClient Cliente { get; private set; } = null!;

        public static async Task<Academia> Arrancar(string? clase = Clase)
        {
            var a = new Academia();
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["muestra:Flujos:Carpeta"] = Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "OfertaDeclarada", "flujos"),
                    ["muestra:Storage:Root"] = a._raiz,
                    ["muestra:Oferta:Clase"] = clase,
                    ["Muestra:ApiKey"] = Llave,
                });
                builder.Services.AddSingleton<IHttpClientFactory>(a.Capacidades);
                builder.AddCorrelation();
                builder.AddOrquestadorDeclarado(ArranqueDeLaMuestra.Vocabulario, typeof(OfertaDeclaradaTests).Assembly);

                a._app = builder.Build();
                a._app.UseCorrelation();
                a._app.UseSharedKeyAuth(a._app.Configuration["Muestra:ApiKey"]);
                a._app.MapFlujosDeclarados();
                a._app.MapOfertaDeclarada();
                await a._app.StartAsync();
                a.Cliente = a._app.GetTestClient();
                a.Cliente.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
                return a;
            }
            catch
            {
                await a.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Cliente?.Dispose();
            if (_app is not null) await _app.DisposeAsync();
            Capacidades.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Publicar(object oferta, string? llave = "oferta-1")
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/ofertas") { Content = JsonContent.Create(oferta) };
            if (llave is not null) peticion.Headers.Add("Idempotency-Key", llave);
            return Enviar(peticion);
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Retirar(string pieza, string llave = "retiro-1")
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/ofertas/{pieza}/retirar");
            peticion.Headers.Add("Idempotency-Key", llave);
            return Enviar(peticion);
        }

        /// <summary>Abre la matrícula de la muestra por la puerta: el curso como referencia de su clase y el plan, anulable.</summary>
        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string? plan, string llave, string curso = "mate-1")
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/muestra.matricula")
            {
                Content = JsonContent.Create(new { curso = new { kind = Clase, id = curso }, plan }),
            };
            peticion.Headers.Add("Idempotency-Key", llave);
            // Un alumno por matrícula: lo que se mide es el precio, no el tope de sagas de un dueño.
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"muestra.alumno:{llave}");
            return Enviar(peticion);
        }

        /// <summary>El total con que abre la matrícula del plan: lo que cotizó el flujo con el nombre de la oferta.</summary>
        public async Task<decimal> Total(string? plan)
        {
            var (estado, cuerpo) = await Abrir(plan, $"m-{plan ?? "pieza"}-{Guid.NewGuid():N}");
            Assert.True(estado == HttpStatusCode.Created, $"La matrícula del plan «{plan}» no abrió: {(int)estado} {cuerpo}");
            return cuerpo.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal();
        }

        /// <summary>El recurso de la pieza en la agenda REAL, leído por la clase de la oferta.</summary>
        public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Recurso(string pieza = "mate-1")
        {
            using var booking = Capacidades.CreateClient("booking");
            return await Enviar(booking, new HttpRequestMessage(HttpMethod.Get, $"v1/resources/por-sujeto?subjectKind={Clase}&subjectId={pieza}"));
        }

        /// <summary>Cuántas franjas lista la agenda real para el recurso en ese día.</summary>
        public async Task<int> Franjas(string recurso, DateTimeOffset dia)
        {
            using var booking = Capacidades.CreateClient("booking");
            var (estado, cuerpo) = await Enviar(booking, new HttpRequestMessage(HttpMethod.Get,
                $"v1/resources/{recurso}/slots?from={Uri.EscapeDataString(dia.ToString("O"))}&to={Uri.EscapeDataString(dia.AddDays(1).ToString("O"))}"));
            Assert.True(estado == HttpStatusCode.OK, $"La agenda no listó las franjas: {(int)estado} {cuerpo}");
            return cuerpo.GetProperty("items").GetArrayLength();
        }

        /// <summary>Reserva de verdad una ventana en la agenda real: aparta y confirma.</summary>
        public async Task<string> Reservar(string recurso, DateTimeOffset inicio, int minutos)
        {
            using var booking = Capacidades.CreateClient("booking");
            var apartar = new HttpRequestMessage(HttpMethod.Post, "v1/holds")
            {
                Content = JsonContent.Create(new
                {
                    resourceId = recurso, start = inicio, end = inicio.AddMinutes(minutos), heldForKind = "muestra.alumno", heldForId = "a-1",
                }),
            };
            apartar.Headers.Add("Idempotency-Key", "apartar-1");
            var (estado, apartado) = await Enviar(booking, apartar);
            Assert.True(estado == HttpStatusCode.Created, $"No se apartó la ventana: {(int)estado} {apartado}");

            var confirmar = new HttpRequestMessage(HttpMethod.Post, $"v1/holds/{apartado.GetProperty("id").GetString()}/confirm");
            confirmar.Headers.Add("Idempotency-Key", "confirmar-1");
            var (otro, reserva) = await Enviar(booking, confirmar);
            Assert.True(otro == HttpStatusCode.Created, $"No se confirmó la reserva: {(int)otro} {reserva}");
            return reserva.GetProperty("id").GetString()!;
        }

        /// <summary>La reserva, leída en la agenda real.</summary>
        public async Task<JsonElement> Reserva(string id)
        {
            using var booking = Capacidades.CreateClient("booking");
            return (await Enviar(booking, new HttpRequestMessage(HttpMethod.Get, $"v1/reservations/{id}"))).Item2;
        }

        private Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion) => Enviar(Cliente, peticion);

        private static async Task<(HttpStatusCode, JsonElement)> Enviar(HttpClient cliente, HttpRequestMessage peticion)
        {
            using (peticion)
            {
                using var r = await cliente.SendAsync(peticion);
                var texto = await r.Content.ReadAsStringAsync();
                return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
            }
        }
    }
}
