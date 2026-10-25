using System.Text.Json;
using Synergos.Api.Notifications.Domain;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Gobierno.Contracts;
using Synergos.Bff.Pasos;
using Synergos.CMS.Tests.Architecture;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Gobierno;

/// <summary>
/// La plantilla del aviso de la radicación (<c>gobierno.tramite.radicado</c>, 009-gobierno-tramite T029): la que pide
/// <c>flujos/gobierno.radicacion.json</c>, contra la que aprovisiona <c>tools/provisionar.plantillas.json</c>, rellena
/// con lo que el paso <c>notifications.avisar</c> manda de verdad.
/// </summary>
/// <remarks>
/// Como la de la matrícula, en los dos sentidos, con UNA excepción a propósito: <c>{numero}</c>. El paso lo manda —es
/// el de la saga— y la plantilla no lo usa, porque el radicado lo acuña el CMS al guardar las respuestas (FR-016) y el
/// orquestador no lo conoce: el correo remite a «Mis solicitudes» sin afirmar un número (FR-019).
/// </remarks>
public sealed class PlantillaDeLaRadicacionTests
{
    private static PasoDef ElAvisoDeLaRadicacion()
        => FlujosDeclarados.Incrustado(typeof(OfertaDeTramiteResponse).Assembly, "flujos/gobierno.radicacion.json").Pasos["avisar"];

    private static Template LaAprovisionada(string clave)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var p = doc.RootElement.EnumerateArray().SingleOrDefault(e => e.TryGetProperty("key", out var k) && k.GetString() == clave);
        Assert.True(p.ValueKind == JsonValueKind.Object,
            $"gobierno.radicacion.json pide «{clave}» y tools/provisionar.plantillas.json no la declara: en un servidor limpio el aviso saldría template_not_found.");
        Assert.True(Enum.TryParse<Channel>(p.GetProperty("channel").GetString(), ignoreCase: true, out var canal));
        return new Template("provisionada", clave, canal, p.GetProperty("subject").GetString()!, p.GetProperty("body").GetString()!);
    }

    [Fact]
    public void La_plantilla_de_la_radicacion_se_rellena_con_lo_que_manda_el_paso_y_sin_el_numero_de_la_saga()
    {
        var paso = ElAvisoDeLaRadicacion();
        Assert.Equal("gobierno.tramite.radicado", paso.Plantilla);
        var plantilla = LaAprovisionada(paso.Plantilla!);

        var valores = PasoAvisar.Valores("radicacion-1",
            new Contacto("maria@ejemplo.co", "María", "https://sitio/gobierno/tramites", "Alcaldía de prueba"), Money.Of(65_500m, "COP"));
        var relleno = NotificationRules.Fill(plantilla, valores);
        Assert.True(relleno.IsOk, $"La plantilla «{plantilla.Key}» no se rellena con lo que manda el paso: {relleno.Rejection?.Message}");

        var sinLeer = valores.Keys
            .Where(k => !plantilla.Subject.Contains("{" + k + "}", StringComparison.Ordinal) && !plantilla.Body.Contains("{" + k + "}", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["numero"], sinLeer);
    }

    [Fact]
    public void El_correo_de_la_radicacion_remite_a_mis_solicitudes_sin_prometer_lo_que_no_hace()
    {
        var plantilla = LaAprovisionada(ElAvisoDeLaRadicacion().Plantilla!);
        var texto = plantilla.Subject + " " + plantilla.Body;

        Assert.Contains("«Mis solicitudes»", texto, StringComparison.Ordinal);
        foreach (var promesa in new[] { "aprobad", "resuelt", "devol", "reembols", "no se te cobr" })
        {
            Assert.DoesNotContain(promesa, texto, StringComparison.OrdinalIgnoreCase);
        }
    }
}
