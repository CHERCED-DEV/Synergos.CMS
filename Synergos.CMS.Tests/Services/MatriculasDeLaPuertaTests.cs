using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La cara del libro para lo que cierra la puerta (<see cref="IMatriculasDeLaPuerta"/>, 010-educacion-matricula T014):
/// los cuatro casos canónicos (ADR 0075) —vacío, feliz, filtro, idempotente—, que no avisa (el aviso es el paso del
/// flujo) y que el aula la ve igual que una de la ruta directa (FR-013).
/// </summary>
public sealed class MatriculasDeLaPuertaTests
{
    private const string Curso = "dev-clean-architecture";
    private const string Correo = "ana@synergos.co";

    private static StubContentStream ContentStream()
    {
        var reloj = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        return new StubContentStream(new StubSocialGraphService(), new StubReactionService(), () => reloj);
    }

    private static (StubEnrollmentService Libro, AvisosApuntados Avisos) Libro()
    {
        var avisos = new AvisosApuntados();
        var libro = new StubEnrollmentService(new StubCourseCatalogProvider(ContentStream()), new StubPaymentProvider(), null, null, null, avisos);
        return (libro, avisos);
    }

    private static MatriculaDeLaPuerta DeLaPuerta(string id = "matricula-dev-clean-architecture", decimal total = 320_000m, string correo = Correo)
        => new(id, "TECTURE1", Curso, new Student("Ana", correo), total, "COP");

    [Fact]
    public async Task Sin_matriculas_de_la_puerta_no_hay_ninguna_anotada()
    {
        var (libro, _) = Libro();

        Assert.Empty(await libro.AnotadasAsync(Correo));
        Assert.Empty(await libro.AnotadasAsync(""));
    }

    [Fact]
    public async Task Anotar_la_deja_activa_y_el_aula_la_ve_como_cualquier_otra()
    {
        var (libro, _) = Libro();

        var anotacion = await libro.AnotarAsync(DeLaPuerta());

        Assert.False(anotacion.YaEstaba);
        Assert.Equal(Curso, anotacion.CourseId);
        var fila = Assert.Single(await libro.GetEnrollmentsAsync(Correo));
        Assert.Equal(anotacion.EnrollmentId, fila.EnrollmentId);
        Assert.Equal(0, (await libro.GetProgressAsync(Curso, Correo)).Percent);
        var roster = Assert.Single(await libro.GetCourseRosterAsync(Curso));
        Assert.Equal("Ana", roster.StudentName);
        Assert.Equal(new CourseEnrollmentStats(1, 320_000m), await libro.GetCourseStatsAsync(Curso));
        Assert.Equal(["matricula-dev-clean-architecture"], await libro.AnotadasAsync(Correo));
    }

    [Fact]
    public async Task Las_anotadas_son_solo_las_de_ese_alumno()
    {
        var (libro, _) = Libro();
        await libro.AnotarAsync(DeLaPuerta());
        await libro.AnotarAsync(DeLaPuerta("matricula-de-beto", correo: "beto@synergos.co"));

        Assert.Equal(["matricula-dev-clean-architecture"], await libro.AnotadasAsync(Correo));
        Assert.Equal(["matricula-de-beto"], await libro.AnotadasAsync("BETO@synergos.co"));
    }

    [Fact]
    public async Task Anotar_dos_veces_la_misma_deja_una_sola_fila()
    {
        var (libro, _) = Libro();
        var primera = await libro.AnotarAsync(DeLaPuerta());

        var segunda = await libro.AnotarAsync(DeLaPuerta());

        Assert.True(segunda.YaEstaba);
        Assert.False(segunda.CobradaDeMas);
        Assert.Equal(primera.EnrollmentId, segunda.EnrollmentId);
        Assert.Single(await libro.GetEnrollmentsAsync(Correo));
    }

    [Fact]
    public async Task Con_el_curso_ya_activo_por_la_directa_no_crea_otra_fila_y_dice_que_se_cobro_de_mas()
    {
        var (libro, _) = Libro();
        var directa = await libro.EnrollAsync("dev-git-fundamentals", new Student("Ana", Correo));
        Assert.True(directa.Enrolled);

        var anotacion = await libro.AnotarAsync(new MatriculaDeLaPuerta("matricula-git", "GIT00001", "dev-git-fundamentals",
            new Student("Ana", Correo), 50_000m, "COP"));

        Assert.True(anotacion.YaEstaba);
        Assert.True(anotacion.CobradaDeMas);
        Assert.Equal(directa.EnrollmentId, anotacion.EnrollmentId);
        Assert.Single(await libro.GetEnrollmentsAsync(Correo));
    }

    [Fact]
    public async Task Una_matricula_de_la_puerta_no_manda_el_aviso_del_cms()
    {
        var (libro, avisos) = Libro();

        await libro.AnotarAsync(DeLaPuerta());

        Assert.Empty(avisos.Enviados);
    }

    [Fact]
    public async Task Sin_id_curso_o_correo_no_se_anota_nada()
    {
        var (libro, _) = Libro();

        await Assert.ThrowsAsync<ArgumentException>(() => libro.AnotarAsync(DeLaPuerta(id: " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => libro.AnotarAsync(DeLaPuerta(correo: "")));
        Assert.Empty(await libro.GetEnrollmentsAsync(Correo));
    }

    /// <summary>Apunta lo que el motor manda al notificador del CMS.</summary>
    private sealed class AvisosApuntados : ITransactionalNotifier
    {
        public List<NotificationEvent> Enviados { get; } = [];

        public Task DispatchAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
        {
            Enviados.Add(notification);
            return Task.CompletedTask;
        }
    }
}
