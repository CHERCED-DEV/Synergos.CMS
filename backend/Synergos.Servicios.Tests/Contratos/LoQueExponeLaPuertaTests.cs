using System.Text.Json.Nodes;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>
/// Lo que un orquestador expone en la puerta del CMS (ADR 0140 F3), sobre los contratos COMITEADOS: los
/// que el CMS incrusta y de los que arma su tabla.
/// </summary>
/// <remarks>
/// <para><b>La lista es cerrada por defecto, y por eso lo que importa es lo que NO se marca.</b> Reintentar
/// una compra rendida y la vista de compensaciones son de operación: con la marca, cualquier miembro con
/// sesión podría reintentar compensaciones ajenas o listar las de todos. Y una operación marcada que no
/// declara el sujeto no puede saber de quién es lo que toca.</para>
///
/// <para>La deriva (<c>ContratoOpenApiTests</c>) garantiza que el comiteado es el del código: marcar en
/// el código sin regenerar ya es rojo allá, y regenerado, es rojo acá.</para>
/// </remarks>
public sealed class LoQueExponeLaPuertaTests
{
    /// <summary>Los contratos de orquestador que se publican.</summary>
    public static TheoryData<string> Orquestadores()
    {
        var datos = new TheoryData<string>();
        foreach (var p in ContratoOpenApi.Piezas.Where(p => p.Ensamblado.StartsWith("Synergos.Bff.", StringComparison.Ordinal)))
        {
            datos.Add(p.Ensamblado);
        }
        return datos;
    }

    private static IEnumerable<(OperacionPublicada Op, JsonObject Marca)> Marcadas(string ensamblado)
        => ContratoOpenApi.Operaciones(ContratoOpenApi.Comiteado(ensamblado))
            .Where(o => o.Op[ContratoOpenApi.MarcaDelFlujo] is JsonObject)
            .Select(o => (o, o.Op[ContratoOpenApi.MarcaDelFlujo]!.AsObject()));

    [Theory]
    [MemberData(nameof(Orquestadores))]
    public void Ni_reintentar_ni_las_compensaciones_se_abren_al_navegador(string ensamblado)
    {
        var abiertas = Marcadas(ensamblado)
            .Where(m => m.Op.Ruta.EndsWith("/retry", StringComparison.Ordinal)
                        || m.Op.Ruta.StartsWith("/v1/compensations", StringComparison.Ordinal))
            .Select(m => m.Op.ToString())
            .ToList();

        Assert.True(abiertas.Count == 0,
            $"{ensamblado} expone en la puerta operaciones de operación: {string.Join(", ", abiertas)}. "
            + "Cualquier miembro con sesión podría reintentar o listar las compensaciones de todos.");
    }

    [Theory]
    [MemberData(nameof(Orquestadores))]
    public void Toda_operacion_expuesta_exige_el_sujeto(string ensamblado)
    {
        var sinSujeto = Marcadas(ensamblado)
            .Where(m => !(m.Op.Op["parameters"]?.AsArray() ?? []).Any(p =>
                (string?)p!["in"] == "header"
                && (string?)p["name"] == CabecerasDeLaPuerta.Sujeto
                && (bool?)p["required"] == true
                && (bool?)p[ContratoOpenApi.MarcaDeLaPuerta] == true))
            .Select(m => m.Op.ToString())
            .ToList();

        Assert.True(sinSujeto.Count == 0,
            $"{ensamblado} expone sin exigir {CabecerasDeLaPuerta.Sujeto}: {string.Join(", ", sinSujeto)}. "
            + "Sin sujeto, el orquestador no puede saber de quién es lo que toca.");
    }

    [Fact]
    public void Eventos_expone_la_compra_y_nada_mas()
    {
        var marcadas = Marcadas("Synergos.Bff.Eventos")
            .Select(m => $"{m.Op.Op["operationId"]} {m.Marca["flujo"]}/{m.Marca["operacion"]}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
        [
            "BuyTickets eventos.compra/abrir",
            "CancelTicketPurchase eventos.compra/cancelar",
            "ConfirmTicketPurchase eventos.compra/cerrar",
            "GetTicketPurchase eventos.compra/consultar",
        ], marcadas);
    }
}
