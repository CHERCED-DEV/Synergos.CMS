using System.Text.Json;
using System.Text.Json.Nodes;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Los tipos de valor que un flujo declara: leen lo que tiene su forma, rechazan lo demás con el nombre
/// del campo, y escriben lo que leen (núcleo N1).
/// </summary>
public sealed class TiposDeValorTests
{
    private static readonly TiposDeValor Tipos = TiposDeValor.Con();

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static object? Leer(string tipo, string json) => Tipos.Resolver(tipo)!.Leer(J(json), "campo");

    public static TheoryData<string, string> IdaYVuelta() => new()
    {
        { "texto", "\"hola\"" },
        { "entero", "42" },
        { "decimal", "12.5" },
        { "booleano", "true" },
        { "instante", "\"2026-10-15T10:00:00.0000000-05:00\"" },
        { "ref", """{"kind":"salud.profesional","id":"p-1"}""" },
        { "dinero", """{"amount":50000,"currency":"COP"}""" },
        { "ventana", """{"start":"2026-10-15T10:00:00.0000000+00:00","end":"2026-10-15T10:30:00.0000000+00:00"}""" },
    };

    [Theory]
    [MemberData(nameof(IdaYVuelta))]
    public void Lo_que_lee_lo_escribe_igual(string tipo, string json)
    {
        var resuelto = Tipos.Resolver(tipo)!;
        var valor = resuelto.Leer(J(json), "campo");
        var escrito = resuelto.Escribir(valor);

        // Por igualdad de JSON y no de texto: el escritor escapa «+» como +, que es el mismo JSON.
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), escrito), $"{tipo}: se leyó {json} y se escribió {escrito?.ToJsonString()}.");
        Assert.Equal(resuelto.Tipo.Clr, valor!.GetType());
    }

    public static TheoryData<string, string, string> Malos() => new()
    {
        { "texto", "3", "un texto" },
        { "entero", "1.5", "un entero" },
        // es-CO: el punto es de miles. En JSON un texto «500.000» no es un número, y no se adivina.
        { "decimal", "\"500.000\"", "un número" },
        { "booleano", "\"true\"", "true o false" },
        // sin desfase, el instante dependería del huso del servidor
        { "instante", "\"2026-10-15T10:00:00\"", "desfase" },
        { "ref", """{"kind":"x","id":"1","extra":1}""", "«extra»" },
        { "ref", """{"kind":"x","id":"  "}""", "campo.id" },
        { "dinero", """{"amount":1,"currency":"CO"}""", "ISO 4217" },
        { "dinero", """{"amount":"1","currency":"COP"}""", "campo.amount" },
        { "ventana", """{"start":"2026-10-15T10:00:00Z","end":"2026-10-15T10:00:00Z"}""", "después de su inicio" },
    };

    [Theory]
    [MemberData(nameof(Malos))]
    public void Lo_que_no_tiene_su_forma_se_rechaza_con_el_nombre_del_campo(string tipo, string json, string mensaje)
    {
        var ex = Assert.Throws<FormatException>(() => Leer(tipo, json));

        Assert.Contains("campo", ex.Message, StringComparison.Ordinal);
        Assert.Contains(mensaje, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void El_anulable_acepta_nulo_y_ausencia_y_el_texto_en_blanco_es_nulo()
    {
        Assert.Null(Leer("texto?", "null"));
        Assert.Null(Tipos.Resolver("dinero?")!.Leer(null, "campo"));
        Assert.Null(Leer("texto?", "\"   \""));
        Assert.Equal("hola", Leer("texto?", "\"hola\""));

        var ex = Assert.Throws<FormatException>(() => Leer("texto", "null"));
        Assert.Contains("hace falta", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void El_esquema_del_anulable_admite_null_como_lo_reconoce_el_suelo_del_contrato()
    {
        var escalar = Tipos.Resolver("texto?")!.Esquema();
        var objeto = Tipos.Resolver("dinero?")!.Esquema();

        Assert.Equal("""["string","null"]""", escalar["type"]!.ToJsonString());
        Assert.Equal("null", (string?)objeto["oneOf"]![1]!["type"]);
        Assert.Equal("object", (string?)objeto["oneOf"]![0]!["type"]);
        Assert.Equal("decimal", (string?)Tipos.Resolver("decimal")!.Esquema()["format"]);
    }

    [Fact]
    public void Una_lista_lee_cada_item_con_sus_campos_y_rechaza_los_que_no_declara()
    {
        var campos = new Dictionary<string, string> { ["sujeto"] = "ref", ["cantidad"] = "entero", ["butaca"] = "texto?" };

        var items = Tipos.LeerLista(J("""[{"sujeto":{"kind":"k","id":"1"},"cantidad":2}]"""), campos, "lineas");

        var item = Assert.Single(items);
        Assert.Equal(Ref.Create("k", "1"), item.Get<Ref>("sujeto"));
        Assert.Equal(2, item.Get<int>("cantidad"));
        Assert.Null(item.Get<string?>("butaca"));

        var ex = Assert.Throws<FormatException>(() =>
            Tipos.LeerLista(J("""[{"sujeto":{"kind":"k","id":"1"},"cantidad":2,"precio":5}]"""), campos, "lineas"));
        Assert.Contains("lineas[0]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("«precio»", ex.Message, StringComparison.Ordinal);

        var esquema = Tipos.EsquemaDeLista(campos);
        Assert.Equal("""["sujeto","cantidad"]""", esquema["items"]!["required"]!.ToJsonString());
    }

    [Fact]
    public void Un_tipo_que_nadie_registro_no_se_resuelve_y_dos_con_el_mismo_nombre_no_conviven()
    {
        Assert.Null(Tipos.Resolver("moneda"));
        Assert.Throws<InvalidOperationException>(() => new TiposDeValor(TiposDeValor.Comunes.Concat([new Texto()])));
    }
}
