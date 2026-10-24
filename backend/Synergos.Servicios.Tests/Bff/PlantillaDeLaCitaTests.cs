using System.Text.Json;
using Synergos.Api.Notifications.Domain;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Salud.Contracts;
using Synergos.CMS.Tests.Architecture;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La plantilla del aviso de la cita (<c>salud.cita.confirmada</c>, 003-salud-cita T024): la que pide
/// <c>flujos/salud.cita.json</c>, contra la que aprovisiona <c>tools/provisionar.plantillas.json</c>, rellena con
/// lo que el paso <c>notifications.avisar</c> manda de verdad.
/// </summary>
/// <remarks>
/// Como el de las entradas de Eventos, en los dos sentidos: un marcador que el paso no manda deja cada aviso
/// en <c>missing_placeholder</c>; uno que manda y nadie lee es un dato de más en el rastro. Y el texto no
/// promete lo que la cita no hace: cancelar una confirmada no existe (ficha F4).
/// </remarks>
public sealed class PlantillaDeLaCitaTests
{
    private static PasoDef ElAvisoDeLaCita()
        => FlujosDeclarados.Incrustado(typeof(AppointmentResponse).Assembly, "flujos/salud.cita.json").Pasos["avisar"];

    private static Template LaAprovisionada(string clave)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var p = doc.RootElement.EnumerateArray().SingleOrDefault(e => e.TryGetProperty("key", out var k) && k.GetString() == clave);
        Assert.True(p.ValueKind == JsonValueKind.Object,
            $"salud.cita.json pide «{clave}» y tools/provisionar.plantillas.json no la declara: en un servidor limpio el aviso saldría template_not_found.");
        Assert.True(Enum.TryParse<Channel>(p.GetProperty("channel").GetString(), ignoreCase: true, out var canal));
        return new Template("provisionada", clave, canal, p.GetProperty("subject").GetString()!, p.GetProperty("body").GetString()!);
    }

    [Fact]
    public void La_plantilla_de_la_cita_usa_justo_los_valores_que_manda_el_paso()
    {
        var paso = ElAvisoDeLaCita();
        var plantilla = LaAprovisionada(paso.Plantilla!);
        var valores = PasoAvisar.Valores("pta-cita-1", new Contacto("paciente@ejemplo.co", "Ana", "https://sitio/healthcare/?cita=x", "Clínica"),
            Money.Of(50_000m, "COP"));

        var relleno = NotificationRules.Fill(plantilla, valores);
        Assert.True(relleno.IsOk, $"La plantilla «{plantilla.Key}» no se rellena con lo que manda el paso: {relleno.Rejection?.Message}");

        var sinLeer = valores.Keys
            .Where(k => !plantilla.Subject.Contains("{" + k + "}", StringComparison.Ordinal) && !plantilla.Body.Contains("{" + k + "}", StringComparison.Ordinal))
            .ToList();
        Assert.True(sinLeer.Count == 0, $"El paso manda {string.Join(", ", sinLeer)} y la plantilla de la cita no lo usa.");
    }

    [Fact]
    public void El_correo_de_la_cita_no_promete_lo_que_la_cita_no_hace()
    {
        var plantilla = LaAprovisionada(ElAvisoDeLaCita().Plantilla!);
        var texto = plantilla.Subject + " " + plantilla.Body;

        Assert.DoesNotContain("cancel", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no se te cobr", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reembols", texto, StringComparison.OrdinalIgnoreCase);
    }
}
