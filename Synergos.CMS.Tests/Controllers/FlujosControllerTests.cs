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
/// COMITEADO de <c>Bff.Eventos</c> con las cinco operaciones de la compra marcadas en memoria.
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

    /// <summary>La ruta del aviso que declara el sitio de la petición en la sección de prueba «Sitio» (D-006-AVISO).</summary>
    private string? _rutaDelSitio;

    /// <summary>Una sección de negocio de prueba con un campo de texto para la ruta del aviso y uno que no es texto.</summary>
    internal sealed record NegocioDelSitio(string? Aviso, int Cupos = 0);

    private AvisoEnVivoDeLaPuerta? _enVivo;

    public FlujosControllerTests() => Sesion(Ana);

    public void Dispose() => _orquestador.Dispose();

    /// <summary>El contrato comiteado de Bff.Eventos, con las cinco operaciones de la compra marcadas.</summary>
    internal static TablaDeLaPuerta TablaDeLaCompra() => TablaDeLaPuerta.De([("Synergos.Bff.Eventos.json", ContratoDeLaCompra().ToJsonString())]);

    private static JsonNode ContratoDeLaCompra()
    {
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi",
            "Synergos.Bff.Eventos.json")))!;
        void Marcar(string ruta, string metodo, string operacion)
            => doc["paths"]![ruta]![metodo]![TablaDeLaPuerta.MarcaDelFlujo] = new JsonObject { ["flujo"] = Flujo, ["operacion"] = operacion };
        Marcar("/v1/ticket-purchases", "post", "abrir");
        Marcar("/v1/ticket-purchases/{id}", "get", "consultar");
        Marcar("/v1/ticket-purchases/{id}/confirm", "post", "cerrar");
        Marcar("/v1/ticket-quotes", "post", "cotizar");
        Marcar("/v1/ticket-purchases/{id}/cancel", "post", "cancelar");
        return doc;
    }

    /// <summary>
    /// La compra con una lectura sin saga (NB-3): <c>disponibilidad</c>, con cuerpo y SIN ninguna cabecera de la puerta,
    /// como la publica el host genérico.
    /// </summary>
    private static TablaDeLaPuerta TablaConLectura() => TablaDeLaPuerta.De([("Synergos.Bff.Eventos.json", ContratoConLectura().ToJsonString())]);

    private static JsonNode ContratoConLectura()
    {
        var doc = ContratoDeLaCompra();
        doc["paths"]!["/v1/flujos/eventos.compra/disponibilidad"] = new JsonObject
        {
            ["post"] = new JsonObject
            {
                ["operationId"] = "EventosCompraDisponibilidad",
                ["requestBody"] = new JsonObject
                {
                    ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["type"] = "object" } } },
                },
                [TablaDeLaPuerta.MarcaDelFlujo] = new JsonObject { ["flujo"] = Flujo, ["operacion"] = "disponibilidad" },
            },
        };
        return doc;
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
        Action<IHeaderDictionary>? cabeceras = null, string flujo = Flujo, bool mismoOrigen = true, bool porTrozos = false,
        TablaDeLaPuerta? tabla = null, FrenoDeLaPuerta? freno = null, string? ip = null)
    {
        var http = new DefaultHttpContext();
        if (ip is not null) http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        http.Request.Method = metodo;
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("sitio.test");
        if (consulta is not null) http.Request.QueryString = new QueryString("?" + consulta);
        if (cuerpo is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(cuerpo);
            http.Request.Body = porTrozos ? new PorTrozos(bytes) : new MemoryStream(bytes);
            http.Request.ContentLength = porTrozos ? null : bytes.Length;
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
            [
                new SeccionDeNegocioRegistrada("Eventos", typeof(NegocioDeEventos), _ => new NegocioDeEventos("/api/eventos", 12m, 10m)),
                new SeccionDeNegocioRegistrada("Sitio", typeof(NegocioDelSitio), _ => new NegocioDelSitio(_rutaDelSitio)),
            ],
            new ServiceCollection().BuildServiceProvider());

        var sut = new FlujosController(
            tabla ?? TablaDeLaCompra(), monitor, new DestinosDeLaPuerta(_conDestino ? ["Eventos"] : []), _miembro,
            new ReenvioDeLaPuerta(new Fabrica(_orquestador), NullLogger<ReenvioDeLaPuerta>.Instance), secciones, enVivo: _enVivo,
            freno: freno)
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

    /// <summary>Un cuerpo por trozos: no dice su largo ni deja buscar, como uno con Transfer-Encoding: chunked.</summary>
    private sealed class PorTrozos(byte[] bytes) : Stream
    {
        private readonly MemoryStream _dentro = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _dentro.Read(buffer, offset, Math.Min(count, 4096));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static Action<IHeaderDictionary> Llave(string llave = "k1") => h => h["Idempotency-Key"] = llave;

    // ── La tabla: sólo lo marcado ──────────────────────────────────────────────────

    [Fact]
    public void La_tabla_trae_solo_las_cinco_operaciones_marcadas_con_su_forma()
    {
        var tabla = TablaDeLaCompra();

        Assert.Equal(["abrir", "cancelar", "cerrar", "consultar", "cotizar"], tabla.Todas.Select(o => o.Operacion).Order(StringComparer.Ordinal));
        var abrir = tabla.Buscar(Flujo, "abrir")!;
        Assert.Equal(("POST", "v1/ticket-purchases", true, true), (abrir.Metodo, abrir.Ruta, abrir.ConCuerpo, abrir.LlaveRequerida));
        Assert.True(abrir.Declara(LoQuePoneLaPuerta.CabeceraDelSujeto) && abrir.Declara(LoQuePoneLaPuerta.CabeceraDelNegocio));
        var cerrar = tabla.Buscar(Flujo, "cerrar")!;
        Assert.False(cerrar.ConCuerpo);
        Assert.True(cerrar.Declara(LoQuePoneLaPuerta.CabeceraDelContacto));
        Assert.Equal("id", Assert.Single(cerrar.Parametros).Nombre);
        // Cotizar (el cupón): el mismo cuerpo que abrir, con el sujeto y el negocio, y SIN llave: no crea nada.
        var cotizar = tabla.Buscar(Flujo, "cotizar")!;
        Assert.Equal(("POST", "v1/ticket-quotes", true, (int?)null), (cotizar.Metodo, cotizar.Ruta, cotizar.ConCuerpo, cotizar.LargoDeLaLlave));
        Assert.True(cotizar.Declara(LoQuePoneLaPuerta.CabeceraDelSujeto) && cotizar.Declara(LoQuePoneLaPuerta.CabeceraDelNegocio));
        Assert.False(cotizar.Declara(LoQuePoneLaPuerta.CabeceraDelContacto));
        Assert.Empty(cotizar.Parametros);
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
    public async Task Un_flujo_sin_abrir_es_503_y_no_toca_la_red()
    {
        _ajustes.Flujos.Clear();
        var sinAbrir = await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave());

        Assert.Equal((503, "puerta.flujo_no_disponible"), (sinAbrir.Estado, sinAbrir.Codigo));
        Assert.False(sinAbrir.Json.GetProperty("transient").GetBoolean());
        Assert.Empty(_orquestador.Pedidos);
    }

    [Fact]
    public async Task Un_flujo_abierto_cuyo_orquestador_no_tiene_destino_es_503_y_no_toca_la_red()
    {
        // Es el clon limpio: appsettings abre eventos.compra y no trae Synergos:Eventos:BaseUrl. El flujo
        // SÍ está abierto acá —lo que falta es el destino—, así que el 503 sale de esa mitad de la regla.
        _conDestino = false;
        var r = await Pedir("GET", "consultar", "id=s-1");

        Assert.Equal((503, "puerta.flujo_no_disponible"), (r.Estado, r.Codigo));
        Assert.False(r.Json.GetProperty("transient").GetBoolean());
        Assert.Empty(_orquestador.Pedidos);
    }

    /// <summary>
    /// Lo que dice <see cref="PuertaDeLosFlujos"/> —con lo que el CMS declara el camino de compra del
    /// elemento (ADR 0140)— es lo contrario del 503 de la puerta, en los cuatro casos: el elemento nunca va a
    /// una puerta que no lo pasaría, ni deja de ir a una que sí.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Lo_que_abre_la_puerta_es_lo_contrario_de_su_503(bool abierto, bool conDestino)
    {
        if (!abierto) _ajustes.Flujos.Clear();
        _conDestino = conDestino;
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(_ajustes);
        var abre = new PuertaDeLosFlujos(TablaDeLaCompra(), monitor, new DestinosDeLaPuerta(conDestino ? ["Eventos"] : [])).Abre(Flujo);

        var r = await Pedir("GET", "consultar", "id=s-1");

        Assert.Equal(abierto && conDestino, abre);
        Assert.Equal(!abre, r.Estado == 503 && r.Codigo == "puerta.flujo_no_disponible");
    }

    /// <summary>
    /// El ORDEN de los códigos es el contrato (FlujosController): cuando fallan dos cosas, contesta la primera.
    /// </summary>
    [Fact]
    public async Task Cuando_fallan_dos_cosas_contesta_la_primera_del_orden()
    {
        (int, string?) C(Respuesta r) => (r.Estado, r.Codigo);
        Sesion(null);

        // 404 antes que 401: sin sesión y algo que no existe no enseña que hace falta sesión.
        Assert.Equal((404, "puerta.operacion_desconocida"), C(await Pedir("POST", "abrir", flujo: "nada.inventado", cuerpo: Compra, cabeceras: Llave())));
        // 405 antes que 401.
        Assert.Equal((405, "puerta.metodo_no_permitido"), C(await Pedir("GET", "abrir")));
        // 503 antes que 401: un flujo que no está abierto no pide iniciar sesión.
        _conDestino = false;
        Assert.Equal((503, "puerta.flujo_no_disponible"), C(await Pedir("GET", "consultar", "id=s-1")));
        _conDestino = true;
        // 401 antes que 403: sin sesión y de otro origen.
        Assert.Equal((401, "puerta.sesion_requerida"), C(await Pedir("POST", "abrir", cuerpo: Compra, mismoOrigen: false, cabeceras: Llave())));

        Sesion(Ana);
        // 403 antes que 415: de otro origen y sin JSON.
        Assert.Equal((403, "puerta.origen_no_permitido"),
            C(await Pedir("POST", "abrir", cuerpo: Compra, tipo: "text/plain", mismoOrigen: false, cabeceras: Llave())));
        // 415 antes que 400: sin JSON y sin llave.
        Assert.Equal((415, "puerta.tipo_no_soportado"), C(await Pedir("POST", "abrir", cuerpo: Compra, tipo: "text/plain")));
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
    public async Task Un_cuerpo_por_trozos_que_no_declara_su_largo_y_pasa_de_64_KB_tambien_es_413()
    {
        // Sin Content-Length el techo lo pone lo que de verdad llega: sin contar, se leería entero en memoria.
        var grande = await Pedir("POST", "abrir", cuerpo: "{\"x\":\"" + new string('a', 64 * 1024) + "\"}", cabeceras: Llave(), porTrozos: true);
        var justo = await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave(), porTrozos: true);

        Assert.Equal((413, "puerta.cuerpo_demasiado_grande"), (grande.Estado, grande.Codigo));
        Assert.Equal(200, justo.Estado);   // el control: por trozos y chico, pasa
        Assert.Equal(Compra, Encoding.UTF8.GetString(Assert.Single(_orquestador.Pedidos).Cuerpo));
    }

    [Theory]
    [InlineData("POST", "cerrar", "id=..")]
    [InlineData("POST", "cancelar", "id=.")]
    [InlineData("GET", "consultar", "id=%2E%2E")]
    [InlineData("POST", "cerrar", "id=%2e")]
    public async Task Un_parametro_de_ruta_que_es_un_segmento_de_punto_es_400_y_no_sale(string metodo, string operacion, string consulta)
    {
        // Escapado no cambia, y resuelto contra la dirección del orquestador movería la petición a una ruta
        // que la tabla no marcó (cerrar con id=.. llegaba como POST /v1/confirm).
        var r = await Pedir(metodo, operacion, consulta);

        Assert.Equal((400, "puerta.parametro_invalido"), (r.Estado, r.Codigo));
        Assert.Empty(_orquestador.Pedidos);
    }

    [Fact]
    public async Task Un_valor_con_puntos_que_no_es_un_segmento_de_punto_sale_en_su_ruta()
    {
        // El control: lo que se rechaza son «.» y «..» enteros, no cualquier punto.
        var r = await Pedir("POST", "cerrar", "id=s.1..a");

        Assert.Equal(200, r.Estado);
        Assert.Equal("/v1/ticket-purchases/s.1..a/confirm", Assert.Single(_orquestador.Pedidos).Ruta);
    }

    [Fact]
    public async Task El_reenvio_tampoco_pone_un_segmento_de_punto_en_la_ruta_aunque_se_lo_pasen()
    {
        // La red de abajo: quien arme los parámetros sin pasar por la puerta no sale a otra ruta.
        var reenvio = new ReenvioDeLaPuerta(new Fabrica(_orquestador), NullLogger<ReenvioDeLaPuerta>.Instance);
        var cerrar = TablaDeLaCompra().Buscar(Flujo, "cerrar")!;

        await Assert.ThrowsAsync<ArgumentException>(() => reenvio.EnviarAsync(
            cerrar, new Dictionary<string, string> { ["id"] = ".." }, null, new Dictionary<string, string>(), default));
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

    // ── El sujeto por el correo de la sesión (núcleo N3, NE-4 de 007) ─────────────────────

    private static FlujoDeLaPuertaSettings ConSujetoDe(FlujoDeLaPuertaSettings f, string sujetoDe) => new()
    {
        Acceso = f.Acceso, SujetoKind = f.SujetoKind, Negocio = f.Negocio, Aviso = f.Aviso, Camino = f.Camino, SujetoDe = sujetoDe,
    };

    [Fact]
    public async Task Con_SujetoDe_correo_el_sujeto_es_el_seudonimo_del_correo_de_la_sesion()
    {
        _ajustes.Flujos[Flujo] = ConSujetoDe(_ajustes.Flujos[Flujo], " Correo ");

        await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave("k1"));

        var (_, _, cabeceras, _) = Assert.Single(_orquestador.Pedidos);
        // El seudónimo, calculado aparte: SHA-256 del correo en minúsculas, sus primeros 16 hexadecimales.
        var seudonimo = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("ana@ejemplo.co")))[..16]
            .ToLowerInvariant();
        Assert.Equal($"eventos.comprador:{seudonimo}", cabeceras["X-Synergos-Sujeto"]);
        Assert.DoesNotContain(Ana.ToString("n"), cabeceras["X-Synergos-Sujeto"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Con_SujetoDe_correo_una_sesion_sin_correo_no_pasa_y_sin_declararlo_el_sujeto_es_el_miembro()
    {
        _ajustes.Flujos[Flujo] = ConSujetoDe(_ajustes.Flujos[Flujo], SujetoDeLaPuerta.Correo);
        _miembro.CurrentMemberEmail.Returns((string?)null);

        var r = await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave("k1"));

        Assert.Equal(401, r.Estado);
        Assert.Equal("puerta.sesion_requerida", r.Codigo);
        Assert.Empty(_orquestador.Pedidos);

        // El control: sin declararlo, la misma sesión sin correo pasa con su miembro.
        _ajustes.Flujos[Flujo] = ConSujetoDe(_ajustes.Flujos[Flujo], "  ");
        await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave("k1"));
        Assert.Equal($"eventos.comprador:{Ana:n}", Assert.Single(_orquestador.Pedidos).Cabeceras["X-Synergos-Sujeto"]);
    }

    // ── Los avisos en vivo que declara un flujo (núcleo N3, NE-6 de 007) ──────────────────

    private sealed class CanalQueRecuerda : Synergos.CMS.Interfaces.IRealtimeNotifier
    {
        public List<(string Canal, string Evento, string Datos)> Publicados { get; } = [];
        public bool Rompe { get; set; }

        public Task PublishAsync(string channel, string eventName, string payloadJson, CancellationToken cancellationToken = default)
        {
            if (Rompe) throw new InvalidOperationException("el canal no contesta");
            Publicados.Add((channel, eventName, payloadJson));
            return Task.CompletedTask;
        }
    }

    private CanalQueRecuerda ConAvisos(params AvisoEnVivoSettings[] avisos)
    {
        var f = _ajustes.Flujos[Flujo];
        _ajustes.Flujos[Flujo] = new FlujoDeLaPuertaSettings
        {
            Acceso = f.Acceso, SujetoKind = f.SujetoKind, Negocio = f.Negocio, Aviso = f.Aviso, EnVivo = [.. avisos],
        };
        var canal = new CanalQueRecuerda();
        _enVivo = new AvisoEnVivoDeLaPuerta(canal, NullLogger<AvisoEnVivoDeLaPuerta>.Instance);
        return canal;
    }

    [Fact]
    public async Task Tras_una_operacion_que_sale_bien_avisa_en_vivo_en_su_canal_con_lo_que_trae_la_respuesta()
    {
        var canal = ConAvisos(
            new AvisoEnVivoSettings { Operacion = "cerrar", Canal = "flujo:cerrado:{id}", Nombre = "cerrado" },
            new AvisoEnVivoSettings { Operacion = "cerrar", Canal = "flujo:evento:{eventoId}", Nombre = "entrada" },
            new AvisoEnVivoSettings { Operacion = "cancelar", Canal = "flujo:cancelado:{id}", Nombre = "cancelado" });
        _orquestador.Responde = _ => Orquestador.Json(HttpStatusCode.OK, """{"id":"s-1","datos":{"eventoId":"ev-9"}}""");

        var r = await Pedir("POST", "cerrar", "id=s-1");

        Assert.Equal(200, r.Estado);
        Assert.Equal(
            [("flujo:cerrado:s-1", "cerrado", """{"eventoId":"ev-9"}"""), ("flujo:evento:ev-9", "entrada", """{"eventoId":"ev-9"}""")],
            canal.Publicados);
    }

    [Fact]
    public async Task Un_rechazo_un_canal_sin_su_dato_o_un_canal_roto_no_avisan_ni_cambian_la_respuesta()
    {
        var canal = ConAvisos(
            new AvisoEnVivoSettings { Operacion = "cerrar", Canal = "flujo:cerrado:{id}", Nombre = "cerrado" },
            new AvisoEnVivoSettings { Operacion = "cerrar", Canal = "flujo:nada:{nada}", Nombre = "nada" });

        _orquestador.Responde = _ => Orquestador.Json(HttpStatusCode.Conflict, """{"code":"eventos.already_completed","transient":false}""",
            "application/problem+json");
        Assert.Equal(409, (await Pedir("POST", "cerrar", "id=s-1")).Estado);
        Assert.Empty(canal.Publicados);

        // El que no se puede llenar no sale; el otro sí.
        _orquestador.Responde = _ => Orquestador.Json(HttpStatusCode.OK, """{"id":"s-1"}""");
        Assert.Equal(200, (await Pedir("POST", "cerrar", "id=s-1")).Estado);
        Assert.Equal(["flujo:cerrado:s-1"], canal.Publicados.Select(p => p.Canal));

        canal.Rompe = true;
        var r = await Pedir("POST", "cerrar", "id=s-1");
        Assert.Equal(200, r.Estado);
        Assert.Equal("""{"id":"s-1"}""", r.Cuerpo);
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
    public async Task Cotizar_manda_el_cuerpo_tal_cual_con_el_sujeto_y_el_negocio_y_sin_llave_aunque_el_navegador_la_mande()
    {
        const string conCupon = """{"eventId":"e1","lines":[{"tier":"GEN","quantity":2}],"promotionCode":"BIENVENIDA10"}""";

        var r = await Pedir("POST", "cotizar", cuerpo: conCupon, cabeceras: Llave("k1"));

        Assert.Equal(200, r.Estado);
        var (metodo, ruta, cabeceras, cuerpo) = Assert.Single(_orquestador.Pedidos);
        Assert.Equal(("POST", "/v1/ticket-quotes"), (metodo, ruta));
        Assert.Equal(conCupon, Encoding.UTF8.GetString(cuerpo));
        Assert.Equal($"eventos.comprador:{Ana:n}", cabeceras["X-Synergos-Sujeto"]);
        Assert.Equal(["X-Synergos-Negocio", "X-Synergos-Sujeto"], cabeceras.Keys.Order(StringComparer.Ordinal));
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

    /// <summary>El flujo de la compra con su aviso declarado POR SITIO en la sección de prueba (D-006-AVISO).</summary>
    private void AvisoPorSitio()
    {
        var bien = _ajustes.Flujos[Flujo];
        _ajustes.Flujos[Flujo] = new FlujoDeLaPuertaSettings
        {
            Acceso = bien.Acceso, SujetoKind = bien.SujetoKind, Negocio = bien.Negocio,
            Aviso = new AvisoDelFlujoSettings { Ruta = "/eventos/compra?compra={id}", Seccion = "Sitio", Campo = "Aviso" },
        };
    }

    private string EnlaceDelContacto()
    {
        var cabecera = Assert.Single(_orquestador.Pedidos).Cabeceras["X-Synergos-Contacto"];
        return JsonDocument.Parse(Convert.FromBase64String(cabecera.Replace('-', '+').Replace('_', '/').PadRight((cabecera.Length + 3) / 4 * 4, '=')))
            .RootElement.GetProperty("enlace").GetString()!;
    }

    [Fact]
    public async Task El_enlace_del_aviso_es_la_ruta_del_sitio_donde_se_reservo_si_el_flujo_la_declara_por_sitio()
    {
        AvisoPorSitio();
        _rutaDelSitio = "/hoteles/?compra={id}";

        await Pedir("POST", "cerrar", "id=s-1");

        Assert.Equal("https://sitio.test/hoteles/?compra=s-1", EnlaceDelContacto());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hoteles/?compra={id}")]
    [InlineData("https://evil.test/?compra={id}")]
    [InlineData("//evil.test/?compra={id}")]
    [InlineData("/hoteles/?compra={orden}")]
    public async Task Un_sitio_sin_ruta_o_con_una_que_no_se_puede_armar_cae_en_la_ruta_del_flujo(string? delSitio)
    {
        AvisoPorSitio();
        _rutaDelSitio = delSitio;

        await Pedir("POST", "cerrar", "id=s-1");

        Assert.Equal("https://sitio.test/eventos/compra?compra=s-1", EnlaceDelContacto());
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

    // ── Lo que se abre a invitados (núcleo, NB-3 de 011) ───────────────────────────

    private FlujoDeLaPuertaSettings Abierto(int? freno = null, params string[] invitado)
    {
        var bien = _ajustes.Flujos[Flujo];
        return new FlujoDeLaPuertaSettings
        {
            Acceso = bien.Acceso, SujetoKind = bien.SujetoKind, Negocio = bien.Negocio, Aviso = bien.Aviso,
            Invitado = invitado.Length == 0 ? ["disponibilidad"] : [.. invitado], FrenoPorMinuto = freno,
        };
    }

    [Fact]
    public async Task Una_lectura_abierta_a_invitados_pasa_sin_sesion_y_sin_sujeto()
    {
        // Ver las horas antes de iniciar sesión: la puerta pasa la lectura sin sujeto, porque no lo hay ni lo pide.
        Sesion(null);
        _ajustes.Flujos[Flujo] = Abierto();

        var r = await Pedir("POST", "disponibilidad", cuerpo: """{"desde":"2026-10-20T14:00:00Z"}""", tabla: TablaConLectura(), freno: new FrenoDeLaPuerta());

        Assert.Equal(200, r.Estado);
        var (metodo, ruta, cabeceras, cuerpo) = Assert.Single(_orquestador.Pedidos);
        Assert.Equal(("POST", "/v1/flujos/eventos.compra/disponibilidad"), (metodo, ruta));
        Assert.False(cabeceras.ContainsKey(LoQuePoneLaPuerta.CabeceraDelSujeto));
        Assert.Equal("""{"desde":"2026-10-20T14:00:00Z"}""", Encoding.UTF8.GetString(cuerpo));
    }

    [Fact]
    public async Task Sin_abrirla_o_sin_freno_una_lectura_sigue_pidiendo_sesion_y_abrir_siempre()
    {
        // Falla cerrada: lo que el flujo no abre, o un freno que no está, es «hace falta iniciar sesión». Y abrir una
        // saga nunca se abre a invitados, aunque la lectura sí.
        Sesion(null);
        var cerrada = await Pedir("POST", "disponibilidad", cuerpo: "{}", tabla: TablaConLectura(), freno: new FrenoDeLaPuerta());
        _ajustes.Flujos[Flujo] = Abierto();
        var sinFreno = await Pedir("POST", "disponibilidad", cuerpo: "{}", tabla: TablaConLectura());
        var abrir = await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave(), tabla: TablaConLectura(), freno: new FrenoDeLaPuerta());

        Assert.Equal((401, "puerta.sesion_requerida"), (cerrada.Estado, cerrada.Codigo));
        Assert.Equal((401, "puerta.sesion_requerida"), (sinFreno.Estado, sinFreno.Codigo));
        Assert.Equal((401, "puerta.sesion_requerida"), (abrir.Estado, abrir.Codigo));
        Assert.Empty(_orquestador.Pedidos);
    }

    [Fact]
    public async Task El_freno_corta_a_quien_pide_de_mas_y_no_a_los_demas()
    {
        Sesion(null);
        _ajustes.Flujos[Flujo] = Abierto(freno: 2);
        var freno = new FrenoDeLaPuerta();
        Task<Respuesta> Leer(string ip) => Pedir("POST", "disponibilidad", cuerpo: "{}", tabla: TablaConLectura(), freno: freno, ip: ip);

        Assert.Equal(200, (await Leer("203.0.113.7")).Estado);
        Assert.Equal(200, (await Leer("203.0.113.7")).Estado);
        var demas = await Leer("203.0.113.7");
        var otro = await Leer("198.51.100.9");

        Assert.Equal((429, "puerta.demasiadas_peticiones", true), (demas.Estado, demas.Codigo, demas.Json.GetProperty("transient").GetBoolean()));
        Assert.InRange(int.Parse(demas.Cabeceras.RetryAfter.ToString(), System.Globalization.CultureInfo.InvariantCulture), 1, 60);
        Assert.Equal(200, otro.Estado);
        Assert.Equal(3, _orquestador.Pedidos.Count);
    }

    [Fact]
    public void A_un_invitado_solo_se_le_abre_una_lectura_sin_dueno()
    {
        // Lo dice el CONTRATO: abrir y consultar ponen sujeto; listarlas en Invitado no las vuelve de nadie.
        ValidateOptionsResult Con(FlujoDeLaPuertaSettings f) => new ValidadorDeLaPuerta(TablaConLectura(), Secciones)
            .Validate(null, new PuertaSettings { Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [Flujo] = f } });

        Assert.True(Con(Abierto()).Succeeded);
        Assert.Contains("a quien no inició sesión sólo se le abre una lectura sin dueño", Con(Abierto(null, "abrir")).FailureMessage, StringComparison.Ordinal);
        Assert.Contains("a quien no inició sesión sólo se le abre una lectura sin dueño", Con(Abierto(null, "consultar")).FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Invitado abre «vender», que no es una operación del flujo", Con(Abierto(null, "vender")).FailureMessage, StringComparison.Ordinal);
        Assert.Contains("FrenoPorMinuto es 0", Con(Abierto(freno: 0)).FailureMessage, StringComparison.Ordinal);
    }

    // ── El acceso del personal (núcleo, P8 = A del lote 6) ─────────────────────────

    /// <summary>La misma tabla con todas sus operaciones marcadas <c>acceso: personal</c>, como un flujo del personal.</summary>
    private static TablaDeLaPuerta DelPersonal(bool conLectura = false)
    {
        var doc = conLectura ? ContratoConLectura() : ContratoDeLaCompra();
        foreach (var ruta in doc["paths"]!.AsObject())
        {
            foreach (var metodo in ruta.Value!.AsObject())
            {
                if (metodo.Value![TablaDeLaPuerta.MarcaDelFlujo] is JsonObject marca) marca["acceso"] = TablaDeLaPuerta.AccesoDelPersonal;
            }
        }
        return TablaDeLaPuerta.De([("Synergos.Bff.Eventos.json", doc.ToJsonString())]);
    }

    private FlujoDeLaPuertaSettings Personal(params string[] roles)
    {
        var bien = _ajustes.Flujos[Flujo];
        return new FlujoDeLaPuertaSettings
        {
            Acceso = AccesoDeLaPuerta.Personal, Roles = [.. roles], SujetoKind = "eventos.personal",
            Negocio = bien.Negocio, Aviso = bien.Aviso,
        };
    }

    [Fact]
    public async Task Una_operacion_del_personal_pasa_con_el_rol_y_el_sujeto_es_la_persona_del_personal()
    {
        _ajustes.Flujos[Flujo] = Personal("vendedor", "admin");
        _miembro.HasAnyRole(Arg.Any<string?>()).Returns(true);

        var r = await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave(), tabla: DelPersonal());

        Assert.Equal(200, r.Estado);
        _miembro.Received().HasAnyRole("vendedor,admin");
        var (_, _, cabeceras, _) = Assert.Single(_orquestador.Pedidos);
        Assert.Equal(LoQuePoneLaPuerta.Sujeto("eventos.personal", Ana), cabeceras[LoQuePoneLaPuerta.CabeceraDelSujeto]);
    }

    [Fact]
    public async Task Sin_el_rol_es_403_sin_sesion_401_y_no_sale_nada()
    {
        _ajustes.Flujos[Flujo] = Personal("vendedor");
        _miembro.HasAnyRole(Arg.Any<string?>()).Returns(false);
        var sinRol = await Pedir("POST", "abrir", cuerpo: Compra, cabeceras: Llave(), tabla: DelPersonal());
        Sesion(null);
        var sinSesion = await Pedir("GET", "consultar", consulta: "id=s-1", tabla: DelPersonal());

        Assert.Equal((403, "puerta.personal_requerido", false), (sinRol.Estado, sinRol.Codigo, sinRol.Json.GetProperty("transient").GetBoolean()));
        Assert.Equal((401, "puerta.sesion_requerida"), (sinSesion.Estado, sinSesion.Codigo));
        Assert.Empty(_orquestador.Pedidos);
    }

    [Fact]
    public async Task Una_operacion_del_personal_no_se_abre_a_invitados_aunque_la_listen()
    {
        // Ni al arrancar ni, si llegara, al pedirla: lo del personal pide el rol, y un invitado no tiene ninguno.
        var tabla = DelPersonal(conLectura: true);
        var ajustes = Personal("vendedor");
        var abierta = new FlujoDeLaPuertaSettings
        {
            Acceso = ajustes.Acceso, Roles = ajustes.Roles, SujetoKind = ajustes.SujetoKind, Negocio = ajustes.Negocio,
            Aviso = ajustes.Aviso, Invitado = ["disponibilidad"],
        };
        var arranque = new ValidadorDeLaPuerta(tabla, Secciones)
            .Validate(null, new PuertaSettings { Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [Flujo] = abierta } });
        Sesion(null);
        _ajustes.Flujos[Flujo] = abierta;
        var r = await Pedir("POST", "disponibilidad", cuerpo: "{}", tabla: tabla, freno: new FrenoDeLaPuerta());

        Assert.Contains("Invitado abre «disponibilidad», que pone un sujeto, una llave o un contacto, o es del personal", arranque.FailureMessage, StringComparison.Ordinal);
        Assert.Equal((401, "puerta.sesion_requerida"), (r.Estado, r.Codigo));
        Assert.Empty(_orquestador.Pedidos);
    }

    [Fact]
    public void El_acceso_del_personal_se_cruza_con_el_contrato_en_los_dos_sentidos()
    {
        ValidateOptionsResult Con(TablaDeLaPuerta tabla, FlujoDeLaPuertaSettings f) => new ValidadorDeLaPuerta(tabla, Secciones)
            .Validate(null, new PuertaSettings { Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [Flujo] = f } });

        Assert.True(Con(DelPersonal(), Personal("vendedor")).Succeeded);
        Assert.Contains("Acceso es «Miembro» y el contrato marca del personal abrir, cancelar, cerrar, consultar, cotizar",
            Con(DelPersonal(), _ajustes.Flujos[Flujo]).FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Acceso es «Personal» y el contrato no marca del personal abrir, cancelar, cerrar, consultar, cotizar",
            Con(TablaDeLaCompra(), Personal("vendedor")).FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Roles está vacío o tiene uno en blanco", Con(DelPersonal(), Personal()).FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Roles está vacío o tiene uno en blanco", Con(DelPersonal(), Personal(" ")).FailureMessage, StringComparison.Ordinal);
        var miembroConRoles = Personal("vendedor");
        Assert.Contains("Roles sólo vale con Acceso «Personal»",
            Con(TablaDeLaCompra(), new FlujoDeLaPuertaSettings
            {
                Acceso = AccesoDeLaPuerta.Miembro, Roles = miembroConRoles.Roles, SujetoKind = "eventos.comprador",
                Negocio = miembroConRoles.Negocio, Aviso = miembroConRoles.Aviso,
            }).FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_marca_con_otro_acceso_no_arma_la_tabla()
    {
        var doc = ContratoDeLaCompra();
        doc["paths"]!["/v1/ticket-quotes"]!["post"]![TablaDeLaPuerta.MarcaDelFlujo]!["acceso"] = "duenos";

        var ex = Assert.Throws<InvalidOperationException>(() => TablaDeLaPuerta.De([("Synergos.Bff.Eventos.json", doc.ToJsonString())]));
        Assert.Contains("el acceso «duenos» no es uno que la puerta sepa dar", ex.Message, StringComparison.Ordinal);
    }

    // ── La configuración, validada al arrancar ─────────────────────────────────────

    private static readonly SeccionesDelSitio Secciones = new(
        [
            new SeccionDeNegocioRegistrada("Eventos", typeof(NegocioDeEventos), _ => new NegocioDeEventos("/api/eventos", 12m, 10m)),
            new SeccionDeNegocioRegistrada("Sitio", typeof(NegocioDelSitio), _ => new NegocioDelSitio(null)),
        ],
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
        { "sujeto-de", "SujetoDe «documento» tiene que ser «miembro» o «correo»" },
        { "en-vivo", "EnVivo avisa tras «pagar», que no es una operación del flujo" },
        { "aviso-a-medias", "Aviso necesita Seccion y Campo juntos" },
        { "aviso-seccion", "Aviso:Seccion «Sitios» no es una sección de negocio registrada" },
        { "aviso-campo", "Aviso:Campo «Cupos» no es un campo de texto de NegocioDelSitio" },
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
            "sujeto-de" => (Flujo, ConSujetoDe(bien, "documento")),
            "en-vivo" => (Flujo, new FlujoDeLaPuertaSettings
            {
                Acceso = bien.Acceso, SujetoKind = bien.SujetoKind, Negocio = bien.Negocio, Aviso = bien.Aviso,
                EnVivo = [new AvisoEnVivoSettings { Operacion = "pagar", Canal = "flujo:x:{id}", Nombre = "x" }],
            }),
            "aviso-a-medias" => (Flujo, ConAviso(bien, new AvisoDelFlujoSettings { Ruta = bien.Aviso!.Ruta, Seccion = "Sitio" })),
            "aviso-seccion" => (Flujo, ConAviso(bien, new AvisoDelFlujoSettings { Ruta = bien.Aviso!.Ruta, Seccion = "Sitios", Campo = "Aviso" })),
            "aviso-campo" => (Flujo, ConAviso(bien, new AvisoDelFlujoSettings { Ruta = bien.Aviso!.Ruta, Seccion = "Sitio", Campo = "Cupos" })),
            _ => (Flujo, new FlujoDeLaPuertaSettings { Acceso = bien.Acceso, SujetoKind = bien.SujetoKind, Negocio = bien.Negocio, Aviso = new AvisoDelFlujoSettings { Ruta = "/compra?c={orden}" } }),
        };

        var r = Validar(clave, flujo);

        Assert.True(r.Failed, caso);
        Assert.Contains(motivo, r.FailureMessage, StringComparison.Ordinal);
    }

    private static FlujoDeLaPuertaSettings ConAviso(FlujoDeLaPuertaSettings bien, AvisoDelFlujoSettings aviso)
        => new() { Acceso = bien.Acceso, SujetoKind = bien.SujetoKind, Negocio = bien.Negocio, Aviso = aviso };

    [Fact]
    public void La_ruta_del_aviso_por_sitio_es_opcional_y_con_su_campo_de_texto_arranca()
    {
        var bien = _ajustes.Flujos[Flujo];

        Assert.True(Validar(Flujo, ConAviso(bien, new AvisoDelFlujoSettings { Ruta = bien.Aviso!.Ruta, Seccion = "Sitio", Campo = "Aviso" })).Succeeded);
        // Sin la ruta del flujo no arranca aunque declare la del sitio: es la que rige donde un sitio no la declara.
        Assert.True(Validar(Flujo, ConAviso(bien, new AvisoDelFlujoSettings { Seccion = "Sitio", Campo = "Aviso" })).Failed);
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
