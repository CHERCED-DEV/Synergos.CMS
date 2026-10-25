using System.Xml.Linq;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff.Gobierno;

/// <summary>
/// El orquestador de Gobierno habla con las capacidades por HTTP y no referencia ninguna (009-gobierno-tramite T020).
/// </summary>
/// <remarks>
/// <para><b>Es de la línea porque el árbol no lo vigila.</b> Medido al crear el proyecto: un <c>Bff.Gobierno</c> que
/// referenciara <c>Synergos.Api.Workflow</c> compilaba sin avisos y dejaba en verde Arquitectura entera —
/// <c>BackendSegregationTests</c> prohíbe orquestador→orquestador y capacidad→orquestador, no orquestador→capacidad—.
/// La regla para todos los orquestadores es NG-13 de 009.</para>
/// <para><b>Las barras se normalizan</b>: el <c>.csproj</c> escribe <c>..\..\capacidades\…</c>, y en el CI de Linux
/// <c>\</c> no separa rutas — sin normalizar, el nombre leído sería la ruta entera y la prueba pasaría sin mirar.</para>
/// </remarks>
public sealed class SegregacionDeGobiernoTests
{
    [Fact]
    public void Bff_Gobierno_no_referencia_ninguna_capacidad()
    {
        var csproj = Path.Combine(Proyectos.Dir("Synergos.Bff.Gobierno"), "Synergos.Bff.Gobierno.csproj");
        var referencias = XDocument.Load(csproj).Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(((string?)r.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
            .ToList();

        Assert.Contains("Synergos.Bff.Pasos", referencias); // control: lee las referencias de verdad
        Assert.DoesNotContain(referencias, r => r.StartsWith("Synergos.Api.", StringComparison.Ordinal));
    }
}
