using System.Net.Http.Json;
using System.Text.Json;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Tienda.Domain;
using Synergos.Core;
using TiendaCapabilities = Synergos.Bff.Tienda.Clients.TiendaCapabilities;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Los pasos interinos del despacho (013-seller-operaciones, T014; interinos de NS-1), contra la capacidad de envíos DE
/// VERDAD en el arnés: lo que cada uno deja en el envío, y lo que contesta cuando el envío ya estaba, cuando la
/// transición no se permite y cuando la capacidad no responde.
/// </summary>
public sealed class PasosDelEnvioTests : IDisposable
{
    /// <summary>Los pasos como los declaran los flujos del despacho (<c>contracts/</c> de 013).</summary>
    private static readonly PasoDef EnCamino = new(
        "en-camino", "tienda.envio-en-camino", ["envio", "guia"], ["estadoDelEnvio"], null, null, null, null,
        "no se pudo marcar el envío en camino");

    private static readonly PasoDef Entregado = new(
        "entregado", "tienda.envio-entregado", ["envio"], ["estadoDelEnvio"], null, null, null, null,
        "no se pudo marcar el envío entregado");

    private readonly ArnesDeCapacidades _arnes = new ArnesDeCapacidades()
        .Levanta<Synergos.Api.Fulfillment.Contracts.ShipmentResponse>(TiendaCapabilities.Fulfillment);

    public void Dispose() => _arnes.Dispose();

    private EnviosDelDespacho Envios(IHttpClientFactory? cable = null)
    {
        var envios = new EnviosDelDespacho();
        envios.Conectar(new TiendaCapabilities(cable ?? _arnes));
        return envios;
    }

    private static Task<SalidaDePaso> Correr(IPaso paso, PasoDef def, string envio, string? guia = null)
        => paso.EjecutarAsync(
            new EntradaDePaso("despacho-1", Ref.Create("tienda.envio", "despacho-1"), def,
                new FlowContext().Set("envio", envio).Set("guia", guia), null, null),
            CancellationToken.None);

    /// <summary>Un envío listo en la capacidad, como lo deja el despacho de la compra.</summary>
    private async Task<string> UnEnvio()
    {
        using var http = _arnes.CreateClient(TiendaCapabilities.Fulfillment);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/shipments")
        {
            Content = JsonContent.Create(new
            {
                forKind = "tienda.compra", forId = "compra-1", carrier = "Servientrega",
                to = new { line1 = "Calle 10 # 5-20", city = "Bogotá", country = "CO" },
            }),
        };
        peticion.Headers.Add("Idempotency-Key", "envio-compra-1");
        using var r = await http.SendAsync(peticion);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private async Task<(string Estado, List<(string? Estado, string? Nota)> Eventos)> ElEnvio(string id)
    {
        using var http = _arnes.CreateClient(TiendaCapabilities.Fulfillment);
        var envio = await http.GetFromJsonAsync<JsonElement>($"v1/shipments/{id}");
        return (envio.GetProperty("status").GetString()!,
            envio.GetProperty("events").EnumerateArray()
                .Select(e => (e.GetProperty("status").GetString(), e.GetProperty("note").GetString()))
                .ToList());
    }

    [Fact]
    public async Task En_camino_lleva_la_guia_como_nota_y_entregado_lo_cierra()
    {
        var envios = Envios();
        var id = await UnEnvio();

        var enCamino = await Correr(new PasoEnvioEnCamino(envios), EnCamino, id, "  GUIA-123  ");
        Assert.Equal(PasoResultado.Continuar, enCamino.Control);
        Assert.Equal("InTransit", Assert.Single(enCamino.Valores));

        var entregado = await Correr(new PasoEnvioEntregado(envios), Entregado, id);
        Assert.Equal(PasoResultado.Continuar, entregado.Control);
        Assert.Equal("Delivered", Assert.Single(entregado.Valores));

        var (estado, eventos) = await ElEnvio(id);
        Assert.Equal("Delivered", estado);
        Assert.Equal([("Ready", null), ("InTransit", "GUIA-123"), ("Delivered", null)], eventos);
    }

    [Fact]
    public async Task Una_guia_en_blanco_no_es_nota()
    {
        var id = await UnEnvio();

        await Correr(new PasoEnvioEnCamino(Envios()), EnCamino, id, "   ");

        Assert.Equal(("InTransit", (string?)null), (await ElEnvio(id)).Eventos[^1]);
    }

    [Fact]
    public async Task Ya_estaba_es_hecho_y_no_suma_otro_evento()
    {
        var envios = Envios();
        var id = await UnEnvio();
        await Correr(new PasoEnvioEnCamino(envios), EnCamino, id, "GUIA-123");

        // El reintento de una respuesta perdida, u otra persona del personal que dio la misma orden.
        var otraVez = await Correr(new PasoEnvioEnCamino(envios), EnCamino, id, "GUIA-999");

        Assert.Equal(PasoResultado.Continuar, otraVez.Control);
        Assert.Null(otraVez.Rechazo);
        Assert.Equal("InTransit", Assert.Single(otraVez.Valores));
        Assert.Equal([("Ready", null), ("InTransit", "GUIA-123")], (await ElEnvio(id)).Eventos);
    }

    [Fact]
    public async Task Una_transicion_que_no_se_permite_sube_con_su_codigo_y_no_es_transitoria()
    {
        var envios = Envios();
        var id = await UnEnvio();

        var sinEnviar = await Correr(new PasoEnvioEntregado(envios), Entregado, id);

        Assert.Equal(PasoResultado.Abortar, sinEnviar.Control);
        Assert.Equal("fulfillment.transition_not_allowed", sinEnviar.Rechazo!.Code);
        Assert.False(sinEnviar.Rechazo.IsTransient);
        Assert.Equal("Ready", (await ElEnvio(id)).Estado);

        // Entregado es final: volver a ponerlo en camino sube con el código de la capacidad.
        await Correr(new PasoEnvioEnCamino(envios), EnCamino, id);
        await Correr(new PasoEnvioEntregado(envios), Entregado, id);
        var tarde = await Correr(new PasoEnvioEnCamino(envios), EnCamino, id);
        Assert.Equal("fulfillment.shipment_closed", tarde.Rechazo!.Code);
    }

    [Fact]
    public async Task Con_los_envios_caidos_el_rechazo_es_transitorio()
    {
        var salida = await Correr(new PasoEnvioEnCamino(Envios(new SinRespuesta())), EnCamino, "envio-1", "GUIA-1");

        Assert.Equal(PasoResultado.Abortar, salida.Control);
        Assert.True(salida.Rechazo!.IsTransient, salida.Rechazo.ToString());
    }

    [Fact]
    public async Task Sin_conectar_al_arrancar_no_hay_cliente_que_inventar()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Correr(new PasoEnvioEntregado(new EnviosDelDespacho()), Entregado, "envio-1"));

        Assert.Contains("no se conectaron", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Actuan_sin_llave_y_escriben_el_estado_como_texto()
    {
        var pasos = PasosDelDespacho.Propios(new EnviosDelDespacho());

        Assert.Equal(["tienda.envio-en-camino", "tienda.envio-entregado"], pasos.Select(p => p.Tipo));
        Assert.All(pasos, p =>
        {
            Assert.Equal(LlaveRequerida.Ninguna, p.Llave);
            Assert.True(p.ActuaSinLlave);
            Assert.Equal([typeof(string)], p.TiposDeEscritura!);
        });
    }

    /// <summary>La capacidad que no responde: el cable falla como una red caída.</summary>
    private sealed class SinRespuesta : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Caido()) { BaseAddress = new Uri($"http://{name}/") };

        private sealed class Caido : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("envíos no responde");
        }
    }
}
