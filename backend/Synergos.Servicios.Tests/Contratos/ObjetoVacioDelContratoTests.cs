using System.Text.Json.Nodes;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>
/// D-003-CUERPO: un objeto vacío A PROPÓSITO —la entrada de un abrir sin campos, los datos de una salida vacía— sale
/// cerrado en el contrato (<c>additionalProperties: false</c>), y el generador del UI lo traduce a
/// <c>Record&lt;string, never&gt;</c>. Uno sin declarar (sin <c>properties</c>) no se toca: el generador lo sigue
/// rechazando, porque <c>{}</c> en TypeScript acepta casi cualquier cosa.
/// </summary>
public sealed class ObjetoVacioDelContratoTests
{
    [Fact]
    public void Un_objeto_vacio_a_proposito_sale_cerrado_y_uno_sin_declarar_no_se_toca()
    {
        var vacio = ContratoOpenApi.EsquemasDeFlujos.Esquema(new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() });
        var desnudo = ContratoOpenApi.EsquemasDeFlujos.Esquema(new JsonObject { ["type"] = "object" });
        var conCampos = ContratoOpenApi.EsquemasDeFlujos.Esquema(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["x"] = new JsonObject { ["type"] = "string" } },
        });

        Assert.False(vacio.AdditionalPropertiesAllowed);
        Assert.True(desnudo.AdditionalPropertiesAllowed);
        Assert.True(conCampos.AdditionalPropertiesAllowed);
    }
}
