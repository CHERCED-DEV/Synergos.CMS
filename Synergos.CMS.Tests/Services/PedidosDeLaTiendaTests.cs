using System.Text.Json;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La bandeja del personal (013-seller-operaciones, T007, R7): los pedidos PAGADOS de la tienda entera, de los dos
/// caminos, del más reciente al más antiguo y de a 50, cada uno en la etapa de su seguimiento y con los actos que admite
/// (R4); y las cuentas por etapa de toda la tienda, no de la página.
/// </summary>
public sealed class PedidosDeLaTiendaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ana = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>Una tienda en memoria: el almacén de órdenes, el seguimiento de verdad y la puerta.</summary>
    private sealed class Tienda
    {
        public InMemoryJsonEntityStore Almacen { get; } = new();
        public StubOrderTrackingService Seguimiento { get; }
        public IPuertaDeLosFlujos Puerta { get; } = Substitute.For<IPuertaDeLosFlujos>();
        public PedidosDeLaTienda Pedidos { get; }

        public Tienda(bool despachoAbierto = true)
        {
            Seguimiento = new StubOrderTrackingService(null, () => T0, Almacen, "tracking-shop");
            Puerta.Abre(Arg.Any<string>()).Returns(despachoAbierto);
            Pedidos = new PedidosDeLaTienda(new OrdenesDeLaTienda(Almacen), Seguimiento, Puerta);
        }

        /// <summary>Una orden de la directa, como la persiste el motor en proceso.</summary>
        public Task Directa(string orderRef, DateTimeOffset cuando, OrderStatus estado = OrderStatus.Paid)
            => Almacen.WriteAsync(LibroDeComprasDeLaPuerta.ResourceType, orderRef, JsonSerializer.Serialize(new PersistedOrder(
                orderRef, $"N-{orderRef}", $"ps_{orderRef}", "Beto", "beto@ejemplo.co", 54_000m, "COP",
                [new PersistedOrderLine("p-1", null, "Taza", 1, 54_000m, 54_000m, "COP", "res_1")],
                cuando, estado, Ana)));

        /// <summary>Una compra de la puerta, pagada por el libro como la paga el registro.</summary>
        public async Task DeLaPuerta(string id, DateTimeOffset cuando, bool pagada = true)
        {
            var libro = new LibroDeComprasDeLaPuerta(Almacen, Seguimiento, null, () => cuando);
            var compra = new CompraDeLaPuerta(id, $"N-{id}", Ana, "Ana", "ana@ejemplo.co",
                [new LineaDeLaCompra("p-2", null, "Camiseta", 1, 35_000m)], 35_000m, "COP");
            if (pagada) await libro.ConfirmarAsync(compra);
            else await libro.AnotarPendienteAsync(compra);
        }

        public Task Etapa(string orderRef, string etapa) => Seguimiento.AdvanceAsync(orderRef, etapa);
    }

    [Fact]
    public async Task Sin_pedidos_la_bandeja_esta_vacia_con_las_cuatro_cuentas_en_cero()
    {
        var bandeja = await new Tienda().Pedidos.BandejaAsync(null, 1, default);

        Assert.Empty(bandeja.Pedidos);
        Assert.Equal(0, bandeja.Total);
        Assert.Equal(PedidosDeLaTienda.PorPagina, bandeja.PorPagina);
        Assert.Equal(["paid", "preparing", "shipped", "delivered"], bandeja.Cuentas.Keys.ToArray());
        Assert.All(bandeja.Cuentas.Values, c => Assert.Equal(0, c));
    }

    [Fact]
    public async Task Ve_los_pagados_de_los_dos_caminos_con_su_camino_su_etapa_y_su_acto()
    {
        var tienda = new Tienda();
        await tienda.Directa("dir-1", T0);
        await tienda.DeLaPuerta("pta-1", T0.AddMinutes(1));

        var bandeja = await tienda.Pedidos.BandejaAsync(null, 1, default);

        Assert.Equal(2, bandeja.Total);
        var puerta = Assert.Single(bandeja.Pedidos, p => p.Orden.OrderRef == "pta-1");
        var directa = Assert.Single(bandeja.Pedidos, p => p.Orden.OrderRef == "dir-1");
        Assert.Equal(("puerta", "paid"), (puerta.Camino, puerta.Etapa));
        Assert.Equal(("directa", "paid"), (directa.Camino, directa.Etapa));
        Assert.Equal(["preparar"], puerta.Actos);
        Assert.Null(puerta.MotivoSinActos);
    }

    [Fact]
    public async Task Fuera_los_pendientes_y_los_cancelados_de_los_dos_caminos()
    {
        var tienda = new Tienda();
        await tienda.Directa("dir-pagado", T0);
        await tienda.Directa("dir-pendiente", T0, OrderStatus.Pending);
        await tienda.Directa("dir-cancelado", T0, OrderStatus.Cancelled);
        await tienda.DeLaPuerta("pta-pendiente", T0, pagada: false);

        var bandeja = await tienda.Pedidos.BandejaAsync(null, 1, default);

        Assert.Equal(["dir-pagado"], bandeja.Pedidos.Select(p => p.Orden.OrderRef).ToArray());
        Assert.Equal(1, bandeja.Cuentas.Values.Sum());
        Assert.Null(await tienda.Pedidos.PedidoAsync("pta-pendiente", default));
        Assert.Null(await tienda.Pedidos.PedidoAsync("dir-cancelado", default));
    }

    [Fact]
    public async Task Del_mas_reciente_al_mas_antiguo()
    {
        var tienda = new Tienda();
        await tienda.Directa("viejo", T0);
        await tienda.DeLaPuerta("nuevo", T0.AddDays(2));
        await tienda.Directa("medio", T0.AddDays(1));

        var bandeja = await tienda.Pedidos.BandejaAsync(null, 1, default);

        Assert.Equal(["nuevo", "medio", "viejo"], bandeja.Pedidos.Select(p => p.Orden.OrderRef).ToArray());
    }

    [Fact]
    public async Task El_filtro_por_etapa_deja_los_de_esa_etapa_y_una_etapa_desconocida_se_rechaza()
    {
        var tienda = new Tienda();
        await tienda.Directa("a", T0);
        await tienda.Directa("b", T0.AddMinutes(1));
        await tienda.DeLaPuerta("c", T0.AddMinutes(2));
        await tienda.Etapa("b", "shipped");
        await tienda.Etapa("c", "shipped");

        var enviados = await tienda.Pedidos.BandejaAsync("shipped", 1, default);

        Assert.Equal(["c", "b"], enviados.Pedidos.Select(p => p.Orden.OrderRef).ToArray());
        Assert.Equal(2, enviados.Total);
        await Assert.ThrowsAsync<ArgumentException>(() => tienda.Pedidos.BandejaAsync("enviado", 1, default));
    }

    [Fact]
    public async Task Cincuenta_y_un_pedidos_son_dos_paginas_y_las_cuentas_son_de_la_tienda_entera()
    {
        var tienda = new Tienda();
        for (var i = 0; i < 51; i++) await tienda.Directa($"o-{i:00}", T0.AddMinutes(i));
        await tienda.Etapa("o-00", "delivered");
        await tienda.Etapa("o-01", "preparing");

        var primera = await tienda.Pedidos.BandejaAsync(null, 1, default);
        var segunda = await tienda.Pedidos.BandejaAsync(null, 2, default);
        var tercera = await tienda.Pedidos.BandejaAsync(null, 3, default);

        Assert.Equal(50, primera.Pedidos.Count);
        Assert.Equal("o-50", primera.Pedidos[0].Orden.OrderRef);
        Assert.Equal(["o-00"], segunda.Pedidos.Select(p => p.Orden.OrderRef).ToArray());
        Assert.Empty(tercera.Pedidos);
        Assert.Equal(51, segunda.Total);
        // Las cuentas no son de la página: el entregado y el que se prepara están en la segunda y en la primera.
        Assert.Equal(49, segunda.Cuentas["paid"]);
        Assert.Equal(1, segunda.Cuentas["preparing"]);
        Assert.Equal(1, segunda.Cuentas["delivered"]);

        var entregados = await tienda.Pedidos.BandejaAsync("delivered", 1, default);
        Assert.Equal(1, entregados.Total);
        Assert.Equal(49, entregados.Cuentas["paid"]);
    }

    [Fact]
    public async Task Cada_etapa_tiene_su_acto_y_entregado_ninguno()
    {
        var tienda = new Tienda();
        string[] etapas = ["paid", "preparing", "shipped", "delivered"];
        for (var i = 0; i < etapas.Length; i++)
        {
            await tienda.Directa(etapas[i], T0.AddMinutes(i));
            await tienda.Etapa(etapas[i], etapas[i]);
        }

        var porEtapa = (await tienda.Pedidos.BandejaAsync(null, 1, default)).Pedidos.ToDictionary(p => p.Etapa);

        Assert.Equal(["preparar"], porEtapa["paid"].Actos);
        Assert.Equal(["enviar"], porEtapa["preparing"].Actos);
        Assert.Equal(["entregar"], porEtapa["shipped"].Actos);
        Assert.Empty(porEtapa["delivered"].Actos);
        Assert.Equal("entregado", porEtapa["delivered"].MotivoSinActos);
    }

    [Fact]
    public async Task Sin_el_despacho_abierto_uno_de_la_puerta_no_tiene_actos_y_uno_de_la_directa_si()
    {
        var tienda = new Tienda(despachoAbierto: false);
        await tienda.DeLaPuerta("pta-1", T0);
        await tienda.Directa("dir-1", T0);

        var puerta = await tienda.Pedidos.PedidoAsync("pta-1", default);
        var directa = await tienda.Pedidos.PedidoAsync("dir-1", default);

        Assert.Empty(puerta!.Actos);
        Assert.Equal("despacho_no_disponible", puerta.MotivoSinActos);
        Assert.Equal(["preparar"], directa!.Actos);
        tienda.Puerta.Received().Abre(PedidosDeLaTienda.FlujoDelEnvio);
    }

    [Fact]
    public async Task Basta_que_la_puerta_no_abra_uno_de_los_dos_flujos_del_despacho()
    {
        var tienda = new Tienda();
        tienda.Puerta.Abre(PedidosDeLaTienda.FlujoDeLaEntrega).Returns(false);
        await tienda.DeLaPuerta("pta-1", T0);

        var puerta = await tienda.Pedidos.PedidoAsync("pta-1", default);

        Assert.Empty(puerta!.Actos);
        Assert.Equal("despacho_no_disponible", puerta.MotivoSinActos);
    }

    [Fact]
    public async Task Una_orden_ilegible_se_salta_y_el_detalle_de_una_que_no_existe_es_nulo()
    {
        var tienda = new Tienda();
        await tienda.Directa("dir-1", T0);
        await tienda.Almacen.WriteAsync(LibroDeComprasDeLaPuerta.ResourceType, "rota", "{ no es json");

        var bandeja = await tienda.Pedidos.BandejaAsync(null, 1, default);

        Assert.Equal(["dir-1"], bandeja.Pedidos.Select(p => p.Orden.OrderRef).ToArray());
        Assert.Null(await tienda.Pedidos.PedidoAsync("rota", default));
        Assert.Null(await tienda.Pedidos.PedidoAsync("no-existe", default));
        Assert.Null(await tienda.Pedidos.PedidoAsync("  ", default));
    }
}
