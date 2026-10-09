using System.Text.Json.Nodes;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Lo que la puerta no sabe pasar revienta al ARRANCAR (ADR 0140 F3): cada regla de
/// <see cref="TablaDeLaPuerta.De"/>, sobre el contrato COMITEADO de <c>Bff.Eventos</c> con una sola cosa
/// cambiada en memoria.
/// </summary>
/// <remarks>
/// El gemelo del UI (<c>contrato-http.spec.mjs</c>) tiene los suyos; éste es el que protege la puerta en
/// ejecución. Sin él, una segunda marca del mismo par se descartaba en silencio, una llave más corta que
/// la reemitida rompía cada «abrir» en producción, y una cabecera que nadie pone daba un rechazo por
/// petición en vez de impedir el arranque.
/// </remarks>
public sealed class TablaDeLaPuertaTests
{
    private static JsonNode Contrato()
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi",
            "Synergos.Bff.Eventos.json")))!;

    private static JsonObject Op(JsonNode doc, string ruta, string metodo) => doc["paths"]![ruta]![metodo]!.AsObject();

    private static JsonArray Parametros(JsonNode doc, string ruta, string metodo) => Op(doc, ruta, metodo)["parameters"]!.AsArray();

    private static JsonObject Marca(string flujo, string operacion)
        => new() { ["flujo"] = flujo, ["operacion"] = operacion };

    /// <summary>Cada regla, con la mutación que la dispara y lo que su mensaje tiene que decir.</summary>
    private static (Func<IEnumerable<(string, string)>> Contratos, string Motivo) Caso(string regla)
    {
        var doc = Contrato();
        IEnumerable<(string, string)> Uno() => [("Synergos.Bff.Eventos.json", doc.ToJsonString())];
        switch (regla)
        {
            case "titulo":
                doc["info"]!["title"] = "Synergos.Api.Pricing";
                return (Uno, "no es de un orquestador");
            case "marca":
                Op(doc, "/v1/ticket-purchases", "post")[TablaDeLaPuerta.MarcaDelFlujo]!["sobra"] = true;
                return (Uno, "tiene que ser { flujo, operacion } y nada más");
            case "clave":
                Op(doc, "/v1/ticket-purchases", "post")[TablaDeLaPuerta.MarcaDelFlujo] = Marca("compra", "abrir");
                return (Uno, "no es una clave de flujo");
            case "vocabulario":
                Op(doc, "/v1/ticket-purchases/{id}/retry", "post")[TablaDeLaPuerta.MarcaDelFlujo] = Marca("eventos.compra", "reintentar");
                return (Uno, "no es del vocabulario de la puerta");
            case "verbo":
                var borrar = Op(doc, "/v1/ticket-purchases/{id}", "get").DeepClone().AsObject();
                borrar[TablaDeLaPuerta.MarcaDelFlujo] = Marca("eventos.otro", "cancelar");
                doc["paths"]!["/v1/ticket-purchases/{id}"]!["delete"] = borrar;
                return (Uno, "la puerta sólo pasa GET y POST");
            case "repetida":
                Op(doc, "/v1/ticket-purchases/{id}/cancel", "post")[TablaDeLaPuerta.MarcaDelFlujo] = Marca("eventos.compra", "cerrar");
                return (Uno, "eventos.compra/cerrar ya la expone otra operación");
            case "repartido":
                // La segunda copia, de OTRO orquestador, expone del mismo flujo una operación que la primera no tiene.
                Op(doc, "/v1/ticket-purchases/{id}/cancel", "post").Remove(TablaDeLaPuerta.MarcaDelFlujo);
                var otro = Contrato();
                otro["info"]!["title"] = "Synergos.Bff.Otro";
                foreach (var (ruta, metodo) in new[] { ("/v1/ticket-purchases", "post"), ("/v1/ticket-purchases/{id}", "get"), ("/v1/ticket-purchases/{id}/confirm", "post") })
                {
                    Op(otro, ruta, metodo).Remove(TablaDeLaPuerta.MarcaDelFlujo);
                }
                return (() => [("Synergos.Bff.Eventos.json", doc.ToJsonString()), ("Synergos.Bff.Otro.json", otro.ToJsonString())],
                    "El flujo eventos.compra está repartido entre orquestadores");
            case "dos-veces":
                Parametros(doc, "/v1/ticket-purchases/{id}", "get").Add(new JsonObject { ["name"] = "id", ["in"] = "query", ["schema"] = new JsonObject { ["type"] = "string" } });
                return (Uno, "«id» llegaría dos veces");
            case "sin-declarar":
                Parametros(doc, "/v1/ticket-purchases/{id}", "get").First(p => (string?)p!["name"] == "id")!["name"] = "ident";
                return (Uno, "la ruta lleva {id} y no lo declara");
            case "llave-corta":
                Parametros(doc, "/v1/ticket-purchases", "post").First(p => (string?)p!["name"] == "Idempotency-Key")!["schema"]!["maxLength"] = 40;
                return (Uno, $"acepta llaves de 40 y la puerta reemite de {LoQuePoneLaPuerta.LargoDeLaLlave}");
            case "cabecera-ajena":
                Parametros(doc, "/v1/ticket-purchases", "post").Add(new JsonObject
                {
                    ["name"] = "X-Synergos-Otra", ["in"] = "header", ["schema"] = new JsonObject { ["type"] = "string" },
                    [TablaDeLaPuerta.MarcaDeLaPuerta] = true,
                });
                return (Uno, "la puerta no sabe poner la cabecera X-Synergos-Otra");
            case "cabecera-exigida":
                Parametros(doc, "/v1/ticket-purchases", "post").Add(new JsonObject
                {
                    ["name"] = "X-Cualquiera", ["in"] = "header", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" },
                });
                return (Uno, "exige X-Cualquiera y la puerta no la pone ni la copia");
            default:
                throw new ArgumentOutOfRangeException(nameof(regla), regla, null);
        }
    }

    [Theory]
    [InlineData("titulo")]
    [InlineData("marca")]
    [InlineData("clave")]
    [InlineData("vocabulario")]
    [InlineData("verbo")]
    [InlineData("repetida")]
    [InlineData("repartido")]
    [InlineData("dos-veces")]
    [InlineData("sin-declarar")]
    [InlineData("llave-corta")]
    [InlineData("cabecera-ajena")]
    [InlineData("cabecera-exigida")]
    public void Lo_que_la_puerta_no_sabe_pasar_no_arranca_y_dice_por_que(string regla)
    {
        var (contratos, motivo) = Caso(regla);

        var ex = Assert.Throws<InvalidOperationException>(() => TablaDeLaPuerta.De(contratos()));

        Assert.Contains(motivo, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void El_contrato_comiteado_arma_la_tabla_con_las_cuatro_de_la_compra()
    {
        // El control: sin mutar nada, la misma lectura arma la tabla. Sin esto, la Theory de arriba pasaría
        // también si TablaDeLaPuerta.De lanzara siempre.
        var tabla = TablaDeLaPuerta.De([("Synergos.Bff.Eventos.json", Contrato().ToJsonString())]);

        Assert.Equal(["abrir", "cancelar", "cerrar", "consultar"], tabla.Todas.Select(o => o.Operacion).Order(StringComparer.Ordinal));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
