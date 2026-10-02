using System.Reflection;
using System.Text.RegularExpressions;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Toda sección de negocio (ADR 0137) está ENCHUFADA: un composer la registra con
/// <c>AddSeccionDeNegocio</c> y el literal de su sección.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> Una sección escrita y no registrada compila, sus tests pasan
/// —se arman su propio contenedor— y el test del contrato también —registra los valores base de
/// cada sección por su cuenta—. Falla en producción: el resolver pide
/// <c>INegocioDelSitio&lt;T&gt;</c> y la página contesta 500 la primera vez que alguien la pide.
/// Ningún build compila las vistas.</para>
///
/// <para><b>Se descubren por la forma</b> (<c>SeccionDeNegocio&lt;,&gt;</c>) y no se listan: la escala
/// de la ADR (#196) suma una por funcionalidad, y una lista escrita a mano es justo lo que se
/// olvida. El literal tiene que ser el de la constante <c>Seccion</c> de la clase, que es la que lee
/// todo lo demás, y es lo que mide <c>SeccionesDeConfiguracionTests</c>.</para>
/// </remarks>
public sealed class SeccionesDeNegocioTests
{
    private static string Composers()
        => string.Join('\n', Directory
            .EnumerateFiles(Proyectos.Ruta("Synergos.CMS.Web", "Composers"), "*.cs", SearchOption.AllDirectories)
            .Select(f => string.Join('\n', File.ReadAllLines(f).Select(l =>
            {
                var t = l.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith('*'))
                {
                    return string.Empty;
                }
                var i = l.IndexOf("//", StringComparison.Ordinal);
                return i >= 0 ? l[..i] : l;
            }))));

    private static IReadOnlyList<Type> Secciones()
        => typeof(SeccionDeNegocio<,>).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.BaseType is { IsGenericType: true } b
                && b.GetGenericTypeDefinition() == typeof(SeccionDeNegocio<,>))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Cada_seccion_de_negocio_la_registra_un_composer_con_su_literal()
    {
        var secciones = Secciones();

        // Sin piso, un descubrimiento roto pasaría en verde: eventos (piloto) y realty (#196) ya las tienen.
        Assert.True(secciones.Count >= 2, $"Se descubrieron {secciones.Count} secciones de negocio.");

        var composers = Composers();
        var sueltas = new List<string>();
        foreach (var seccion in secciones)
        {
            var nombre = seccion.GetField("Seccion", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string;
            var negocio = seccion.BaseType!.GenericTypeArguments[1].Name;
            var patron = $@"AddSeccionDeNegocio<{seccion.Name},\s*{negocio}>\(\s*builder\.Config\.GetSection\(""{Regex.Escape(nombre ?? "?")}""\)\)";

            if (nombre is null || !Regex.IsMatch(composers, patron))
            {
                sueltas.Add($"{seccion.Name} ({nombre ?? "sin constante Seccion"})");
            }
        }

        Assert.True(sueltas.Count == 0,
            "Estas secciones de negocio no las registra ningún composer con AddSeccionDeNegocio y el "
            + "literal de su constante Seccion: " + string.Join(", ", sueltas)
            + ". Sin el registro, el resolver que las lee contesta 500 al pintar la página.");
    }

    /// <summary>
    /// Los valores base de cada sección sirven: son los que rigen en un sitio sin override, y uno que
    /// no pasara su propia validación no dejaría arrancar un despliegue sin configurar nada.
    /// </summary>
    [Fact]
    public void Los_valores_base_de_cada_seccion_pasan_su_propia_validacion()
    {
        var malas = Secciones()
            .Select(s => (Seccion: s.Name, Problemas: ((ISeccionDeNegocio)Activator.CreateInstance(s)!).Problemas()))
            .Where(x => x.Problemas.Count > 0)
            .Select(x => $"{x.Seccion}: {string.Join("; ", x.Problemas)}")
            .ToList();

        Assert.True(malas.Count == 0, "Estas secciones no validan sus propios valores base: " + string.Join(" | ", malas));
    }
}
