using System;
using System.Linq;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="CourseContentRules"/> — lo que se acepta de un <c>coursePage</c>, lo que se
/// deriva y lo que hace que un curso no se pueda servir (#100). Lógica pura: no levanta un
/// contexto de Umbraco.
/// </summary>
public class CourseContentRulesTests
{
    private static AuthoredLessonDraft Lesson(string? id, string? title, int minutes = 10, string? body = null)
        => new(id, title, minutes, null, body, null, false);

    // ── Precio ──────────────────────────────────────────────────────────

    /// <summary>
    /// «180.000» se RECHAZA aunque parsee.
    /// </summary>
    /// <remarks>
    /// Es la trampa que ya costó dinero dos veces en este repo: en InvariantCulture el punto es
    /// separador DECIMAL, así que "180.000" da <b>180</b> — un precio plausible equivocado por
    /// 1000×, que ninguna guarda de «&gt; 0» ve. Y aquí es peor que una etiqueta mal puesta: el
    /// motor de matrícula resuelve el precio DESDE EL CATÁLOGO como defensa anti-tampering, así
    /// que es lo que se cobra.
    /// </remarks>
    [Theory]
    [InlineData("180.000")]
    [InlineData("180,000")]
    [InlineData("$180000")]
    [InlineData("180000 COP")]
    public void UnPrecioAmbiguo_SeRechaza(string raw)
    {
        Assert.False(CourseContentRules.TryParsePrice(raw, out _));
    }

    [Theory]
    [InlineData("180000", 180000)]
    [InlineData("0", 0)]
    [InlineData("", 0)]          // vacío es legítimo: curso gratuito o sin precio publicado
    [InlineData(null, 0)]
    public void UnPrecioInequivoco_SeAcepta(string? raw, int esperado)
    {
        Assert.True(CourseContentRules.TryParsePrice(raw, out var price));
        Assert.Equal(esperado, price);
    }

    // ── Nivel ───────────────────────────────────────────────────────────

    /// <summary>
    /// Los dos vocabularios colapsan al mismo valor.
    /// </summary>
    /// <remarks>
    /// <b>El fixture EXIGE la regla al probar los PARES.</b> El schema le pide al editor
    /// <c>intermediate</c>, el seed lleva <c>Intermedio</c>, y el filtro por nivel compara el
    /// valor crudo: sin normalizar, filtrar por «Intermedio» devolvería menos cursos y no
    /// fallaría — que es lo que no se ve. Probar sólo el español pasaría en verde con la mitad
    /// de la regla quitada.
    /// </remarks>
    [Theory]
    [InlineData("intermediate", "Intermedio")]
    [InlineData("Intermedio", "Intermedio")]
    [InlineData("advanced", "Avanzado")]
    [InlineData("Avanzado", "Avanzado")]
    [InlineData("beginner", "Principiante")]
    [InlineData("Principiante", "Principiante")]
    [InlineData("", "Principiante")]
    public void ElNivel_ColapsaALosDelSeed(string raw, string esperado)
    {
        var result = CourseContentRules.NormalizeLevel("curso", raw);

        Assert.Equal(esperado, result.Value);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void UnNivelDesconocido_CaeAPrincipianteYAvisa()
    {
        // Un nivel desconocido elige ETIQUETA, no precio: omitir el curso entero por una errata
        // sería peor que servirlo en el nivel más bajo. Pero se avisa, o la errata no se ve.
        var result = CourseContentRules.NormalizeLevel("curso", "experto");

        Assert.Equal("Principiante", result.Value);
        Assert.Single(result.Issues);
    }

    // ── Currículum ──────────────────────────────────────────────────────

    /// <summary>
    /// Reordenar el temario NO cambia el id de una lección.
    /// </summary>
    /// <remarks>
    /// <b>Es la decisión más fina del currículum y el fixture la exige moviendo de sitio la
    /// misma lección.</b> El id de una lección es la clave con la que se recuerda qué item del
    /// feed lleva su cuerpo y por dónde va cada alumno
    /// (<c>CourseProgress.CompletedLessonIds</c>). Si el id derivado llevara el número de orden
    /// —que es lo natural de escribir—, reordenar el temario reescribiría la historia de todos
    /// los matriculados en silencio: quien ya vio esa lección dejaría de tenerla por vista.
    /// </remarks>
    [Fact]
    public void ReordenarElTemario_NoCambiaElIdDeUnaLeccion()
    {
        var antes = CourseContentRules.BuildCurriculum("excel", new[]
        {
            new AuthoredModuleDraft(null, "Fundamentos", new[]
            {
                Lesson(null, "Flujo de caja"),
                Lesson(null, "Balance general"),
            }),
        });

        var despues = CourseContentRules.BuildCurriculum("excel", new[]
        {
            new AuthoredModuleDraft(null, "Fundamentos", new[]
            {
                Lesson(null, "Balance general"),   // el editor la subió
                Lesson(null, "Flujo de caja"),
            }),
        });

        var idAntes = antes.Value[0].Lessons.Single(l => l.Title == "Flujo de caja").Id;
        var idDespues = despues.Value[0].Lessons.Single(l => l.Title == "Flujo de caja").Id;

        Assert.Equal(idAntes, idDespues);

        // Y el ORDEN sí cambia: es la posición en la lista, no un campo.
        Assert.Equal(2, despues.Value[0].Lessons.Single(l => l.Title == "Flujo de caja").Order);
    }

    [Fact]
    public void ElEditorPuedeFijarElId_YGanaSobreElDerivado()
    {
        var result = CourseContentRules.BuildCurriculum("excel", new[]
        {
            new AuthoredModuleDraft("fundamentos", "Fundamentos", new[]
            {
                Lesson("flujo-de-caja", "Un título que cambió después"),
            }),
        });

        Assert.Equal("fundamentos", result.Value[0].Id);
        Assert.Equal("flujo-de-caja", result.Value[0].Lessons[0].Id);
    }

    /// <summary>
    /// Dos lecciones con el mismo título conservan las dos.
    /// </summary>
    /// <remarks>
    /// «Práctica» dos veces es raro pero legítimo, y descartar la segunda sería perder media
    /// clase por un nombre repetido. Se desempata con un sufijo.
    /// </remarks>
    [Fact]
    public void TitulosRepetidos_NoPierdenLaSegundaLeccion()
    {
        var result = CourseContentRules.BuildCurriculum("excel", new[]
        {
            new AuthoredModuleDraft(null, "Módulo", new[]
            {
                Lesson(null, "Práctica"),
                Lesson(null, "Práctica"),
            }),
        });

        var lessons = result.Value[0].Lessons;
        Assert.Equal(2, lessons.Count);
        Assert.NotEqual(lessons[0].Id, lessons[1].Id);
    }

    [Fact]
    public void UnModuloSinLeccionesServibles_SeOmiteConAviso()
    {
        var result = CourseContentRules.BuildCurriculum("excel", new[]
        {
            new AuthoredModuleDraft(null, "Vacío", Array.Empty<AuthoredLessonDraft>()),
            new AuthoredModuleDraft(null, "Con contenido", new[] { Lesson(null, "Una lección") }),
        });

        Assert.Equal("Con contenido", Assert.Single(result.Value).Title);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void SinCuerpo_SeSiembraElTitulo()
    {
        // El player pide el item del feed igual: una lección que devuelve 404 se ve peor que
        // una con una línea.
        var result = CourseContentRules.BuildCurriculum("excel", new[]
        {
            new AuthoredModuleDraft(null, "Módulo", new[] { Lesson(null, "Flujo de caja", body: null) }),
        });

        Assert.Equal("Flujo de caja", result.Value[0].Lessons[0].Body);
    }

    // ── Agregados ───────────────────────────────────────────────────────

    /// <summary>
    /// Con currículum, la suma MANDA sobre lo que declaró el editor.
    /// </summary>
    /// <remarks>
    /// Son el mismo hecho contado dos veces, y dos fuentes para un mismo hecho derivan: la
    /// tarjeta anunciaría «12 lecciones» sobre un temario de dos. El fixture declara valores
    /// que NO coinciden con el temario a propósito — con valores que coincidieran, quitar la
    /// regla pasaría en verde.
    /// </remarks>
    [Fact]
    public void ConCurriculum_LosAgregadosSalenDelTemarioYNoDeLoDeclarado()
    {
        var curriculum = CourseContentRules.BuildCurriculum("excel", new[]
        {
            new AuthoredModuleDraft(null, "Módulo", new[]
            {
                Lesson(null, "Una", minutes: 30),
                Lesson(null, "Dos", minutes: 45),
            }),
        });

        var (lessonCount, duration) = CourseContentRules.Aggregate(curriculum.Value, 12, 999);

        Assert.Equal(2, lessonCount);
        Assert.Equal(75, duration);
    }

    [Fact]
    public void SinCurriculum_SeRespetaLoDeclarado()
    {
        // Un curso puede anunciarse antes de que su temario esté cargado.
        var (lessonCount, duration) = CourseContentRules.Aggregate(Array.Empty<AuthoredModule>(), 12, 480);

        Assert.Equal(12, lessonCount);
        Assert.Equal(480, duration);
    }

    // ── Material descargable ────────────────────────────────────────────

    [Theory]
    [InlineData("https://cdn.synergos.co/plantilla.pdf", "pdf")]
    [InlineData("https://cdn.synergos.co/datos.csv", "dataset")]
    [InlineData("https://cdn.synergos.co/paquete.zip", "archive")]
    [InlineData("https://cdn.synergos.co/hoja.xlsx?v=2", "dataset")]
    [InlineData("https://docs.synergos.co/guia", "link")]
    public void ElTipoDelRecurso_SaleDeLaExtension(string url, string kind)
    {
        var resources = CourseContentRules.BuildResources(new[] { new AuthoredResourceDraft("Plantilla", url) });

        Assert.Equal(kind, Assert.Single(resources).Kind);
    }

    [Fact]
    public void UnEnlaceSinNombre_UsaLaUrlComoTitulo()
    {
        var resources = CourseContentRules.BuildResources(new[]
        {
            new AuthoredResourceDraft(null, "https://cdn.synergos.co/plantilla.pdf"),
        });

        Assert.Equal("https://cdn.synergos.co/plantilla.pdf", Assert.Single(resources).Title);
    }

    [Fact]
    public void UnEnlaceSinUrl_SeDescarta()
    {
        Assert.Empty(CourseContentRules.BuildResources(new[] { new AuthoredResourceDraft("Roto", "  ") }));
    }
}
