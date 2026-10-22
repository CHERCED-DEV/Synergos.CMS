using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Core;
using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Contratos;

namespace Synergos.CMS.Tests.Bff.FlujoDeMuestra;

/// <summary>
/// Un flujo del caso común —apartar una hora y autorizar el cobro; capturar, confirmar y avisar— declarado
/// en JSON y servido por el host genérico contra las capacidades REALES en proceso (núcleo N1, historia 1).
/// </summary>
/// <remarks>
/// <para><b>Contra las reales</b> (<see cref="ArnesDeCapacidades"/>), y no contra dobles: lo que se prueba es
/// que el host, los adaptadores comunes y los pasos de agenda hablan con Booking, Pricing, Payments y
/// Notifications como dicen sus contratos. Lo que ninguna real sabe hacer a pedido (rechazar un cobro,
/// fallar una confirmación) se inyecta en el cable, sin tocar el host.</para>
/// </remarks>
public sealed class FlujoDeMuestraTests
{
    private const string Llave = "llave-de-la-muestra";

    private static readonly DateTimeOffset Inicio = new(DateTimeOffset.UtcNow.Date.AddDays(10).AddHours(15), TimeSpan.Zero);

    private static string Carpeta() => Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "FlujoDeMuestra", "flujos");

    /// <summary>La muestra arrancada, con sus capacidades y lo que pone la puerta.</summary>
    private sealed class Muestra : IAsyncDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "muestra-" + Guid.NewGuid().ToString("N"));
        private WebApplication? _app;

        public ArnesDeCapacidades Capacidades { get; } = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>("notifications");

        public HttpClient Cliente { get; private set; } = null!;

        public SagaEngine<SagaDeFlujo> Sagas => _app!.Services.GetRequiredService<SagaEngine<SagaDeFlujo>>();

        public static async Task<Muestra> Arrancar(decimal precio = 50_000m, bool conPlantilla = true,
            Func<HttpRequestMessage, string?>? falla = null)
        {
            var m = new Muestra();
            await m.Aprovisionar(precio, conPlantilla);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["muestra:Flujos:Carpeta"] = Carpeta(),
                ["muestra:Storage:Root"] = m._raiz,
                ["Muestra:ApiKey"] = Llave,
            });
            builder.Services.AddSingleton<IHttpClientFactory>(falla is null ? m.Capacidades : new ConFallo(m.Capacidades, falla));

            m._app = ArranqueDeLaMuestra.Construir(builder);
            await m._app.StartAsync();
            m.Cliente = m._app.GetTestClient();
            m.Cliente.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
            return m;
        }

        public async ValueTask DisposeAsync()
        {
            Cliente.Dispose();
            if (_app is not null) await _app.DisposeAsync();
            Capacidades.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }

        private async Task Aprovisionar(decimal precio, bool conPlantilla)
        {
            // Con franja de 30 y horario de 08 a 20 todos los días (NB-1/NB-2): la hora de la muestra cae en su grilla, y
            // la lectura de disponibilidad tiene franjas que contar.
            await Post("booking", "v1/resources", new
            {
                subjectKind = "muestra.profesional", subjectId = "dra-1", capacity = 1, timeZoneId = "UTC", slotMinutes = 30,
                opening = Enum.GetNames<DayOfWeek>().Select(d => new { day = d, from = "08:00", to = "20:00" }).ToArray(),
            }, "agenda");
            await Post("pricing", "v1/prices", new
            {
                subjectKind = "muestra.servicio", subjectId = "consulta", amount = new { amount = precio, currency = "COP" }, taxRateBasisPoints = 0,
            }, "precio");
            if (conPlantilla)
            {
                await Post("notifications", "v1/templates", new
                {
                    key = "muestra.cita.confirmada", channel = "Email",
                    subject = "Tu cita {numero}", body = "<p>Hola {nombre}: tu cita {numero} en {sitio} quedó confirmada ({total}). {enlace}</p>",
                }, "plantilla");
            }
        }

        private async Task Post(string capacidad, string ruta, object cuerpo, string llave)
        {
            using var http = Capacidades.CreateClient(capacidad);
            using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
            peticion.Headers.Add("Idempotency-Key", llave);
            using var r = await http.SendAsync(peticion);
            Assert.True(r.StatusCode == HttpStatusCode.Created, $"{capacidad} {ruta}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(
            string llave = "cita-1", string sujeto = "u-1", string claseDelProfesional = "muestra.profesional")
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/muestra.cita")
            {
                Content = JsonContent.Create(new
                {
                    profesional = new { kind = claseDelProfesional, id = "dra-1" },
                    ventana = new { start = Inicio, end = Inicio.AddMinutes(30) },
                    servicio = new { kind = "muestra.servicio", id = "consulta" },
                }),
            };
            peticion.Headers.Add("Idempotency-Key", llave);
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"muestra.paciente:{sujeto}");
            return Enviar(peticion);
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar(string id, string sujeto = "u-1")
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/muestra.cita/{id}/cerrar");
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"muestra.paciente:{sujeto}");
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { correo = "ana@ejemplo.co", nombre = "Ana", enlace = "https://sitio.ejemplo/citas", sitio = "Clínica de prueba" }))));
            return Enviar(peticion);
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Consultar(string id, string sujeto = "u-1")
        {
            var peticion = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/muestra.cita/{id}");
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"muestra.paciente:{sujeto}");
            return Enviar(peticion);
        }

        /// <summary>La lectura de disponibilidad, SIN sujeto: la pide quien todavía no inició sesión (NB-3).</summary>
        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Disponibilidad(object cuerpo)
            => Enviar(new HttpRequestMessage(HttpMethod.Post, "v1/flujos/muestra.cita/disponibilidad") { Content = JsonContent.Create(cuerpo) });

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Listar(string consulta = "", string? sujeto = "u-1")
        {
            var peticion = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/muestra.cita{consulta}");
            if (sujeto is not null) peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"muestra.paciente:{sujeto}");
            return Enviar(peticion);
        }

        /// <summary>Devolver una parte de una cita completada: la operación posterior, un flujo aparte (P6 = A).</summary>
        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Devolver(
            string cita, decimal monto, string llave, string sujeto = "u-1", string flujo = "muestra.devolucion", string clase = "muestra.paciente")
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/{flujo}")
            {
                Content = JsonContent.Create(new { cita, monto = new { amount = monto, currency = "COP" } }),
            };
            peticion.Headers.Add("Idempotency-Key", llave);
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"{clase}:{sujeto}");
            return Enviar(peticion);
        }

        /// <summary>Una operación cualquiera de la muestra, con lo que pondría la puerta.</summary>
        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Pedir(string ruta, object? cuerpo, string sujeto, string? llave = null)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo ?? new { }) };
            if (llave is not null) peticion.Headers.Add("Idempotency-Key", llave);
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto);
            return Enviar(peticion);
        }

        public async Task<JsonElement> Leer(string capacidad, string ruta)
        {
            using var http = Capacidades.CreateClient(capacidad);
            return await http.GetFromJsonAsync<JsonElement>(ruta);
        }

        private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
        {
            using (peticion)
            {
                using var r = await Cliente.SendAsync(peticion);
                var texto = await r.Content.ReadAsStringAsync();
                return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
            }
        }
    }

    /// <summary>Contesta un rechazo en el cable cuando <c>falla</c> devuelve un código; si no, deja pasar.</summary>
    private sealed class ConFallo(IHttpClientFactory real, Func<HttpRequestMessage, string?> falla) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var interno = real.CreateClient(name);
            return new HttpClient(new Cable(interno, falla)) { BaseAddress = interno.BaseAddress };
        }

        private sealed class Cable(HttpClient interno, Func<HttpRequestMessage, string?> falla) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                if (falla(r) is { } codigo)
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict)
                    {
                        Content = JsonContent.Create(new { code = codigo, detail = "inyectado en el cable" }),
                    };
                }

                using var copia = new HttpRequestMessage(r.Method, r.RequestUri) { Content = r.Content };
                foreach (var h in r.Headers) copia.Headers.TryAddWithoutValidation(h.Key, h.Value);
                return await interno.SendAsync(copia, ct);
            }
        }
    }

    private static bool Es(HttpRequestMessage r, string metodo, string patron)
        => r.Method.Method == metodo && Regex.IsMatch(r.RequestUri!.AbsolutePath, patron);

    [Fact]
    public async Task Abre_aparta_y_autoriza_cierra_captura_confirma_y_avisa()
    {
        await using var m = await Muestra.Arrancar();

        var (estado, abierta) = await m.Abrir();
        Assert.True(estado == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());
        Assert.Equal(50_000m, abierta.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        var id = abierta.GetProperty("id").GetString()!;

        var (cerro, cerrada) = await m.Cerrar(id);
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        var reserva = cerrada.GetProperty("datos").GetProperty("reservaId").GetString()!;

        Assert.Equal("Confirmed", (await m.Leer("booking", $"v1/reservations/{reserva}")).GetProperty("status").GetString());
        var cobro = m.Sagas.Find(id)!.Datos["cobroId"].Valor!.Value.GetString()!;
        Assert.Equal("Captured", (await m.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
        var avisos = (await m.Leer("notifications", "v1/deliveries?toKind=muestra.paciente&toId=u-1")).GetProperty("items");
        Assert.Equal(1, avisos.GetArrayLength());
    }

    [Fact]
    public async Task La_disponibilidad_la_ve_un_invitado_y_cuenta_lo_que_la_agenda_ya_aparto()
    {
        // NB-3 de 011: quien reserva quiere ver las horas antes de iniciar sesión. Sin sujeto, sin saga y sin apartar
        // nada; la hora que otra cita ya apartó sale llena, porque lo libre lo cuenta la agenda real.
        await using var m = await Muestra.Arrancar();
        var (abrio, abierta) = await m.Abrir("cita-1");
        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());

        var (estado, lectura) = await m.Disponibilidad(new
        {
            profesional = new { kind = "muestra.profesional", id = "dra-1" },
            desde = Inicio.AddHours(-1),
            hasta = Inicio.AddHours(1),
        });

        Assert.True(estado == HttpStatusCode.OK, lectura.ToString());
        Assert.Equal("muestra.cita", lectura.GetProperty("flujo").GetString());
        var franjas = lectura.GetProperty("datos").GetProperty("franjas").EnumerateArray()
            .Select(f => (f.GetProperty("inicio").GetDateTimeOffset(), f.GetProperty("libres").GetInt32()))
            .ToList();
        Assert.Equal(new[] { (Inicio.AddHours(-1), 1), (Inicio.AddMinutes(-30), 1), (Inicio, 0), (Inicio.AddMinutes(30), 1) }, franjas);

        var (otraClase, problema) = await m.Disponibilidad(new
        {
            profesional = new { kind = "muestra.paciente", id = "dra-1" }, desde = Inicio, hasta = Inicio.AddHours(1),
        });
        Assert.Equal((HttpStatusCode.BadRequest, "muestra.entrada_invalida"), (otraClase, problema.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task Mis_citas_son_las_del_dueno_por_estado_y_por_pagina()
    {
        // NB-6 de 011 y NN-7 de 005: sin esto, una cita sólo se encontraba por el enlace del aviso. La segunda
        // pide la MISMA hora, así que la agenda la rechaza y queda deshecha: dos sagas del mismo dueño en dos
        // estados. Las de otro no salen nunca, y el dueño lo pone la puerta, no la consulta.
        await using var m = await Muestra.Arrancar();
        var (_, una) = await m.Abrir("cita-1");
        var (cerro, _) = await m.Cerrar(una.GetProperty("id").GetString()!);
        Assert.Equal(HttpStatusCode.OK, cerro);
        var (rechazada, _) = await m.Abrir("cita-2");
        Assert.Equal(HttpStatusCode.Conflict, rechazada);

        var (estado, todas) = await m.Listar();
        var (_, completadas) = await m.Listar("?estado=Completed");
        var (_, pagina) = await m.Listar("?limit=1");
        var (_, deOtro) = await m.Listar(sujeto: "u-2");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(["cita-1", "cita-2"], todas.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
        var completada = Assert.Single(completadas.GetProperty("items").EnumerateArray());
        Assert.Equal(("cita-1", "Completed"), (completada.GetProperty("id").GetString(), completada.GetProperty("estado").GetString()));
        Assert.Equal(50_000m, completada.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        Assert.Equal((1, 2, true), (pagina.GetProperty("items").GetArrayLength(), pagina.GetProperty("total").GetInt32(), pagina.GetProperty("hasMore").GetBoolean()));
        Assert.Equal(0, deOtro.GetProperty("total").GetInt32());

        var (inventado, problema) = await m.Listar("?estado=Pagada");
        Assert.Equal((HttpStatusCode.BadRequest, "muestra.entrada_invalida"), (inventado, problema.GetProperty("code").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await m.Listar(sujeto: null)).Estado);
    }

    [Fact]
    public async Task Lo_posterior_al_cierre_devuelve_una_parte_de_la_cita_una_vez_y_hasta_lo_cobrado()
    {
        // P6 = A del lote 6: devolver es un flujo aparte, posterior a muestra.cita, que lee su cobro con flow.leer-original
        // y devuelve contra Payments real. La cita no se toca, y el tope acumulado lo pone la capacidad.
        await using var m = await Muestra.Arrancar();
        var (_, abierta) = await m.Abrir("cita-1");
        var cita = abierta.GetProperty("id").GetString()!;

        var (antes, sinCerrar) = await m.Devolver(cita, 10_000m, "dev-0");
        Assert.Equal((HttpStatusCode.Conflict, PasoLeerOriginal.NoCompletada), (antes, sinCerrar.GetProperty("code").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await m.Cerrar(cita)).Estado);
        var cobro = m.Sagas.Find(cita)!.Datos["cobroId"].Valor!.Value.GetString()!;
        async Task<decimal> Devuelto() => (await m.Leer("payments", $"v1/payments/{cobro}")).GetProperty("refunded").GetProperty("amount").GetDecimal();

        var (devolvio, devolucion) = await m.Devolver(cita, 20_000m, "dev-1");
        Assert.True(devolvio == HttpStatusCode.Created, devolucion.ToString());
        Assert.Equal("Completed", devolucion.GetProperty("estado").GetString());
        Assert.Equal(20_000m, await Devuelto());

        Assert.Equal(HttpStatusCode.Created, (await m.Devolver(cita, 20_000m, "dev-1")).Estado);
        Assert.Equal(20_000m, await Devuelto());

        var (_, demas) = await m.Devolver(cita, 40_000m, "dev-2");
        Assert.Equal("payments.refund_over_captured", demas.GetProperty("code").GetString());
        Assert.Equal(20_000m, await Devuelto());

        var (ajena, nada) = await m.Devolver(cita, 1_000m, "dev-3", sujeto: "u-2");
        Assert.Equal((HttpStatusCode.NotFound, PasoLeerOriginal.NoEncontrada), (ajena, nada.GetProperty("code").GetString()));
        Assert.Equal(20_000m, await Devuelto());
        Assert.Equal(SagaStatus.Completed, m.Sagas.Find(cita)!.Status);
    }

    [Fact]
    public async Task El_personal_devuelve_la_cita_de_otro_por_su_flujo_y_el_flujo_del_dueno_no_lo_deja()
    {
        // P8 = A del lote 6: muestra.reembolso es «acceso: personal». Quien pide no es el dueño de la cita —la puerta ya
        // comprobó su rol—, así que flow.leer-original no aplica la regla de dueño; el sujeto es la persona del personal.
        await using var m = await Muestra.Arrancar();
        var (_, abierta) = await m.Abrir("cita-1");
        var cita = abierta.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await m.Cerrar(cita)).Estado);
        var cobro = m.Sagas.Find(cita)!.Datos["cobroId"].Valor!.Value.GetString()!;

        var (porElDueno, nada) = await m.Devolver(cita, 5_000m, "dev-p1", sujeto: "s-1", clase: "muestra.personal");
        Assert.Equal((HttpStatusCode.NotFound, PasoLeerOriginal.NoEncontrada), (porElDueno, nada.GetProperty("code").GetString()));

        var (estado, reembolso) = await m.Devolver(cita, 15_000m, "reemb-1", sujeto: "s-1", flujo: "muestra.reembolso", clase: "muestra.personal");
        Assert.True(estado == HttpStatusCode.Created, reembolso.ToString());
        Assert.Equal(15_000m, (await m.Leer("payments", $"v1/payments/{cobro}")).GetProperty("refunded").GetProperty("amount").GetDecimal());
        Assert.Equal(Ref.Create("muestra.personal", "s-1"), m.Sagas.Find(reembolso.GetProperty("id").GetString()!)!.Dueno);
    }

    [Fact]
    public async Task La_garantia_conservada_sigue_autorizada_al_completar_y_el_personal_la_liquida_despues()
    {
        // P7 + P6 + P8 del lote 6, contra Payments real: la garantía se retiene (payments.autorizar) y se conserva al
        // cerrar (payments.conservar), así que la saga completa con la autorización VIVA; días después, alguien del
        // personal la liquida con un flujo aparte: captura y devuelve lo que no se cobra.
        await using var m = await Muestra.Arrancar();
        const string Inquilino = "muestra.paciente:u-1";

        var (abrio, abierta) = await m.Pedir("v1/flujos/muestra.garantia", new { garantia = new { amount = 300_000m, currency = "COP" } }, Inquilino, "gar-1");
        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
        var alquiler = abierta.GetProperty("id").GetString()!;
        var (cerro, cerrada) = await m.Pedir($"v1/flujos/muestra.garantia/{alquiler}/cerrar", null, Inquilino);
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        var garantia = cerrada.GetProperty("datos").GetProperty("garantiaId").GetString()!;
        Assert.DoesNotContain(m.Sagas.Find(alquiler)!.Compensations, c => c.IsPending);

        var retenida = await m.Leer("payments", $"v1/payments/{garantia}");
        Assert.Equal(("Authorized", 0m), (retenida.GetProperty("status").GetString(), retenida.GetProperty("refunded").GetProperty("amount").GetDecimal()));

        var (liquido, liquidacion) = await m.Pedir(
            "v1/flujos/muestra.liquidacion", new { alquiler, devolver = new { amount = 200_000m, currency = "COP" } }, "muestra.personal:s-1", "liq-1");
        Assert.True(liquido == HttpStatusCode.Created, liquidacion.ToString());
        Assert.Equal("Completed", liquidacion.GetProperty("estado").GetString());

        var liquidada = await m.Leer("payments", $"v1/payments/{garantia}");
        Assert.Equal(("Captured", 300_000m, 200_000m), (liquidada.GetProperty("status").GetString(),
            liquidada.GetProperty("amount").GetProperty("amount").GetDecimal(), liquidada.GetProperty("refunded").GetProperty("amount").GetDecimal()));
        Assert.Equal(HttpStatusCode.Created, (await m.Pedir(
            "v1/flujos/muestra.liquidacion", new { alquiler, devolver = new { amount = 200_000m, currency = "COP" } }, "muestra.personal:s-1", "liq-1")).Estado);
        Assert.Equal(200_000m, (await m.Leer("payments", $"v1/payments/{garantia}")).GetProperty("refunded").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Soltar_la_garantia_no_publica_cotizar_y_abrirla_la_anula()
    {
        // NR-8 de 008: muestra.liberacion anula la garantía en la fase que abre, con un paso que no pide llave. Si se
        // anticipara, «cotizar» la anularía sin saga, sin llave y sin rastro: no se publica, y la garantía sigue viva.
        var doc = await ContratoDeFlujosGenericosTests.Documento();
        Assert.NotNull(doc["paths"]!["/v1/flujos/muestra.cita/cotizar"]);
        Assert.NotNull(doc["paths"]!["/v1/flujos/muestra.liberacion"]);
        Assert.Null(doc["paths"]!["/v1/flujos/muestra.liberacion/cotizar"]);

        await using var m = await Muestra.Arrancar();
        const string Inquilino = "muestra.paciente:u-1";
        var (_, abierta) = await m.Pedir("v1/flujos/muestra.garantia", new { garantia = new { amount = 300_000m, currency = "COP" } }, Inquilino, "gar-1");
        var alquiler = abierta.GetProperty("id").GetString()!;
        var (_, cerrada) = await m.Pedir($"v1/flujos/muestra.garantia/{alquiler}/cerrar", null, Inquilino);
        var garantia = cerrada.GetProperty("datos").GetProperty("garantiaId").GetString()!;

        var (cotizo, _) = await m.Pedir("v1/flujos/muestra.liberacion/cotizar", new { alquiler }, Inquilino);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, cotizo);
        Assert.Equal("Authorized", (await m.Leer("payments", $"v1/payments/{garantia}")).GetProperty("status").GetString());

        var (solto, liberacion) = await m.Pedir("v1/flujos/muestra.liberacion", new { alquiler }, Inquilino, "lib-1");
        Assert.True(solto == HttpStatusCode.Created, liberacion.ToString());
        Assert.Equal("Completed", liberacion.GetProperty("estado").GetString());
        Assert.Equal("Voided", (await m.Leer("payments", $"v1/payments/{garantia}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Un_cobro_rechazado_suelta_la_hora_y_devuelve_el_rechazo_original()
    {
        await using var m = await Muestra.Arrancar(falla: r => Es(r, "POST", "^/v1/payments$") ? "payments.payment_declined" : null);

        var (estado, cuerpo) = await m.Abrir();

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("payments.payment_declined", cuerpo.GetProperty("code").GetString());
        var saga = m.Sagas.Find("cita-1")!;
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        var apartado = saga.Datos["apartadoId"].Valor!.Value.GetString()!;
        Assert.True((await m.Leer("booking", $"v1/holds/{apartado}")).GetProperty("released").GetBoolean());
    }

    [Fact]
    public async Task Si_confirmar_falla_tras_capturar_se_devuelve_el_cobro_y_se_suelta_la_hora()
    {
        await using var m = await Muestra.Arrancar(falla: r => Es(r, "POST", "^/v1/holds/[^/]+/confirm$") ? "booking.hold_expired" : null);
        var (_, abierta) = await m.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cuerpo) = await m.Cerrar(id);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.hold_expired", cuerpo.GetProperty("code").GetString());
        var saga = m.Sagas.Find(id)!;
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        var cobro = saga.Datos["cobroId"].Valor!.Value.GetString()!;
        Assert.Equal(0m, (await m.Leer("payments", $"v1/payments/{cobro}")).GetProperty("refundable").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task El_doble_clic_no_captura_dos_veces_y_una_cita_ajena_no_existe()
    {
        await using var m = await Muestra.Arrancar();
        var (_, abierta) = await m.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NotFound, (await m.Consultar(id, sujeto: "u-2")).Estado);
        Assert.Equal(HttpStatusCode.NotFound, (await m.Cerrar(id, sujeto: "u-2")).Estado);

        Assert.Equal(HttpStatusCode.OK, (await m.Cerrar(id)).Estado);
        var (otraVez, cuerpo) = await m.Cerrar(id);
        Assert.Equal(HttpStatusCode.OK, otraVez);
        Assert.Equal("Completed", cuerpo.GetProperty("estado").GetString());

        // La misma llave devuelve la misma cita, sin apartar otra.
        var (reabre, misma) = await m.Abrir();
        Assert.Equal(HttpStatusCode.Created, reabre);
        Assert.Equal(id, misma.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Una_referencia_de_otra_clase_que_la_declarada_no_entra_ni_llega_a_ninguna_capacidad()
    {
        // NR-1 de 008: la clase de «profesional» la declara la definición, y el host no lee otra.
        var llamadas = new List<string>();
        await using var m = await Muestra.Arrancar(falla: r =>
        {
            // El sondeo de los avisos en cola corre solo, sin que nadie lo pida: no es de esta petición.
            if (r.RequestUri!.AbsolutePath != "/v1/deliveries/queued")
            {
                lock (llamadas) llamadas.Add($"{r.Method} {r.RequestUri.AbsolutePath}");
            }
            return null;
        });

        var (estado, cuerpo) = await m.Abrir(llave: "otra-clase", claseDelProfesional: "muestra.otra-clase");

        Assert.True(estado == HttpStatusCode.BadRequest, cuerpo.ToString());
        Assert.Equal("muestra.entrada_invalida", cuerpo.GetProperty("code").GetString());
        Assert.Contains("profesional.kind", cuerpo.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Empty(llamadas);
        Assert.Null(m.Sagas.Find("otra-clase"));

        // El control: la clase declarada abre la cita, por el mismo camino.
        var (abre, abierta) = await m.Abrir();
        Assert.True(abre == HttpStatusCode.Created, abierta.ToString());
        Assert.NotEmpty(llamadas);
    }

    [Fact]
    public async Task Gratis_no_cobra_y_un_aviso_que_no_sale_no_deshace_la_cita()
    {
        await using var m = await Muestra.Arrancar(precio: 0m, conPlantilla: false);
        var (_, abierta) = await m.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cerrada) = await m.Cerrar(id);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, m.Sagas.Find(id)!.Datos["cobroId"].Valor?.ValueKind ?? JsonValueKind.Null);
    }
}

/// <summary>
/// La medida de la meta (SC-001, SC-002): lo que un vertical del caso común escribe en C# para declarar un
/// flujo, contado como el informe 81 (sin comentarios ni líneas en blanco), y en cuántos ficheros.
/// </summary>
public sealed class MetricaDelFlujoDeMuestraTests
{
    private static string Carpeta() => Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "FlujoDeMuestra");

    /// <summary>Las líneas de código de un fichero C#: sin comentarios, sin líneas en blanco.</summary>
    internal static int LineasDeCodigo(string fuente)
    {
        var sinBloques = Regex.Replace(fuente, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return sinBloques.Split('\n')
            .Select(l => l.Trim())
            .Count(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_flujo_nuevo_del_caso_comun_cuesta_como_mucho_60_lineas_de_CSharp_propio()
    {
        var propio = Directory.EnumerateFiles(Carpeta(), "*.cs").Where(f => !f.EndsWith("Tests.cs", StringComparison.Ordinal)).ToList();

        var lineas = propio.Sum(f => LineasDeCodigo(File.ReadAllText(f)));

        Assert.True(propio.Count > 0, "No se encontró el arranque de la muestra: la medida dejó de ver.");
        Assert.True(lineas <= 60, $"El C# propio de la muestra son {lineas} líneas y la meta es ≤ 60 (hoy un flujo declarado costaba ~316).");
    }

    /// <summary>
    /// SC-002, que es POR FLUJO: declarar uno toca su definición, sus pasos de dominio si los tiene, el arranque y la
    /// configuración.
    /// </summary>
    /// <remarks>
    /// La carpeta sirve varios flujos de muestra con UN arranque (la cita, la compra y las dos operaciones posteriores
    /// al cierre del lote 6), así que contar la carpeta entera mediría cuántas muestras hay, no lo que cuesta una. Lo
    /// que no es el arranque ni una definición no puede estar aquí sin contarse.
    /// </remarks>
    [Fact]
    public void Declarar_un_flujo_toca_como_mucho_cuatro_ficheros_propios()
    {
        var propios = Directory.EnumerateFiles(Carpeta(), "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("Tests.cs", StringComparison.Ordinal))
            .ToList();
        var definiciones = propios.Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "flujos" && f.EndsWith(".json", StringComparison.Ordinal)).ToList();
        var deTodos = propios.Except(definiciones).ToList();

        Assert.True(definiciones.Count > 0 && deTodos.Count > 0, "No se encontró la muestra: la medida dejó de ver.");
        Assert.True(deTodos.Count + 1 <= 4,
            $"Declarar un flujo toca su definición y {deTodos.Count} ficheros propios más ({string.Join(", ", deTodos.Select(Path.GetFileName))}) "
            + "y la meta es ≤ 4.");
    }

    [Fact]
    public void La_cuenta_ignora_los_comentarios_y_las_lineas_en_blanco()
    {
        Assert.Equal(2, LineasDeCodigo("// uno\nvar a = 1;\n\n/* dos\n tres */\n  /// doc\nvar b = 2;\n"));
    }
}
