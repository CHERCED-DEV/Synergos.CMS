using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// «Reservar ahora» de cada ficha de Booking lleva al formulario de reserva con su servicio ya elegido (016, D-016-C24).
/// Lo que el repo versiona (uSync) cruza tres cosas: el enlace de la ficha, una opción REAL del campo «Servicio» del
/// formulario <c>reserva-cita</c>, y la configuración que deja a ese formulario, y sólo a él, leer la dirección.
/// </summary>
public sealed class ReservarDesdeLaFichaTests
{
    private static string Web()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) { dir = dir.Parent; }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Synergos.CMS.Web");
    }

    private static readonly IReadOnlyList<string> Contenido = Directory
        .GetFiles(Path.Combine(Web(), "uSync", "v9", "Content"), "*.config")
        .Select(File.ReadAllText)
        .ToList();

    private static string PorKey(Guid key) => Contenido.Single(x => x.Contains($"<Content Key=\"{key}\"", StringComparison.Ordinal));

    /// <summary>Las opciones del campo «servicio» del formulario <c>reserva-cita</c>, tal como las guarda el repo.</summary>
    private static IReadOnlyList<string> OpcionesDelServicio()
    {
        var formulario = Contenido.Single(x => x.Contains("\"formInternalKey\": \"reserva-cita\"", StringComparison.Ordinal));
        var campo = Regex.Match(formulario, "\"fieldName\": \"servicio\",\\s*\"fieldOptions\": (\"(?:[^\"\\\\]|\\\\.)*\")");
        Assert.True(campo.Success, "El formulario reserva-cita no tiene su campo «servicio» con opciones.");
        return JsonNode.Parse(campo.Groups[1].Value)!.GetValue<string>().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [Fact]
    public void Cada_ficha_reserva_una_opcion_del_formulario_y_cubren_todas()
    {
        var opciones = OpcionesDelServicio();

        foreach (var (ficha, servicio) in DevContenidoDeVerticales.ServiciosDeBooking)
        {
            Assert.Contains(servicio, opciones);
            Assert.Contains($"\"url\": \"{DevContenidoDeVerticales.ReservarElServicio(servicio)}\"", PorKey(ficha), StringComparison.Ordinal);
        }
        Assert.Equal(opciones.Order(StringComparer.Ordinal), DevContenidoDeVerticales.ServiciosDeBooking.Select(s => s.Servicio).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Solo_el_formulario_de_reserva_toma_el_servicio_de_la_direccion()
    {
        var formularios = new ConfigurationBuilder().AddJsonFile(Path.Combine(Web(), "appsettings.json")).Build()
            .GetSection("Synergos:Forms").Get<FormsSettings>()!;

        Assert.Equal(["reserva-cita"], formularios.CamposDesdeLaDireccion.Keys);
        Assert.Equal(["servicio"], formularios.CamposDesdeLaDireccionDe("reserva-cita")!);
    }
}
