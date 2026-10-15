using System.Text.Json;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Los pasos de N2 —permiso, canasta, pedido y despacho— contra puertos falsos, y el tipo de valor de la
/// dirección (núcleo N2). Lo que mandan de verdad a cada capacidad lo cruza <c>ContratoConsumidorPasosTests</c>.
/// </summary>
public sealed class PasosDeCompraTests
{
    private static readonly Ref Comprador = Ref.Create("prueba.comprador", "u-1");
    private static readonly Ref Producto = Ref.Create("prueba.producto", "p-1");
    private static readonly Money Cien = Money.Of(100_000m, "COP");

    private sealed class Puertos : IConsentPort, ICartPort, IOrdersPort, IFulfillmentPort
    {
        public List<string> Llamadas { get; } = [];
        public Rejection? Rechaza { get; set; }
        public CanastaView Canasta { get; set; } = new(Comprador, [(Producto, 2)], Cerrada: false, Abierta: true);

        public Task<Result<string>> VerificarAsync(Ref sujeto, string proposito, CancellationToken ct)
            => Hacer("gr-1", $"permiso {sujeto} {proposito}");

        public Task<Result<CanastaView>> LeerAsync(string canasta, CancellationToken ct)
        {
            Llamadas.Add($"leer {canasta}");
            return Task.FromResult(Rechaza is { } r ? Result.Rejected<CanastaView>(r) : Result.Ok(Canasta));
        }

        public Task<Result<string>> CerrarAsync(string canasta, CancellationToken ct) => Hacer(canasta, $"cerrar {canasta}");

        public Task<Result<string>> CrearAsync(
            Ref comprador, IReadOnlyList<LineaDePedido> lineas, Money total, IdempotencyKey llave, CancellationToken ct)
            => Hacer("pd-1", $"crear {comprador} {lineas.Count} {total} {llave.Value}");

        public Task<Result<string>> CumplirAsync(string pedido, CancellationToken ct) => Hacer(pedido, $"cumplir {pedido}");

        public Task<Result<string>> DespacharAsync(
            Ref paraQue, DireccionDeEnvio direccion, string transportadora, IdempotencyKey llave, CancellationToken ct)
            => Hacer("en-1", $"despachar {paraQue} {direccion.Ciudad} {transportadora} {llave.Value}");

        private Task<Result<string>> Hacer(string valor, string llamada)
        {
            Llamadas.Add(llamada);
            return Task.FromResult(Rechaza is { } r ? Result.Rejected<string>(r) : Result.Ok(valor));
        }
    }

    private static EntradaDePaso Entrada(string tipo, string[] lee, FlowContext ctx, string? llave = null, string? proposito = null)
        => new("compra-1", Ref.Create("prueba.compra", "compra-1"),
            new PasoDef("p", tipo, lee, [], llave, null, null, null, null, Proposito: proposito), ctx, null, null);

    private static FlowContext Ctx() => new FlowContext()
        .Set("comprador", Comprador).Set("canasta", "ca-1").Set("pedido", "pd-1").Set("transportadora", "servientrega")
        .Set("lineas", (IReadOnlyList<LineaDePedido>)[new LineaDePedido(Producto, 2, Money.Of(50_000m, "COP"))]).Set("total", Cien)
        .Set("direccion", new DireccionDeEnvio("Calle 1 # 2-3", null, "Bogotá", null, null, "CO", "Ana"));

    [Fact]
    public async Task Verificar_pide_el_permiso_con_el_proposito_que_declara_la_definicion_y_devuelve_el_no_tal_cual()
    {
        var puertos = new Puertos();
        var paso = new PasoConsentimientoVerificar(puertos);

        var si = await paso.EjecutarAsync(Entrada("consent.verificar", ["comprador"], Ctx(), proposito: "salud.agenda"), CancellationToken.None);
        Assert.Equal(PasoResultado.Continuar, si.Control);
        Assert.Equal("permiso prueba.comprador:u-1 salud.agenda", Assert.Single(puertos.Llamadas));

        puertos.Rechaza = Rejection.Conflict("consent.not_granted", "guionado");
        var no = await paso.EjecutarAsync(Entrada("consent.verificar", ["comprador"], Ctx(), proposito: "salud.agenda"), CancellationToken.None);
        Assert.Equal("consent.not_granted", no.Rechazo?.Code);
    }

    [Fact]
    public async Task Leer_la_canasta_escribe_sus_lineas()
    {
        var puertos = new Puertos();

        var salida = await new PasoCanastaLeer(puertos).EjecutarAsync(Entrada("cart.leer", ["canasta", "comprador"], Ctx()), CancellationToken.None);

        Assert.Equal(PasoResultado.Continuar, salida.Control);
        var lineas = Assert.IsAssignableFrom<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(Assert.Single(salida.Valores));
        Assert.Equal((Producto, 2), Assert.Single(lineas));
    }

    [Theory]
    // La de otro no existe para éste: comprarla adivinando su id es lo que esto cierra.
    [InlineData("de otro", "u-2", false, true, 1, "cart.cart_not_found")]
    [InlineData("cerrada", "u-1", true, false, 1, "cart.already_checked_out")]
    [InlineData("vencida", "u-1", false, false, 1, "cart.expired")]
    [InlineData("vacía", "u-1", false, true, 0, "cart.empty_cart")]
    public async Task Una_canasta_que_no_se_puede_comprar_se_rechaza_antes_de_tocar_nada(
        string cual, string dueno, bool cerrada, bool abierta, int lineas, string codigo)
    {
        var puertos = new Puertos
        {
            Canasta = new CanastaView(Ref.Create("prueba.comprador", dueno), Enumerable.Repeat((Producto, 1), lineas).ToList(), cerrada, abierta),
        };

        var salida = await new PasoCanastaLeer(puertos).EjecutarAsync(Entrada("cart.leer", ["canasta", "comprador"], Ctx()), CancellationToken.None);

        Assert.True(salida.Rechazo?.Code == codigo, $"la canasta {cual} contestó {salida.Rechazo?.Code ?? "sigue"} y es {codigo}.");
    }

    [Fact]
    public async Task Cerrar_la_canasta_no_lleva_llave_y_cumplir_el_pedido_tampoco()
    {
        var puertos = new Puertos();

        Assert.Equal(PasoResultado.Continuar,
            (await new PasoCanastaCerrar(puertos).EjecutarAsync(Entrada("cart.cerrar", ["canasta"], Ctx()), CancellationToken.None)).Control);
        Assert.Equal(PasoResultado.Continuar,
            (await new PasoPedidoCumplir(puertos).EjecutarAsync(Entrada("orders.cumplir", ["pedido"], Ctx()), CancellationToken.None)).Control);

        Assert.Equal(new[] { "cerrar ca-1", "cumplir pd-1" }, puertos.Llamadas);
        Assert.Equal(LlaveRequerida.Ninguna, new PasoCanastaCerrar(puertos).Llave);
        Assert.Equal(LlaveRequerida.Ninguna, new PasoPedidoCumplir(puertos).Llave);
    }

    [Fact]
    public async Task Crear_el_pedido_reserva_el_pedido_con_la_llave_fija()
    {
        var puertos = new Puertos();

        var salida = await new PasoPedidoCrear(puertos).EjecutarAsync(
            Entrada("orders.crear", ["comprador", "lineas", "total"], Ctx(), llave: "order"), CancellationToken.None);

        Assert.Equal("pd-1", salida.Reservado?.Id);
        Assert.Equal("pd-1", Assert.Single(salida.Valores));
        Assert.Equal("crear prueba.comprador:u-1 1 100000 COP compra-1|order", Assert.Single(puertos.Llamadas));
    }

    [Fact]
    public async Task Despachar_manda_el_pedido_como_para_que()
    {
        var puertos = new Puertos();

        var salida = await new PasoDespachar(puertos).EjecutarAsync(
            Entrada("fulfillment.despachar", ["pedido", "direccion", "transportadora"], Ctx(), llave: "ship"), CancellationToken.None);

        Assert.Equal("en-1", Assert.Single(salida.Valores));
        Assert.Equal("despachar orders.pedido:pd-1 Bogotá servientrega compra-1|ship", Assert.Single(puertos.Llamadas));
    }

    [Fact]
    public void La_direccion_se_lee_entera_y_no_se_guarda()
    {
        var tipo = new TipoDireccion();
        using var json = JsonDocument.Parse("""{"linea1":"Calle 1 # 2-3","ciudad":"Bogotá","pais":"CO","contacto":"Ana"}""");

        var leida = Assert.IsType<DireccionDeEnvio>(tipo.Leer(json.RootElement, "direccion"));

        Assert.Equal(new DireccionDeEnvio("Calle 1 # 2-3", null, "Bogotá", null, null, "CO", "Ana"), leida);
        Assert.Equal("Bogotá", (string?)tipo.Escribir(leida)["ciudad"]);
        Assert.False(tipo.Guardable);
        Assert.Equal(new[] { "linea1", "ciudad", "pais" }, tipo.Esquema()["required"]!.AsArray().Select(n => (string)n!));
    }

    [Theory]
    [InlineData("""{"linea1":"Calle 1","pais":"CO"}""", "sin «ciudad»")]
    [InlineData("""{"linea1":"Calle 1","ciudad":"Bogotá"}""", "sin «pais»")]
    [InlineData("""{"linea1":"Calle 1","ciudad":"Bogotá","pais":"CO","piso":"3"}""", "trae «piso»")]
    [InlineData("""{"linea1":"Calle 1","ciudad":5,"pais":"CO"}""", "direccion.ciudad: tiene que ser un texto")]
    public void Una_direccion_incompleta_o_con_campos_de_mas_no_se_lee(string cuerpo, string mensaje)
    {
        using var json = JsonDocument.Parse(cuerpo);

        var ex = Assert.Throws<FormatException>(() => new TipoDireccion().Leer(json.RootElement, "direccion"));

        Assert.Contains(mensaje, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void La_definicion_lee_el_proposito_de_un_paso()
    {
        var flujo = FlujoDef.Leer("""
            {
              "clave": "prueba.permiso",
              "entrada": ["comprador"],
              "fases": { "abrir": ["permiso"] },
              "pasos": { "permiso": { "tipo": "consent.verificar", "lee": ["comprador"], "proposito": "salud.agenda" } }
            }
            """);

        Assert.Equal("salud.agenda", flujo.Pasos["permiso"].Proposito);
    }
}
