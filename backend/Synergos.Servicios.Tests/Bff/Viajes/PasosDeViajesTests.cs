using System.Text.Json;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Viajes.Contracts;
using Synergos.Bff.Viajes.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// Los pasos propios de la reserva por la puerta (línea 006, T006): la regla del itinerario con los códigos de
/// <see cref="TripFlow"/>, el sujeto del recurso desde el producto, el primer vencimiento y el tipo <c>itinerario</c>.
/// </summary>
public sealed class PasosDeViajesTests
{
    private static readonly DateTimeOffset Dia = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static FlowContext Item(string producto, DateTimeOffset inicio, DateTimeOffset fin, string? etiqueta = null)
        => new FlowContext().Set("producto", producto).Set("etiqueta", etiqueta).Set("ventana", TimeWindow.Of(inicio, fin));

    private static Task<SalidaDePaso> Correr(IPaso paso, string id, FlowContext ctx, string? alias = null, FlowContext? item = null)
    {
        var def = FlujosDeclarados.Incrustado(typeof(TripResponse).Assembly, "flujos/viajes.reserva.json").Pasos[id];
        return paso.EjecutarAsync(new EntradaDePaso("viaje-1", Ref.Create("viajes.reserva", "viaje-1"), def, ctx, alias, item),
            CancellationToken.None);
    }

    private static Task<SalidaDePaso> Revisar(params FlowContext[] items)
        => Correr(new PasoRevisarItinerario(), "revisar", new FlowContext().Set("items", (IReadOnlyList<FlowContext>)items));

    [Fact]
    public async Task Un_itinerario_se_cotiza_por_la_TARIFA_de_cada_producto_y_se_guarda_sin_ids()
    {
        var salida = await Revisar(
            Item("vuelo/AV-8020", Dia, Dia.AddHours(2), "Bogotá → Cartagena"),
            Item("hotel/caribe/DBL", Dia.AddHours(7), Dia.AddDays(2)));

        Assert.Equal(PasoResultado.Continuar, salida.Control);
        var aCotizar = Assert.IsAssignableFrom<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(salida.Valores[0]);
        Assert.Equal(new[] { (Ref.Create("viajes.tarifa", "vuelo/AV-8020"), 1), (Ref.Create("viajes.tarifa", "hotel/caribe/DBL"), 1) }, aCotizar);

        var itinerario = Assert.IsAssignableFrom<IReadOnlyList<ParadaDelViaje>>(salida.Valores[1]);
        Assert.Equal(new ParadaDelViaje("vuelo/AV-8020", "Bogotá → Cartagena", Dia, Dia.AddHours(2)), itinerario[0]);
        // Sin etiqueta, el nombre en pantalla es el producto (como TripFlow).
        Assert.Equal("hotel/caribe/DBL", itinerario[1].Etiqueta);
    }

    [Fact]
    public async Task El_mismo_producto_en_OTRO_periodo_vale_y_en_el_mismo_no()
    {
        var idaYVuelta = await Revisar(Item("vuelo/AV-8020", Dia, Dia.AddHours(2)), Item("vuelo/AV-8020", Dia.AddDays(4), Dia.AddDays(4).AddHours(2)));
        Assert.Equal(PasoResultado.Continuar, idaYVuelta.Control);

        var repetido = await Revisar(Item("vuelo/AV-8020", Dia, Dia.AddHours(2)), Item("vuelo/AV-8020", Dia, Dia.AddHours(2)));
        Assert.Equal("viajes.duplicate_item", repetido.Rechazo!.Code);
    }

    [Fact]
    public async Task Los_bordes_del_itinerario_dicen_los_codigos_de_TripFlow()
    {
        Assert.Equal("viajes.no_items", (await Revisar()).Rechazo!.Code);
        Assert.Equal("viajes.bad_product", (await Revisar(Item("  ", Dia, Dia.AddHours(1)))).Rechazo!.Code);

        var doce = Enumerable.Range(0, TripFlow.MaxItems).Select(i => Item($"p/{i}", Dia, Dia.AddHours(1))).ToArray();
        Assert.Equal(PasoResultado.Continuar, (await Revisar(doce)).Control);

        var trece = Enumerable.Range(0, TripFlow.MaxItems + 1).Select(i => Item($"p/{i}", Dia, Dia.AddHours(1))).ToArray();
        Assert.Equal("viajes.too_many_items", (await Revisar(trece)).Rechazo!.Code);
    }

    [Fact]
    public async Task El_recurso_se_busca_por_el_SUJETO_del_producto()
    {
        var item = new FlowContext().Set("producto", " hotel/caribe/DBL ");
        var salida = await Correr(new PasoSujetoDelProducto(), "recurso", new FlowContext(), "item", item);

        Assert.Equal(Ref.Create("viajes.producto", "hotel/caribe/DBL"), salida.Valores.Single());
    }

    [Fact]
    public async Task Vence_el_PRIMERO_y_sin_dato_no_se_inventa_uno()
    {
        IReadOnlyList<FlowContext> conVencimientos =
        [
            new FlowContext().Set("vence", (DateTimeOffset?)Dia.AddMinutes(15)),
            new FlowContext().Set("vence", (DateTimeOffset?)Dia.AddMinutes(10)),
            new FlowContext().Set("vence", (DateTimeOffset?)null),
        ];
        var primero = await Correr(new PasoVence(), "vence", new FlowContext().Set("items", conVencimientos));
        Assert.Equal(Dia.AddMinutes(10), primero.Valores.Single());

        IReadOnlyList<FlowContext> sinDato = [new FlowContext(), new FlowContext()];
        var nada = await Correr(new PasoVence(), "vence", new FlowContext().Set("items", sinDato));
        Assert.Null(nada.Valores.Single());
    }

    [Fact]
    public void El_itinerario_se_escribe_y_se_lee_igual()
    {
        var tipo = new TipoItinerario();
        IReadOnlyList<ParadaDelViaje> itinerario =
        [
            new("vuelo/AV-8020", "Bogotá → Cartagena", Dia, Dia.AddHours(2)),
            new("hotel/caribe/DBL", "Hotel Caribe — doble", Dia.AddHours(7), Dia.AddDays(2)),
        ];

        var json = JsonDocument.Parse(tipo.Escribir(itinerario).ToJsonString()).RootElement;
        var leido = Assert.IsAssignableFrom<IReadOnlyList<ParadaDelViaje>>(tipo.Leer(json, "itinerario"));

        Assert.Equal(itinerario, leido);
        Assert.True(tipo.Guardable);
        Assert.Throws<FormatException>(() => tipo.Leer(JsonDocument.Parse("""[{"producto":"x","etiqueta":"x","inicio":"2026-09-01T08:00:00+00:00","fin":"2026-09-01T09:00:00+00:00","precio":1}]""").RootElement, "itinerario"));
    }
}

/// <summary>
/// <c>viajes.menos-retencion</c> (línea 006, T025): lo que se devuelve al cancelar lo confirmado es el total menos la
/// penalidad que calculó quien vende; nulo si no queda nada, y <c>viajes.bad_penalty</c> si no cabe.
/// </summary>
public sealed class RetencionDeLaCancelacionTests
{
    private static Money Pesos(decimal monto, string moneda = "COP") => Money.Of(monto, moneda);

    private static Task<SalidaDePaso> Retener(Money total, Money retener)
    {
        var def = FlujosDeclarados.Incrustado(typeof(TripResponse).Assembly, "flujos/viajes.cancelacion.json").Pasos["retencion"];
        var ctx = new FlowContext().Set("total", total).Set("retener", retener);
        return new PasoMenosRetencion().EjecutarAsync(
            new EntradaDePaso("cancelacion-1", Ref.Create("viajes.cancelacion", "cancelacion-1"), def, ctx, null, null), CancellationToken.None);
    }

    [Fact]
    public async Task Devuelve_el_total_menos_la_penalidad_y_sin_penalidad_el_total()
    {
        Assert.Equal(Pesos(1_000_000m), (await Retener(Pesos(1_200_000m), Pesos(200_000m))).Valores.Single());
        Assert.Equal(Pesos(1_200_000m), (await Retener(Pesos(1_200_000m), Pesos(0m))).Valores.Single());
    }

    /// <summary>Sin resto no hay devolución que pedir: Payments rechaza una de cero, y el paso que devuelve se salta.</summary>
    [Fact]
    public async Task La_penalidad_entera_o_un_viaje_gratis_no_dejan_nada_que_devolver()
    {
        Assert.Null((await Retener(Pesos(1_200_000m), Pesos(1_200_000m))).Valores.Single());
        Assert.Null((await Retener(Pesos(0m), Pesos(0m))).Valores.Single());
    }

    [Theory]
    [InlineData(1_300_000, "COP")]
    [InlineData(-1, "COP")]
    [InlineData(100_000, "USD")]
    public async Task Una_penalidad_que_no_cabe_es_bad_penalty(int retener, string moneda)
    {
        var salida = await Retener(Pesos(1_200_000m), Pesos(retener, moneda));

        Assert.Equal(PasoResultado.Abortar, salida.Control);
        Assert.Equal(PasoMenosRetencion.PenalidadMala, salida.Rechazo!.Code);
    }
}
