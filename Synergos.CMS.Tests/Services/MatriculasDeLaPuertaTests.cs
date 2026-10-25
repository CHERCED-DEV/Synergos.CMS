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

    /// <summary>
    /// Dos pestañas que cierran la misma matrícula anotan a la vez, y las dos leen el libro antes de que la otra escriba
    /// (visto en vivo en la copia piloto: dos filas del mismo curso en «mi aprendizaje» y el doble en la consola).
    /// </summary>
    [Fact]
    public async Task Anotar_la_misma_desde_dos_pestanas_a_la_vez_deja_una_sola_fila()
    {
        var store = new LecturasCruzadas(new InMemoryJsonEntityStore());
        var libro = new StubEnrollmentService(new StubCourseCatalogProvider(ContentStream()), new StubPaymentProvider(), null, store, null);

        var anotaciones = await Task.WhenAll(libro.AnotarAsync(DeLaPuerta()), libro.AnotarAsync(DeLaPuerta()));

        Assert.Equal(2, store.Retenidas);
        var fila = Assert.Single(await libro.GetEnrollmentsAsync(Correo));
        Assert.All(anotaciones, a => Assert.Equal(fila.EnrollmentId, a.EnrollmentId));
        Assert.Equal(new CourseEnrollmentStats(1, 320_000m), await libro.GetCourseStatsAsync(Curso));
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

    // ── La baja tras la devolución del personal (lote 8, 010-P2 B, T035) ─────────────────────

    [Fact]
    public async Task Dar_de_baja_la_devuelta_la_saca_del_aula_de_la_consola_y_de_las_cuentas()
    {
        var (libro, _) = Libro();
        await libro.AnotarAsync(DeLaPuerta());

        Assert.True(await libro.DarDeBajaAsync("matricula-dev-clean-architecture"));

        Assert.Empty(await libro.GetEnrollmentsAsync(Correo));
        Assert.Empty(await libro.GetCourseRosterAsync(Curso));
        Assert.Equal(new CourseEnrollmentStats(0, 0m), await libro.GetCourseStatsAsync(Curso));
        Assert.Equal(new Dictionary<string, int> { [Curso] = 1 }, await libro.DevueltasAsync(Correo));
    }

    [Fact]
    public async Task Dar_de_baja_dos_veces_no_hace_nada_y_una_que_el_libro_no_tiene_es_false()
    {
        var (libro, _) = Libro();
        await libro.AnotarAsync(DeLaPuerta());

        Assert.True(await libro.DarDeBajaAsync("matricula-dev-clean-architecture"));
        Assert.True(await libro.DarDeBajaAsync("matricula-dev-clean-architecture"));
        Assert.False(await libro.DarDeBajaAsync("no-existe"));
        Assert.False(await libro.DarDeBajaAsync(" "));

        Assert.Equal(1, (await libro.DevueltasAsync(Correo))[Curso]);
    }

    [Fact]
    public async Task La_devuelta_no_se_reactiva_al_reconciliarla_y_el_alumno_puede_matricularse_otra_vez()
    {
        var (libro, _) = Libro();
        await libro.AnotarAsync(DeLaPuerta());
        await libro.DarDeBajaAsync("matricula-dev-clean-architecture");

        // En el orquestador sigue completada: reconciliar o el enlace del aviso la vuelven a pedir.
        var otraVez = await libro.AnotarAsync(DeLaPuerta());
        Assert.True(otraVez.YaEstaba);
        Assert.True(otraVez.Devuelta);
        Assert.Empty(await libro.GetEnrollmentsAsync(Correo));

        // La nueva matrícula (otra saga, la llave matricula-{curso}-1) sí entra.
        var nueva = await libro.AnotarAsync(DeLaPuerta("matricula-dev-clean-architecture-1"));
        Assert.False(nueva.YaEstaba);
        Assert.False(nueva.Devuelta);
        Assert.Equal(nueva.EnrollmentId, Assert.Single(await libro.GetEnrollmentsAsync(Correo)).EnrollmentId);
        Assert.Equal(1, (await libro.DevueltasAsync(Correo))[Curso]);
    }

    [Fact]
    public async Task La_consola_sabe_que_fila_salio_por_la_puerta_y_cuanto_se_cobro()
    {
        var (libro, _) = Libro();
        await libro.AnotarAsync(DeLaPuerta());
        Assert.True((await libro.EnrollAsync("dev-git-fundamentals", new Student("Beto", "beto@synergos.co"))).Enrolled);

        var ana = Assert.Single(await libro.GetCourseRosterAsync(Curso));
        Assert.Equal(("matricula-dev-clean-architecture", 320_000m), (ana.MatriculaDeLaPuerta, ana.Total));
        var beto = Assert.Single(await libro.GetCourseRosterAsync("dev-git-fundamentals"));
        Assert.Null(beto.MatriculaDeLaPuerta);
        Assert.Equal(0m, beto.Total);
        Assert.Empty(await libro.DevueltasAsync(Correo));
    }

    /// <summary>
    /// Retiene las dos primeras lecturas de matrículas hasta que llegan las dos: cada anotación ve el libro como estaba
    /// antes de la otra. Con plazo, para que una anotación que ya no lea dos veces falle en vez de colgarse.
    /// </summary>
    private sealed class LecturasCruzadas(IJsonEntityStore interno) : IJsonEntityStore
    {
        private readonly TaskCompletionSource _ambas = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _lecturas;

        public int Retenidas => Math.Min(Volatile.Read(ref _lecturas), 2);

        public async Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default)
        {
            var documentos = await interno.ListAsync(resourceType, cancellationToken);
            if (resourceType == "enrollments" && Interlocked.Increment(ref _lecturas) is var n && n <= 2)
            {
                if (n == 2) _ambas.TrySetResult();
                await _ambas.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return documentos;
        }

        public Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default)
            => interno.WriteAsync(resourceType, key, json, cancellationToken);

        public Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => interno.ReadAsync(resourceType, key, cancellationToken);

        public Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => interno.DeleteAsync(resourceType, key, cancellationToken);
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
