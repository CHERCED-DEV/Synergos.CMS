using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La FORMA del JSON que <c>AcademyController</c> emite, contra las claves que la app
/// <c>&lt;synergos-academy&gt;</c> lee (#102).
/// </summary>
/// <remarks>
/// <b>Esto es lo que no existía, y es el defecto de fondo del ticket.</b> Los tests del CMS
/// ejercitaban el controller contra sus propios DTOs —comprobaban que la respuesta llevara lo
/// que el controller decidió poner, que es una tautología—, así que doce claves pudieron
/// desviarse del consumidor sin que nada se pusiera rojo. El gate que sí existe
/// (<c>tools/validate-cms-contracts.mjs</c> en <c>Synergos.UI</c>) cruza el registry contra los
/// DocTypes: otra superficie. <b>La forma del JSON de un controller no la miraba nadie.</b>
///
/// <para><b>Por qué se afirma la CLAVE y no el valor.</b> El normalizador del cliente es
/// defensivo (<c>value[uiKey] ?? value[legacyKey]</c>), así que una clave que falta casi nunca
/// revienta: degrada en silencio —una segunda línea vacía, un plan sin id, una lección que
/// nunca se puede previsualizar— y eso no lo caza un test de valores. Lo que hay que vigilar es
/// que la clave ESTÉ.</para>
///
/// <para><b>Y por qué la lista va escrita a mano, que es lo que este repo suele evitar.</b>
/// Contar no sirve acá: el conjunto correcto no se deduce del árbol del CMS, vive en el otro
/// repo. Cada entrada de abajo lleva de dónde salió —el <c>normalizeX()</c> que la lee— para
/// que la próxima se añada mirando el cliente y no de memoria. El cruce automático de los dos
/// árboles es trabajo aparte, anotado en #102.</para>
/// </remarks>
public class AcademyContractShapeTests
{
    /// <summary>La misma política de nombres con la que ASP.NET serializa la respuesta.</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static IReadOnlySet<string> KeysOf(object dto)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, dto.GetType(), Json));
        return doc.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void AssertHas(object dto, params string[] keys)
    {
        var actual = KeysOf(dto);
        var missing = keys.Where(k => !actual.Contains(k)).ToList();
        Assert.True(
            missing.Count == 0,
            $"{dto.GetType().Name} no emite {string.Join(", ", missing)}. "
            + "La app de Academy las lee y su normalizador degrada en SILENCIO cuando faltan "
            + $"(#102). Emitidas: {string.Join(", ", actual.OrderBy(k => k, StringComparer.Ordinal))}");
    }

    private static AcademyController.CourseDto Course() => new(
        Id: "excel", Title: "Excel financiero", Summary: "Resumen", Subtitle: "Resumen",
        Category: "Finanzas", Level: "intermediate", InstructorName: "Elena Restrepo",
        CoverImageUrl: null, Cover: null, Price: 180000m, PriceFormatted: "$180.000",
        Currency: "COP", IsFree: false, Rating: 4.8, LessonCount: 12, DurationMinutes: 480,
        StudentCount: null, Description: null, Outcomes: null);

    [Fact] // normalizeCourse lee `subtitle` (cae a `description`, null en el listado)
    public void La_tarjeta_del_curso_emite_subtitle()
        => AssertHas(Course(), "id", "title", "subtitle", "price", "currency", "isFree");

    [Fact] // normalizeLesson lee `preview` y `kind`
    public void La_leccion_emite_preview_y_kind()
        => AssertHas(
            new AcademyController.LessonDto(
                Id: "l1", Title: "Flujo de caja", Order: 1, DurationMinutes: 45,
                VideoRef: null, ContentItemId: "c1",
                Resources: Array.Empty<AcademyController.ResourceDto>(),
                IsPreview: true, Preview: true, Kind: "reading"),
            "id", "title", "order", "durationMinutes", "preview", "kind");

    [Fact] // normalizePlan lee `id`; sin él inventa plan-<random> EN CADA RENDER
    public void El_plan_emite_id()
        => AssertHas(
            new AcademyController.PlanDto(
                Code: "emi-3", Id: "emi-3", Label: "3 cuotas", Total: 194400m, Amount: 194400m,
                Price: 194400m, TotalFormatted: "$194.400", Currency: "COP",
                Installments: "3 x $64.800", InstallmentCount: 3,
                InstallmentFormatted: "3 x $64.800"),
            "id", "code", "label", "total");

    [Fact] // normalizeDetail lee `outcomes` y `description` en la RAÍZ, no dentro de `course`
    public void La_ficha_emite_outcomes_y_description_en_la_raiz()
        => AssertHas(
            new AcademyController.CourseDetailResponse(
                Course: Course(),
                Modules: Array.Empty<AcademyController.ModuleDto>(),
                Instructor: new AcademyController.InstructorDto("ins", "Elena", "", "", null, null),
                Plans: Array.Empty<AcademyController.PlanDto>(),
                Outcomes: new[] { "Construir un flujo de caja" },
                Description: "Un curso de finanzas para quien nunca abrió una hoja de cálculo."),
            // `description` se quedó sin emitir cuando se subió `outcomes`, en la línea de al
            // lado. El normalizador cae a `course.subtitle` si falta, así que la ficha de TODO
            // curso pintaba el resumen corto donde va la descripción, sin fallar (#102).
            "course", "modules", "instructor", "plans", "outcomes", "description");

    [Fact] // normalizeCertificate lee `courseTitle` — el diploma lo imprime
    public void La_credencial_emite_courseTitle()
        => AssertHas(
            new AcademyController.CertificateDto(
                Id: "CERT-1", StudentName: "Juan Pérez", CourseTitle: "Excel financiero",
                IssuedAt: DateTimeOffset.UnixEpoch, VerifyUrl: "https://x/verify/CERT-1"),
            "id", "studentName", "courseTitle", "issuedAt", "verifyUrl");

    /// <summary>
    /// Cada fila de la consola del instructor lleva el id y el título APLANADOS.
    /// </summary>
    /// <remarks>
    /// Sin el id en la raíz, <c>normalizeInstructorCourse</c> devolvía <c>null</c> por cada
    /// fila; con la lista vacía, la consola entera caía al mock y encendía el cartel de «datos
    /// de ejemplo» aunque el servidor tuviera los cursos reales.
    ///
    /// <para><b><c>status</c> y <c>publishedAt</c> eran las dos que quedaban</b> de las siete
    /// que este ticket enumeró. La primera no era cosmética: <c>readCourseStatus</c> degrada a
    /// <c>published</c> cuando la clave falta, así que la píldora «Estado» decía «Publicado»
    /// de todo curso y el KPI contaba «N publicados / N en total». Que el estado se PASE
    /// —y no se afirme aquí— lo vigila <c>AcademyPublicationStateTests</c>; esto vigila que la
    /// clave salga, con el casing que el cliente lee.</para>
    /// </remarks>
    [Fact]
    public void La_fila_del_instructor_emite_id_y_titulo_aplanados()
        => AssertHas(
            new AcademyController.InstructorCourseDto(
                Course: Course(), Id: "excel", Title: "Excel financiero",
                Status: "published", PublishedAt: new DateOnly(2026, 2, 17), Price: 180000m,
                PriceFormatted: "$180.000", StudentCount: 42, Students: 42,
                Revenue: 7560000m, RevenueFormatted: "$7.560.000", Rating: 4.8),
            "id", "title", "status", "publishedAt", "price", "studentCount", "revenue", "rating");

    /// <summary>
    /// Cada alumno de la consola lleva las seis claves que <c>normalizeInstructorStudent</c> lee.
    /// </summary>
    /// <remarks>
    /// <b>Sin <c>id</c> el normalizador descarta la fila entera</b>, igual que hacía con las de
    /// curso antes de #102. Y <c>enrolledAt</c> se pinta CRUDO en la celda («Inscrito»), sin
    /// pasar por <c>formatDate</c> como sí hace <c>lastActivityAt</c>: por eso viaja como
    /// <c>yyyy-MM-dd</c> y no como un <c>DateTimeOffset</c>, que sacaría la T y el desfase
    /// horario a la tabla.
    /// </remarks>
    [Fact]
    public void El_alumno_de_la_consola_emite_lo_que_la_UI_lee()
        => AssertHas(
            new AcademyController.InstructorStudentDto(
                Id: "9f1c", Name: "Ana Rincón", CourseId: "excel",
                CourseTitle: "Excel financiero", Percent: 40, EnrolledAt: "2026-06-20"),
            "id", "name", "courseId", "courseTitle", "percent", "enrolledAt");

    /// <summary>
    /// La respuesta de la consola emite <c>students</c> en la RAÍZ.
    /// </summary>
    /// <remarks>
    /// <para>Es donde <c>normalizeInstructorDesk</c> lo busca; anidarlo por curso lo dejaría
    /// invisible, y con las tres listas vacías la consola entera cae al mock con el cartel de
    /// «datos de ejemplo» encendido.</para>
    ///
    /// <para><b><c>questions</c> NO se emite, y eso también se afirma</b> (#107): nadie puede
    /// escribir ni responder una pregunta —las dos acciones del otro lado son locales y no tocan
    /// la red—, así que emitirla sería servir un buzón decorativo. Si alguien la añade, que sea
    /// porque decidió cambiar eso y no de pasada.</para>
    /// </remarks>
    [Fact]
    public void La_consola_del_instructor_emite_students_en_la_raiz_y_no_questions()
    {
        var response = new AcademyController.InstructorCoursesResponse(
            Instructor: "elena@synergos.co",
            Courses: Array.Empty<AcademyController.InstructorCourseDto>(),
            Students: Array.Empty<AcademyController.InstructorStudentDto>(),
            TotalStudents: 0,
            TotalRevenue: 0m,
            TotalRevenueFormatted: "$0");

        AssertHas(response, "courses", "students", "totalStudents", "totalRevenue");
        Assert.DoesNotContain("questions", KeysOf(response));
    }

    [Fact] // normalizeLearning lee `enrollments` y `paths`
    public void Mi_aprendizaje_emite_enrollments_y_paths()
        => AssertHas(
            new AcademyController.LearningResponse(
                Enrollments: Array.Empty<AcademyController.EnrolledCourseDto>(),
                Paths: Array.Empty<AcademyController.LearningPathDto>()),
            "enrollments", "paths");

    [Fact] // normalizeEnrolledCourse lee estas cinco
    public void La_fila_de_mi_aprendizaje_emite_lo_que_la_UI_lee()
        => AssertHas(
            new AcademyController.EnrolledCourseDto(
                EnrollmentId: "enrl_1", Course: Course(), Percent: 40, LessonCount: 12,
                CompletedCount: 5, LastActivityAt: DateTimeOffset.UnixEpoch, Completed: false),
            "enrollmentId", "course", "percent", "lessonCount", "completedCount", "lastActivityAt");

    /// <summary>
    /// El request de matrícula acepta el plan que eligió el alumno.
    /// </summary>
    /// <remarks>
    /// <b>Era plata</b> (#102): la UI manda <c>planId</c> desde siempre y el record no lo tenía,
    /// así que System.Text.Json lo descartaba sin decir nada y se cobraba el precio líder del
    /// curso. Se comprueba DESERIALIZANDO el cuerpo que la app manda —no construyendo el
    /// record a mano—, porque el defecto vivía justo ahí: el record se construía bien en los
    /// tests y el binding perdía el campo en producción.
    /// </remarks>
    [Fact]
    public void El_cuerpo_de_matricula_conserva_el_plan_elegido()
    {
        const string body = """
            {"courseId":"excel","planId":"emi-3","student":{"name":"Juan","email":"j@x.co"}}
            """;

        var request = JsonSerializer.Deserialize<AcademyController.EnrollRequest>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(request);
        Assert.Equal("emi-3", request!.PlanId);
    }
}
