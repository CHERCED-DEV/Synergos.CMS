using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La puerta de los flujos (ADR 0140 F3), contra un orquestador falso armado desde el contrato
/// COMITEADO de <c>Bff.Eventos</c> con las cuatro operaciones de la compra marcadas en memoria.
/// </summary>
/// <remarks>
/// <para><b>El contrato es el de verdad</b>: la tabla sale del mismo JSON que la deriva mantiene fiel al
/// orquestador, y sólo se le agregan las marcas que el paso 14 pone en el código. Así lo que la puerta
/// manda —ruta, cuerpo, cabeceras— se juzga contra la forma real de las operaciones.</para>
///
/// <para>Cada código de la puerta, en su orden; lo que pasa byte a byte; lo que pone la puerta y lo que
/// del navegador NO viaja.</para>
/// </remarks>
public sealed class FlujosControllerTests : IDisposable
{
    private const string Flujo = "eventos.compra";
    private static readonly Guid Ana = Guid.Parse("8c0b1a52-7f1d-4c5e-9d6e-3a2b1c0d9e8f");
    private static readonly Guid Beto = Guid.Parse("1f2e3d4c-5b6a-4978-8695-a4b3c2d1e0f9");

    private readonly IMemberAccessGate _miembro = Substitute.For<IMemberAccessGate>();
    private readonly Orquestador _orquestador = new();
    private readonly PuertaSettings _ajustes = new()
    {
        Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal)
        {
            [Flujo] = new()
            {
                Acceso = AccesoDeLaPuerta.Miembro,
                SujetoKind = "eventos.comprador",
                Negocio = new NegocioDelFlujoSettings { Seccion = "Eventos", Campos = ["FeePercent"] },
                Aviso = new AvisoDelFlujoSettings { Ruta = "/eventos/compra?compra={id}" },
            },
        },
    };

    private bool _conDestino = true;

    public FlujosControllerTests() => Sesion(Ana);

    public void Dispose() => _orquestador.Dispose();

    /// <summary>El contrato comiteado de Bff.Eventos, con las cuatro operaciones de la compra marcadas.</summary>
    internal static TablaDeLaPuerta TablaDeLaCompra()
    {
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi",
            "Synergos.Bff.Eventos.json")))!;
        void Marcar(string ruta, string metodo, string operacion)
            => doc["paths"]![ruta]![metodo]![TablaDeLaPuerta.MarcaDelFlujo] = new JsonObject { ["flujo"] = Flujo, ["operacion"] = operacion };
        Marcar("/v1/ticket-purchases", "post", "abrir");
        Marcar("/v1/ticket-purchases/{id}", "get", "consultar");
        Marcar("/v1/ticket-purchases/{id}/confirm", "post", "cerrar");
        Marcar("/v1/ticket-purchases/{id}/cancel", "post", "cancelar");
        return TablaDeLaPuerta.De([("Synergos.Bff.Eventos.json", doc.ToJsonString())]);
    }

    private void Sesion(Guid? miembro)
    {
        _miembro.IsAuthenticated.Returns(miembro is not null);
        _miembro.CurrentMemberKey.Returns(miembro);
        _miembro.CurrentMemberEmail.Returns(miembro is null ? null : "ana@ejemplo.co");
        _miembro.CurrentMemberDisplayName.Returns(miembro is null ? null : "Ana María");
    }

    internal sealed class Orquestador : HttpMessageHandler
    {
        public List<(string Metodo, string Ruta, Dictionary<string, string> Cabeceras, byte[] Cuerpo)> Pedidos { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Responde { get; set; } = _ => Json(HttpStatusCode.OK, """{"id":"s-1"}""");

        public static HttpResponseMessage Json(HttpStatusCode estado, string json, string tipo = "application/json")
            => new(estado) { Content = new StringContent(json, Encoding.UTF8, tipo) };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Pedidos.Add((r.Method.Method, r.RequestUri!.PathAndQuery,
                r.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
                r.Content is null ? [] : await r.Content.ReadAsByteArrayAsync(ct)));
            return Responde(r);
        }
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(ReenvioDeLaPuerta.Cliente("Eventos"), name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-eventos.local/") };
        }
    }

    private sealed record Respuesta(int Estado, string? Tipo, string Cuerpo, IHeaderDictionary Cabeceras, StatusCodePagesFeature Paginas)
    {
        public JsonElement Json => JsonDocument.Parse(Cuerpo).RootElement;

        public string? Codigo => Json.GetProperty("code").GetString();
    }

    private async Task<Respuesta> Pedir(
        string metodo, string operacion, string? consulta = null, string? cuerpo = null, string? tipo = "application/json",
        Action<IHeaderDictionary>? cabeceras = null, string flujo = Flujo, bool mismoOrigen = true)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = metodo;
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("sitio.test");
        if (consulta is not null) http.Request.QueryString = new QueryString("?" + consulta);
        if (cuerpo is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(cuerpo);
            http.Request.Body = new MemoryStream(bytes);
            http.Request.ContentLength = bytes.Length;
            http.Request.ContentType = tipo;
        }
        if (mismoOrigen) http.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        cabeceras?.Invoke(http.Request.Headers);
        http.Response.Body = new MemoryStream();
        var paginas = new StatusCodePagesFeature();
        http.Features.Set<IStatusCodePagesFeature>(paginas);

        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(_ajustes);
        var secciones = new SeccionesDelSitio(
            [new SeccionDeNegocioRegistrada("Eventos", typeof(NegocioDeEventos), _ => new NegocioDeEventos("/api/eventos", 12m, 10m))],
            new ServiceCollection().BuildServiceProvider());

        var sut = new FlujosController(
            TablaDeLaCompra(), monitor, new DestinosDeLaPuerta(_conDestino ? ["Eventos"] : []), _miembro,
            new ReenvioDeLaPuerta(new Fabrica(_orquestador), NullLogger<ReenvioDeLaPuerta>.Instance), secciones)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var resultado = await sut.Pasar(flujo, operacion, default);
        if (resultado is ContentResult c) return new Respuesta(c.StatusCode!.Value, c.ContentType, c.Content!, http.Response.Headers, paginas);

        Assert.IsType<EmptyResult>(resultado);
        http.Response.Body.Position = 0;
        return new Respuesta(http.Response.StatusCode, http.Response.ContentType,
            await new StreamReader(http.Response.Body).ReadToEndAsync(), http.Response.Headers, paginas);
    }

    private static readonly string Compra = """{"eventId":"evt-1","lines":[{"tier":"GEN","quantity":2}]}""";

    private static Action<IHeaderDictionary> Llave(string llave = "k1") => h => h["Idempotency-Key"] = llave;

    // ── La tabla: sólo lo marcado ──────────────────────────────────────────────────

    [Fact]
    public void La_tabla_trae_solo_las_cuatro_operaciones_marcadas_con_su_forma()
    {
        var tabla = TablaDeLaCompra();

        Assert.Equal(["abrir", "cancelar", "cerrar", "consultar"], tabla.Todas.Select(o => o.Operacion).Order(StringComparer.Ordinal));
        var abrir = tabla.Buscar(Flujo, "abrir")!;
        Assert.Equal(("POST", "v1/ticket-purchases", true, true), (abrir.Metodo, abrir.Ruta, abrir.ConCuerpo, abrir.LlaveRequerida));
        Assert.True(abrir.Declara(LoQuePoneLaPuerta.CabeceraDelSujeto) && abrir.Declara(LoQuePoneLaPuerta.CabeceraDelNegocio));
        var cerrar = tabla.Buscar(Flujo, "cerrar")!;
        Assert.False(cerrar.ConCuerpo);
        Assert.True(cerrar.Declara(LoQuePoneLaPuerta.CabeceraDelContacto));
        Assert.Equal("id", Assert.Single(cerrar.Parametros).Nombre);
    }

    // ── Los códigos de la puerta, en su orden ──────────────────────────────────────

    [Fact]
    public async Task Un_flujo_que_no_existe_y_una_operacion_no_marcada_dan_el_mismo_404()
    {
        var inexistente = await Pedir("POST", "abrir", flujo: "nada.inventado", cuerpo: Compra, cabeceras: Llave());
        var noMarcada = await Pedir("POST", "reintentar", cuerpo: Compra, cabeceras: Llave());

        Assert.Equal(404, inexistente.Estado);
        Assert.Equal("puerta.operacion_desconocida", inexistente.Codigo);
        Assert.Equal(inexistente.Cuerpo, noMarcada.Cuerpo);
        Assert.Equal("application/problem+json", inexistente.Tipo);
        Assert.Empty(_orquestador.Pedidos);
    }

    [Fact]
    public async Task Por_otro_metodo_es_405_con_el_que_va()
    {
        var r = await Pedir("GET", "abrir");

        Assert.Equal((405, "puerta.metodo_no_permitido"), (r.Estado, r.Codigo));
        Assert.Equal("POST", r.Cabeceras.Allow.ToString());
    }

    [Fact]
    public async Task Un_flujo_sin_abrir_o_sin_destino_es_503_y_no_toca_la_red()
    {
        _ajustes.Flujos.Clear();
        var sinAbrir = await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave());
        Assert.Equal((503, "puerta.flujo_no_disponible"), (sinAbrir.Estado, sinAbrir.Codigo));
        Assert.False(sinAbrir.Json.GetProperty("transient").GetBoolean());

        _conDestino = false;
        Assert.Equal(503, (await Pedir("GET", "consultar", "id=s-1")).Estado);
        Assert.Empty(_orquestador.Pedidos);
    }

    [Fact]
    public async Task Sin_sesion_es_401_y_no_404_ni_html()
    {
        Sesion(null);

        var r = await Pedir("GET", "consultar", "id=s-1");

        Assert.Equal((401, "puerta.sesion_requerida"), (r.Estado, r.Codigo));
        Assert.Equal("application/problem+json", r.Tipo);
        Assert.False(r.Paginas.Enabled, "Con las páginas de estado encendidas, un error de la puerta podría salir como la página HTML del sitio.");
        Assert.Equal("no-store", r.Cabeceras.CacheControl.ToString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("cross-site", null)]
    [InlineData("same-site", null)]
    [InlineData(null, "https://evil.test")]
    public async Task Un_POST_de_otro_origen_es_403(string? secFetchSite, string? origen)
    {
        var r = await Pedir("POST", "abrir", cuerpo: Compra, mismoOrigen: false, cabeceras: h =>
        {
            h["Idempotency-Key"] = "k1";
            if (secFetchSite is not null) h["Sec-Fetch-Site"] = secFetchSite;
            if (origen is not null) h.Origin = origen;
        });

        Assert.Equal((403, "puerta.origen_no_permitido"), (r.Estado, r.Codigo));
        Assert.Empty(_orquestador.Pedidos);
    }

    [Fact]
    public async Task Un_POST_con_el_Origin_del_sitio_y_sin_Sec_Fetch_Site_pasa()
    {
        var r = await Pedir("POST", "abrir", cuerpo: Compra, mismoOrigen: false, cabeceras: h =>
        {
            h["Idempotency-Key"] = "k1";
            h.Origin = "https://sitio.test";
        });

        Assert.Equal(200, r.Estado);
    }

    [Fact]
    public async Task Un_cuerpo_que_no_es_JSON_es_415_y_uno_de_mas_de_64_KB_es_413()
    {
        var texto = await Pedir("POST", "abrir", cuerpo: Compra, tipo: "text/plain", cabeceras: Llave());
        var grande = await Pedir("POST", "abrir", cuerpo: "{\"x\":\"" + new string('a', 64 * 1024) + "\"}", cabeceras: Llave());

        Assert.Equal((415, "puerta.tipo_no_soportado"), (texto.Estado, texto.Codigo));
        Assert.Equal((413, "puerta.cuerpo_demasiado_grande"), (grande.Estado, grande.Codigo));
        Assert.Empty(_orquestador.Pedidos);
    }

    [Fact]
    public async Task Sin_llave_es_400_y_sin_el_parametro_de_la_ruta_tambien()
    {
        var sinLlave = await Pedir("POST", "abrir", cuerpo: Compra);
        var sinId = await Pedir("POST", "cerrar");

        Assert.Equal((400, "puerta.llave_requerida"), (sinLlave.Estado, sinLlave.Codigo));
        Assert.Equal((400, "puerta.parametro_requerido"), (sinId.Estado, sinId.Codigo));
        Assert.Empty(_orquestador.Pedidos);
    }

    // ── Lo que viaja ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Abrir_manda_el_cuerpo_tal_cual_y_lo_que_pone_la_puerta_y_nada_del_navegador()
    {
        _orquestador.Responde = _ =>
        {
            var r = Orquestador.Json(HttpStatusCode.Created, """{"id":"s-1","status":"Running"}""");
            r.Headers.Location = new Uri("http://bff-eventos.local/v1/ticket-purchases/s-1");
            return r;
        };

        var r = await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: h =>
        {
            h["Idempotency-Key"] = "k1";
            h["X-Synergos-Sujeto"] = "eventos.comprador:otro";
            h["X-Synergos-Negocio"] = "eyJmZWVQZXJjZW50IjowfQ";
            h.Cookie = "UMB_MEMBER=secreto";
            h.Authorization = "Bearer secreto";
        });

        Assert.Equal(201, r.Estado);
        Assert.Equal("""{"id":"s-1","status":"Running"}""", r.Cuerpo);
        Assert.False(r.Cabeceras.ContainsKey("Location"), "El Location es una ruta interna del orquestador.");

        var (metodo, ruta, cabeceras, cuerpo) = Assert.Single(_orquestador.Pedidos);
        Assert.Equal(("POST", "/v1/ticket-purchases"), (metodo, ruta));
        Assert.Equal(Compra, Encoding.UTF8.GetString(cuerpo));
        Assert.Equal($"eventos.comprador:{Ana:n}", cabeceras["X-Synergos-Sujeto"]);
        Assert.Equal(LoQuePoneLaPuerta.Base64Url(Encoding.UTF8.GetBytes("""{"feePercent":12}""")), cabeceras["X-Synergos-Negocio"]);
        Assert.False(cabeceras.ContainsKey("Cookie") || cabeceras.ContainsKey("Authorization") || cabeceras.ContainsKey("X-Synergos-Contacto"));
        Assert.Equal(["Idempotency-Key", "X-Synergos-Negocio", "X-Synergos-Sujeto"], cabeceras.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task La_llave_se_reemite_atada_al_sujeto_y_cabe_en_la_de_una_saga()
    {
        await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave("k1"));
        await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave("k1"));
        Sesion(Beto);
        await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave("k1"));
        await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave(new string('k', 128)));

        var llaves = _orquestador.Pedidos.Select(p => p.Cabeceras["Idempotency-Key"]).ToList();
        Assert.Equal(llaves[0], llaves[1]);
        Assert.NotEqual(llaves[0], llaves[2]);
        Assert.NotEqual("k1", llaves[0]);
        Assert.All(llaves, l => Assert.True(l.Length <= 83 && l.StartsWith("pta-", StringComparison.Ordinal), l));
    }

    [Fact]
    public async Task Consultar_lleva_el_id_en_la_ruta_y_el_sujeto_y_nada_mas()
    {
        var r = await Pedir("GET", "consultar", "id=s%2F1&otro=x");

        Assert.Equal(200, r.Estado);
        var (metodo, ruta, cabeceras, _) = Assert.Single(_orquestador.Pedidos);
        Assert.Equal(("GET", "/v1/ticket-purchases/s%2F1"), (metodo, ruta));
        Assert.Equal(["X-Synergos-Sujeto"], cabeceras.Keys);
    }

    [Fact]
    public async Task Cerrar_lleva_el_contacto_con_el_enlace_del_flujo()
    {
        await Pedir("POST", "cerrar", "id=s-1");

        var cabeceras = Assert.Single(_orquestador.Pedidos).Cabeceras;
        var contacto = JsonDocument.Parse(Convert.FromBase64String(
            cabeceras["X-Synergos-Contacto"].Replace('-', '+').Replace('_', '/').PadRight((cabeceras["X-Synergos-Contacto"].Length + 3) / 4 * 4, '='))).RootElement;
        Assert.Equal("ana@ejemplo.co", contacto.GetProperty("correo").GetString());
        Assert.Equal("Ana María", contacto.GetProperty("nombre").GetString());
        Assert.Equal("https://sitio.test/eventos/compra?compra=s-1", contacto.GetProperty("enlace").GetString());
        Assert.Equal("sitio.test", contacto.GetProperty("sitio").GetString());
    }

    // ── Lo que contesta ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_rechazo_del_orquestador_pasa_byte_a_byte()
    {
        const string rechazo = """{"type":"x","title":"Conflict","status":409,"detail":"no","code":"pricing.price_not_in_effect","transient":false}""";
        _orquestador.Responde = _ => Orquestador.Json(HttpStatusCode.Conflict, rechazo, "application/problem+json");

        var r = await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave());

        Assert.Equal((409, rechazo), (r.Estado, r.Cuerpo));
        Assert.Equal("application/problem+json; charset=utf-8", r.Tipo);
    }

    [Fact]
    public async Task Un_401_del_orquestador_es_502_y_no_un_inicia_sesion()
    {
        _orquestador.Responde = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var r = await Pedir("GET", "consultar", "id=s-1");

        Assert.Equal((502, "puerta.llave_rechazada"), (r.Estado, r.Codigo));
    }

    [Fact]
    public async Task Un_5xx_o_un_error_sin_cuerpo_es_502()
    {
        _orquestador.Responde = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var sinCuerpo = await Pedir("GET", "consultar", "id=s-1");
        _orquestador.Responde = _ => Orquestador.Json(HttpStatusCode.NotFound, "<html>no</html>", "text/html");
        var html = await Pedir("GET", "consultar", "id=s-1");

        Assert.Equal((502, "puerta.respuesta_invalida"), (sinCuerpo.Estado, sinCuerpo.Codigo));
        Assert.Equal((502, "puerta.respuesta_invalida"), (html.Estado, html.Codigo));
    }

    [Fact]
    public async Task Sin_red_es_503_y_sin_respuesta_a_tiempo_504_los_dos_transitorios()
    {
        _orquestador.Responde = _ => throw new HttpRequestException("Connection refused");
        var caido = await Pedir("GET", "consultar", "id=s-1");
        _orquestador.Responde = _ => throw new TaskCanceledException("timeout");
        var lento = await Pedir("GET", "consultar", "id=s-1");

        Assert.Equal((503, "puerta.orquestador_no_disponible", true), (caido.Estado, caido.Codigo, caido.Json.GetProperty("transient").GetBoolean()));
        Assert.Equal((504, "puerta.tiempo_agotado", true), (lento.Estado, lento.Codigo, lento.Json.GetProperty("transient").GetBoolean()));
    }

    // ── La configuración, validada al arrancar ─────────────────────────────────────

    private static readonly SeccionesDelSitio Secciones = new(
        [new SeccionDeNegocioRegistrada("Eventos", typeof(NegocioDeEventos), _ => new NegocioDeEventos("/api/eventos", 12m, 10m))],
        new ServiceCollection().BuildServiceProvider());

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validar(string clave, FlujoDeLaPuertaSettings flujo)
        => new ValidadorDeLaPuerta(TablaDeLaCompra(), Secciones).Validate(null, new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [clave] = flujo },
        });

    [Fact]
    public void Un_flujo_bien_configurado_arranca_y_sin_flujos_tambien()
    {
        Assert.True(new ValidadorDeLaPuerta(TablaDeLaCompra(), Secciones).Validate(null, _ajustes).Succeeded);
        Assert.True(new ValidadorDeLaPuerta(TablaDeLaCompra(), Secciones).Validate(null, new PuertaSettings()).Succeeded);
    }

    public static TheoryData<string, string> Configuraciones() => new()
    {
        { "nada.inventado", "ningún contrato incrustado expone" },
        { "acceso", "sólo sabe dar" },
        { "kind", "SujetoKind" },
        { "sin-negocio", "Negocio falta" },
        { "seccion", "no es una sección de negocio registrada" },
        { "campo", "no es un campo de NegocioDeEventos" },
        { "ruta", "tiene que ser una ruta del sitio" },
        { "marcador", "la operación que avisa no lo trae" },
    };

    [Theory]
    [MemberData(nameof(Configuraciones))]
    public void Lo_que_fallaria_en_cada_peticion_no_deja_arrancar(string caso, string motivo)
    {
        var bien = _ajustes.Flujos[Flujo];
        var (clave, flujo) = caso switch
        {
            "nada.inventado" => ("nada.inventado", bien),
            "acceso" => (Flujo, new FlujoDeLaPuertaSettings { Acceso = "Visitante", SujetoKind = bien.SujetoKind, Negocio = bien.Negocio, Aviso = bien.Aviso }),
            "kind" => (Flujo, new FlujoDeLaPuertaSettings { Acceso = bien.Acceso, SujetoKind = "Eventos Comprador", Negocio = bien.Negocio, Aviso = bien.Aviso }),
            "sin-negocio" => (Flujo, new FlujoDeLaPuertaSettings { Acceso = bien.Acceso, SujetoKind = bien.SujetoKind, Aviso = bien.Aviso }),
            "seccion" => (Flujo, new FlujoDeLaPuertaSettings { Acceso = bien.Acceso, SujetoKind = bien.SujetoKind, Aviso = bien.Aviso, Negocio = new NegocioDelFlujoSettings { Seccion = "Evento", Campos = ["FeePercent"] } }),
            "campo" => (Flujo, new FlujoDeLaPuertaSettings { Acceso = bien.Acceso, SujetoKind = bien.SujetoKind, Aviso = bien.Aviso, Negocio = new NegocioDelFlujoSettings { Seccion = "Eventos", Campos = ["Comision"] } }),
            "ruta" => (Flujo, new FlujoDeLaPuertaSettings { Acceso = bien.Acceso, SujetoKind = bien.SujetoKind, Negocio = bien.Negocio, Aviso = new AvisoDelFlujoSettings { Ruta = "https://evil.test/x" } }),
            _ => (Flujo, new FlujoDeLaPuertaSettings { Acceso = bien.Acceso, SujetoKind = bien.SujetoKind, Negocio = bien.Negocio, Aviso = new AvisoDelFlujoSettings { Ruta = "/compra?c={orden}" } }),
        };

        var r = Validar(clave, flujo);

        Assert.True(r.Failed, caso);
        Assert.Contains(motivo, r.FailureMessage, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
