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

    [Fact] // normalizeDetail lee `outcomes` en la RAÍZ, no dentro de `course`
    public void La_ficha_emite_outcomes_en_la_raiz()
        => AssertHas(
            new AcademyController.CourseDetailResponse(
                Course: Course(),
                Modules: Array.Empty<AcademyController.ModuleDto>(),
                Instructor: new AcademyController.InstructorDto("ins", "Elena", "", "", null, null),
                Plans: Array.Empty<AcademyController.PlanDto>(),
                Outcomes: new[] { "Construir un flujo de caja" }),
            "course", "modules", "instructor", "plans", "outcomes");

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
    /// </remarks>
    [Fact]
    public void La_fila_del_instructor_emite_id_y_titulo_aplanados()
        => AssertHas(
            new AcademyController.InstructorCourseDto(
                Course: Course(), Id: "excel", Title: "Excel financiero", Price: 180000m,
                PriceFormatted: "$180.000", StudentCount: 42, Students: 42,
                Revenue: 7560000m, RevenueFormatted: "$7.560.000", Rating: 4.8),
            "id", "title", "price", "studentCount", "revenue", "rating");

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
