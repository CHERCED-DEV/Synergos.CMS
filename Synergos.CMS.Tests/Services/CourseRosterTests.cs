using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// <see cref="IEnrollmentMetrics.GetCourseRosterAsync"/> — QUIÉNES están matriculados en un
/// curso, no cuántos (#107). Los cuatro canónicos (ADR 0075) más lo que de verdad decide esta
/// HU: <b>qué sale de cada alumno y qué no</b>.
/// </summary>
/// <remarks>
/// El seam sabía contestar «¿cuántos hay?» (<see cref="IEnrollmentMetrics.GetCourseStatsAsync"/>)
/// y «¿cómo va este alumno?» (<see cref="IEnrollmentService.GetProgressAsync"/>), y nadie sabía
/// contestar la pregunta que convierte un número en trabajo.
/// </remarks>
public class CourseRosterTests
{
    private const string PaidCourse = "dev-clean-architecture"; // 320.000 COP
    private const string FreeCourse = "dev-git-fundamentals";   // 0 COP

    private static ICourseCatalogProvider Catalog()
    {
        var clock = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        return new StubCourseCatalogProvider(
            new StubContentStream(new StubSocialGraphService(), new StubReactionService(), () => clock));
    }

    /// <summary>
    /// El fixture: dos alumnos ACTIVOS en el curso de pago (uno con avance), uno PENDIENTE de
    /// pago en el mismo curso, y uno activo en OTRO curso.
    /// </summary>
    /// <remarks>
    /// <b>Los tres distractores están ahí porque el filtro no se puede probar sin ellos.</b> Con
    /// un fixture de puros activos del mismo curso, quitar el <c>Where</c> entero daría el mismo
    /// resultado y el test no estaría vigilando nada — que es la trampa que ya costó dos verdes
    /// falsos en el repo hermano.
    /// </remarks>
    private static async Task<(StubEnrollmentService Svc, ICourseCatalogProvider Catalog)> Fixture()
    {
        var catalog = Catalog();
        // Reloj que avanza: sin él las cuatro matrículas comparten timestamp y el orden
        // «los últimos primero» no se podría afirmar.
        var tick = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
        var svc = new StubEnrollmentService(
            catalog, new StubPaymentProvider(), () => tick = tick.AddHours(1));

        var ana = await svc.EnrollAsync(PaidCourse, new Student("Ana Rincón", "ana@synergos.co"));
        await svc.ConfirmAsync(ana.OrderRef!);

        var beto = await svc.EnrollAsync(PaidCourse, new Student("Beto Salas", "beto@synergos.co"));
        await svc.ConfirmAsync(beto.OrderRef!);
        var lesson = (await catalog.GetCourseAsync(PaidCourse))!.Modules[0].Lessons[0];
        await svc.MarkLessonAsync(PaidCourse, lesson.Id, "beto@synergos.co");

        // Distractor 1: se inscribió y NO pagó → sigue en PendingPayment.
        await svc.EnrollAsync(PaidCourse, new Student("Caro Díaz", "caro@synergos.co"));

        // Distractor 2: activa, pero en otro curso (el gratis activa de inmediato).
        await svc.EnrollAsync(FreeCourse, new Student("Dani Ortiz", "dani@synergos.co"));

        return (svc, catalog);
    }

    // ── 1. empty ───────────────────────────────────────────────────────

    [Fact]
    public async Task Vacio_CursoSinMatriculasYCursoInexistente_DevuelvenListaVacia()
    {
        var svc = new StubEnrollmentService(Catalog(), new StubPaymentProvider());

        Assert.Empty(await svc.GetCourseRosterAsync(PaidCourse));
        Assert.Empty(await svc.GetCourseRosterAsync("no-existe"));
        Assert.Empty(await svc.GetCourseRosterAsync("  "));
        // `null` incluido: el guardarraíl de arriba es lo único entre esto y un NRE.
        Assert.Empty(await svc.GetCourseRosterAsync(null!));
    }

    // ── 2. happy ───────────────────────────────────────────────────────

    [Fact]
    public async Task Happy_ListaALosMatriculadosConSuAvanceYFecha()
    {
        var (svc, _) = await Fixture();

        var roster = await svc.GetCourseRosterAsync(PaidCourse);

        Assert.Equal(2, roster.Count);
        Assert.Equal(new[] { "Beto Salas", "Ana Rincón" }, roster.Select(r => r.StudentName));

        // Beto marcó una lección; Ana ninguna. El avance es de cada uno, no del curso.
        var beto = roster.Single(r => r.StudentName == "Beto Salas");
        var ana = roster.Single(r => r.StudentName == "Ana Rincón");
        Assert.True(beto.Percent > 0);
        Assert.Equal(0, ana.Percent);
        Assert.All(roster, r => Assert.Equal(PaidCourse, r.CourseId));
        Assert.True(beto.EnrolledAt > ana.EnrolledAt, "los últimos en matricularse van primero");
    }

    /// <summary>
    /// El listado y el conteo del panel dicen lo MISMO.
    /// </summary>
    /// <remarks>
    /// Son dos lecturas del mismo hecho y la consola las pinta juntas —el KPI «Alumnos» encima
    /// de la tabla—: si discreparan, diría «3 alumnos» sobre 2 filas y nadie sabría cuál de los
    /// dos está mal.
    /// </remarks>
    [Fact]
    public async Task ElListadoYElConteoNoSeContradicen()
    {
        var (svc, _) = await Fixture();

        var roster = await svc.GetCourseRosterAsync(PaidCourse);
        var stats = await svc.GetCourseStatsAsync(PaidCourse);

        Assert.Equal(stats.Students, roster.Count);
    }

    // ── 3. filter ──────────────────────────────────────────────────────

    /// <summary>
    /// Ni el que no pagó ni el de otro curso aparecen.
    /// </summary>
    /// <remarks>
    /// Una matrícula en <c>PendingPayment</c> no es un alumno: es un carrito abandonado.
    /// Listarla le diría al instructor que tiene a alguien cursando algo que nadie pagó — y al
    /// alumno, si alguna vez ve su propia fila, que tiene acceso.
    /// </remarks>
    [Fact]
    public async Task Filtro_NiLaPendienteDePagoNiLaDeOtroCurso()
    {
        var (svc, _) = await Fixture();

        var roster = await svc.GetCourseRosterAsync(PaidCourse);
        var nombres = roster.Select(r => r.StudentName).ToList();

        Assert.DoesNotContain("Caro Díaz", nombres);   // no pagó
        Assert.DoesNotContain("Dani Ortiz", nombres);  // otro curso

        // Y el fixture EXIGE la regla: los dos distractores existen de verdad.
        Assert.Single(await svc.GetCourseRosterAsync(FreeCourse));
    }

    // ── 4. idempotent ──────────────────────────────────────────────────

    [Fact]
    public async Task Idempotente_DosLlamadasSeguidasDanLaMismaLista()
    {
        var (svc, catalog) = await Fixture();

        var primera = await svc.GetCourseRosterAsync(PaidCourse);
        var segunda = await svc.GetCourseRosterAsync(PaidCourse);

        Assert.Equal(
            primera.Select(r => (r.StudentId, r.Percent, r.EnrolledAt)),
            segunda.Select(r => (r.StudentId, r.Percent, r.EnrolledAt)));

        // Y volver a marcar la misma lección no mueve el avance de nadie.
        var lesson = (await catalog.GetCourseAsync(PaidCourse))!.Modules[0].Lessons[0];
        await svc.MarkLessonAsync(PaidCourse, lesson.Id, "beto@synergos.co");

        var tercera = await svc.GetCourseRosterAsync(PaidCourse);
        Assert.Equal(primera.Select(r => r.Percent), tercera.Select(r => r.Percent));
    }

    // ── Lo que la HU decide de verdad: qué sale de cada alumno ─────────

    /// <summary>
    /// El correo del alumno NO sale en ningún campo de la fila.
    /// </summary>
    /// <remarks>
    /// <para><b>Se afirma sobre el JSON serializado y no campo por campo</b>, porque lo que hay
    /// que impedir no es «que exista una propiedad llamada Email» sino que la dirección salga —
    /// por el nombre, por el id, o por un campo que alguien añada mañana. Un
    /// <c>Assert.Null(row.Email)</c> no habría visto ninguna de las tres.</para>
    ///
    /// <para>Es la decisión de #47 (el comprador viaja seudonimizado hacia el orquestador) y la
    /// de <c>GovController</c> con <c>OpenedBy</c>, aplicada a la consola del instructor: cuatro
    /// columnas, ninguna acción que use una dirección, y un <c>GET</c> que si la emitiera sería
    /// la lista de correos de la escuela entera.</para>
    /// </remarks>
    [Fact]
    public async Task ElCorreoDelAlumnoNoSaleEnLaFila()
    {
        var (svc, _) = await Fixture();

        var roster = await svc.GetCourseRosterAsync(PaidCourse);
        Assert.NotEmpty(roster);

        foreach (var row in roster)
        {
            var json = JsonSerializer.Serialize(row);
            Assert.DoesNotContain("@", json, StringComparison.Ordinal);
            Assert.DoesNotContain("synergos.co", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// El id del alumno es el MISMO seudónimo que usan Tienda, Eventos y la visita al inmueble.
    /// </summary>
    /// <remarks>
    /// SHA-256 del correo normalizado, 16 hex (#33a, defecto #47). Inventar una cuarta receta
    /// haría que la misma persona fuera dos según por dónde entró — y este test es lo que lo
    /// impide, porque la receta está copiada y no compartida (Application no referencia Web).
    /// </remarks>
    [Fact]
    public async Task ElIdDelAlumnoEsElSeudonimoDeSiempre()
    {
        var (svc, _) = await Fixture();

        var esperado = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ana@synergos.co")))[..16]
            .ToLowerInvariant();

        var ana = (await svc.GetCourseRosterAsync(PaidCourse)).Single(r => r.StudentName == "Ana Rincón");

        Assert.Equal(esperado, ana.StudentId);
    }

    /// <summary>
    /// Un «nombre» que es una dirección se descarta, no se publica.
    /// </summary>
    /// <remarks>
    /// Es la puerta de al lado del test anterior: nada impide que una matrícula se cree con el
    /// correo en el campo del nombre, y entonces la dirección saldría por el campo que SÍ se
    /// pinta. Mismo guardarraíl que <c>PublicHolderName</c> aplica al portador de un
    /// certificado público. Vacío es la verdad; quien lo pinta escribe «Estudiante».
    /// </remarks>
    [Fact]
    public async Task UnNombreQueEsUnCorreoNoSePublica()
    {
        var catalog = Catalog();
        var svc = new StubEnrollmentService(catalog, new StubPaymentProvider());
        var sin = await svc.EnrollAsync(
            FreeCourse, new Student("zoe@synergos.co", "zoe@synergos.co"));
        Assert.True(sin.Enrolled);   // el fixture exige la regla: la matrícula existe

        var fila = Assert.Single(await svc.GetCourseRosterAsync(FreeCourse));

        Assert.Equal(string.Empty, fila.StudentName);
    }
}
