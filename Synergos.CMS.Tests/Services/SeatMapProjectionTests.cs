using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="SeatMapProjection"/> — la traducción entre lo que publica un
/// <see cref="ISeatMapProvider"/> y la carga <c>seatmap</c> que el bundle publicado sabe leer.
/// </summary>
/// <remarks>
/// <para><b>Por qué estas pruebas y no otras.</b> La proyección es el único punto donde el
/// CMS puede romper el mapa sin que nada se queje: la vista compila en runtime, el bundle
/// normaliza a la defensiva (una clave que no entiende la ignora en silencio) y el resultado
/// es un plano de cabina dibujado mal, no un error. Cada prueba fija una propiedad que, si se
/// pierde, produce exactamente eso — una pantalla que se ve bien y miente.</para>
///
/// <para><b>La carga que el bundle lee es la fuente de verdad (ADR 0083):</b>
/// <c>rows[].rowNumber</c>, <c>rows[].seats[]</c> con <c>id</c> / <c>type</c> /
/// <c>available</c> / <c>price</c>, y <c>aisleAfterColumns</c>. Las claves se verifican
/// serializando, no leyendo el record — un rename las rompería sin tocar ninguna otra
/// prueba.</para>
/// </remarks>
public sealed class SeatMapProjectionTests
{
    private static SeatMapSeat Seat(
        string id,
        string column,
        string status = "free",
        string position = "middle",
        decimal price = 0m,
        IReadOnlyList<string>? features = null)
        => new(id, column, status, position, price, features);

    private static SeatMapRow Row(string label, params SeatMapSeat[] seats)
        => new(label, "economy", seats);

    private static SeatMapLayout Layout(int aisleAfterColumns, params SeatMapRow[] rows)
        => new("A320-BOGMDE", "Airbus A320 · BOG → MDE", "COP", aisleAfterColumns, rows);

    /// <summary>Una fila 3-3 completa: A B C | D E F.</summary>
    private static SeatMapRow FilaTresTres(string label) => Row(
        label,
        Seat($"{label}A", "A", position: "window"),
        Seat($"{label}B", "B"),
        Seat($"{label}C", "C", position: "aisle"),
        Seat($"{label}D", "D", position: "aisle"),
        Seat($"{label}E", "E"),
        Seat($"{label}F", "F", position: "window"));

    // ── El pasillo ───────────────────────────────────────────────────────────

    /// <summary>
    /// Protege <c>aisleAfterColumns</c> en una cabina <b>2-4-2</b>. Es el caso que revienta:
    /// el bundle, cuando no recibe el dato, parte la fila más ancha por la mitad
    /// (<c>ceil(8/2) = 4</c>) y dibuja el pasillo entre los dos asientos del centro. El
    /// proveedor dice 2 y ese 2 tiene que llegar intacto.
    /// </summary>
    [Fact]
    public void El_pasillo_de_una_cabina_2_4_2_llega_donde_lo_puso_el_proveedor()
    {
        var fila = Row(
            "20",
            Seat("20A", "A", position: "window"),
            Seat("20B", "B", position: "aisle"),
            Seat("20C", "C", position: "aisle"),
            Seat("20D", "D"),
            Seat("20E", "E"),
            Seat("20F", "F", position: "aisle"),
            Seat("20G", "G", position: "aisle"),
            Seat("20H", "H", position: "window"));

        var payload = SeatMapProjection.Project(Layout(2, fila));

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.AisleAfterColumns);
        // Y NO el 4 que el bundle calcularía solo si la clave no llegara.
        Assert.NotEqual(payload.Rows[0].Seats.Count / 2, payload.AisleAfterColumns);
    }

    /// <summary>
    /// Protege el paso a través del <c>0</c>: cuando el proveedor no conoce la geometría, la
    /// proyección no inventa una — emite 0 y deja que el bundle aplique su propio default.
    /// Inventar aquí sería peor que no saber.
    /// </summary>
    [Fact]
    public void Sin_geometria_declarada_la_proyeccion_no_inventa_un_pasillo()
    {
        var payload = SeatMapProjection.Project(Layout(0, FilaTresTres("12")));

        Assert.NotNull(payload);
        Assert.Equal(0, payload!.AisleAfterColumns);
    }

    // ── El tipo de asiento ───────────────────────────────────────────────────

    /// <summary>
    /// Protege el mapeo <see cref="SeatMapSeat.Position"/> → <c>type</c>. El bundle sólo sabe
    /// nombrar cuatro tipos y usa el valor para pintar la clase CSS y el aria-label: un tipo
    /// mal mapeado no falla, sólo pinta un asiento de ventana como si fuera del centro.
    /// </summary>
    [Fact]
    public void El_tipo_de_asiento_se_mapea_a_los_cuatro_valores_que_el_bundle_entiende()
    {
        var fila = Row(
            "12",
            Seat("12A", "A", position: "window"),
            Seat("12B", "B", position: "middle"),
            Seat("12C", "C", position: "aisle"),
            Seat("12D", "D", position: "WINDOW"));

        var payload = SeatMapProjection.Project(Layout(3, fila));

        var tipos = payload!.Rows[0].Seats.Select(s => s.Type).ToArray();
        Assert.Equal(new[] { "window", "middle", "aisle", "window" }, tipos);
    }

    /// <summary>
    /// Protege el aplanado de <see cref="SeatMapSeat.Features"/>: <c>extra-legroom</c> es el
    /// único rasgo que el enum <c>type</c> del bundle puede nombrar, y gana sobre la posición.
    /// Un rasgo que el contrato no tiene (<c>bulkhead</c>) NO desplaza la posición real.
    /// </summary>
    [Fact]
    public void Espacio_extra_gana_sobre_la_posicion_y_los_demas_rasgos_no_la_pisan()
    {
        var fila = Row(
            "14",
            Seat("14A", "A", position: "window", features: new[] { "extra-legroom" }),
            Seat("14B", "B", position: "middle", features: new[] { "bulkhead", "recline-limited" }),
            Seat("14C", "C", position: "aisle", features: new[] { "exit-row", "extra-legroom" }));

        var payload = SeatMapProjection.Project(Layout(3, fila));

        var tipos = payload!.Rows[0].Seats.Select(s => s.Type).ToArray();
        Assert.Equal(new[] { "extra-legroom", "middle", "extra-legroom" }, tipos);
    }

    /// <summary>
    /// Protege el default: una posición que el proveedor nombró distinto cae en <c>middle</c>,
    /// que es exactamente lo que el bundle haría con un valor que no reconoce. Emitir el valor
    /// crudo dejaría el asiento sin ninguna clase de tipo.
    /// </summary>
    [Fact]
    public void Una_posicion_que_el_contrato_no_conoce_cae_en_middle()
    {
        var fila = Row(
            "9",
            Seat("9A", "A", position: "aisle-left"),
            Seat("9B", "B", position: ""),
            Seat("9C", "C", position: "   "));

        var payload = SeatMapProjection.Project(Layout(2, fila));

        Assert.All(payload!.Rows[0].Seats, s => Assert.Equal("middle", s.Type));
    }

    // ── Disponibilidad ───────────────────────────────────────────────────────

    /// <summary>
    /// Protege la regla de disponibilidad. El bundle deshabilita el botón sólo cuando
    /// <c>available</c> es <b>literalmente</b> <c>false</c> (<c>entry['available'] !== false</c>),
    /// así que omitir la clave vende una butaca ocupada. <c>sold</c> y <c>blocked</c> son
    /// distintos entre sí para el proveedor pero igual de no-vendibles para el visitante, y
    /// <c>held</c> es de otra sesión de compra ahora mismo.
    /// </summary>
    [Fact]
    public void Vendidos_bloqueados_y_retenidos_llegan_como_no_disponibles()
    {
        var fila = Row(
            "16",
            Seat("16A", "A", status: "free"),
            Seat("16B", "B", status: "sold"),
            Seat("16C", "C", status: "blocked"),
            Seat("16D", "D", status: "held"),
            Seat("16E", "E", status: "FREE"));

        var payload = SeatMapProjection.Project(Layout(3, fila));

        var libres = payload!.Rows[0].Seats.ToDictionary(s => s.Id, s => s.Available);
        Assert.True(libres["16A"]);
        Assert.False(libres["16B"]);
        Assert.False(libres["16C"]);
        Assert.False(libres["16D"]);
        Assert.True(libres["16E"]);
    }

    /// <summary>
    /// Protege el precio: negativo o cero se normaliza a 0, que es lo que el bundle usa para
    /// decidir si muestra etiqueta de recargo. Un negativo dibujaría un precio en rojo inventado.
    /// </summary>
    [Fact]
    public void Un_precio_negativo_se_normaliza_a_cero()
    {
        var fila = Row(
            "18",
            Seat("18A", "A", price: 45_000m),
            Seat("18B", "B", price: 0m),
            Seat("18C", "C", price: -1m));

        var payload = SeatMapProjection.Project(Layout(3, fila));

        var precios = payload!.Rows[0].Seats.Select(s => s.Price).ToArray();
        Assert.Equal(new[] { 45_000m, 0m, 0m }, precios);
    }

    // ── Las letras de columna ────────────────────────────────────────────────

    /// <summary>
    /// Protege el salto de la <b>I</b>. El bundle letrea cada asiento por su ÍNDICE contra el
    /// alfabeto <c>ABCDEFGHJK</c>, no por lo que diga el proveedor; si la proyección ordenara
    /// con un alfabeto que incluye la I, el asiento que el pasajero tiene impreso como
    /// <c>30J</c> saldría dibujado en la posición de una <c>I</c> que no existe.
    /// </summary>
    [Fact]
    public void Las_letras_de_columna_se_saltan_la_I()
    {
        Assert.Equal(8, SeatMapProjection.ColumnRank("H"));
        // La siguiente NO es la I: es la J, y va en el puesto 9.
        Assert.Equal(9, SeatMapProjection.ColumnRank("J"));
        Assert.Equal(10, SeatMapProjection.ColumnRank("K"));
        // La I no es una columna válida en una cabina.
        Assert.Equal(0, SeatMapProjection.ColumnRank("I"));
        Assert.Equal(0, SeatMapProjection.ColumnRank(null));
        Assert.Equal(0, SeatMapProjection.ColumnRank(""));
    }

    /// <summary>
    /// Protege el orden emitido en un widebody 3-4-3: los diez asientos tienen que salir en el
    /// orden que hace que el índice del bundle produzca la misma letra que el proveedor puso —
    /// con la <c>J</c> en el índice 8 (el noveno), donde otro alfabeto pondría una <c>I</c>.
    /// </summary>
    [Fact]
    public void Un_widebody_de_diez_columnas_sale_en_el_orden_que_letrea_bien()
    {
        // A propósito desordenado: el proveedor no garantiza orden.
        var fila = Row(
            "30",
            Seat("30K", "K", position: "window"),
            Seat("30D", "D"),
            Seat("30A", "A", position: "window"),
            Seat("30J", "J"),
            Seat("30C", "C", position: "aisle"),
            Seat("30G", "G"),
            Seat("30B", "B"),
            Seat("30H", "H", position: "aisle"),
            Seat("30E", "E"),
            Seat("30F", "F"));

        var payload = SeatMapProjection.Project(Layout(3, fila));

        var ids = payload!.Rows[0].Seats.Select(s => s.Id).ToArray();
        Assert.Equal(
            new[] { "30A", "30B", "30C", "30D", "30E", "30F", "30G", "30H", "30J", "30K" },
            ids);

        // El índice 8 (noveno asiento) es el que el bundle letrea como "J".
        Assert.Equal("30J", ids[8]);
        Assert.Equal('J', SeatMapProjection.ColumnAlphabet[8]);
    }

    /// <summary>
    /// Protege el orden de las columnas que el contrato no sabe letrear: van al final y entre
    /// ellas conservan el orden del proveedor. Ponerlas primero correría todas las demás y
    /// desalinearía la fila completa.
    /// </summary>
    [Fact]
    public void Una_columna_que_el_contrato_no_letrea_va_al_final_sin_correr_a_las_demas()
    {
        var fila = Row(
            "7",
            Seat("7-XX", "XX"),
            Seat("7B", "B"),
            Seat("7-ZZ", "ZZ"),
            Seat("7A", "A"));

        var payload = SeatMapProjection.Project(Layout(2, fila));

        var ids = payload!.Rows[0].Seats.Select(s => s.Id).ToArray();
        Assert.Equal(new[] { "7A", "7B", "7-XX", "7-ZZ" }, ids);
    }

    // ── La fila ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Protege el rótulo de fila como <b>texto</b>. Las cabinas reales se saltan la 13 y los
    /// teatros rotulan con letra: convertirlo a número perdería el rótulo del recinto.
    /// </summary>
    [Fact]
    public void El_rotulo_de_la_fila_se_emite_tal_cual_lo_nombra_el_proveedor()
    {
        var payload = SeatMapProjection.Project(Layout(
            3,
            Row("12", Seat("12A", "A")),
            Row("14", Seat("14A", "A")),
            Row("  A  ", Seat("A1", "A"))));

        var rotulos = payload!.Rows.Select(r => r.RowNumber).ToArray();
        Assert.Equal(new[] { "12", "14", "A" }, rotulos);
    }

    // ── Degradación ──────────────────────────────────────────────────────────

    /// <summary>
    /// Protege la degradación cuando el proveedor no conoce la referencia. Sin carga
    /// <c>seatmap</c> el bundle pinta su estado vacío; con una carga de filas vacías pintaría
    /// una cabina sin asientos y un resumen mintiendo "0 de 1 asiento".
    /// </summary>
    [Fact]
    public void Un_mapa_que_el_proveedor_no_conoce_no_produce_carga()
    {
        Assert.Null(SeatMapProjection.Project(null));
        Assert.Null(SeatMapProjection.Project(Layout(3)));
        Assert.Null(SeatMapProjection.Project(Layout(3, Row("12"))));
    }

    /// <summary>
    /// Protege el descarte de asientos sin id: el bundle los deja caer igual (sin id no se
    /// pueden seleccionar), y una fila que se queda sin asientos usables no se emite.
    /// </summary>
    [Fact]
    public void Una_fila_cuyos_asientos_no_tienen_id_no_se_emite()
    {
        var payload = SeatMapProjection.Project(Layout(
            3,
            Row("12", Seat("", "A"), Seat("   ", "B")),
            Row("13", Seat("13A", "A"))));

        Assert.NotNull(payload);
        Assert.Single(payload!.Rows);
        Assert.Equal("13", payload.Rows[0].RowNumber);
    }

    /// <summary>
    /// Protege el prop bag que va al emitter: sin mapa NO se emite la clave <c>seatmap</c>.
    /// Emitir <c>null</c> o un objeto vacío haría que el bundle intentara dibujar.
    /// </summary>
    [Fact]
    public void Sin_mapa_el_prop_bag_no_lleva_la_clave_seatmap()
    {
        var props = SeatMapProjection.BuildProps(null, currencyOverride: null, maxSelectable: null);

        Assert.False(props.ContainsKey("seatmap"));
        Assert.False(props.ContainsKey("currency"));
        Assert.False(props.ContainsKey("maxSelectable"));
        Assert.Empty(props);
    }

    // ── Los knobs del editor ─────────────────────────────────────────────────

    /// <summary>
    /// Protege la precedencia de la moneda: lo que el editor escribió gana sobre la del mapa,
    /// y si no escribió nada se usa la del proveedor. Si no hay ninguna, la clave se omite y el
    /// bundle aplica COP — emitir vacío rompería <c>Intl.NumberFormat</c>.
    /// </summary>
    [Fact]
    public void La_moneda_del_editor_gana_sobre_la_del_mapa_y_el_blanco_no_borra_nada()
    {
        var layout = Layout(3, FilaTresTres("12"));

        Assert.Equal("USD", SeatMapProjection.BuildProps(layout, " usd ", null)["currency"] as string);
        Assert.Equal("COP", SeatMapProjection.BuildProps(layout, "   ", null)["currency"] as string);
        Assert.Equal("COP", SeatMapProjection.BuildProps(layout, null, null)["currency"] as string);

        var sinMoneda = new SeatMapLayout("r", "n", "  ", 3, new[] { FilaTresTres("12") });
        Assert.False(SeatMapProjection.BuildProps(sinMoneda, null, null).ContainsKey("currency"));
    }

    /// <summary>
    /// Protege el tope de selección: Umbraco.Integer devuelve 0 con el campo en blanco, y un 0
    /// emitido dejaría al visitante sin poder elegir ningún asiento. Se omite y el bundle usa 1.
    /// </summary>
    [Fact]
    public void Un_tope_de_seleccion_en_blanco_o_cero_no_se_emite()
    {
        var layout = Layout(3, FilaTresTres("12"));

        Assert.Equal(3, Assert.IsType<int>(SeatMapProjection.BuildProps(layout, null, 3)["maxSelectable"]));
        Assert.False(SeatMapProjection.BuildProps(layout, null, 0).ContainsKey("maxSelectable"));
        Assert.False(SeatMapProjection.BuildProps(layout, null, -2).ContainsKey("maxSelectable"));
        Assert.False(SeatMapProjection.BuildProps(layout, null, null).ContainsKey("maxSelectable"));
    }

    // ── El contrato de claves (ADR 0083) ─────────────────────────────────────

    /// <summary>
    /// Protege los NOMBRES de las claves contra un rename de propiedades C#. El bundle lee
    /// <c>rows</c>, <c>rowNumber</c>, <c>seats</c>, <c>id</c>, <c>type</c>, <c>available</c>,
    /// <c>price</c> y <c>aisleAfterColumns</c>; con cualquier otra escritura la carga se
    /// normaliza a cero filas y el mapa sale vacío <b>sin ningún error</b>.
    /// </summary>
    [Fact]
    public void Las_claves_serializadas_son_exactamente_las_que_el_bundle_lee()
    {
        var payload = SeatMapProjection.Project(Layout(
            3,
            Row("12", Seat("12A", "A", status: "sold", position: "window", price: 45_000m))));

        // Las mismas opciones que usa DefaultSynHostEmitter para el atributo config.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        var json = JsonSerializer.Serialize(payload, options);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(3, root.GetProperty("aisleAfterColumns").GetInt32());
        var row = root.GetProperty("rows")[0];
        Assert.Equal("12", row.GetProperty("rowNumber").GetString());
        var seat = row.GetProperty("seats")[0];
        Assert.Equal("12A", seat.GetProperty("id").GetString());
        Assert.Equal("window", seat.GetProperty("type").GetString());
        Assert.False(seat.GetProperty("available").GetBoolean());
        Assert.Equal(45_000m, seat.GetProperty("price").GetDecimal());

        // `available: false` NO puede desaparecer: el bundle trata la ausencia como disponible.
        Assert.Contains("\"available\":false", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Protege que la proyección no filtre inventario que el contrato no puede expresar. La
    /// clase de servicio de la fila y la marca de fila de emergencia se DESCARTAN a propósito:
    /// el bundle no tiene dónde ponerlas, y emitir claves que nadie lee es el drift que este
    /// repo combate. Si algún día el contrato UI las declara, esta prueba es la que cae.
    /// </summary>
    [Fact]
    public void La_clase_de_servicio_y_la_fila_de_emergencia_no_viajan_en_la_carga()
    {
        var fila = new SeatMapRow(
            "14",
            "business",
            new[] { Seat("14A", "A", position: "window", features: new[] { "exit-row" }) },
            IsExitRow: true);

        var payload = SeatMapProjection.Project(Layout(3, fila));
        var json = JsonSerializer.Serialize(payload);

        Assert.DoesNotContain("business", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exit", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serviceClass", json, StringComparison.OrdinalIgnoreCase);
        // Y el asiento conserva su posición real, no se lo come el rasgo que no cabe.
        Assert.Equal("window", payload!.Rows[0].Seats[0].Type);
    }

    // ── Idempotencia ─────────────────────────────────────────────────────────

    /// <summary>
    /// Protege que la proyección sea pura: dos pasadas sobre el mismo layout producen la misma
    /// carga. Sin esto, un caché de página serviría un mapa distinto al de la primera visita.
    /// </summary>
    [Fact]
    public void Proyectar_dos_veces_el_mismo_mapa_da_exactamente_lo_mismo()
    {
        var layout = Layout(3, FilaTresTres("12"), FilaTresTres("14"));

        var primera = JsonSerializer.Serialize(SeatMapProjection.Project(layout));
        var segunda = JsonSerializer.Serialize(SeatMapProjection.Project(layout));

        Assert.Equal(primera, segunda);
    }
}
