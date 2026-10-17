using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Propiedades.Domain;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff.Propiedades;

/// <summary>
/// La visita a un inmueble DE VERDAD (008-propiedades-visita): el <c>Program</c> de <c>Bff.Propiedades</c> contra la
/// agenda y los avisos reales del arnés, con lo que pone la puerta en cabeceras.
/// </summary>
/// <remarks>
/// <para>El inmueble se registra en la agenda como lo hace la herramienta de provisión (008-P4): la clase de la ruta
/// directa (<c>realty.listado</c>), capacidad 1, el horario de visitas de 9:00 a 12:00 en la hora del sitio, franjas de
/// 60 minutos (008-P3, NB-1) y dos horas de aviso para cancelar. Al lado, un profesional de Salud con el mismo horario:
/// el recurso AJENO que la visita no puede tocar (NR-1); y un inmueble con un mes de aviso, donde toda visita confirmada
/// ya está fuera de plazo (historia 8).</para>
///
/// <para>Lo que ninguna capacidad real sabe hacer a pedido (fallar una confirmación) se inyecta en el cable con
/// <c>falla</c>, sin tocar el orquestador.</para>
/// </remarks>
internal sealed class VisitaReal : IDisposable
{
    public const string Llave = "llave-de-propiedades";
    public const string Inmueble = "prop-001";
    public const string Clase = "realty.listado";
    public const string Ajeno = "dr-1";
    public const string ClaseAjena = "salud.profesional";
    public const string Tardio = "prop-tarde";

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "propiedades-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<PasoValidarVisita> _fabrica;

    /// <summary>Las 9:00 de Bogotá dentro de diez días: la primera franja del horario de visitas.</summary>
    public static readonly DateTimeOffset Inicio = ALas(9);

    /// <summary>Esa hora de Bogotá, dentro de diez días, como instante.</summary>
    public static DateTimeOffset ALas(int hora, int minuto = 0)
    {
        var dia = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Bogota).Date.AddDays(10).AddHours(hora).AddMinutes(minuto);
        return new DateTimeOffset(dia, Bogota.GetUtcOffset(dia));
    }

    public VisitaReal(Func<HttpRequestMessage, string?>? falla = null, IReadOnlyDictionary<string, string>? ajustes = null)
    {
        Capacidades = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>("notifications");
        var franja = ajustes?.GetValueOrDefault("Propiedades:Visita:Minutos") is { } m ? int.Parse(m, System.Globalization.CultureInfo.InvariantCulture) : 60;
        Registrar(Clase, Inmueble, franja).GetAwaiter().GetResult();
        Registrar(ClaseAjena, Ajeno, franja).GetAwaiter().GetResult();
        Registrar(Clase, Tardio, franja, avisoEnMinutos: 30 * 24 * 60).GetAwaiter().GetResult();
        PublicarPlantilla().GetAwaiter().GetResult();

        IHttpClientFactory fabrica = falla is null ? Capacidades : new ConFallo(Capacidades, falla);
        _fabrica = new WebApplicationFactory<PasoValidarVisita>().WithWebHostBuilder(b =>
        {
            b.UseSetting("propiedades:Storage:Root", Almacen);
            b.UseSetting("Propiedades:ApiKey", Llave);
            foreach (var (clave, valor) in ajustes ?? new Dictionary<string, string>()) b.UseSetting(clave, valor);
            b.ConfigureTestServices(s => s.AddSingleton(fabrica));
        });
        Orquestador = _fabrica.CreateClient();
        Orquestador.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
    }

    public ArnesDeCapacidades Capacidades { get; }

    public HttpClient Orquestador { get; }

    public IServiceProvider Servicios => _fabrica.Services;

    public SagaEngine<SagaDeFlujo> Sagas => Servicios.GetRequiredService<SagaEngine<SagaDeFlujo>>();

    /// <summary>La raíz del almacén del orquestador (sus sagas).</summary>
    public string Almacen => Path.Combine(_raiz, "propiedades");

    public void Dispose()
    {
        Orquestador.Dispose();
        _fabrica.Dispose();
        Capacidades.Dispose();
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal con un fichero aún abierto */ }
    }

    // ── Lo que hace quien agenda, por la puerta ─────────────────────────────

    public static string Sujeto(string id) => $"propiedades.interesado:{id}";

    /// <summary>El cuerpo de abrir: el inmueble como referencia con la clase que declara la definición (NR-1).</summary>
    public static object Cuerpo(
        string inmueble = Inmueble, DateTimeOffset? inicio = null, int minutos = 60, string clase = Clase, string? modalidad = null)
    {
        var desde = inicio ?? Inicio;
        return new
        {
            inmueble = new { kind = clase, id = inmueble },
            ventana = new { start = desde, end = desde.AddMinutes(minutos) },
            modalidad,
        };
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string llave = "visita-1", string quien = "u-1", object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/propiedades.visita") { Content = JsonContent.Create(cuerpo ?? Cuerpo()) };
        peticion.Headers.Add("Idempotency-Key", llave);
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(quien));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar(string id, string quien = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/propiedades.visita/{id}/cerrar");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(quien));
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { correo = "ana@ejemplo.co", nombre = "Ana", enlace = "https://sitio.ejemplo/propiedades/?visita=x", sitio = "Propiedades de prueba" }))));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Consultar(string id, string quien = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/propiedades.visita/{id}");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(quien));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cancelar(string id, string quien = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/propiedades.visita/{id}/cancelar");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(quien));
        return Enviar(peticion);
    }

    /// <summary>Abrir y cerrar: una visita confirmada de <paramref name="quien"/>, y su id.</summary>
    public async Task<string> Confirmada(string llave = "visita-1", string quien = "u-1", object? cuerpo = null)
    {
        var (abrio, abierta) = await Abrir(llave, quien, cuerpo);
        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
        var id = abierta.GetProperty("id").GetString()!;
        var (cerro, cerrada) = await Cerrar(id, quien);
        Assert.True(cerro == HttpStatusCode.OK && cerrada.GetProperty("estado").GetString() == "Completed", cerrada.ToString());
        return id;
    }

    /// <summary>
    /// La lectura «disponibilidad» como la pide la puerta para un invitado: SIN la cabecera del sujeto (no la pone para
    /// una operación abierta a invitados, núcleo NB-3).
    /// </summary>
    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Disponibilidad(
        DateTimeOffset desde, DateTimeOffset hasta, string inmueble = Inmueble, string clase = Clase)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/propiedades.visita/disponibilidad")
        {
            Content = JsonContent.Create(new { inmueble = new { kind = clase, id = inmueble }, desde, hasta }),
        };
        return Enviar(peticion);
    }

    /// <summary>
    /// Pedir la cancelación de una visita YA confirmada: abre la operación posterior al cierre (008-P5, núcleo P6), que
    /// comprueba que la visita es de quien pide y está confirmada, y no toca la agenda.
    /// </summary>
    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> PedirCancelacion(string visita, string llave = "cancelar-1", string quien = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/propiedades.visita-cancelacion")
        {
            Content = JsonContent.Create(new { visita }),
        };
        peticion.Headers.Add("Idempotency-Key", llave);
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(quien));
        return Enviar(peticion);
    }

    /// <summary>Confirmar la cancelación pedida: la agenda cancela la reserva, con su plazo.</summary>
    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> ConfirmarCancelacion(string id, string quien = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/propiedades.visita-cancelacion/{id}/cerrar");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(quien));
        return Enviar(peticion);
    }

    /// <summary>Pedir y confirmar: lo que hace la pantalla. Contesta la primera que no salió bien, o la confirmación.</summary>
    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> CancelarConfirmada(string visita, string llave = "cancelar-1", string quien = "u-1")
    {
        var (abrio, abierta) = await PedirCancelacion(visita, llave, quien);
        if (abrio is not (HttpStatusCode.Created or HttpStatusCode.OK)) return (abrio, abierta);
        return await ConfirmarCancelacion(abierta.GetProperty("id").GetString()!, quien);
    }

    /// <summary>«Mis …» de un flujo: las sagas de <paramref name="quien"/> (núcleo NB-6), por estado si se pide.</summary>
    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Listar(string flujo, string quien = "u-1", string? estado = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/{flujo}" + (estado is null ? "" : $"?estado={estado}"));
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(quien));
        return Enviar(peticion);
    }

    // ── Lo que quedó en las capacidades ─────────────────────────────────────

    public async Task<JsonElement> Leer(string capacidad, string ruta)
    {
        using var http = Capacidades.CreateClient(capacidad);
        return await http.GetFromJsonAsync<JsonElement>(ruta);
    }

    /// <summary>Cuántos ocupan la franja en la agenda de ese sujeto (apartados vivos más reservas).</summary>
    public async Task<int> Ocupadas(string clase = Clase, string id = Inmueble, DateTimeOffset? inicio = null, int minutos = 60)
    {
        var recurso = (await Leer("booking", $"v1/resources/por-sujeto?subjectKind={clase}&subjectId={id}")).GetProperty("id").GetString();
        var desde = inicio ?? Inicio;
        var a = await Leer("booking",
            $"v1/resources/{recurso}/availability?start={Uri.EscapeDataString(desde.ToString("O"))}&end={Uri.EscapeDataString(desde.AddMinutes(minutos).ToString("O"))}");
        return a.GetProperty("taken").GetInt32();
    }

    /// <summary>El estado de cada reserva de la agenda a nombre de quien agenda.</summary>
    public async Task<List<string>> Reservas(string quien = "u-1")
        => (await Leer("booking", $"v1/reservations?forKind=propiedades.interesado&forId={quien}")).GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("status").GetString()!).ToList();

    public string Dato(string saga, string nombre) => Sagas.Find(saga)!.Datos[nombre].Valor!.Value.GetString()!;

    /// <summary>Si la saga llegó a guardar un valor con ese nombre (no nulo).</summary>
    public bool Tiene(string saga, string nombre)
        => Sagas.Find(saga) is { } s && s.Datos.TryGetValue(nombre, out var v) && v.Valor is { ValueKind: not JsonValueKind.Null };

    /// <summary>Las entregas de aviso a quien agenda.</summary>
    public async Task<List<JsonElement>> Avisos(string quien = "u-1")
        => (await Leer("notifications", $"v1/deliveries?toKind=propiedades.interesado&toId={quien}")).GetProperty("items")
            .EnumerateArray().ToList();

    private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
    {
        using (peticion)
        {
            using var r = await Orquestador.SendAsync(peticion);
            var texto = await r.Content.ReadAsStringAsync();
            return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
        }
    }

    /// <summary>El recurso en la agenda, con el horario de visitas: todos los días de 9:00 a 12:00 en Bogotá, por franjas de 60 minutos.</summary>
    private async Task Registrar(string clase, string id, int franja, int avisoEnMinutos = 120)
    {
        using var http = Capacidades.CreateClient("booking");
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/resources")
        {
            Content = JsonContent.Create(new
            {
                subjectKind = clase,
                subjectId = id,
                capacity = 1,
                timeZoneId = "America/Bogota",
                opening = Enum.GetValues<DayOfWeek>().Select(d => new { day = d.ToString(), from = "09:00", to = "12:00" }).ToList(),
                cancellationNoticeMinutes = avisoEnMinutos,
                slotMinutes = franja,
            }),
        };
        peticion.Headers.Add("Idempotency-Key", $"recurso-{clase}-{id}");
        using var r = await http.SendAsync(peticion);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"recurso {clase}:{id}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    /// <summary>La plantilla del aviso, del fichero que la provisiona de verdad.</summary>
    private async Task PublicarPlantilla()
    {
        using var fichero = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var p = fichero.RootElement.EnumerateArray()
            .Single(e => e.TryGetProperty("key", out var k) && k.GetString() == "propiedades.visita.confirmada");
        using var http = Capacidades.CreateClient("notifications");
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/templates")
        {
            Content = JsonContent.Create(new
            {
                key = p.GetProperty("key").GetString(),
                channel = p.GetProperty("channel").GetString(),
                subject = p.GetProperty("subject").GetString(),
                body = p.GetProperty("body").GetString(),
            }),
        };
        peticion.Headers.Add("Idempotency-Key", "plantilla-propiedades");
        using var r = await http.SendAsync(peticion);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"plantilla: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    public static bool Es(HttpRequestMessage r, string metodo, string patron)
        => r.Method.Method == metodo && Regex.IsMatch(r.RequestUri!.AbsolutePath, patron);

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
}
