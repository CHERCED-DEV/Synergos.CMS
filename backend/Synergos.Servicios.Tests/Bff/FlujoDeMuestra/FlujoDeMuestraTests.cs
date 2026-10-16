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
using Synergos.CMS.Tests.Architecture;

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
            await Post("booking", "v1/resources", new { subjectKind = "muestra.profesional", subjectId = "dra-1", capacity = 1, timeZoneId = "UTC" }, "agenda");
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

    [Fact]
    public void Declararlo_toca_como_mucho_cuatro_ficheros_propios()
    {
        var propios = Directory.EnumerateFiles(Carpeta(), "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("Tests.cs", StringComparison.Ordinal))
            .ToList();

        Assert.True(propios.Count is > 0 and <= 4,
            $"Declarar la muestra tocó {propios.Count} ficheros propios ({string.Join(", ", propios.Select(Path.GetFileName))}) y la meta es ≤ 4.");
    }

    [Fact]
    public void La_cuenta_ignora_los_comentarios_y_las_lineas_en_blanco()
    {
        Assert.Equal(2, LineasDeCodigo("// uno\nvar a = 1;\n\n/* dos\n tres */\n  /// doc\nvar b = 2;\n"));
    }
}
