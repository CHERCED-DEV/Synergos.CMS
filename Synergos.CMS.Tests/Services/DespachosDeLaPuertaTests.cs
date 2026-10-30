using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El despacho en el CMS (013-seller-operaciones, T018; US 2): enviar o entregar una compra de la PUERTA abre el flujo
/// posterior del orquestador —<c>tienda.envio</c> o <c>tienda.entrega</c>, del personal— por el reenvío de la puerta, con
/// el molde de <see cref="DevolucionesDeLaPuertaTests"/>.
/// </summary>
public sealed class DespachosDeLaPuertaTests
{
    private static readonly Guid Ana = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Rosa = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Tomas = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private const string Compra = "pta-compra-1";

    [Fact]
    public async Task El_personal_lo_envia_por_la_puerta_con_su_sujeto_la_llave_del_acto_y_la_guia()
    {
        var orquestador = new Orquestador();

        var r = await Pieza(orquestador, Personal(Rosa)).EnviarAsync(Compra, "  GUIA-123  ", CancellationToken.None);

        Assert.True(r.Bien, r.Mensaje);
        Assert.Equal("InTransit", r.EstadoDelEnvio);
        var (metodo, ruta, cabeceras, cuerpo) = Assert.Single(orquestador.Pedidos);
        Assert.Equal(("POST", "/v1/flujos/tienda.envio"), (metodo, ruta));
        Assert.Equal($"tienda.personal:{Rosa:n}", cabeceras["X-Synergos-Sujeto"]);
        Assert.Equal(DespachosDeLaPuerta.Llave(DespachosDeLaPuerta.FlujoDelEnvio, Compra, "enviar"), cabeceras["Idempotency-Key"]);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal(Compra, json.RootElement.GetProperty("compra").GetString());
        Assert.Equal("GUIA-123", json.RootElement.GetProperty("guia").GetString());
    }

    [Fact]
    public async Task Entregar_va_por_tienda_entrega_con_su_propia_llave_y_sin_guia()
    {
        var orquestador = new Orquestador();
        var pieza = Pieza(orquestador, Personal(Rosa));

        await pieza.EnviarAsync(Compra, null, CancellationToken.None);
        var r = await pieza.EntregarAsync(Compra, CancellationToken.None);

        Assert.Equal("Delivered", r.EstadoDelEnvio);
        var (envio, entrega) = (orquestador.Pedidos[0], orquestador.Pedidos[1]);
        Assert.Equal("/v1/flujos/tienda.entrega", entrega.Ruta);
        Assert.NotEqual(envio.Cabeceras["Idempotency-Key"], entrega.Cabeceras["Idempotency-Key"]);
        using var cuerpo = JsonDocument.Parse(entrega.Cuerpo);
        Assert.False(cuerpo.RootElement.TryGetProperty("guia", out _));
        // Sin guía, el envío no lleva una nota vacía.
        using var sinGuia = JsonDocument.Parse(envio.Cuerpo);
        Assert.Equal(JsonValueKind.Null, sinGuia.RootElement.GetProperty("guia").ValueKind);
    }

    [Fact]
    public async Task Dos_personas_del_personal_llevan_la_misma_llave_para_el_mismo_acto()
    {
        var orquestador = new Orquestador();

        await Pieza(orquestador, Personal(Rosa)).EnviarAsync(Compra, "GUIA-123", CancellationToken.None);
        await Pieza(orquestador, Personal(Tomas)).EnviarAsync(Compra, "GUIA-123", CancellationToken.None);
        await Pieza(orquestador, Personal(Tomas)).EnviarAsync("pta-compra-2", "GUIA-123", CancellationToken.None);

        var llaves = orquestador.Pedidos.Select(p => p.Cabeceras["Idempotency-Key"]).ToList();
        Assert.Equal(llaves[0], llaves[1]);
        Assert.NotEqual(llaves[0], llaves[2]);
        Assert.NotEqual(orquestador.Pedidos[0].Cabeceras["X-Synergos-Sujeto"], orquestador.Pedidos[1].Cabeceras["X-Synergos-Sujeto"]);
    }

    [Fact]
    public async Task Sin_el_rol_del_flujo_o_sin_sesion_no_sale_nada()
    {
        var orquestador = new Orquestador();

        var sinRol = await Pieza(orquestador, Miembro(Ana)).EnviarAsync(Compra, null, CancellationToken.None);
        var sinSesion = await Pieza(orquestador, Miembro(null)).EntregarAsync(Compra, CancellationToken.None);

        Assert.Equal("puerta.personal_requerido", sinRol.Codigo);
        Assert.Equal("puerta.sesion_requerida", sinSesion.Codigo);
        Assert.Empty(orquestador.Pedidos);
    }

    [Fact]
    public async Task Sin_el_despacho_abierto_en_el_sitio_no_sale_nada()
    {
        var orquestador = new Orquestador();

        var sinDestino = await Pieza(orquestador, Personal(Rosa), conDestino: false).EnviarAsync(Compra, null, CancellationToken.None);
        var sinEntrada = await Pieza(orquestador, Personal(Rosa), declaradas: false).EntregarAsync(Compra, CancellationToken.None);

        Assert.Equal(DespachosDeLaPuerta.NoDisponible, sinDestino.Codigo);
        Assert.Equal(DespachosDeLaPuerta.NoDisponible, sinEntrada.Codigo);
        Assert.Empty(orquestador.Pedidos);
    }

    [Fact]
    public async Task Un_rechazo_del_orquestador_pasa_con_su_codigo_y_no_dice_estado()
    {
        var orquestador = new Orquestador
        {
            Responde = _ => Orquestador.Json(HttpStatusCode.Conflict,
                """{"code":"fulfillment.transition_not_allowed","detail":"De Ready no se pasa a Delivered.","transient":false}""",
                "application/problem+json"),
        };

        var r = await Pieza(orquestador, Personal(Rosa)).EntregarAsync(Compra, CancellationToken.None);

        Assert.False(r.Bien);
        Assert.Equal(("fulfillment.transition_not_allowed", null, false), (r.Codigo, r.EstadoDelEnvio, r.Transitorio));
    }

    [Fact]
    public async Task Una_caida_o_un_despacho_que_no_termino_son_transitorios()
    {
        var caido = new Orquestador
        {
            Responde = _ => Orquestador.Json(HttpStatusCode.ServiceUnavailable,
                """{"code":"fulfillment.unreachable","detail":"fulfillment no respondió.","transient":true}""", "application/problem+json"),
        };
        var aMedias = new Orquestador
        {
            Responde = _ => Orquestador.Json(HttpStatusCode.Created,
                """{"flujo":"tienda.envio","id":"despacho-1","numero":"ENV00001","estado":"Running","datos":{"compra":"pta-compra-1","estadoDelEnvio":null},"error":null}"""),
        };

        var r1 = await Pieza(caido, Personal(Rosa)).EnviarAsync(Compra, null, CancellationToken.None);
        var r2 = await Pieza(aMedias, Personal(Rosa)).EnviarAsync(Compra, null, CancellationToken.None);

        Assert.Equal(("fulfillment.unreachable", true), (r1.Codigo, r1.Transitorio));
        Assert.Equal(("tienda.despacho_en_curso", true, null), (r2.Codigo, r2.Transitorio, r2.EstadoDelEnvio));
    }

    [Theory]
    [InlineData(DespachosDeLaPuerta.FlujoDelEnvio, true)]
    [InlineData(DespachosDeLaPuerta.FlujoDeLaEntrega, false)]
    public void Lo_que_manda_y_lee_el_despacho_existe_en_el_contrato_publicado(string flujo, bool conGuia)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Raiz(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Tienda.json")));
        var raiz = doc.RootElement;
        var op = raiz.GetProperty("paths").GetProperty($"/v1/flujos/{flujo}").GetProperty("post");

        var marca = op.GetProperty(TablaDeLaPuerta.MarcaDelFlujo);
        Assert.Equal((flujo, DespachosDeLaPuerta.Operacion, TablaDeLaPuerta.AccesoDelPersonal),
            (marca.GetProperty("flujo").GetString(), marca.GetProperty("operacion").GetString(), marca.GetProperty("acceso").GetString()));
        var cabeceras = op.GetProperty("parameters").EnumerateArray()
            .Where(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("required").GetBoolean())
            .Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Contains(LoQuePoneLaPuerta.CabeceraDelSujeto, cabeceras);
        Assert.Contains(LoQuePoneLaPuerta.CabeceraDeLaLlave, cabeceras);

        var entrada = Esquema(raiz, op.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"))
            .GetProperty("properties");
        Assert.True(entrada.TryGetProperty("compra", out _));
        Assert.Equal(conGuia, entrada.TryGetProperty("guia", out _));

        var respuesta = Esquema(raiz, op.GetProperty("responses").GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.True(respuesta.GetProperty("properties").TryGetProperty("estado", out _));
        var datos = Esquema(raiz, respuesta.GetProperty("properties").GetProperty("datos")).GetProperty("properties");
        Assert.True(datos.TryGetProperty("estadoDelEnvio", out _));
    }

    private static JsonElement Esquema(JsonElement raiz, JsonElement esquema)
    {
        if (esquema.TryGetProperty("oneOf", out var ramas))
        {
            esquema = ramas.EnumerateArray().First(r => !(r.TryGetProperty("type", out var t) && t.GetString() == "null"));
        }
        while (esquema.TryGetProperty("$ref", out var referencia))
        {
            esquema = raiz.GetProperty("components").GetProperty("schemas").GetProperty(referencia.GetString()!.Split('/').Last());
        }
        return esquema;
    }

    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ── Los dobles ───────────────────────────────────────────────────────────────────────────

    private static IMemberAccessGate Personal(Guid quien)
    {
        var gate = Miembro(quien);
        gate.HasAnyRole(Arg.Is<string?>(r => r != null && r.Split(',', StringSplitOptions.None).Contains("admin"))).Returns(true);
        return gate;
    }

    private static IMemberAccessGate Miembro(Guid? quien)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(quien is not null);
        gate.CurrentMemberKey.Returns(quien);
        return gate;
    }

    private static DespachosDeLaPuerta Pieza(Orquestador orquestador, IMemberAccessGate miembro, bool conDestino = true, bool declaradas = true)
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        var puerta = new PuertaSettings();
        if (declaradas)
        {
            foreach (var flujo in new[] { DespachosDeLaPuerta.FlujoDelEnvio, DespachosDeLaPuerta.FlujoDeLaEntrega })
            {
                puerta.Flujos[flujo] = new FlujoDeLaPuertaSettings
                {
                    Acceso = AccesoDeLaPuerta.Personal, Roles = ["admin"], SujetoKind = "tienda.personal",
                };
            }
        }
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(puerta);
        var destinos = new DestinosDeLaPuerta(conDestino ? ["Tienda"] : []);
        return new DespachosDeLaPuerta(
            tabla, new PuertaDeLosFlujos(tabla, monitor, destinos),
            new ReenvioDeLaPuerta(new Fabrica(orquestador), NullLogger<ReenvioDeLaPuerta>.Instance), miembro, monitor);
    }

    /// <summary>El orquestador de la tienda: por defecto, el acto completado con el estado que deja su flujo.</summary>
    private sealed class Orquestador : HttpMessageHandler
    {
        public List<(string Metodo, string Ruta, Dictionary<string, string> Cabeceras, byte[] Cuerpo)> Pedidos { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage>? Responde { get; init; }

        public static HttpResponseMessage Json(HttpStatusCode estado, string json, string tipo = "application/json")
            => new(estado) { Content = new StringContent(json, Encoding.UTF8, tipo) };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var cuerpo = r.Content is null ? [] : await r.Content.ReadAsByteArrayAsync(ct);
            Pedidos.Add((r.Method.Method, r.RequestUri!.AbsolutePath,
                r.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase), cuerpo));
            if (Responde is not null) return Responde(r);

            var flujo = r.RequestUri.AbsolutePath.Split('/').Last();
            var estado = flujo == DespachosDeLaPuerta.FlujoDelEnvio ? "InTransit" : "Delivered";
            using var pedido = JsonDocument.Parse(cuerpo);
            var compra = pedido.RootElement.GetProperty("compra").GetString();
            return Json(HttpStatusCode.Created,
                $$"""{"flujo":"{{flujo}}","id":"despacho-1","numero":"ENV00001","estado":"Completed","datos":{"compra":"{{compra}}","estadoDelEnvio":"{{estado}}"},"error":null}""");
        }
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(ReenvioDeLaPuerta.Cliente("Tienda"), name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-tienda.local/") };
        }
    }
}
