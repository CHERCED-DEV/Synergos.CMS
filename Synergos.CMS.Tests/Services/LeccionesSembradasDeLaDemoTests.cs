using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El stub de Educación anota en el store las lecciones de la demo que ya sembró (012-blogs-social, el catálogo lento):
/// con el mapping en memoria, cada arranque volvía a sembrar sus 14 lecciones en el stream, que sí es durable, y la base
/// real llegó a 1358, 97 copias de cada una. Un arranque es un stream y un catálogo nuevos sobre el mismo store, como un
/// proceso nuevo.
/// </summary>
public sealed class LeccionesSembradasDeLaDemoTests
{
    private static readonly int LeccionesDeLaDemo = AcademyDemoSeed.Courses.Sum(c => c.LessonCount);

    private DateTime _ahora = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>El stream del CMS, contando cuántas veces lo leen: cada lectura lee todo su almacén.</summary>
    private sealed class StreamQueCuenta(IContentStream interno) : IContentStream
    {
        public int Lecturas { get; private set; }

        public Task<ContentStreamPage> GetFeedAsync(FeedQuery query, CancellationToken cancellationToken = default)
        {
            Lecturas++;
            return interno.GetFeedAsync(query, cancellationToken);
        }

        public Task<ContentStreamItem?> GetItemAsync(string id, CancellationToken cancellationToken = default)
            => interno.GetItemAsync(id, cancellationToken);

        public Task<ContentStreamItem> CreateAsync(NewContentItem item, CancellationToken cancellationToken = default)
            => interno.CreateAsync(item, cancellationToken);
    }

    private StubContentStream Stream(IJsonEntityStore almacen)
        => new(new StubSocialGraphService(), new StubReactionService(), () => _ahora = _ahora.AddSeconds(1), almacen, null);

    /// <summary>Un arranque del CMS: el catálogo siembra con la primera búsqueda, como en el sitio. Devuelve cuántas veces
    /// leyó el stream.</summary>
    private async Task<int> Arrancar(IJsonEntityStore almacen)
    {
        var stream = new StreamQueCuenta(Stream(almacen));
        await new StubCourseCatalogProvider(stream, store: almacen).SearchAsync(new CourseQuery());
        return stream.Lecturas;
    }

    /// <summary>Un arranque de antes del mapping durable: sembraba otra vez todas las lecciones.</summary>
    private async Task ArrancarComoAntes(IJsonEntityStore almacen)
    {
        var stream = Stream(almacen);
        foreach (var curso in AcademyDemoSeed.Courses)
        {
            foreach (var leccion in curso.Modules.SelectMany(m => m.Lessons))
            {
                await stream.CreateAsync(new NewContentItem(curso.InstructorId, leccion.ContentBody, leccion.VideoRef, "lesson"));
            }
        }
    }

    private async Task<HashSet<string>> Lecciones(IJsonEntityStore almacen)
        => (await Stream(almacen).GetFeedAsync(new FeedQuery(Kind: "lesson", PageSize: 1000))).Items
            .Select(i => i.Id)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public async Task Dos_arranques_dejan_en_el_stream_una_vez_las_14_lecciones_de_la_demo()
    {
        var almacen = new InMemoryJsonEntityStore();

        await Arrancar(almacen);
        var lecturasDelSegundo = await Arrancar(almacen);

        Assert.Equal(14, LeccionesDeLaDemo);
        Assert.Equal(LeccionesDeLaDemo, (await Lecciones(almacen)).Count);
        // Sale de las anotaciones: no busca en el stream, que en la real son 1358 ficheros por lectura.
        Assert.Equal(0, lecturasDelSegundo);
    }

    [Fact]
    public async Task Sin_anotaciones_adopta_las_mas_recientes_del_stream_y_no_siembra_otra_tanda()
    {
        // La base real: arranques de antes, que dejaron las lecciones repetidas y ninguna anotación.
        var almacen = new InMemoryJsonEntityStore();
        await ArrancarComoAntes(almacen);
        var primeraTanda = await Lecciones(almacen);
        await ArrancarComoAntes(almacen);
        var segundaTanda = (await Lecciones(almacen)).Except(primeraTanda).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(LeccionesDeLaDemo, segundaTanda.Count);

        await Arrancar(almacen);
        await Arrancar(almacen);

        Assert.Equal(2 * LeccionesDeLaDemo, (await Lecciones(almacen)).Count);
        var catalogo = new StubCourseCatalogProvider(Stream(almacen), store: almacen);
        foreach (var curso in AcademyDemoSeed.Courses)
        {
            var detalle = await catalogo.GetCourseAsync(curso.Id);
            Assert.All(detalle!.Modules.SelectMany(m => m.Lessons), l => Assert.Contains(l.ContentItemId, segundaTanda));
        }
    }
}
