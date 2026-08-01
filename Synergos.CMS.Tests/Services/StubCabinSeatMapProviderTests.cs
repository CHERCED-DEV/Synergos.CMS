using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre el proveedor exógeno de mapas de asientos que emula una cabina de avión (ADR 0127).
/// </summary>
/// <remarks>
/// <para>La propiedad que protegen estas pruebas no es "devuelve butacas": es que el mapa se
/// vea <b>creíble para alguien que vuela</b>. Un mapa con fila 13, con columna I, o con el
/// pasillo dibujado a la mitad de un <c>2-4-2</c> se lee como falso al primer vistazo, y esa es
/// exactamente la impresión que una demo no puede permitirse.</para>
///
/// <para>La segunda mitad es el determinismo. Las butacas ocupadas se derivan de un hash de
/// (referencia, butaca), no de un aleatorio: un mapa que cambia en cada refresco no se puede
/// grabar para una demo, y un test sobre él sería intermitente.</para>
/// </remarks>
public sealed class StubCabinSeatMapProviderTests
{
    private static readonly CabinSpec Narrow = new(
        Ref: "test-3-3",
        Name: "Prueba 3-3",
        Formula: "3-3",
        Sections: new[] { new CabinSectionSpec("economy", 10, 12, 0m) });

    private static SeatMapRow Row(SeatMapLayout map, string label)
        => map.Rows.Single(r => r.Label == label);

    // ── Resolución ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Una_referencia_desconocida_devuelve_null_en_vez_de_lanzar()
    {
        // Es un dato del editor, no una falla del sistema: degrada la pantalla, no la tumba.
        var sut = new StubCabinSeatMapProvider();

        Assert.Null(await sut.GetLayoutAsync("no-existe"));
        Assert.Null(await sut.GetLayoutAsync("   "));
        Assert.Null(await sut.GetLayoutAsync(string.Empty));
    }

    [Fact]
    public async Task Las_cabinas_sembradas_se_resuelven_y_no_distinguen_mayusculas()
    {
        var sut = new StubCabinSeatMapProvider();

        Assert.NotNull(await sut.GetLayoutAsync("a320-domestico"));
        Assert.NotNull(await sut.GetLayoutAsync("A320-DOMESTICO"));
        Assert.NotNull(await sut.GetLayoutAsync("b787-internacional"));
        Assert.NotNull(await sut.GetLayoutAsync("a350-suites"));
    }

    // ── Lo que hace creíble el mapa ──────────────────────────────────────────

    [Fact]
    public void La_fila_13_NO_existe()
    {
        // Media flota mundial la omite. Incluirla delata el mapa al instante.
        var map = StubCabinSeatMapProvider.Build(Narrow with { Sections = new[] { new CabinSectionSpec("economy", 11, 15, 0m) } });

        Assert.DoesNotContain(map.Rows, r => r.Label == "13");
        Assert.Equal(new[] { "11", "12", "14", "15" }, map.Rows.Select(r => r.Label));
    }

    [Fact]
    public void La_columna_I_NO_se_usa()
    {
        // En un asiento impreso se confunde con un 1. El componente aplica la misma regla, así
        // que apartarse desalinearía el rótulo del servidor con el del cliente.
        var map = StubCabinSeatMapProvider.Build(Narrow with { Formula = "3-4-3" });

        var letras = map.Rows.SelectMany(r => r.Seats).Select(s => s.Column).Distinct();
        Assert.DoesNotContain("I", letras);
        Assert.Equal(10, letras.Count());
    }

    [Fact]
    public void El_pasillo_va_donde_lo_dicta_la_distribucion_no_a_la_mitad()
    {
        // Es el único dato de geometría que el componente consume; sin él dibuja el pasillo a
        // la mitad de la fila, que en un 2-4-2 queda en el sitio equivocado.
        Assert.Equal(3, StubCabinSeatMapProvider.Build(Narrow with { Formula = "3-3" }).AisleAfterColumns);
        Assert.Equal(2, StubCabinSeatMapProvider.Build(Narrow with { Formula = "2-4-2" }).AisleAfterColumns);
        Assert.Equal(1, StubCabinSeatMapProvider.Build(Narrow with { Formula = "1-2-1" }).AisleAfterColumns);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-una-formula")]
    [InlineData("0-0")]
    [InlineData("9-9-9")]   // más columnas que letras disponibles
    public void Una_formula_ilegible_cae_a_3_3_en_vez_de_lanzar(string formula)
    {
        // La escribe una persona. Un mapa con la distribución más común del mundo es un
        // desperfecto visible que se corrige; una excepción es una pantalla en blanco.
        var map = StubCabinSeatMapProvider.Build(Narrow with { Formula = formula });

        Assert.Equal(3, map.AisleAfterColumns);
        Assert.Equal(6, Row(map, "10").Seats.Count);
    }

    // ── Ventana / pasillo / centro ───────────────────────────────────────────

    [Fact]
    public void En_un_3_3_las_posiciones_son_las_de_un_narrowbody()
    {
        var seats = Row(StubCabinSeatMapProvider.Build(Narrow with { Formula = "3-3" }), "10").Seats;

        Assert.Equal(
            new[] { "window", "middle", "aisle", "aisle", "middle", "window" },
            seats.Select(s => s.Position));
    }

    [Fact]
    public void En_un_2_4_2_el_bloque_CENTRAL_no_tiene_ninguna_ventana()
    {
        // Por evidente que parezca mirando un 3-3, contar desde el borde de la fila diría que
        // la D es ventana. No lo es: el bloque central de un widebody no toca el fuselaje.
        var seats = Row(StubCabinSeatMapProvider.Build(Narrow with { Formula = "2-4-2" }), "10").Seats;

        Assert.Equal(
            new[] { "window", "aisle", "aisle", "middle", "middle", "aisle", "aisle", "window" },
            seats.Select(s => s.Position));
        Assert.Equal(2, seats.Count(s => s.Position == "window"));
    }

    [Fact]
    public void En_un_1_2_1_una_butaca_que_es_ventana_y_pasillo_a_la_vez_gana_ventana()
    {
        // Es lo que el pasajero elige, y es como lo rotula cualquier sistema de aerolínea.
        var seats = Row(StubCabinSeatMapProvider.Build(Narrow with { Formula = "1-2-1" }), "10").Seats;

        Assert.Equal(new[] { "window", "aisle", "aisle", "window" }, seats.Select(s => s.Position));
    }

    // ── Secciones, salidas y bloqueos ────────────────────────────────────────

    [Fact]
    public void Cada_fila_lleva_la_clase_de_servicio_de_su_tramo()
    {
        var map = StubCabinSeatMapProvider.Build(Narrow with
        {
            Sections = new[]
            {
                new CabinSectionSpec("business", 1, 2, 500_000m),
                new CabinSectionSpec("economy", 3, 4, 0m),
            },
        });

        Assert.Equal("business", Row(map, "1").ServiceClass);
        Assert.Equal("economy", Row(map, "4").ServiceClass);
        Assert.Equal(500_000m, Row(map, "1").Seats[0].Price);
        Assert.Equal(0m, Row(map, "4").Seats[0].Price);
    }

    [Fact]
    public void Una_fila_de_salida_SIEMPRE_trae_mas_espacio()
    {
        // No es una cortesía comercial: el espacio extra es el requisito regulatorio que define
        // la fila de salida.
        var map = StubCabinSeatMapProvider.Build(Narrow with { ExitRows = new[] { 11 } });

        var salida = Row(map, "11");
        Assert.True(salida.IsExitRow);
        Assert.All(salida.Seats, s =>
        {
            Assert.Contains("exit-row", s.Features!);
            Assert.Contains("extra-legroom", s.Features!);
        });

        Assert.False(Row(map, "10").IsExitRow);
        Assert.Null(Row(map, "10").Seats[0].Features);
    }

    [Fact]
    public void Una_butaca_bloqueada_NO_es_lo_mismo_que_una_vendida()
    {
        // Bloqueada existe físicamente y no se vende (tripulación, avería): no se libera sola y
        // no la compró nadie. Confundirlas haría que un reporte de ocupación mintiera.
        var map = StubCabinSeatMapProvider.Build(Narrow with { BlockedSeats = new[] { "10A" } });

        Assert.Equal("blocked", Row(map, "10").Seats.Single(s => s.Column == "A").Status);
    }

    [Fact]
    public void El_id_de_la_butaca_es_fila_mas_columna()
    {
        var seats = Row(StubCabinSeatMapProvider.Build(Narrow), "12").Seats;

        Assert.Equal(new[] { "12A", "12B", "12C", "12D", "12E", "12F" }, seats.Select(s => s.Id));
    }

    // ── Determinismo ─────────────────────────────────────────────────────────

    [Fact]
    public void El_mismo_mapa_pedido_dos_veces_es_IDENTICO()
    {
        var primera = StubCabinSeatMapProvider.Build(Narrow);
        var segunda = StubCabinSeatMapProvider.Build(Narrow);

        Assert.Equal(
            primera.Rows.SelectMany(r => r.Seats).Select(s => s.Id + s.Status),
            segunda.Rows.SelectMany(r => r.Seats).Select(s => s.Id + s.Status));
    }

    [Fact]
    public void Dos_cabinas_distintas_NO_tienen_las_mismas_butacas_ocupadas()
    {
        // Si el reparto no dependiera de la referencia, todos los vuelos se verían iguales.
        var a = StubCabinSeatMapProvider.Build(Narrow with { Ref = "vuelo-a" });
        var b = StubCabinSeatMapProvider.Build(Narrow with { Ref = "vuelo-b" });

        Assert.NotEqual(
            a.Rows.SelectMany(r => r.Seats).Select(s => s.Status),
            b.Rows.SelectMany(r => r.Seats).Select(s => s.Status));
    }

    [Fact]
    public void La_cabina_queda_ni_vacia_ni_llena()
    {
        // Un mapa vacío no se ve usado; uno lleno deja al comprador sin nada que elegir.
        var map = StubCabinSeatMapProvider.Build(Narrow with
        {
            Sections = new[] { new CabinSectionSpec("economy", 10, 40, 0m) },
        });

        var todas = map.Rows.SelectMany(r => r.Seats).ToList();
        var vendidas = todas.Count(s => s.Status == "sold");

        Assert.InRange(vendidas, todas.Count / 5, todas.Count / 2);
    }

    [Fact]
    public void Un_conjunto_de_cabinas_nulo_es_error_de_programacion_y_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => new StubCabinSeatMapProvider(null!));
    }
}
