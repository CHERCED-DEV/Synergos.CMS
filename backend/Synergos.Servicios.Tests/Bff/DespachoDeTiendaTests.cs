using System.Net;
using System.Text.Json;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El despacho del personal (013-seller-operaciones, T015; US 2): los flujos posteriores <c>tienda.envio</c> y
/// <c>tienda.entrega</c> leen el envío de la compra con <c>flow.leer-original</c> y lo mueven en la capacidad de envíos
/// con los pasos interinos de NS-1, contra el orquestador y las capacidades reales del arnés (quickstart §2).
/// </summary>
/// <remarks>
/// Lo que se mira es lo que la capacidad dice del envío —su estado y sus eventos—, no sólo lo que contestó el flujo; y
/// la compra original no se toca.
/// </remarks>
public sealed class DespachoDeTiendaTests
{
    /// <summary>Una compra de la canasta de siempre, abierta y cerrada por su comprador: su envío queda listo.</summary>
    private static async Task<(string Id, string Envio)> Completada(CompraDeTiendaReal t, string llave = "compra-1")
    {
        var (_, abierta) = await t.Abrir(llave: llave);
        var id = abierta.GetProperty("id").GetString()!;
        var (estado, cerrada) = await t.Cerrar(id);
        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        return (id, t.Dato(id, "envio"));
    }

    private static async Task<(string Estado, List<(string? Estado, string? Nota)> Eventos)> ElEnvio(CompraDeTiendaReal t, string envio)
    {
        var leido = await t.Leer("fulfillment", $"v1/shipments/{envio}");
        return (leido.GetProperty("status").GetString()!,
            leido.GetProperty("events").EnumerateArray()
                .Select(e => (e.GetProperty("status").GetString(), e.GetProperty("note").GetString()))
                .ToList());
    }

    private static string EstadoDelEnvio(JsonElement saga) => saga.GetProperty("datos").GetProperty("estadoDelEnvio").GetString()!;

    private static string Seguir(string envio) => $"POST /v1/shipments/{envio}/track";

    [Fact]
    public async Task El_personal_lo_envia_con_la_guia_como_nota_y_despues_lo_entrega()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, envio) = await Completada(t);

        var (estado, enviado) = await t.EnviarElPedido(compra, "GUIA-123", "enviar-1");
        Assert.True(estado == HttpStatusCode.Created, enviado.ToString());
        Assert.Equal("Completed", enviado.GetProperty("estado").GetString());
        Assert.Equal(compra, enviado.GetProperty("datos").GetProperty("compra").GetString());
        Assert.Equal("InTransit", EstadoDelEnvio(enviado));

        var (estado2, entregado) = await t.EntregarElPedido(compra, "entregar-1");
        Assert.True(estado2 == HttpStatusCode.Created, entregado.ToString());
        Assert.Equal("Delivered", EstadoDelEnvio(entregado));

        var (ahora, eventos) = await ElEnvio(t, envio);
        Assert.Equal("Delivered", ahora);
        Assert.Equal([("Ready", null), ("InTransit", "GUIA-123"), ("Delivered", null)], eventos);

        // La compra no se toca: sigue completada, de su comprador, con su envío.
        var (_, original) = await t.Consultar(compra);
        Assert.Equal("Completed", original.GetProperty("estado").GetString());
        Assert.Equal(envio, original.GetProperty("datos").GetProperty("envio").GetString());
    }

    [Fact]
    public async Task La_misma_llave_da_la_misma_saga_y_un_solo_evento()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, envio) = await Completada(t);

        var (_, primera) = await t.EnviarElPedido(compra, "GUIA-123", "enviar-1");
        var (estado, otra) = await t.EnviarElPedido(compra, "GUIA-123", "enviar-1");

        Assert.True(estado is HttpStatusCode.OK or HttpStatusCode.Created, otra.ToString());
        Assert.Equal(primera.GetProperty("id").GetString(), otra.GetProperty("id").GetString());
        Assert.Equal(1, t.Veces(Seguir(envio)));
        Assert.Equal([("Ready", null), ("InTransit", "GUIA-123")], (await ElEnvio(t, envio)).Eventos);
    }

    [Fact]
    public async Task Otra_persona_con_la_misma_llave_tiene_su_propia_saga_y_no_suma_otro_evento()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, envio) = await Completada(t);
        var (_, primera) = await t.EnviarElPedido(compra, "GUIA-123", "enviar-1", personal: "staff-1");

        // La llave es de su dueño (ADR 0140 F3): la de otra persona no se reutiliza. Lo que la hace inocua es el paso,
        // que cuenta como hecho lo que ya estaba.
        var (estado, otra) = await t.EnviarElPedido(compra, "GUIA-123", "enviar-1", personal: "staff-2");

        Assert.True(estado == HttpStatusCode.Created, otra.ToString());
        Assert.NotEqual(primera.GetProperty("id").GetString(), otra.GetProperty("id").GetString());
        Assert.Equal("InTransit", EstadoDelEnvio(otra));
        Assert.Equal(2, t.Veces(Seguir(envio)));
        Assert.Equal([("Ready", null), ("InTransit", "GUIA-123")], (await ElEnvio(t, envio)).Eventos);
    }

    [Fact]
    public async Task Otra_llave_sobre_lo_ya_en_camino_cuenta_como_hecho_sin_otro_evento()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, envio) = await Completada(t);
        await t.EnviarElPedido(compra, "GUIA-123", "enviar-1");

        var (estado, otra) = await t.EnviarElPedido(compra, "GUIA-999", "enviar-2", personal: "staff-2");

        Assert.True(estado == HttpStatusCode.Created, otra.ToString());
        Assert.Equal("Completed", otra.GetProperty("estado").GetString());
        Assert.Equal("InTransit", EstadoDelEnvio(otra));
        Assert.Equal(2, t.Veces(Seguir(envio)));
        Assert.Equal([("Ready", null), ("InTransit", "GUIA-123")], (await ElEnvio(t, envio)).Eventos);
    }

    [Fact]
    public async Task Si_la_respuesta_se_pierde_repetir_con_la_misma_llave_lo_termina_sin_otro_evento()
    {
        using var t = new CompraDeTiendaReal(pierdeLaPrimera: "/track");
        var (compra, envio) = await Completada(t);

        var (perdida, _) = await t.EnviarElPedido(compra, "GUIA-123", "enviar-1");
        var (estado, repetida) = await t.EnviarElPedido(compra, "GUIA-123", "enviar-1");

        Assert.NotEqual(HttpStatusCode.Created, perdida);
        Assert.True(estado is HttpStatusCode.OK or HttpStatusCode.Created, repetida.ToString());
        Assert.Equal("Completed", repetida.GetProperty("estado").GetString());
        Assert.Equal("InTransit", EstadoDelEnvio(repetida));
        Assert.Equal([("Ready", null), ("InTransit", "GUIA-123")], (await ElEnvio(t, envio)).Eventos);
    }

    [Fact]
    public async Task Entregar_sin_enviar_lo_rechaza_la_capacidad_y_el_envio_sigue_listo()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, envio) = await Completada(t);

        var (estado, rechazo) = await t.EntregarElPedido(compra, "entregar-1");

        Assert.Equal((HttpStatusCode.Conflict, "fulfillment.transition_not_allowed"), (estado, rechazo.GetProperty("code").GetString()));
        Assert.Equal("Ready", (await ElEnvio(t, envio)).Estado);
    }

    [Fact]
    public async Task Sobre_una_compra_en_curso_o_que_no_existe_no_se_mueve_ningun_envio()
    {
        using var t = new CompraDeTiendaReal();
        var (_, abierta) = await t.Abrir(llave: "en-curso");
        var enCurso = abierta.GetProperty("id").GetString()!;

        var (estado1, r1) = await t.EnviarElPedido(enCurso, null, "enviar-1");
        var (estado2, r2) = await t.EntregarElPedido("no-existe", "entregar-1");

        Assert.Equal((HttpStatusCode.Conflict, PasoLeerOriginal.NoCompletada), (estado1, r1.GetProperty("code").GetString()));
        Assert.Equal((HttpStatusCode.NotFound, PasoLeerOriginal.NoEncontrada), (estado2, r2.GetProperty("code").GetString()));
        Assert.DoesNotContain(t.Llamadas, l => l.EndsWith("/track", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Con_los_envios_caidos_es_transitorio_y_al_volver_la_misma_llave_lo_termina()
    {
        using var t = new CompraDeTiendaReal(cae: ["POST /v1/shipments/*/track"]);
        var (compra, envio) = await Completada(t);

        var (caido, rechazo) = await t.EnviarElPedido(compra, "GUIA-123", "enviar-1");

        Assert.True(caido == HttpStatusCode.ServiceUnavailable, $"{(int)caido} {rechazo}");
        Assert.Equal("Ready", (await ElEnvio(t, envio)).Estado);

        t.Encender("POST /v1/shipments/*/track");
        var (estado, enviado) = await t.EnviarElPedido(compra, "GUIA-123", "enviar-1");

        Assert.True(estado is HttpStatusCode.OK or HttpStatusCode.Created, enviado.ToString());
        Assert.Equal("InTransit", EstadoDelEnvio(enviado));
        Assert.Equal([("Ready", null), ("InTransit", "GUIA-123")], (await ElEnvio(t, envio)).Eventos);
    }

    [Fact]
    public async Task La_saga_del_acto_es_de_quien_lo_hizo_y_no_del_comprador()
    {
        using var t = new CompraDeTiendaReal();
        var (compra, _) = await Completada(t);

        var (_, enviado) = await t.EnviarElPedido(compra, null, "enviar-1", personal: "staff-7");

        var saga = t.Sagas.Find(enviado.GetProperty("id").GetString()!)!;
        Assert.Equal("tienda.envio", saga.Flujo);
        Assert.Equal(Ref.Create("tienda.personal", "staff-7"), saga.Dueno);
        Assert.Equal(Ref.Create("tienda.comprador", "u-1"), t.Sagas.Find(compra)!.Dueno);
    }
}
