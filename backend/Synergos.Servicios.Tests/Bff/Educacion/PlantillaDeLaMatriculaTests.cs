using System.Text.Json;
using Synergos.Api.Notifications.Domain;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Educacion.Contracts;
using Synergos.Bff.Pasos;
using Synergos.CMS.Tests.Architecture;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Educacion;

/// <summary>
/// La plantilla del aviso de la matrícula (<c>educacion.matricula.confirmada</c>, 010-educacion-matricula T009): la que
/// pide <c>flujos/educacion.matricula.json</c>, contra la que aprovisiona <c>tools/provisionar.plantillas.json</c>,
/// rellena con lo que el paso <c>notifications.avisar</c> manda de verdad.
/// </summary>
/// <remarks>
/// Como la de la cita, en los dos sentidos: un marcador que el paso no manda deja cada aviso en
/// <c>missing_placeholder</c>; uno que manda y nadie lee es un dato de más. Los que manda son los fijos del paso y los
/// que declara la definición en <c>valores</c> (el título del curso y el nombre del plan: NM-2 con T091 del núcleo). Y
/// el texto no promete lo que la matrícula no hace: ni cuotas (lote 8, 010-P1) ni devolución (010-P2).
/// </remarks>
public sealed class PlantillaDeLaMatriculaTests
{
    private static PasoDef ElAvisoDeLaMatricula()
        => FlujosDeclarados.Incrustado(typeof(OfertaDeCursoResponse).Assembly, "flujos/educacion.matricula.json").Pasos["avisar"];

    private static Template LaAprovisionada(string clave)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var p = doc.RootElement.EnumerateArray().SingleOrDefault(e => e.TryGetProperty("key", out var k) && k.GetString() == clave);
        Assert.True(p.ValueKind == JsonValueKind.Object,
            $"educacion.matricula.json pide «{clave}» y tools/provisionar.plantillas.json no la declara: en un servidor limpio el aviso saldría template_not_found.");
        Assert.True(Enum.TryParse<Channel>(p.GetProperty("channel").GetString(), ignoreCase: true, out var canal));
        return new Template("provisionada", clave, canal, p.GetProperty("subject").GetString()!, p.GetProperty("body").GetString()!);
    }

    [Fact]
    public void La_plantilla_de_la_matricula_usa_justo_los_valores_que_manda_el_paso()
    {
        var paso = ElAvisoDeLaMatricula();
        Assert.Equal("educacion.matricula.confirmada", paso.Plantilla);
        var plantilla = LaAprovisionada(paso.Plantilla!);
        Assert.Equal(["nombreDelCurso", "nombreDelPlan"], paso.Valores);
        var valores = new Dictionary<string, string>(PasoAvisar.Valores("matricula-gestion-agil",
            new Contacto("alumna@ejemplo.co", "Ana", "https://sitio/educacion/?matricula=x", "Academia"), Money.Of(320_000m, "COP")));
        foreach (var nombre in paso.Valores!)
        {
            valores[nombre] = PasoAvisar.Escribir(nombre == "nombreDelCurso" ? "Gestión ágil" : "Pago de contado", TimeZoneInfo.Utc, nombre);
        }

        var relleno = NotificationRules.Fill(plantilla, valores);
        Assert.True(relleno.IsOk, $"La plantilla «{plantilla.Key}» no se rellena con lo que manda el paso: {relleno.Rejection?.Message}");

        var sinLeer = valores.Keys
            .Where(k => !plantilla.Subject.Contains("{" + k + "}", StringComparison.Ordinal) && !plantilla.Body.Contains("{" + k + "}", StringComparison.Ordinal))
            .ToList();
        Assert.True(sinLeer.Count == 0, $"El paso manda {string.Join(", ", sinLeer)} y la plantilla de la matrícula no lo usa.");
    }

    [Fact]
    public void El_correo_de_la_matricula_no_promete_lo_que_la_matricula_no_hace()
    {
        var plantilla = LaAprovisionada(ElAvisoDeLaMatricula().Plantilla!);
        var texto = plantilla.Subject + " " + plantilla.Body;

        foreach (var promesa in new[] { "cuota", "devol", "reembols", "retract", "no se te cobr" })
        {
            Assert.DoesNotContain(promesa, texto, StringComparison.OrdinalIgnoreCase);
        }
    }
}
