using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Las secciones de negocio que el CMS REGISTRA (ADR 0137), derivadas de sus composers: cada
/// <c>AddSeccionDeNegocio&lt;TSeccion, TNegocio&gt;(builder.Config.GetSection("Synergos:Features:X"))</c> es la sección
/// <c>X</c>, con la configuración de tipo <c>TNegocio</c>. Es lo que pide el validador de la puerta, sin componer Umbraco.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> Las pruebas de la puerta armaban el registro con una lista escrita a mano, y cada línea
/// que abría un flujo con su propia sección la tenía que sumar (003 sumó la de Ehr; 005, 008 y 009 iban detrás). Leído
/// de los composers, lo que valida la prueba es lo que arranca.</para>
///
/// <para><b>Sólo sirve para validar</b>: la configuración de cada sección (<see cref="SeccionesDelSitio.Actual"/>) es la
/// del sitio de una petición, y aquí no hay petición.</para>
/// </remarks>
internal static class SeccionesDeNegocioDelArbol
{
    private static readonly Regex Registro = new(
        @"\.AddSeccionDeNegocio<\s*\w+\s*,\s*(?<negocio>\w+)\s*>\(\s*builder\.Config\.GetSection\(\s*""Synergos:Features:(?<nombre>[^""]+)""\s*\)\s*\)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Toda llamada, se entienda o no su forma: si son más que las leídas, una se escaparía en verde.</summary>
    internal static readonly Regex Llamada = new(@"\.AddSeccionDeNegocio<", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Los composers del CMS, sin sus comentarios: lo que se nombra en un comentario no se registra.</summary>
    internal static IReadOnlyList<(string Ruta, string Texto)> Composers()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "Synergos.CMS.sln"))) raiz = raiz.Parent;
        Assert.NotNull(raiz);

        return Directory.EnumerateFiles(Path.Combine(raiz!.FullName, "Synergos.CMS.Web", "Composers"), "*.cs", SearchOption.AllDirectories)
            .Select(f => (Ruta: Path.GetFileName(f), Texto: Regex.Replace(File.ReadAllText(f), @"/\*.*?\*/|//[^\n]*", string.Empty, RegexOptions.Singleline)))
            .ToList();
    }

    /// <summary>Cada sección registrada: su nombre y el tipo de su configuración.</summary>
    public static IReadOnlyList<(string Nombre, Type Negocio)> Leidas()
    {
        var ensamblados = new[] { typeof(NegocioDeEventos).Assembly, typeof(EhrFeatureSettings).Assembly };
        var leidas = new List<(string, Type)>();
        foreach (var (ruta, texto) in Composers())
        {
            foreach (Match m in Registro.Matches(texto))
            {
                var nombre = m.Groups["negocio"].Value;
                var tipos = ensamblados.SelectMany(a => a.GetTypes()).Where(t => t.Name == nombre).Distinct().ToList();
                Assert.True(tipos.Count == 1, $"{ruta} registra la sección «{m.Groups["nombre"].Value}» con «{nombre}», y se encontraron {tipos.Count} tipos con ese nombre.");
                leidas.Add((m.Groups["nombre"].Value, tipos[0]));
            }
        }
        return leidas;
    }

    /// <summary>El registro de secciones como lo arma el CMS al arrancar, para el validador de la puerta.</summary>
    public static SeccionesDelSitio Todas()
        => new(
            Leidas().Select(s => new SeccionDeNegocioRegistrada(s.Nombre, s.Negocio,
                _ => throw new InvalidOperationException("Las secciones del árbol sólo sirven para validar: aquí no hay el sitio de una petición."))),
            new ServiceCollection().BuildServiceProvider());
}

/// <summary>El ayudante lee TODAS las secciones que registran los composers, y con el tipo que registran.</summary>
public sealed class SeccionesDeNegocioDelArbolTests
{
    [Fact]
    public void Lee_cada_seccion_que_registra_un_composer_con_el_tipo_de_su_configuracion()
    {
        var llamadas = SeccionesDeNegocioDelArbol.Composers().Sum(c => SeccionesDeNegocioDelArbol.Llamada.Count(c.Texto));
        var leidas = SeccionesDeNegocioDelArbol.Leidas();

        Assert.True(llamadas >= 13,$"Se encontraron {llamadas} llamadas a AddSeccionDeNegocio: el descubrimiento está roto.");
        Assert.True(leidas.Count == llamadas,
            $"Los composers hacen {llamadas} AddSeccionDeNegocio y el ayudante entiende {leidas.Count}: una sección se le escaparía, y "
            + "la prueba de la puerta la daría por no registrada.");
        Assert.Equal(leidas.Count, leidas.Select(s => s.Nombre).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.Contains(("Eventos", typeof(NegocioDeEventos)), leidas);
        Assert.Contains(("Storefront", typeof(NegocioDeStorefront)), leidas);
        Assert.Contains(("Ehr", typeof(NegocioDeEhr)), leidas);
    }
}
