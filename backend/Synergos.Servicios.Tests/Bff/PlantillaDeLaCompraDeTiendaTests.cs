using System.Text.Json;
using Synergos.Api.Notifications.Domain;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Tienda.Contracts;
using Synergos.CMS.Tests.Architecture;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La plantilla del aviso de la compra de la tienda (<c>tienda.compra.confirmada</c>, 004-tienda-compra): la que
/// pide <c>flujos/tienda.compra.json</c>, contra la que aprovisiona <c>tools/provisionar.plantillas.json</c>,
/// rellena con lo que el paso <c>notifications.avisar</c> manda de verdad.
/// </summary>
/// <remarks>
/// En los dos sentidos, como la de las entradas y la de la cita: un marcador que el paso no manda deja cada
/// aviso en <c>missing_placeholder</c>; uno que manda y nadie lee es un dato de más en el rastro. Y el texto no
/// promete lo que la compra no hace: una fecha de entrega o cómo cancelar.
/// </remarks>
public sealed class PlantillaDeLaCompraDeTiendaTests
{
    private static PasoDef ElAvisoDeLaCompra()
        => FlujosDeclarados.Incrustado(typeof(PurchaseResponse).Assembly, "flujos/tienda.compra.json").Pasos["avisar"];

    private static Template LaAprovisionada(string clave)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var p = doc.RootElement.EnumerateArray().SingleOrDefault(e => e.TryGetProperty("key", out var k) && k.GetString() == clave);
        Assert.True(p.ValueKind == JsonValueKind.Object,
            $"tienda.compra.json pide «{clave}» y tools/provisionar.plantillas.json no la declara: en un servidor limpio el aviso saldría template_not_found.");
        Assert.True(Enum.TryParse<Channel>(p.GetProperty("channel").GetString(), ignoreCase: true, out var canal));
        return new Template("provisionada", clave, canal, p.GetProperty("subject").GetString()!, p.GetProperty("body").GetString()!);
    }

    [Fact]
    public void La_plantilla_de_la_compra_usa_justo_los_valores_que_manda_el_paso()
    {
        var paso = ElAvisoDeLaCompra();
        var plantilla = LaAprovisionada(paso.Plantilla!);
        var valores = PasoAvisar.Valores("pta-compra-1", new Contacto("comprador@ejemplo.co", "Ana", "https://sitio/tienda/?compra=x", "Tienda"),
            Money.Of(75_000m, "COP"));

        var relleno = NotificationRules.Fill(plantilla, valores);
        Assert.True(relleno.IsOk, $"La plantilla «{plantilla.Key}» no se rellena con lo que manda el paso: {relleno.Rejection?.Message}");

        var sinLeer = valores.Keys
            .Where(k => !plantilla.Subject.Contains("{" + k + "}", StringComparison.Ordinal) && !plantilla.Body.Contains("{" + k + "}", StringComparison.Ordinal))
            .ToList();
        Assert.True(sinLeer.Count == 0, $"El paso manda {string.Join(", ", sinLeer)} y la plantilla de la compra no lo usa.");
    }

    [Fact]
    public void El_correo_de_la_compra_no_promete_lo_que_la_compra_no_hace()
    {
        var plantilla = LaAprovisionada(ElAvisoDeLaCompra().Plantilla!);
        var texto = plantilla.Subject + " " + plantilla.Body;

        Assert.DoesNotContain("cancel", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no se te cobr", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("llega el", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("estado del envío", texto, StringComparison.OrdinalIgnoreCase);
    }
}
