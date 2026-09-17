using System.Text.Json.Nodes;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="CatalogCourseCatalogProvider"/> — el catálogo de Educación servido desde el
/// CONTENIDO del CMS (#100): los 4 casos canónicos (ADR 0075) más lo que esta clase añade y
/// ninguna de las otras cuatro fuentes necesita — la SIEMBRA de las lecciones al feed.
/// </summary>
public class CatalogCourseCatalogProviderTests
{
    private sealed class FakeSource : ICatalogSource<AuthoredCourse>
    {
        public List<AuthoredCourse> Courses { get; } = new();

        public int Calls { get; private set; }

        public Task<IReadOnlyList<AuthoredCourse>> GetAllAsync(
            string? scope = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<AuthoredCourse>>(Courses.ToList());
        }
    }

    private static StubContentStream Feed()
        => new(new StubSocialGraphService(), new StubReactionService(),
            () => new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

    private static AuthoredCourse Course(
        string slug = "excel-financiero",
        string title = "Excel financiero",
        string category = "Finanzas",
        string level = "Intermedio",
        string instructorName = "Elena Restrepo",
        decimal price = 180000m,
        string lessonBody = "El cuerpo de la lección.")
    {
        var lesson = new AuthoredLesson(
            Id: $"{slug}-fundamentos-flujo-de-caja",
            Title: "Flujo de caja",
            Order: 1,
            DurationMinutes: 45,
            VideoRef: null,
            Body: lessonBody,
            Resources: Array.Empty<CourseResource>(),
            IsPreview: true);

        var module = new AuthoredModule($"{slug}-fundamentos", "Fundamentos", 1, new[] { lesson });

        var summary = new CourseSummary(
            Id: slug, Title: title, Summary: "Resumen", Category: category, Level: level,
            InstructorName: instructorName, CoverImageUrl: null, Price: price, Currency: "COP",
            IsFree: price <= 0m, Rating: 0d, LessonCount: 1, DurationMinutes: 45);

        var detail = new CourseDetail(
            Course: summary,
            Description: "Descripción",
            Outcomes: new[] { "Construir un flujo de caja" },
            Instructor: new CourseInstructor("ins-elena-restrepo", instructorName, "", "", null),
            Modules: Array.Empty<CourseModule>(),
            Plans: CoursePricingRules.Build(price, "COP"));

        return new AuthoredCourse(detail, new[] { module });
    }

    [Fact] // empty: sin contenido, catálogo vacío — no lanza
    public async Task Search_SinContenido_DevuelveVacio()
    {
        var provider = new CatalogCourseCatalogProvider(new FakeSource(), new InMemoryJsonEntityStore(), Feed());

        var result = await provider.SearchAsync(new CourseQuery());

        Assert.Empty(result.Courses);
        Assert.Equal(0, result.Total);
    }

    [Fact] // happy: lo que autoró el editor sale en la búsqueda
    public async Task Search_ConContenido_SirveLoAutorado()
    {
        var source = new FakeSource();
        source.Courses.Add(Course());
        var provider = new CatalogCourseCatalogProvider(source, new InMemoryJsonEntityStore(), Feed());

        var result = await provider.SearchAsync(new CourseQuery());

        var course = Assert.Single(result.Courses);
        Assert.Equal("excel-financiero", course.Id);
        Assert.Equal(180000m, course.Price);
        Assert.False(course.IsFree);
    }

    [Fact] // filter: el MISMO descriptor que el stub — buscar por instructor funciona igual
    public async Task Search_PorInstructor_UsaElMismoDescriptorQueElStub()
    {
        var source = new FakeSource();
        source.Courses.Add(Course(slug: "a", title: "Curso A", instructorName: "Elena Restrepo"));
        source.Courses.Add(Course(slug: "b", title: "Curso B", instructorName: "Mateo Gil"));
        var provider = new CatalogCourseCatalogProvider(source, new InMemoryJsonEntityStore(), Feed());

        var result = await provider.SearchAsync(new CourseQuery(Text: "Restrepo"));

        var course = Assert.Single(result.Courses);
        Assert.Equal("a", course.Id);
    }

    [Fact] // filter: por nivel, con el vocabulario normalizado del seed
    public async Task Search_PorNivel_Filtra()
    {
        var source = new FakeSource();
        source.Courses.Add(Course(slug: "a", level: "Intermedio"));
        source.Courses.Add(Course(slug: "b", level: "Principiante"));
        var provider = new CatalogCourseCatalogProvider(source, new InMemoryJsonEntityStore(), Feed());

        var result = await provider.SearchAsync(new CourseQuery(Level: "Intermedio"));

        Assert.Equal("a", Assert.Single(result.Courses).Id);
    }

    /// <summary>
    /// La lección llega con el id del item del feed que lleva su cuerpo.
    /// </summary>
    /// <remarks>
    /// Es el POLIMORFISMO Blogs: el catálogo no duplica el contenido editorial, lo referencia.
    /// Si el <c>ContentItemId</c> no resolviera, el course-player pediría un item que no existe
    /// y la lección saldría sin cuerpo.
    /// </remarks>
    [Fact]
    public async Task GetCourse_SiembraLaLeccionEnElFeedYLaReferencia()
    {
        var source = new FakeSource();
        source.Courses.Add(Course(lessonBody: "Qué es el flujo de caja y por qué manda."));
        var feed = Feed();
        var provider = new CatalogCourseCatalogProvider(source, new InMemoryJsonEntityStore(), feed);

        var detail = await provider.GetCourseAsync("excel-financiero");

        var lesson = Assert.Single(Assert.Single(detail!.Modules).Lessons);
        Assert.False(string.IsNullOrWhiteSpace(lesson.ContentItemId));

        var item = await feed.GetItemAsync(lesson.ContentItemId);
        Assert.NotNull(item);
        Assert.Equal("Qué es el flujo de caja y por qué manda.", item!.Body);
    }

    [Fact] // idempotent: dos búsquedas no siembran la lección dos veces
    public async Task Search_DosVeces_NoDuplicaElItemDelFeed()
    {
        var source = new FakeSource();
        source.Courses.Add(Course());
        var feed = Feed();
        var provider = new CatalogCourseCatalogProvider(source, new InMemoryJsonEntityStore(), feed);

        var first = await provider.GetCourseAsync("excel-financiero");
        var second = await provider.GetCourseAsync("excel-financiero");

        Assert.Equal(
            first!.Modules[0].Lessons[0].ContentItemId,
            second!.Modules[0].Lessons[0].ContentItemId);
    }

    /// <summary>
    /// La siembra SOBREVIVE un reinicio del proceso.
    /// </summary>
    /// <remarks>
    /// <b>El test que de verdad importa, y el que ningún caché deja ver.</b> El stub resuelve
    /// la siembra con un diccionario en memoria porque su catálogo también vive ahí; aquí el
    /// catálogo lo edita una persona y el proceso se reinicia. Con un mapping en memoria, cada
    /// arranque re-sembraría el feed entero — el mismo curso con sus lecciones duplicadas una
    /// vez por despliegue, creciendo para siempre y sin que nada fallara.
    ///
    /// <para>Por eso se construye un provider NUEVO sobre el MISMO store, igual que el gate de
    /// la bitácora (#82) abre un almacén nuevo sobre el mismo fichero: reutilizar el provider
    /// pasa en verde con el defecto puesto, porque la respuesta sale del caché.</para>
    /// </remarks>
    [Fact]
    public async Task TrasUnReinicio_LaLeccionNoSeVuelveASembrar()
    {
        var source = new FakeSource();
        source.Courses.Add(Course());
        var store = new InMemoryJsonEntityStore();   // el store SOBREVIVE — es durable
        var feed = Feed();                           // el feed también

        var antes = await new CatalogCourseCatalogProvider(source, store, feed)
            .GetCourseAsync("excel-financiero");

        // Proceso nuevo: provider nuevo, mismo store, mismo feed.
        var despues = await new CatalogCourseCatalogProvider(source, store, feed)
            .GetCourseAsync("excel-financiero");

        Assert.Equal(
            antes!.Modules[0].Lessons[0].ContentItemId,
            despues!.Modules[0].Lessons[0].ContentItemId);
    }

    /// <summary>
    /// Si el editor corrige el cuerpo de una lección, el alumno lee la versión nueva.
    /// </summary>
    /// <remarks>
    /// <see cref="IContentStream"/> sólo sabe CREAR, así que un mapping por <c>lessonId</c> a
    /// secas serviría para siempre el primer cuerpo: se edita, se guarda, y no cambia nada.
    /// La huella del cuerpo es lo que lo evita. Y el <c>lessonId</c> NO cambia — es lo que
    /// sostiene el progreso de quien ya vio la lección.
    /// </remarks>
    [Fact]
    public async Task AlCambiarElCuerpo_SeSirveElNuevoYElIdDeLaLeccionNoCambia()
    {
        var source = new FakeSource();
        source.Courses.Add(Course(lessonBody: "Primera versión."));
        var store = new InMemoryJsonEntityStore();
        var feed = Feed();
        var provider = new CatalogCourseCatalogProvider(source, store, feed);

        var antes = await provider.GetCourseAsync("excel-financiero");

        // El editor corrige el texto en el backoffice.
        source.Courses.Clear();
        source.Courses.Add(Course(lessonBody: "Segunda versión, corregida."));

        var despues = await provider.GetCourseAsync("excel-financiero");

        var leccionAntes = antes!.Modules[0].Lessons[0];
        var leccionDespues = despues!.Modules[0].Lessons[0];

        Assert.Equal(leccionAntes.Id, leccionDespues.Id);
        Assert.NotEqual(leccionAntes.ContentItemId, leccionDespues.ContentItemId);

        var item = await feed.GetItemAsync(leccionDespues.ContentItemId);
        Assert.Equal("Segunda versión, corregida.", item!.Body);
    }

    /// <summary>
    /// Lo que publica un instructor GANA sobre el contenido del mismo id.
    /// </summary>
    /// <remarks>
    /// Publicar y no ver el cambio es peor que la ambigüedad — es la misma decisión, y por la
    /// misma razón, que tomó el catálogo de Eventos.
    /// </remarks>
    [Fact]
    public async Task LoPublicadoPorUnInstructor_GanaSobreElContenido()
    {
        var source = new FakeSource();
        var store = new InMemoryJsonEntityStore();
        var provider = new CatalogCourseCatalogProvider(source, store, Feed());

        var publicado = await provider.PublishCourseAsync(new CourseDraft(
            Title: "Curso del instructor",
            Summary: "Resumen",
            Description: "Descripción",
            School: "Finanzas",
            Category: "Finanzas",
            Level: "Principiante",
            InstructorId: "ins-elena",
            Price: 90000m,
            Modules: new[]
            {
                new CourseDraftModule("Módulo 1", new[]
                {
                    new CourseDraftLesson("Lección 1", null, 30, "Cuerpo de la lección."),
                }),
            }));

        // El contenido declara el MISMO id con otro título.
        source.Courses.Add(Course(slug: publicado.Course.Id, title: "El del backoffice"));

        var result = await provider.SearchAsync(new CourseQuery());

        var course = Assert.Single(result.Courses);
        Assert.Equal("Curso del instructor", course.Title);
    }

    [Fact] // idempotent: sin el motor de matrícula enchufado, el panel lista igual con ceros
    public async Task GetForInstructor_SinMetricas_ListaConCeros()
    {
        var source = new FakeSource();
        source.Courses.Add(Course());
        var provider = new CatalogCourseCatalogProvider(source, new InMemoryJsonEntityStore(), Feed());

        var result = await provider.GetForInstructorAsync("ins-elena-restrepo");

        var row = Assert.Single(result.Courses);
        Assert.Equal(0, row.Metrics.Students);
        Assert.Equal(0m, row.Metrics.Revenue);
        Assert.Equal(0, result.TotalStudents);
    }
    // ── Estado y fecha de publicación (#102) ───────────────────────────────────

    private static CourseDraft Draft(string title = "Angular moderno") => new(
        Title: title,
        Summary: "Signals y zoneless",
        Description: "",
        School: "",
        Category: "Desarrollo",
        Level: "Intermedio",
        InstructorId: "ins-elena",
        Price: 300_000m,
        Modules: new[]
        {
            new CourseDraftModule("Fundamentos", new[] { new CourseDraftLesson("Signals", null, 12) }),
        });

    /// <summary>
    /// La fecha con la que se publicó SOBREVIVE al reinicio.
    /// </summary>
    /// <remarks>
    /// <b>Es la mitad que un test sobre la misma instancia no ve.</b> La fecha no se guarda en
    /// un almacén aparte: viaja dentro del documento del overlay, así que lo que se está
    /// comprobando es que el <c>AuthoredCourse</c> serializado la lleve y la recupere. Por eso
    /// el provider de la segunda mitad es NUEVO sobre el mismo store —igual que el gate de la
    /// bitácora (#82)—: con el mismo, la respuesta saldría del caché y pasaría en verde aunque
    /// el campo no se hubiera escrito nunca.
    /// </remarks>
    [Fact]
    public async Task PublishCourse_LaFechaSobreviveAlReinicio()
    {
        var store = new InMemoryJsonEntityStore();
        var feed = Feed();
        var reloj = new DateTimeOffset(2026, 9, 14, 11, 30, 0, TimeSpan.Zero);

        var publicado = await new CatalogCourseCatalogProvider(
            new FakeSource(), store, feed,
            new InMemoryCatalogIndex<CourseSummary>(StubCourseCatalogProvider.Descriptor, CatalogSettings.Unpaged),
            () => reloj).PublishCourseAsync(Draft());

        Assert.Equal(new DateOnly(2026, 9, 14), publicado.Course.PublishedAt);

        // Proceso nuevo: provider nuevo, mismo store.
        var despues = await new CatalogCourseCatalogProvider(new FakeSource(), store, feed)
            .GetCourseAsync(publicado.Course.Id);

        Assert.Equal(new DateOnly(2026, 9, 14), despues!.Course.PublishedAt);
        Assert.Equal(CourseStatuses.Published, despues.Course.Status);
    }

    /// <summary>
    /// Un curso que YA estaba en el overlay antes de que el campo existiera se sirve igual, y
    /// su fecha queda en «no consta».
    /// </summary>
    /// <remarks>
    /// <b>Es el caso que decide que el campo sea nullable.</b> El overlay es durable: el día
    /// del despliegue hay documentos escritos sin fecha, y de ésos no se sabe cuándo se
    /// publicaron. Rellenarlos con la fecha de hoy los pondría los primeros en «Más
    /// recientes» siendo los más viejos — un orden estable y falso, que es peor que uno que no
    /// cambia. Lo que NO puede pasar es que el documento viejo tumbe el catálogo.
    ///
    /// <para>El fixture no simula el documento viejo a mano: serializa uno de verdad y le
    /// QUITA las dos claves, que es exactamente la forma que tiene en disco un curso
    /// publicado antes de este cambio.</para>
    /// </remarks>
    [Fact]
    public async Task UnCursoDelOverlaySinFecha_SeSirveYDiceNoConsta()
    {
        var store = new InMemoryJsonEntityStore();
        var feed = Feed();

        var publicado = await new CatalogCourseCatalogProvider(new FakeSource(), store, feed)
            .PublishCourseAsync(Draft());

        // El documento tal como lo habría escrito la versión anterior: sin las dos claves.
        var json = (await store.ReadAsync(
            CatalogCourseCatalogProvider.ResourceType, publicado.Course.Id))!;
        var node = JsonNode.Parse(json)!;
        var course = node["detail"]!["course"]!.AsObject();
        course.Remove("status");
        course.Remove("publishedAt");
        await store.WriteAsync(
            CatalogCourseCatalogProvider.ResourceType, publicado.Course.Id, node.ToJsonString());

        var viejo = await new CatalogCourseCatalogProvider(new FakeSource(), store, feed)
            .GetCourseAsync(publicado.Course.Id);

        Assert.NotNull(viejo);
        Assert.Null(viejo!.Course.PublishedAt);
        // Sigue estando publicado: el overlay sólo contiene lo que alguien publicó. Lo que no
        // se sabe es CUÁNDO.
        Assert.Equal(CourseStatuses.Published, viejo.Course.Status);
    }
}
