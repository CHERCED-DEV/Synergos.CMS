using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El CABLEADO del catálogo de Educación servido desde el contenido del CMS (#100), no sus
/// reglas: qué elige el flag, y a quién se le enchufan las métricas de matrícula.
/// </summary>
/// <remarks>
/// <b>Mira el composer y no el servicio, a propósito.</b> Es la lección de
/// <c>IdentityGateTests</c>: la primera versión de aquella rebanada pasaba en verde con el
/// defecto puesto porque los tests cubrían el helper y el servicio, y quien decide es lo que
/// hay ENTRE los dos. Aquí pasa igual — los dos catálogos están cubiertos por sus propios
/// tests y el defecto vive en qué instancia recibe la inyección.
/// </remarks>
public class AcademyCatalogSourceTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Composer()
        => File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.Academy.cs"));

    [Fact]
    public void El_seed_de_demo_sigue_siendo_el_default()
    {
        // Un clon limpio tiene que arrancar con catálogo sin que nadie haya autorado un solo
        // coursePage. El modo `cms` es OPT-IN: se entra por IsCmsSource, que devuelve demo
        // cuando la clave no está.
        var composer = Composer();

        Assert.Contains("StubCourseCatalogProvider", composer, StringComparison.Ordinal);
        Assert.Contains("IsCmsSource(sp, UmbracoCourseCatalogSource.Vertical)", composer, StringComparison.Ordinal);
    }

    /// <summary>
    /// Las métricas del panel del instructor se enchufan a QUIEN sirve el catálogo.
    /// </summary>
    /// <remarks>
    /// <b>El defecto que vigila ya estaba descrito en el repo, y este cambio lo reabría por
    /// otra puerta.</b> El ctor de <c>StubCourseCatalogProvider</c> advierte, con todas las
    /// letras, que si la inyección aterriza en una instancia distinta de la registrada «el
    /// panel del instructor mostraría 0 alumnos y $0, en silencio y con los tests en verde
    /// (los tests cablean la inyección ellos mismos)». Con dos catálogos posibles, resolver
    /// el stub POR SU NOMBRE para inyectarle las métricas hace exactamente eso en cuanto el
    /// flag pasa a <c>cms</c>.
    ///
    /// <para>Por eso el gate exige que la inyección parta de resolver
    /// <c>ICourseCatalogProvider</c> —la seam, que es lo que el flag decide— y no un tipo
    /// concreto. Comprobado mutando: con
    /// <c>sp.GetRequiredService&lt;StubCourseCatalogProvider&gt;().EnrollmentMetrics = ...</c>
    /// (que es como estaba antes de #100) este test se pone rojo.</para>
    /// </remarks>
    [Fact]
    public void Las_metricas_se_enchufan_al_catalogo_registrado_y_no_a_un_tipo_concreto()
    {
        var composer = Composer();

        // La inyección tiene que resolverse por la SEAM.
        Assert.Contains(
            "sp.GetRequiredService<ICourseCatalogProvider>()",
            composer,
            StringComparison.Ordinal);

        // Y NO puede haber ninguna asignación de EnrollmentMetrics colgando de un
        // GetRequiredService de un tipo concreto: es justo la forma del defecto.
        var porTipoConcreto = Regex.Matches(
            composer,
            @"GetRequiredService<(StubCourseCatalogProvider|CatalogCourseCatalogProvider)>\(\)\s*\.\s*EnrollmentMetrics");

        Assert.True(
            porTipoConcreto.Count == 0,
            "EnrollmentMetrics se está enchufando resolviendo un catálogo por su TIPO. Con "
            + "Synergos:Catalog:Sources:Academy = cms el catálogo registrado es el otro, así que "
            + "el panel del instructor mostraría 0 alumnos y $0 sin que nada fallara. Resuélvelo "
            + "por ICourseCatalogProvider, que es lo que el flag decide.");
    }

    /// <summary>
    /// Las dos ramas del flag reciben las métricas, no sólo la que alguien recordó.
    /// </summary>
    /// <remarks>
    /// Resolver por la seam no basta: si el <c>switch</c> sólo contempla un caso, la otra rama
    /// se queda sin métricas igual. Se exigen las dos por nombre porque son exactamente dos y
    /// están a la vista — no es una lista que pueda quedarse corta sin que se note.
    /// </remarks>
    [Fact]
    public void Las_dos_implementaciones_del_catalogo_reciben_las_metricas()
    {
        var composer = Composer();

        Assert.Contains("case CatalogCourseCatalogProvider", composer, StringComparison.Ordinal);
        Assert.Contains("case StubCourseCatalogProvider", composer, StringComparison.Ordinal);
    }

    /// <summary>
    /// Educación declara su scope, y sin él la fuente no sirve nada.
    /// </summary>
    /// <remarks>
    /// <c>coursePage</c> vive bajo el siteRoot de Educación, y la fuente falla CERRADO si falta
    /// el scope — servir sin acotar mezclaría los cursos de todos los siteRoots. Que el flag se
    /// pueda mover a <c>cms</c> sin que el scope exista sería un catálogo vacío con los logs en
    /// rojo y nadie mirándolos, así que el scope se declara desde el primer día aunque el flag
    /// siga en <c>demo</c>.
    /// </remarks>
    [Fact]
    public void El_scope_de_Academy_esta_declarado_en_los_dos_entornos()
    {
        foreach (var archivo in new[] { "appsettings.Development.json", "appsettings.Docker.json" })
        {
            var settings = File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", archivo));
            Assert.Contains("\"Academy\"", settings, StringComparison.Ordinal);
            Assert.Contains("\"educacion\"", settings, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// La fuente de contenido NO siembra: sembrar es del catálogo.
    /// </summary>
    /// <remarks>
    /// <b>Es el defecto más caro que este diseño podía tener y no falla ruidosamente.</b>
    /// <c>ICatalogSource.GetAllAsync</c> se llama en CADA búsqueda —el catálogo no cachea, a
    /// propósito, para que el read-your-writes salga gratis—, así que una fuente que sembrara
    /// crearía un item del feed por lección y por búsqueda: el feed creciendo sin tope mientras
    /// todo se ve bien. Por eso la fuente emite <c>AuthoredCourse</c> con el CUERPO y el
    /// catálogo resuelve el <c>ContentItemId</c>.
    /// </remarks>
    [Fact]
    public void La_fuente_de_contenido_no_toca_el_feed()
    {
        var fuente = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Services", "Catalog", "UmbracoCourseCatalogSource.cs"));

        Assert.DoesNotContain("IContentStream", fuente, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsync", fuente, StringComparison.Ordinal);
    }

    /// <summary>
    /// El curso autorado llega con su fecha de publicación y diciendo que está publicado.
    /// </summary>
    /// <remarks>
    /// <b>Sin esto, la regresión sólo aparece al mover una línea de configuración</b>, que es
    /// la peor forma de aparecer: el catálogo de demo seguiría ordenando «Más recientes»
    /// —sus cursos llevan fecha escrita— y el servido desde el CMS caería a «no consta» en
    /// todos, o sea el desplegable vuelto decorativo únicamente con
    /// <c>Synergos:Catalog:Sources:Academy = cms</c>. Los tests de las otras dos fuentes no lo
    /// ven porque ésta no se puede instanciar sin un contexto de Umbraco (#102).
    ///
    /// <para><b>Mira la FUENTE, con lo que eso vale y no más.</b> Comprueba que el proyector
    /// declare las dos claves y de dónde saca la fecha; no puede comprobar que el valor sea
    /// correcto —para eso haría falta levantar Umbraco—. Se vio en rojo quitando cada una de
    /// las dos líneas.</para>
    /// </remarks>
    [Fact]
    public void El_curso_autorado_declara_su_estado_y_su_fecha_de_publicacion()
    {
        var fuente = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Services", "Catalog", "UmbracoCourseCatalogSource.cs"));

        Assert.Contains("Status: CourseStatuses.Published", fuente, StringComparison.Ordinal);
        Assert.Contains("PublishedAt: DateOnly.FromDateTime(node.UpdateDate)", fuente, StringComparison.Ordinal);
    }
}
