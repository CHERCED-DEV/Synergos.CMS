using Synergos.Bff.Core.Flow;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La definición de un flujo puede declarar el tipo y el origen de lo que entra, y lo que sale (núcleo
/// N1, supuesto P1): datos, no expresiones, y con el lector igual de estricto.
/// </summary>
/// <remarks>
/// Cada caso parte de una definición mínima que se lee bien (el control) y cambia UNA cosa; antes de
/// juzgar comprueba que el cambio entró, como <see cref="FlowValidatorTests"/>.
/// </remarks>
public sealed class DefinicionConTiposTests
{
    private const string Minima = """
        {
          "clave": "muestra.prueba",
          "entrada": ["quien", "que", "lineas", "comision"],
          "tipos": {
            "quien": { "de": "sujeto" },
            "que": "ref",
            "lineas": { "lista": { "sujeto": "ref", "cantidad": "entero" } },
            "comision": { "de": "negocio", "campo": "feePercent", "tipo": "decimal" },
            "contacto": { "de": "contacto" }
          },
          "salida": ["total"],
          "efimera": { "cerrar": ["contacto"] },
          "fases": { "abrir": ["uno"], "cerrar": ["dos"] },
          "pasos": {
            "uno": { "tipo": "x.uno", "lee": ["que"], "escribe": ["total"] },
            "dos": { "tipo": "x.dos", "lee": ["contacto"] }
          }
        }
        """;

    private static string Mutar(string antes, string despues)
    {
        Assert.True(Minima.Contains(antes, StringComparison.Ordinal),
            $"La mutación no entra: la definición mínima ya no dice «{antes}». Revisar este test antes que el lector.");
        return Minima.Replace(antes, despues, StringComparison.Ordinal);
    }

    [Fact]
    public void La_definicion_de_Eventos_sigue_leyendose_sin_tipos_ni_salida()
    {
        var eventos = FlujoDef.Leer(File.ReadAllText(Proyectos.Dir("Synergos.Bff.Eventos", "flujos", "eventos.compra.json")));

        Assert.Null(eventos.Tipos);
        Assert.Null(eventos.Salida);
        Assert.Empty(eventos.LoQueSale);
    }

    [Fact]
    public void Los_tipos_y_la_salida_se_leen_con_su_origen()
    {
        var flujo = FlujoDef.Leer(Minima);

        Assert.Equal(new[] { "total" }, flujo.LoQueSale);
        var tipos = flujo.Tipos!;
        Assert.Equal(new TipoDeclarado(OrigenDelValor.Sujeto, "ref"), tipos["quien"]);
        Assert.Equal(new TipoDeclarado(OrigenDelValor.Cuerpo, "ref"), tipos["que"]);
        Assert.Equal(OrigenDelValor.Negocio, tipos["comision"].Origen);
        Assert.Equal("feePercent", tipos["comision"].Campo);
        Assert.Equal("decimal", tipos["comision"].Tipo);
        Assert.Equal(new TipoDeclarado(OrigenDelValor.Contacto, "contacto"), tipos["contacto"]);

        var lineas = tipos["lineas"];
        Assert.True(lineas.EsLista);
        Assert.Null(lineas.Tipo);
        Assert.Equal("entero", lineas.Campos!["cantidad"]);
        Assert.Equal("ref", lineas.Campos!["sujeto"]);
    }

    public static TheoryData<string, string, string> Malas() => new()
    {
        // un origen que nadie pone
        { "{ \"de\": \"sujeto\" }", "{ \"de\": \"sesion\" }", "«sesion»" },
        // un campo de más en un origen
        { "{ \"de\": \"contacto\" }", "{ \"de\": \"contacto\", \"tipo\": \"texto\" }", "«tipo»" },
        // el negocio sin el campo que se lee
        { "\"campo\": \"feePercent\", ", "", "falta «campo»" },
        // una lista sin campos
        { "{ \"sujeto\": \"ref\", \"cantidad\": \"entero\" }", "{ }", "sin campos" },
        // un tipo que no es un nombre
        { "\"que\": \"ref\"", "\"que\": 3", "nombre de un tipo" },
        // la salida que nombra dos veces lo mismo
        { "\"salida\": [\"total\"]", "\"salida\": [\"total\", \"total\"]", "dos veces" },
    };

    [Theory]
    [MemberData(nameof(Malas))]
    public void Una_forma_que_el_lector_no_conoce_no_se_lee(string antes, string despues, string mensaje)
    {
        var ex = Assert.Throws<FormatException>(() => FlujoDef.Leer(Mutar(antes, despues)));

        Assert.Contains(mensaje, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tipos_y_salida_tienen_su_forma()
    {
        const string sinTipos = """
            { "clave": "muestra.prueba", "entrada": [], "fases": { "abrir": ["uno"] },
              "pasos": { "uno": { "tipo": "x.uno" } }, "tipos": [] }
            """;
        const string salidaQueNoEsLista = """
            { "clave": "muestra.prueba", "entrada": [], "fases": { "abrir": ["uno"] },
              "pasos": { "uno": { "tipo": "x.uno" } }, "salida": "total" }
            """;

        Assert.Contains("«tipos»: tiene que ser un objeto", Assert.Throws<FormatException>(() => FlujoDef.Leer(sinTipos)).Message,
            StringComparison.Ordinal);
        Assert.Contains("«salida» tiene que ser una lista",
            Assert.Throws<FormatException>(() => FlujoDef.Leer(salidaQueNoEsLista)).Message, StringComparison.Ordinal);
    }
}
