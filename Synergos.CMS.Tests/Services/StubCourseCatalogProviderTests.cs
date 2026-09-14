using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubCourseCatalogProvider"/> (seam <see cref="ICourseCatalogProvider"/>,
/// catálogo del LMS — dominio Educación): los 4 casos canónicos (ADR 0075) —
/// empty / happy / filter / idempotent — más el caso CLAVE de la OLA: el
/// POLIMORFISMO Blogs (las lecciones se siembran en el <see cref="IContentStream"/>
/// EXISTENTE con <c>Kind=lesson</c> y se referencian por id, sin instanciar Blogs).
/// </summary>
public class StubCourseCatalogProviderTests
{
    // IContentStream REAL (el mismo de Blogs) para ejercer el polimorfismo
    // end-to-end: las lecciones se siembran como Kind=lesson en este stream.
    private static StubContentStream ContentStream()
    {
        var clock = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        return new StubContentStream(new StubSocialGraphService(), new StubReactionService(), () => clock);
    }

    private static StubCourseCatalogProvider Make(IContentStream? stream = null)
        => new(stream ?? ContentStream());

    /// <summary>El mismo catálogo, con el reloj fijado para poder afirmar una fecha.</summary>
    private static StubCourseCatalogProvider MakeAt(DateTimeOffset now, IContentStream? stream = null)
        => new(
            stream ?? ContentStream(),
            new InMemoryCatalogIndex<CourseSummary>(
                StubCourseCatalogProvider.Descriptor, CatalogSettings.Unpaged),
            () => now);

    [Fact] // empty: filtro sin matches → lista vacía (no lanza)
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        var result = await Make().SearchAsync(new CourseQuery(Text: "submarinismo cuántico"));

        Assert.Empty(result.Courses);
        Assert.Equal(0, result.Total);
    }

    /// <summary>
    /// Buscar por el APELLIDO del instructor encuentra sus cursos.
    /// </summary>
    /// <remarks>
    /// <b>El descriptor declara esto como intención explícita</b> —«buscar "Elena" y
    /// encontrar sus cursos es intención real y hay que preservarla»— y no lo vigilaba nadie:
    /// los tests de búsqueda cubrían texto, categoría y nivel, y el campo del instructor es
    /// justo el que no se resuelve leyendo una propiedad sino componiendo dos datos.
    ///
    /// <para><b>El fixture EXIGE la regla y por eso usa el apellido.</b> "Restrepo" aparece
    /// UNA sola vez en todo el seed, en el <c>Name</c> del instructor: no está en ningún
    /// título, resumen ni categoría. Con "Elena" el test también pasaría si el campo
    /// desapareciera del descriptor y el nombre casara por accidente en alguna prosa — un
    /// verde que no prueba nada.</para>
    /// </remarks>
    [Fact] // filter: el instructor es un campo buscable, no un dato de la tarjeta
    public async Task Search_ByInstructorSurname_FindsTheirCourses()
    {
        var result = await Make().SearchAsync(new CourseQuery(Text: "Restrepo"));

        Assert.NotEmpty(result.Courses);
        Assert.All(result.Courses, c => Assert.Equal("Elena Restrepo", c.InstructorName));
    }

    [Fact] // happy: sin filtros devuelve el catálogo sembrado, ordenado por rating
    public async Task Search_NoFilters_ReturnsSeededCatalog()
    {
        var result = await Make().SearchAsync(new CourseQuery());

        Assert.NotEmpty(result.Courses);
        Assert.Equal(result.Courses.Count, result.Total);
        // Orden descendente por rating.
        var ratings = result.Courses.Select(c => c.Rating).ToList();
        Assert.Equal(ratings.OrderByDescending(r => r), ratings);
        // Cada card trae instructor resuelto + agregados.
        Assert.All(result.Courses, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.InstructorName));
            Assert.True(c.LessonCount > 0);
            Assert.True(c.DurationMinutes > 0);
        });
    }

    [Fact] // filter: por categoría exacta solo trae cursos de esa categoría
    public async Task Search_ByCategory_FiltersExactly()
    {
        var result = await Make().SearchAsync(new CourseQuery(Category: "Desarrollo"));

        Assert.NotEmpty(result.Courses);
        Assert.All(result.Courses, c => Assert.Equal("Desarrollo", c.Category));
    }

    [Fact] // filter: por nivel exacto
    public async Task Search_ByLevel_FiltersExactly()
    {
        var result = await Make().SearchAsync(new CourseQuery(Level: "Principiante"));

        Assert.NotEmpty(result.Courses);
        Assert.All(result.Courses, c => Assert.Equal("Principiante", c.Level));
    }

    [Fact] // filter: el texto matchea título/resumen/categoría/instructor
    public async Task Search_ByText_MatchesAcrossFields()
    {
        var result = await Make().SearchAsync(new CourseQuery(Text: "arquitectura"));

        Assert.NotEmpty(result.Courses);
        Assert.Contains(result.Courses, c => c.Id == "dev-clean-architecture");
    }

    [Fact] // happy: el detalle trae módulos → lecciones + instructor + planes
    public async Task GetCourse_ReturnsCurriculumInstructorAndPlans()
    {
        var detail = await Make().GetCourseAsync("dev-clean-architecture");

        Assert.NotNull(detail);
        Assert.False(string.IsNullOrWhiteSpace(detail!.Description));
        Assert.NotEmpty(detail.Outcomes);
        Assert.False(string.IsNullOrWhiteSpace(detail.Instructor.Name));
        Assert.NotEmpty(detail.Modules);
        Assert.All(detail.Modules, m => Assert.NotEmpty(m.Lessons));
        // Curso de pago → plan de contado + plan de cuotas.
        Assert.Contains(detail.Plans, p => p.Installments == 1);
        Assert.Contains(detail.Plans, p => p.Installments > 1);
    }

    [Fact] // detalle de un curso inexistente → null (no lanza)
    public async Task GetCourse_Unknown_ReturnsNull()
    {
        Assert.Null(await Make().GetCourseAsync("no-existe"));
    }

    [Fact] // gratis: el curso gratuito solo expone el plan "free" (total 0)
    public async Task GetCourse_FreeCourse_HasOnlyFreePlan()
    {
        var detail = await Make().GetCourseAsync("dev-git-fundamentals");

        Assert.NotNull(detail);
        Assert.True(detail!.Course.IsFree);
        var plan = Assert.Single(detail.Plans);
        Assert.Equal(0m, plan.Total);
    }

    // ── POLIMORFISMO Blogs (el caso clave de la OLA) ───────────────────

    [Fact] // las lecciones se siembran en el IContentStream EXISTENTE con Kind=lesson
    public async Task GetCourse_LessonsAreSeededIntoContentStream_AsKindLesson()
    {
        var stream = ContentStream();
        var detail = await Make(stream).GetCourseAsync("dev-clean-architecture");

        Assert.NotNull(detail);
        var firstLesson = detail!.Modules.First().Lessons.First();

        // El ContentItemId apunta a un item REAL del stream (no es el lessonId).
        var item = await stream.GetItemAsync(firstLesson.ContentItemId);
        Assert.NotNull(item);
        // El item es Kind=lesson — Educación reusa el feed de Blogs por polimorfismo.
        Assert.Equal("lesson", item!.Kind);
        Assert.False(string.IsNullOrWhiteSpace(item.Body));
    }

    [Fact] // el feed filtrado por Kind=lesson devuelve las lecciones (mismo motor que Blogs)
    public async Task FeedFilteredByLessonKind_SurfacesSeededLessons()
    {
        var stream = ContentStream();
        await Make(stream).SearchAsync(new CourseQuery()); // dispara la siembra

        var page = await stream.GetFeedAsync(new FeedQuery(Kind: "lesson", PageSize: 200));

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, i => Assert.Equal("lesson", i.Kind));
    }

    [Fact] // idempotent: dos lecturas del catálogo NO re-siembran (ContentItemId estable)
    public async Task GetCourse_Twice_ContentItemIdsAreStable_NoReseed()
    {
        var stream = ContentStream();
        var provider = Make(stream);

        var first = await provider.GetCourseAsync("dev-clean-architecture");
        var second = await provider.GetCourseAsync("dev-clean-architecture");

        var firstIds = first!.Modules.SelectMany(m => m.Lessons).Select(l => l.ContentItemId).ToList();
        var secondIds = second!.Modules.SelectMany(m => m.Lessons).Select(l => l.ContentItemId).ToList();
        Assert.Equal(firstIds, secondIds);

        // El stream tiene exactamente una entrada por lección del catálogo (no duplica).
        var lessonItems = await stream.GetFeedAsync(new FeedQuery(Kind: "lesson", PageSize: 500));
        var expectedLessonCount = AcademyTotalLessons();
        Assert.Equal(expectedLessonCount, lessonItems.Items.Count);
    }

    // Total de lecciones del catálogo sembrado (para el assert de no-reseed).
    private static async Task<int> CountLessonsAsync()
    {
        var provider = Make();
        var search = await provider.SearchAsync(new CourseQuery());
        var total = 0;
        foreach (var c in search.Courses)
        {
            var detail = await provider.GetCourseAsync(c.Id);
            total += detail!.Modules.Sum(m => m.Lessons.Count);
        }
        return total;
    }

    private static int AcademyTotalLessons()
        => CountLessonsAsync().GetAwaiter().GetResult();

    // ── Estado y fecha de publicación (#102) ───────────────────────────────────

    /// <summary>
    /// Cada curso del catálogo de demo lleva su fecha de publicación, y son distintas.
    /// </summary>
    /// <remarks>
    /// <b>La segunda mitad es la que importa.</b> Sin fechas DISTINTAS, «Más recientes» en el
    /// borde ordenaría igual que no ordenar y el desplegable seguiría siendo decorativo
    /// contra el catálogo de demo — que es el que corre en un clon limpio y en la
    /// verificación de cualquiera. Que estén escritas y no derivadas es deliberado: esto es
    /// el catálogo de demo, así que la fecha es dato autorado como el precio.
    /// </remarks>
    [Fact]
    public async Task Search_CadaCursoSembradoLlevaSuFechaDePublicacion()
    {
        var result = await Make().SearchAsync(new CourseQuery());

        Assert.All(result.Courses, c => Assert.NotNull(c.PublishedAt));
        Assert.Equal(
            result.Courses.Count,
            result.Courses.Select(c => c.PublishedAt).Distinct().Count());
    }

    [Fact] // happy: lo que sirve este catálogo está publicado, y lo dice
    public async Task Search_LoQueSeSirveEstaPublicado()
    {
        var result = await Make().SearchAsync(new CourseQuery());

        Assert.NotEmpty(result.Courses);
        Assert.All(result.Courses, c => Assert.Equal(CourseStatuses.Published, c.Status));
    }

    /// <summary>
    /// Publicar desde el panel FECHA el curso con el día en que se publicó.
    /// </summary>
    /// <remarks>
    /// Es el único de los tres casos en que la fecha se sabe con certeza: es ahora. No se
    /// hereda del seed ni se deja sin poner — un curso recién publicado que apareciera en «no
    /// consta» caería al final de «Más recientes», que es justo lo contrario de lo que es.
    /// </remarks>
    [Fact]
    public async Task PublishCourse_FechaElCursoConElDiaDeLaPublicacion()
    {
        var provider = MakeAt(new DateTimeOffset(2026, 9, 14, 11, 30, 0, TimeSpan.Zero));

        var published = await provider.PublishCourseAsync(new CourseDraft(
            Title: "Angular moderno",
            Summary: "Signals y zoneless",
            Description: "",
            School: "",
            Category: "Desarrollo",
            Level: "Intermedio",
            InstructorId: "ins-elena",
            Price: 300_000m,
            Modules: new[]
            {
                new CourseDraftModule("Fundamentos", new[]
                {
                    new CourseDraftLesson("Signals", null, 12),
                }),
            }));

        Assert.Equal(new DateOnly(2026, 9, 14), published.Course.PublishedAt);
        Assert.Equal(CourseStatuses.Published, published.Course.Status);
    }
}
