using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del LMS (dominio Educación — OLA 4). Es el equivalente educativo del
/// <see cref="ShopCatalogController"/>/<see cref="BookingController"/>: delega el
/// catálogo + detalle del curso (currículum + instructor + planes) a
/// <see cref="ICourseCatalogProvider"/> y el flujo transaccional enroll → pagar →
/// confirmar + el progreso/certificado a <see cref="IEnrollmentService"/>,
/// formateando precios es-CO con <see cref="IPriceFormatter"/>. Expone el contrato
/// que los módulos Angular <c>course-catalog</c> + <c>course-player</c> consumen:
/// <c>GET courses · GET course/{id} · POST enroll · POST confirm · GET progress ·
/// POST progress</c>.
/// </summary>
/// <remarks>
/// La capa Web SOLO orquesta y mapea a DTOs JSON estables — toda la lógica vive en
/// los seams (Application, sin Umbraco — ADR 0002). Los seams se cambian por
/// adapters reales (Examine sobre coursePage para el catálogo; Stripe/Wompi/PayU
/// para el pago; DB de matrículas) sin tocar este controller.
///
/// <para><b>Polimorfismo Blogs.</b> El contenido editorial de cada lección NO se
/// duplica: la <see cref="CourseLesson.ContentItemId"/> referencia un item del
/// <see cref="IContentStream"/> con <c>Kind=lesson</c> (sembrado por el catálogo).
/// El módulo course-player resuelve ese cuerpo/transcripción consumiendo el feed
/// — el MISMO motor que usa Blogs — vía la abstracción, sin instanciar Blogs.</para>
/// </remarks>
[ApiController]
[Route("api/academy")]
public sealed class AcademyController : ControllerBase
{
    private readonly ICourseCatalogProvider _catalog;
    private readonly IEnrollmentService _enrollments;
    private readonly ICertificateService _certificates;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IMemberAccessGate _gate;

    /// <summary>
    /// La cara de LECTURA del motor de matrícula: cuántos alumnos y —desde #107— quiénes.
    /// </summary>
    /// <remarks>
    /// Se inyecta aparte del catálogo aunque el catálogo ya la componga para las métricas:
    /// colgar el listado de alumnos de <c>InstructorCoursesResult</c> era lo cómodo y metería
    /// datos personales dentro del seam que sirve el catálogo PÚBLICO.
    /// </remarks>
    private readonly IEnrollmentMetrics _metrics;

    public AcademyController(
        ICourseCatalogProvider catalog,
        IEnrollmentService enrollments,
        ICertificateService certificates,
        IPriceFormatter priceFormatter,
        IMemberAccessGate gate,
        IEnrollmentMetrics metrics)
    {
        _catalog = catalog;
        _enrollments = enrollments;
        _certificates = certificates;
        _priceFormatter = priceFormatter;
        _gate = gate;
        _metrics = metrics;
    }


    // ── Identidad server-trusted (molde de Tienda/Gobierno/Eventos/Blogs/Propiedades) ──
    //
    // El expediente académico del alumno se tomaba de un `?student=` / `request.Student`.
    // Leer el progreso ajeno ya era un IDOR; ESCRIBIRLO era peor: `POST /progress` marcaba
    // lecciones como completadas en el expediente de otro. Un registro académico que
    // cualquiera puede escribir no registra nada.

    /// <summary>Rol(es) del panel de autoría de cursos.</summary>
    private const string InstructorRolesCsv = "instructor,admin";

    /// <summary>Exige sesión; devuelve el id server-trusted del alumno. 401 si es anónimo.</summary>
    private (IActionResult? denied, string studentId) RequireStudent()
    {
        var email = _gate.CurrentMemberEmail;
        if (!_gate.IsAuthenticated || string.IsNullOrWhiteSpace(email))
        {
            return (Unauthorized(new { error = "Se requiere iniciar sesión." }), string.Empty);
        }
        return (null, email);
    }

    /// <summary>
    /// Exige rol de INSTRUCTOR. 401 anónimo, 403 sin rol. Publicar un curso al catálogo del
    /// sitio y ver las métricas de matrícula no son cosas de cualquier alumno logueado.
    /// </summary>
    private (IActionResult? denied, string instructorId) RequireInstructor()
    {
        if (!_gate.IsAuthenticated)
        {
            return (Unauthorized(new { error = "Inicie sesión como instructor." }), string.Empty);
        }
        if (!_gate.HasAnyRole(InstructorRolesCsv))
        {
            // StatusCode(403), NO Forbid(): con auth de members Forbid redirige al login.
            return (StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Su cuenta no tiene permiso de instructor." }), string.Empty);
        }
        return (null, _gate.CurrentMemberEmail ?? string.Empty);
    }

    // ── 1. Courses (catálogo buscable) ─────────────────────────────────
    // GET /api/academy/courses?q=&category=&level=&price=&sort= → { courses:[...] }
    /// <summary>
    /// El catálogo buscable, filtrado y ordenado.
    /// </summary>
    /// <remarks>
    /// <b><c>price</c> y <c>sort</c> los mandaba la UI desde siempre y aquí no se ligaban</b>
    /// (#102): contra el servidor real el desplegable de orden y la faceta de precio **no
    /// hacían nada**, y contra el mock sí, porque el mock ordena y filtra. Es la forma del
    /// defecto de la Tienda —el mock describiendo un servidor que no existe— sobre dos
    /// controles que el usuario ve responder.
    ///
    /// <para><b>Se resuelven aquí y no en el seam</b> porque <see cref="CourseQuery"/> promete
    /// TODAS las coincidencias (sin paginar), así que la lista completa ya está en memoria:
    /// bajar el filtro al catálogo obligaría a que las dos fuentes —el seed y el contenido del
    /// CMS— lo implementaran igual, para el mismo resultado.</para>
    /// </remarks>
    [HttpGet("courses")]
    public async Task<IActionResult> Courses(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] string? level,
        [FromQuery] string? price,
        [FromQuery] string? sort,
        CancellationToken cancellationToken)
    {
        var result = await _catalog.SearchAsync(
            new CourseQuery(Text: q, Category: category, Level: level),
            cancellationToken);

        var matched = ApplyPriceBracket(result.Courses, price);
        matched = ApplySort(matched, sort);

        var courses = matched.Select(ToCourseDto).ToList();
        // El total es el de lo que se devuelve, no el de antes de filtrar: la UI lo pinta como
        // «N cursos» junto a la lista, y decir 40 sobre doce tarjetas es peor que no decirlo.
        return Ok(new CoursesResponse(Courses: courses, Total: courses.Count));
    }

    /// <summary>
    /// Umbral de la franja de precio media. <b>Vive aquí porque el servidor es quien filtra</b>:
    /// el vocabulario del contrato es <c>free|mid|premium</c>, así que alguien de este lado
    /// tiene que saber qué significan. Está en pesos enteros, como el resto del motor.
    /// </summary>
    private const decimal MidPriceCeiling = 450_000m;

    private static IReadOnlyList<CourseSummary> ApplyPriceBracket(
        IReadOnlyList<CourseSummary> courses,
        string? bracket)
        => (bracket ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "free" => courses.Where(c => c.Price <= 0m).ToList(),
            "mid" => courses.Where(c => c.Price > 0m && c.Price <= MidPriceCeiling).ToList(),
            "premium" => courses.Where(c => c.Price > MidPriceCeiling).ToList(),
            // Una franja desconocida no filtra en vez de devolver vacío: un vocabulario que se
            // amplíe en la UI antes que aquí tiene que enseñar el catálogo entero, no ninguno.
            _ => courses,
        };

    /// <summary>
    /// Ordena el catálogo según el desplegable.
    /// </summary>
    /// <remarks>
    /// <b><c>newest</c> ya ordena de verdad</b> (#102). Era una opción MUERTA: el desplegable
    /// la ofrecía, el valor llegaba hasta aquí, cruzaba el gate de claves y caía al
    /// <c>_</c> — contra el mock reordenaba y contra el servidor real no hacía nada, que es
    /// la forma del defecto de la Tienda (el mock describiendo un servidor que no existe).
    /// Lo que faltaba era el dato, y es el mismo que le faltaba a la píldora «Estado»:
    /// <see cref="CourseSummary.PublishedAt"/>.
    ///
    /// <para><b>Los cursos sin fecha van al final, no al principio</b>. Nulo es «no consta»
    /// —son los que ya estaban en el overlay durable antes de que el campo existiera— y un
    /// «no consta» no puede encabezar «Más recientes». Sale solo de ordenar descendente sobre
    /// un <c>DateOnly?</c>, pero está dicho porque es una decisión y no un efecto.</para>
    ///
    /// <para>Los empates los rompe el título, como los otros tres órdenes: la fecha es de día
    /// entero (ver <see cref="CourseSummary.PublishedAt"/>), así que empatar es normal.</para>
    /// </remarks>
    private static IReadOnlyList<CourseSummary> ApplySort(IReadOnlyList<CourseSummary> courses, string? sort)
        => (sort ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "price-asc" => courses.OrderBy(c => c.Price).ThenBy(c => c.Title, StringComparer.Ordinal).ToList(),
            "price-desc" => courses.OrderByDescending(c => c.Price).ThenBy(c => c.Title, StringComparer.Ordinal).ToList(),
            "rating" => courses.OrderByDescending(c => c.Rating).ThenBy(c => c.Title, StringComparer.Ordinal).ToList(),
            "newest" => courses.OrderByDescending(c => c.PublishedAt).ThenBy(c => c.Title, StringComparer.Ordinal).ToList(),
            _ => courses,
        };

    // ── 2. Course detail (PDP-curso) ───────────────────────────────────
    // GET /api/academy/course/{id} → { course, modules:[{lessons:[...]}], instructor }
    [HttpGet("course/{id}")]
    public async Task<IActionResult> Course(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del curso es requerido." });
        }

        var detail = await _catalog.GetCourseAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Curso '{id}' no encontrado." });
        }

        var course = ToCourseDto(detail.Course) with
        {
            Description = detail.Description,
            Outcomes = detail.Outcomes,
        };

        var modules = detail.Modules.Select(m => new ModuleDto(
            Id: m.Id,
            Title: m.Title,
            Order: m.Order,
            Lessons: m.Lessons.Select(ToLessonDto).ToList())).ToList();

        var plans = detail.Plans.Select(p =>
        {
            // Cadena EMI que la UI lee en 'installments' (ej "3 x $172.800"); vacía en contado.
            var installmentString = p.Installments > 1
                ? $"{p.Installments} x {_priceFormatter.Format(decimal.Round(p.Total / p.Installments, 0, MidpointRounding.AwayFromZero), p.Currency)}"
                : string.Empty;
            return new PlanDto(
                Code: p.Code,
                // El MISMO código bajo la clave que la UI lee: es el valor que vuelve como
                // `planId` al matricularse, y ahora decide cuánto se cobra.
                Id: p.Code,
                Label: p.Label,
                Total: p.Total,
                Amount: p.Total,
                Price: p.Total,
                TotalFormatted: _priceFormatter.Format(p.Total, p.Currency),
                Currency: p.Currency,
                Installments: installmentString,
                InstallmentCount: p.Installments,
                InstallmentFormatted: p.Installments > 1
                    ? installmentString
                    : _priceFormatter.Format(p.Total, p.Currency));
        }).ToList();

        var instructor = new InstructorDto(
            Id: detail.Instructor.Id,
            Name: detail.Instructor.Name,
            Headline: detail.Instructor.Headline,
            Bio: detail.Instructor.Bio,
            AvatarUrl: detail.Instructor.AvatarUrl,
            Avatar: detail.Instructor.AvatarUrl);

        return Ok(new CourseDetailResponse(
            Course: course,
            Modules: modules,
            Instructor: instructor,
            Plans: plans,
            // También en la RAÍZ, que es donde la UI lo lee. Dentro de `course` se conserva.
            Outcomes: detail.Outcomes,
            // Y la descripción, por lo mismo y en la línea de al lado — que es donde se quedó
            // sin arreglar. El normalizador la lee en la raíz y, si falta, cae a `subtitle`:
            // así que la ficha de TODO curso llevaba el resumen corto donde va la descripción
            // autorada, sin que nada fallara. Es el gemelo exacto de `outcomes` (#102).
            Description: detail.Description));
    }

    // ── 3. Enroll ──────────────────────────────────────────────────────
    // POST /api/academy/enroll { courseId, planId?, student:{name,email} }
    //   → { orderRef, paymentSessionId, amount, currency } | { enrolled:true }
    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll(
        [FromBody] EnrollRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CourseId))
        {
            return BadRequest(new { error = "courseId es requerido." });
        }
        if (request.Student is null
            || string.IsNullOrWhiteSpace(request.Student.Name)
            || string.IsNullOrWhiteSpace(request.Student.Email))
        {
            return BadRequest(new { error = "El alumno (name + email) es requerido." });
        }

        CourseEnrollmentResult result;
        try
        {
            result = await _enrollments.EnrollAsync(
                request.CourseId.Trim(),
                new Student(request.Student.Name.Trim(), request.Student.Email.Trim()),
                request.PlanId,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        // Rama gratis → { enrolled:true } (+ enrollmentId para desbloquear el aula).
        if (result.Enrolled)
        {
            return Ok(new EnrolledResponse(Enrolled: true, EnrollmentId: result.EnrollmentId));
        }

        // Rama de pago → { orderRef, paymentSessionId, amount, currency }.
        return Ok(new EnrollPaymentResponse(
            OrderRef: result.OrderRef!,
            PaymentSessionId: result.PaymentSessionId!,
            Amount: result.Amount,
            AmountFormatted: _priceFormatter.Format(result.Amount, result.Currency),
            Currency: result.Currency!));
    }

    // ── 3b. Learning (mi aprendizaje) ──────────────────────────────────
    // GET /api/academy/learning → { enrollments:[...], paths:[] }   🔒 sesión
    /// <summary>
    /// Lo que el alumno está cursando, con su avance.
    /// </summary>
    /// <remarks>
    /// <b>Este endpoint NO EXISTÍA</b> (#102). La app lo llama desde el día uno, recibía un 404,
    /// y su `catch` servía cursos de ejemplo: «mi aprendizaje» era 100 % mock SIEMPRE, con el
    /// cartel de datos de ejemplo encendido y nadie mirándolo. No era una deriva de claves —era
    /// media pantalla que nunca estuvo conectada.
    ///
    /// <para><b><c>paths</c> sale VACÍO y eso es deliberado.</b> Una ruta de aprendizaje es una
    /// colección curada de cursos con su propio título y descripción, y no existe en ningún
    /// seam ni en el schema: no hay de dónde sacarla. Devolver el array vacío hace que la UI
    /// no pinte la sección, que es la verdad; fabricar rutas agrupando por categoría le pondría
    /// nombre de producto a un <c>GROUP BY</c>.</para>
    ///
    /// <para><b>Quién es el alumno sale de la SESIÓN, y este endpoint nació sin eso</b> (#102).
    /// Se escribió con un <c>?student=</c> en el mismo controller cuya cabecera explica, doce
    /// líneas más arriba, que ese parámetro se había quitado justamente porque «leer el progreso
    /// ajeno ya era un IDOR». Anónimo y enumerable por correo: se pedía la dirección de
    /// cualquiera y salía su expediente —qué cursa, cuánto lleva, cuándo entró por última vez—.
    /// Cerrar un agujero en tres endpoints y reabrirlo al escribir el cuarto es lo que pasa
    /// cuando la regla vive en la costumbre y no en un test: <c>AcademyControllerAuthTests</c>
    /// cubría <c>progress</c> y <c>certificate</c>, y nadie escribió el de éste.</para>
    /// </remarks>
    [HttpGet("learning")]
    public async Task<IActionResult> Learning(CancellationToken cancellationToken)
    {
        var (denied, student) = RequireStudent();
        if (denied is not null) { return denied; }

        var enrollments = await _enrollments.GetEnrollmentsAsync(student, cancellationToken);

        var rows = new List<EnrolledCourseDto>(enrollments.Count);
        foreach (var e in enrollments)
        {
            var detail = await _catalog.GetCourseAsync(e.CourseId, cancellationToken);
            if (detail is null)
            {
                // El curso se despublicó después de que alguien se matriculara. Se omite la
                // fila en vez de emitirla a medias: una tarjeta sin título ni portada en «mis
                // cursos» se lee como un fallo de la pantalla, no como un curso retirado.
                continue;
            }

            rows.Add(new EnrolledCourseDto(
                EnrollmentId: e.EnrollmentId,
                Course: ToCourseDto(detail.Course),
                Percent: e.Percent,
                LessonCount: detail.Course.LessonCount,
                CompletedCount: e.CompletedCount,
                LastActivityAt: e.LastActivityAt,
                Completed: e.Percent >= 100));
        }

        return Ok(new LearningResponse(Enrollments: rows, Paths: Array.Empty<LearningPathDto>()));
    }

    // ── 4. Confirm ─────────────────────────────────────────────────────
    // POST /api/academy/confirm { orderRef } → { status, enrollmentId }
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmEnrollRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OrderRef))
        {
            return BadRequest(new { error = "orderRef es requerido." });
        }

        EnrollmentConfirmation result;
        try
        {
            result = await _enrollments.ConfirmAsync(request.OrderRef.Trim(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Pago no capturable — el cliente reintenta.
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new ConfirmEnrollResponse(
            Status: result.Status,
            EnrollmentId: result.EnrollmentId,
            CourseId: result.CourseId));
    }

    // ── 5. Get progress ────────────────────────────────────────────────
    // GET /api/academy/progress?student=&course= → { completedLessonIds:[...], percent }
    // Acepta el nombre canónico del contrato (course/student) y el legacy (courseId)
    // que aún consume el módulo course-player — sin romper la UI existente.
    [HttpGet("progress")]
    public async Task<IActionResult> GetProgress(
        [FromQuery] string? course,
        [FromQuery] string? courseId,
        CancellationToken cancellationToken)
    {
        var (denied, student) = RequireStudent();
        if (denied is not null) { return denied; }

        var courseKey = FirstNonEmpty(course, courseId);
        if (string.IsNullOrWhiteSpace(courseKey))
        {
            return BadRequest(new { error = "course es requerido." });
        }

        var progress = await _enrollments.GetProgressAsync(courseKey.Trim(), student.Trim(), cancellationToken);
        var certificate = await _certificates.GetAsync(student.Trim(), courseKey.Trim(), cancellationToken);

        return Ok(ToProgressDto(progress, await ToCertificateDtoAsync(certificate, cancellationToken)));
    }

    // ── 6. Post progress (marcar lección) ──────────────────────────────
    // POST /api/academy/progress { student, course, lesson } → { percent }
    // Acepta el shape canónico del contrato (course/lesson) y el legacy
    // (courseId/lessonId) que aún consume el módulo course-player.
    [HttpPost("progress")]
    public async Task<IActionResult> MarkProgress(
        [FromBody] MarkProgressRequest? request,
        CancellationToken cancellationToken)
    {
        var courseKey = request is null ? null : FirstNonEmpty(request.Course, request.CourseId);
        var lessonKey = request is null ? null : FirstNonEmpty(request.Lesson, request.LessonId);
        if (request is null
            || string.IsNullOrWhiteSpace(courseKey)
            || string.IsNullOrWhiteSpace(lessonKey))
        {
            return BadRequest(new { error = "course y lesson son requeridos." });
        }

        // El alumno sale del GATE: se IGNORA `request.Student`, que permitía marcar
        // lecciones completadas en el expediente de otro. El campo se deja en el DTO
        // por compatibilidad del cliente, pero ya no decide nada.
        var (denied, student) = RequireStudent();
        if (denied is not null) { return denied; }

        CourseProgress progress;
        try
        {
            progress = await _enrollments.MarkLessonAsync(
                courseKey.Trim(),
                lessonKey.Trim(),
                student,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var certificate = progress.Percent >= 100
            ? await _certificates.GetAsync(student, courseKey.Trim(), cancellationToken)
            : null;

        return Ok(ToProgressDto(progress, await ToCertificateDtoAsync(certificate, cancellationToken)));
    }

    // ── 7. Certificate (credencial verificable) ─────────────────────────
    // GET /api/academy/certificate?student=&course= → { certificate | null }
    [HttpGet("certificate")]
    public async Task<IActionResult> GetCertificate(
        [FromQuery] string? course,
        CancellationToken cancellationToken)
    {
        // Devuelve el certificado DEL ALUMNO LOGUEADO. Con `?student=` esto era un
        // padrón enumerable de quién estudió qué.
        //
        // Ojo con lo que este endpoint NO es: la verificación por un tercero (un
        // empleador). Esa es la promesa de `VerifyUrl`, y la cumple `VerifyCertificate`
        // más abajo — por el ID de la credencial, que es una capacidad (como el
        // `orderRef` de travel), nunca por el id del alumno.
        var (denied, student) = RequireStudent();
        if (denied is not null) { return denied; }

        if (string.IsNullOrWhiteSpace(course))
        {
            return BadRequest(new { error = "course es requerido." });
        }

        var certificate = await _certificates.GetAsync(student, course.Trim(), cancellationToken);
        return Ok(new CertificateResponse(
            Certificate: await ToCertificateDtoAsync(certificate, cancellationToken)));
    }

    // ── 7b. Verificación PÚBLICA de la credencial ───────────────────────
    // GET /academy/verify/{certificateId}      ← la URL que imprime el QR
    // GET /api/academy/verify/{certificateId}  ← la misma, en la forma del resto de la API
    //   → 200 { valid:true, certificate:{...} } | 404 { valid:false, certificate:null }

    /// <summary>
    /// La respuesta ÚNICA para todo lo que no es una credencial válida. Es una constante a
    /// propósito: mientras haya un solo objeto, no hay forma de que dos ramas de fallo
    /// diverjan con el tiempo y le cuenten al verificador cuál de los dos casos ocurrió.
    /// </summary>
    private static readonly VerifyCertificateResponse NotAValidCredential = new(Valid: false, Certificate: null);

    /// <summary>
    /// Verifica una credencial por su id. <b>Anónimo a propósito</b>: eso es lo que
    /// significa "verificable" — un empleador con el diploma en la mano no tiene cuenta
    /// aquí, y exigirle una convertiría la verificación en otra cosa.
    /// </summary>
    /// <remarks>
    /// <para><b>No enumera y no filtra.</b> Id malformado, id desconocido, id falsificado y
    /// credencial que ya no vale devuelven todos el MISMO 404 con el MISMO cuerpo. Quien
    /// pregunta no aprende si el curso existe, si el alumno existe, ni cuál de los cuatro
    /// casos ocurrió. Lo que sostiene esto no es el 404: es que el id sea infalsificable
    /// (ADR 0124). Con el id anterior —FNV-1a de 31 bits sin secreto— este endpoint habría
    /// sido un padrón consultable de quién estudió qué, por muy uniforme que fuera la
    /// respuesta de error.</para>
    ///
    /// <para><b>Qué ve el público.</b> Lo que una verificación SIGNIFICA: qué curso, quién
    /// lo completó y cuándo. No sale el identificador de la cuenta del alumno (su correo):
    /// el nombre solo se publica si es un nombre; si lo único que el motor tiene es el
    /// correo —el <c>fallback</c> de <c>ResolveStudentName</c>— se responde
    /// <c>studentName: null</c> antes que publicar el correo de alguien en un endpoint
    /// anónimo. Tampoco sale el progreso, ni la matrícula, ni el pago, ni nada que permita
    /// ir del certificado a los OTROS cursos del titular.</para>
    ///
    /// <para><b>Sin caché.</b> Una credencial puede dejar de valer (el seam re-comprueba el
    /// progreso en cada verificación); un intermediario que cachee la respuesta afirmativa
    /// seguiría diciendo que sí después.</para>
    /// </remarks>
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("verify/{certificateId}")]
    [HttpGet("/academy/verify/{certificateId}")]
    public async Task<IActionResult> VerifyCertificate(string? certificateId, CancellationToken cancellationToken)
    {
        var certificate = await _certificates.VerifyAsync(certificateId ?? string.Empty, cancellationToken);
        if (certificate is null)
        {
            return NotFound(NotAValidCredential);
        }

        // El título del curso se resuelve DESPUÉS de que la credencial ya valió: es
        // información sobre lo que el certificado acredita, no una vía para preguntarle al
        // catálogo (para eso está `GET /course/{id}`, que es público por su cuenta).
        var detail = await _catalog.GetCourseAsync(certificate.CourseId, cancellationToken);

        return Ok(new VerifyCertificateResponse(
            Valid: true,
            Certificate: new PublicCertificateDto(
                Id: certificate.Id,
                CourseId: certificate.CourseId,
                CourseTitle: detail?.Course.Title,
                StudentName: PublicHolderName(certificate.StudentName),
                IssuedAt: certificate.IssuedAt)));
    }

    /// <summary>
    /// El nombre del titular, o <c>null</c> si lo único disponible es su identificador de
    /// cuenta. Publicar el nombre es el punto de una verificación; publicar el correo del
    /// alumno en un endpoint anónimo no lo es, y es justo lo que pasaría por el fallback
    /// del motor (<c>StudentName = student</c>) para quien nunca se matriculó con nombre.
    /// </summary>
    private static string? PublicHolderName(string? studentName)
        => string.IsNullOrWhiteSpace(studentName) || studentName.Contains('@', StringComparison.Ordinal)
            ? null
            : studentName.Trim();

    // ── 8. Instructor: sus cursos + métricas (panel de autor) ───────────
    // GET /api/academy/instructor/courses?instructor= → { courses:[...] }
    [HttpGet("instructor/courses")]
    public async Task<IActionResult> InstructorCourses(CancellationToken cancellationToken)
    {
        var (denied, instructor) = RequireInstructor();
        if (denied is not null) { return denied; }

        var result = await _catalog.GetForInstructorAsync(instructor, cancellationToken);

        var courses = result.Courses.Select(ic => new InstructorCourseDto(
            // studentCount real desde las métricas del panel (la card lo lee en course.studentCount).
            Course: ToCourseDto(ic.Course) with { StudentCount = ic.Metrics.Students },
            // Y los mismos datos APLANADOS, que es donde la UI los lee. Sin el id en la raíz su
            // normalizador descartaba la fila entera.
            Id: ic.Course.Id,
            Title: ic.Course.Title,
            // Se PASA lo que dice el catálogo; no se afirma aquí. Escribir "published" en
            // esta línea sería el mismo defecto que ya había del otro lado, con la ventaja
            // de que al menos allá era el fallback de un normalizador defensivo (#102).
            Status: ic.Course.Status,
            PublishedAt: ic.Course.PublishedAt,
            Price: ic.Course.Price,
            PriceFormatted: ic.Course.IsFree
                ? "Gratis"
                : _priceFormatter.Format(ic.Course.Price, ic.Course.Currency),
            StudentCount: ic.Metrics.Students,
            Students: ic.Metrics.Students,
            Revenue: ic.Metrics.Revenue,
            RevenueFormatted: _priceFormatter.Format(ic.Metrics.Revenue, ic.Metrics.Currency),
            Rating: ic.Metrics.Rating)).ToList();

        // Y QUIÉNES son esos alumnos, no sólo cuántos (#107). La consola listaba su portafolio y
        // no sabía nombrar a una sola persona: el conteo es un número hasta que se puede
        // contestar «¿quién se quedó en la lección 2?».
        //
        // El título del curso se pone AQUÍ y no en el seam: ya está resuelto en esta misma
        // vuelta, y pedírselo al motor de matrícula lo obligaría a consultar el catálogo por
        // fila para un dato que es de presentación.
        var students = new List<InstructorStudentDto>();
        foreach (var ic in result.Courses)
        {
            foreach (var row in await _metrics.GetCourseRosterAsync(ic.Course.Id, cancellationToken))
            {
                students.Add(new InstructorStudentDto(
                    Id: row.StudentId,
                    Name: row.StudentName,
                    CourseId: ic.Course.Id,
                    CourseTitle: ic.Course.Title,
                    Percent: row.Percent,
                    // Fecha SIN hora, y no es cosmético: la celda pinta el valor CRUDO
                    // (`{{ row.enrolledAt }}`, sin pasar por formatDate como sí hace
                    // `lastActivityAt`), así que un ISO completo sacaría la T y el desfase
                    // horario a la tabla. Es la forma que el mock ya usa.
                    EnrolledAt: row.EnrolledAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }
        }

        return Ok(new InstructorCoursesResponse(
            Instructor: result.InstructorId,
            Courses: courses,
            Students: students,
            TotalStudents: result.TotalStudents,
            TotalRevenue: result.TotalRevenue,
            TotalRevenueFormatted: _priceFormatter.Format(result.TotalRevenue, result.Currency)));
    }

    // ── 9. Publish course (curriculum builder del instructor) ───────────
    // POST /api/academy/course { draft } → { courseId }
    [HttpPost("course")]
    public async Task<IActionResult> PublishCourse(
        [FromBody] CourseDraftRequest? request,
        CancellationToken cancellationToken)
    {
        // Publicar al catálogo del sitio era anónimo.
        var (denied, _) = RequireInstructor();
        if (denied is not null) { return denied; }

        if (request is null || string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { error = "El título del curso es requerido." });
        }
        if (request.Modules is null || request.Modules.Count == 0)
        {
            return BadRequest(new { error = "El curso requiere al menos un módulo." });
        }

        var draft = new CourseDraft(
            Title: request.Title,
            Summary: request.Summary ?? string.Empty,
            Description: request.Description ?? string.Empty,
            School: request.School ?? string.Empty,
            Category: request.Category ?? string.Empty,
            Level: request.Level ?? string.Empty,
            InstructorId: request.Instructor ?? request.InstructorId ?? string.Empty,
            Price: request.Price,
            Modules: request.Modules.Select(m => new CourseDraftModule(
                Title: m.Title ?? string.Empty,
                Lessons: (m.Lessons ?? new List<CourseDraftLessonRequest>()).Select(l => new CourseDraftLesson(
                    Title: l.Title ?? string.Empty,
                    VideoUrl: l.VideoUrl,
                    DurationMinutes: l.Duration,
                    ContentBody: l.ContentBody)).ToList())).ToList(),
            Outcomes: request.Outcomes,
            CoverImageUrl: request.CoverImageUrl);

        CourseDetail published;
        try
        {
            published = await _catalog.PublishCourseAsync(draft, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new PublishCourseResponse(CourseId: published.Course.Id));
    }

    private static string? FirstNonEmpty(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) ? a : b;

    // ── Helpers ────────────────────────────────────────────────────────

    private CourseDto ToCourseDto(CourseSummary c) => new(
        Id: c.Id,
        Title: c.Title,
        Summary: c.Summary,
        // La MISMA cadena bajo la clave que la UI lee. Va duplicada y no renombrada por la
        // convención de este controller (CoverImageUrl/Cover, Total/Amount/Price).
        Subtitle: c.Summary,
        Category: c.Category,
        Level: MapLevel(c.Level),
        InstructorName: c.InstructorName,
        CoverImageUrl: c.CoverImageUrl,
        Cover: c.CoverImageUrl,
        Price: c.Price,
        PriceFormatted: c.IsFree ? "Gratis" : _priceFormatter.Format(c.Price, c.Currency),
        Currency: c.Currency,
        IsFree: c.IsFree,
        Rating: c.Rating,
        LessonCount: c.LessonCount,
        DurationMinutes: c.DurationMinutes,
        // studentCount no viene en el catálogo (CourseSummary); el panel de instructor lo
        // rellena desde métricas vía `with`. En el grid general queda null (dato ausente).
        StudentCount: null,
        Description: null,
        Outcomes: null);

    // Vocabulario de nivel que la UI lee: 'beginner' | 'intermediate' | 'advanced'.
    // La data del catálogo viene en español (Principiante/Intermedio/Avanzado).
    private static string MapLevel(string? level) => (level ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        // Reconoce también lo que ESTE MISMO método emite. Era una bomba de relojería: un
        // catálogo que sirviera "advanced" —que es lo que el schema de `coursePage` le pide al
        // editor— caía al `_` y salía «beginner», o sea todos los cursos en el nivel más bajo
        // sin que nada fallara. Hoy no pasa porque la fuente de contenido normaliza al
        // vocabulario del seed, pero eso es una coincidencia entre dos ficheros, no una regla.
        "avanzado" or "advanced" => "advanced",
        "intermedio" or "intermediate" => "intermediate",
        _ => "beginner",
    };

    private static LessonDto ToLessonDto(CourseLesson l) => new(
        Id: l.Id,
        Title: l.Title,
        Order: l.Order,
        DurationMinutes: l.DurationMinutes,
        VideoRef: l.VideoRef,
        // ContentItemId = id del item Kind=lesson en el IContentStream (polimorfismo
        // Blogs): el course-player resuelve el cuerpo de la lección del MISMO feed.
        ContentItemId: l.ContentItemId,
        Resources: l.Resources.Select(r => new ResourceDto(r.Title, r.Url, r.Kind)).ToList(),
        IsPreview: l.IsPreview,
        // La MISMA bandera bajo la clave que la UI lee: sin ella la vista previa no se
        // ofrecía nunca, que es el gancho comercial de la ficha.
        Preview: l.IsPreview,
        // DERIVADO de lo que hay, no inventado: con video es una clase, sin él es lectura.
        // La UI pinta un icono por tipo y sin el campo pintaba «video» en todas.
        Kind: string.IsNullOrWhiteSpace(l.VideoRef) ? "reading" : "video");

    /// <summary>
    /// La credencial con el título de su curso, o null si no hay credencial.
    /// </summary>
    /// <remarks>
    /// <b>El título se resuelve del CATÁLOGO y no viaja en <see cref="Certificate"/></b>: el id
    /// de la credencial está sellado (ADR 0124, HU #45) y meterle un campo al record que se
    /// sella es tocar lo que se verifica. Es además el camino que ya usaba la verificación
    /// pública, así que hay uno y no dos.
    /// </remarks>
    private async Task<CertificateDto?> ToCertificateDtoAsync(
        Certificate? certificate,
        CancellationToken cancellationToken)
    {
        if (certificate is null)
        {
            return null;
        }

        var detail = await _catalog.GetCourseAsync(certificate.CourseId, cancellationToken);
        return new CertificateDto(
            Id: certificate.Id,
            StudentName: certificate.StudentName,
            // Vacío y no el id: el diploma lo imprime, y un identificador interno donde va el
            // nombre del curso se ve peor que un hueco.
            CourseTitle: detail?.Course.Title ?? string.Empty,
            IssuedAt: certificate.IssuedAt,
            VerifyUrl: certificate.VerifyUrl);
    }

    private static ProgressResponse ToProgressDto(CourseProgress progress, CertificateDto? certificate) => new(
        CourseId: progress.CourseId,
        CompletedLessonIds: progress.CompletedLessonIds,
        Percent: progress.Percent,
        LastLessonId: progress.LastLessonId,
        Completed: progress.Percent >= 100,
        Certificate: certificate);

    // ── Request DTOs (binding de los módulos course-catalog + course-player) ──

    /// <summary>El alumno en el payload de enroll.</summary>
    public sealed record StudentRequest(string Name, string Email);

    /// <summary>POST /api/academy/enroll — curso + alumno.</summary>
    /// <summary>
    /// <c>POST /enroll</c> — curso + alumno + el plan que eligió.
    /// </summary>
    /// <remarks>
    /// <b><c>PlanId</c> faltaba, y era plata</b> (#102): la UI lo manda desde siempre y
    /// System.Text.Json descarta sin decir nada los miembros que no mapea, así que el alumno
    /// elegía un plan y se le cobraba el precio líder del curso. Llega el CÓDIGO del plan y
    /// nunca su monto — el total lo resuelve el motor desde el catálogo.
    /// </remarks>
    public sealed record EnrollRequest(string CourseId, StudentRequest? Student, string? PlanId = null);

    /// <summary>POST /api/academy/confirm — la inscripción a capturar.</summary>
    public sealed record ConfirmEnrollRequest(string OrderRef);

    /// <summary>
    /// POST /api/academy/progress — la lección a marcar completa. El contrato
    /// canónico usa <c>student/course/lesson</c>; se conservan <c>courseId/lessonId</c>
    /// (nullable) para el módulo course-player existente. El controller toma el
    /// primero no vacío de cada par.
    /// </summary>
    public sealed record MarkProgressRequest(
        string Student,
        string? Course = null,
        string? Lesson = null,
        string? CourseId = null,
        string? LessonId = null);

    /// <summary>POST /api/academy/course — borrador que publica el instructor (curriculum builder).</summary>
    public sealed record CourseDraftRequest(
        string Title,
        string? Summary = null,
        string? Description = null,
        string? School = null,
        string? Category = null,
        string? Level = null,
        string? Instructor = null,
        string? InstructorId = null,
        decimal Price = 0m,
        IReadOnlyList<CourseDraftModuleRequest>? Modules = null,
        IReadOnlyList<string>? Outcomes = null,
        string? CoverImageUrl = null);

    /// <summary>Un módulo del borrador del curso (título + lecciones).</summary>
    public sealed record CourseDraftModuleRequest(
        string? Title,
        List<CourseDraftLessonRequest>? Lessons);

    /// <summary>Una lección del borrador (título + video + duración).</summary>
    public sealed record CourseDraftLessonRequest(
        string? Title,
        string? VideoUrl = null,
        int Duration = 0,
        string? ContentBody = null);

    // ── Response DTOs (JSON estable para la UI) ────────────────────────

    /// <summary>
    /// Un curso para la tarjeta y la ficha.
    /// </summary>
    /// <remarks>
    /// <b><c>Subtitle</c> es la clave que la UI lee</b> y <c>Summary</c> se conserva (#102). La
    /// app cae a <c>description</c> si falta, y en el LISTADO `description` va nula, así que la
    /// segunda línea de cada tarjeta salía vacía — se veía, y se veía mal. Van las dos por la
    /// misma convención que `CoverImageUrl`/`Cover` de aquí al lado: renombrar rompería a
    /// cualquier consumidor no descubierto, y el coste de duplicar una cadena es cero.
    /// </remarks>
    public sealed record CourseDto(
        string Id,
        string Title,
        string Summary,
        string Subtitle,
        string Category,
        string Level,
        string InstructorName,
        string? CoverImageUrl,
        string? Cover,
        decimal Price,
        string PriceFormatted,
        string Currency,
        bool IsFree,
        double Rating,
        int LessonCount,
        int DurationMinutes,
        int? StudentCount,
        string? Description,
        IReadOnlyList<string>? Outcomes);

    public sealed record CoursesResponse(IReadOnlyList<CourseDto> Courses, int Total);

    public sealed record ResourceDto(string Title, string Url, string Kind);

    /// <summary>
    /// Una lección del temario.
    /// </summary>
    /// <remarks>
    /// <b><c>Preview</c> es la clave que la UI lee</b> (#102): sin ella ninguna lección era
    /// reproducible sin matricularse, que es el gancho comercial de la ficha.
    ///
    /// <para><b><c>Kind</c> se DERIVA, no se inventa</b>: una lección con video es
    /// <c>video</c> y una sin él es <c>reading</c>. La UI pinta un icono por tipo y sin el
    /// campo pintaba «video» en todas, incluidas las que no lo son. No se emite
    /// <c>allowAssignment</c> —la entrega de tareas que la UI sabe mostrar— porque eso no es
    /// derivable de nada: no existe en <c>CourseLesson</c> ni en el schema, y emitir un
    /// <c>false</c> fijo diría «este curso no tiene tareas» sobre uno que quizá las tenga.
    /// </para>
    ///
    /// <para><b>Y esto es lo que esa decisión APAGA, que es la mitad que faltaba escrita</b>
    /// (#102). No es un campo cosmético: <c>readBoolean(value['allowAssignment'])</c> da
    /// <c>false</c> para toda lección, así que <b>la pestaña «Tarea» del aula no aparece
    /// nunca</b> — no es que se vea vacía, es que no existe. La diferencia con
    /// <c>questions[]</c> —donde el consumidor del otro lado es local y decorativo— es que
    /// aquí hay una funcionalidad entera del reproductor a la que no se puede llegar. Se deja
    /// así igualmente: emitir un <c>false</c> constante apagaría lo mismo y además afirmaría
    /// algo, y un <c>true</c> constante abriría una pestaña sin nada detrás.</para>
    ///
    /// <para><b>Disparador para que deje de estar apagada:</b> que una lección pueda DECLARAR
    /// que pide entrega. Eso es un campo de <c>elementCourseLesson</c> (schema uSync) leído
    /// por <c>UmbracoCourseCatalogSource</c> y un campo en <see cref="CourseLesson"/>; y en
    /// cuanto la entrega exista de verdad, además, alguien tiene que recibirla y calificarla,
    /// que es trabajo de otro seam. Lo que NO hace falta es tocar esta línea antes de que
    /// exista el dato.</para>
    /// </remarks>
    public sealed record LessonDto(
        string Id,
        string Title,
        int Order,
        int DurationMinutes,
        string? VideoRef,
        string ContentItemId,
        IReadOnlyList<ResourceDto> Resources,
        bool IsPreview,
        bool Preview,
        string Kind);

    public sealed record ModuleDto(
        string Id,
        string Title,
        int Order,
        IReadOnlyList<LessonDto> Lessons);

    public sealed record InstructorDto(
        string Id,
        string Name,
        string Headline,
        string Bio,
        string? AvatarUrl,
        string? Avatar);

    /// <summary>
    /// Un plan de pago del curso.
    /// </summary>
    /// <remarks>
    /// <b><c>Id</c> es la clave que la UI lee</b>, y su ausencia no era cosmética (#102): el
    /// normalizador inventaba <c>plan-&lt;random&gt;</c> EN CADA RENDER, así que la selección
    /// del plan era inestable y el <c>planId</c> que la app manda al matricularse era basura —
    /// justo el campo que ahora decide cuánto se cobra.
    ///
    /// <para>No se emiten <c>description</c>, <c>perks[]</c> ni <c>featured</c>, que la UI
    /// también sabe pintar: no existen en <c>CoursePricingPlan</c> ni los produce
    /// <c>CoursePricingRules</c>. Inventarle viñetas a un plan de pago es escribir una oferta
    /// comercial que nadie autoró.</para>
    /// </remarks>
    public sealed record PlanDto(
        string Code,
        string Id,
        string Label,
        decimal Total,
        decimal Amount,
        decimal Price,
        string TotalFormatted,
        string Currency,
        string Installments,
        int InstallmentCount,
        string InstallmentFormatted);

    /// <summary>
    /// La ficha completa del curso.
    /// </summary>
    /// <remarks>
    /// <b><c>Outcomes</c> va también en la RAÍZ</b> (#102): la UI lo lee ahí y el borde sólo lo
    /// emitía dentro de <c>course</c>, así que «Lo que aprenderás» salía vacío siempre. Se
    /// conserva en los dos sitios porque el listado también lleva <c>CourseDto</c>.
    /// </remarks>
    public sealed record CourseDetailResponse(
        CourseDto Course,
        IReadOnlyList<ModuleDto> Modules,
        InstructorDto Instructor,
        IReadOnlyList<PlanDto> Plans,
        IReadOnlyList<string> Outcomes,
        string Description);

    public sealed record EnrolledResponse(bool Enrolled, string? EnrollmentId);

    public sealed record EnrollPaymentResponse(
        string OrderRef,
        string PaymentSessionId,
        decimal Amount,
        string AmountFormatted,
        string Currency);

    public sealed record ConfirmEnrollResponse(string Status, string EnrollmentId, string CourseId);

    /// <summary>
    /// La credencial de quien la obtuvo.
    /// </summary>
    /// <remarks>
    /// <b>Lleva <c>CourseTitle</c> desde #102</b>: la UI lo imprime en el diploma y el DTO
    /// público ya lo emitía, así que el privado era el único que no sabía de qué curso era la
    /// credencial que enseña.
    /// </remarks>
    public sealed record CertificateDto(
        string Id,
        string StudentName,
        string CourseTitle,
        DateTimeOffset IssuedAt,
        string VerifyUrl);

    public sealed record ProgressResponse(
        string CourseId,
        IReadOnlyList<string> CompletedLessonIds,
        int Percent,
        string? LastLessonId,
        bool Completed,
        CertificateDto? Certificate);

    /// <summary>GET /api/academy/certificate — { certificate | null }.</summary>
    public sealed record CertificateResponse(CertificateDto? Certificate);

    /// <summary>
    /// La cara PÚBLICA de la credencial: lo que un tercero sin cuenta puede ver de ella.
    /// Es un DTO aparte de <see cref="CertificateDto"/> a propósito — no una proyección
    /// "casi igual"— para que añadir un campo al certificado privado no lo publique de
    /// paso. <see cref="StudentName"/> es nullable: ver <c>PublicHolderName</c>.
    /// </summary>
    public sealed record PublicCertificateDto(
        string Id,
        string CourseId,
        string? CourseTitle,
        string? StudentName,
        DateTimeOffset IssuedAt);

    /// <summary>
    /// GET /academy/verify/{id} — <c>{ valid, certificate }</c>. Con <c>valid:false</c>
    /// el certificado es SIEMPRE null y no hay campo de motivo: el verificador no debe
    /// poder distinguir "no existe" de "está falsificado".
    /// </summary>
    public sealed record VerifyCertificateResponse(bool Valid, PublicCertificateDto? Certificate);

    /// <summary>Una fila del panel del instructor: el curso + sus métricas (alumnos/ingresos/rating).</summary>
    /// <summary>
    /// Una fila de la consola del instructor: el curso y sus métricas.
    /// </summary>
    /// <remarks>
    /// <b>Los campos del curso van APLANADOS además de anidados</b> (#102), y no era
    /// cosmético: la UI lee <c>id</c> y <c>title</c> en la raíz de cada fila, no encontraba el
    /// id, y su normalizador devolvía <c>null</c> POR CADA FILA — con la lista vacía, la
    /// consola entera caía al mock y encendía el cartel de «datos de ejemplo» aunque el
    /// servidor tuviera los cursos reales.
    ///
    /// <para><b><c>students[]</c> ya se emite</b> (#107): <c>IEnrollmentMetrics</c> aprendió a
    /// listar el curso, no sólo a contarlo. Va en la RAÍZ de la respuesta y no dentro de cada
    /// fila, que es donde la UI la lee — la consola tiene una pestaña «Alumnos» transversal a
    /// todo el portafolio, no una sublista por curso.</para>
    ///
    /// <para><b><c>questions[]</c> NO se emite, y es una decisión, no un pendiente</b> (#107).
    /// Nadie puede escribir una pregunta ni responderla: del otro lado <c>postQuestion()</c>
    /// empuja a un <c>signal</c> local y la acción «Responder» pone un booleano en memoria —
    /// ninguna de las dos toca la red. Emitir la lista sería servir una lectura cuyo único
    /// camino de escritura es un mock, o sea un buzón decorativo. Y el sitio correcto tampoco
    /// sería un seam nuevo: una pregunta sobre una lección es un comentario, y el almacén de
    /// comentarios ya existe (<c>ICommentWriter</c>, ADR 0038 + ADR 0100) con hilo, moderación y
    /// cola; lo que falta ahí es que el sujeto de un <c>Comment</c> deje de ser un
    /// <c>int NodeId</c> —una lección no es un nodo de Umbraco, es un <c>ContentItemId</c> del
    /// <c>IContentStream</c>— y eso toca Blogs. <b>Disparador para revisarlo:</b> el día que una
    /// pregunta salga del navegador.</para>
    ///
    /// <para><b>Y tampoco se emite <c>questions: []</c>.</b> Una lista vacía dice «no hay
    /// preguntas» cuando la verdad es «esto no existe», y congelaría la clave en la línea base
    /// de G-6 como si cruzara. El normalizador del cliente trata igual la clave ausente y la
    /// lista vacía, así que decir la verdad no cuesta nada.</para>
    ///
    /// <para><b><c>status</c> y <c>publishedAt</c> son datos del catálogo, no de este
    /// mapeo</b> (#102). Faltaban las dos, y la que dolía era la primera: el normalizador del
    /// otro lado degrada a <c>published</c> cuando la clave no está, así que la píldora
    /// «Estado» decía «Publicado» de todo y el KPI «Cursos publicados» contaba el total — una
    /// afirmación que nadie había hecho, sostenida por un valor por defecto. Vienen de
    /// <see cref="CourseSummary.Status"/> / <see cref="CourseSummary.PublishedAt"/>, que es
    /// donde cada fuente dice lo que sabe.</para>
    ///
    /// <para><b><c>publishedAt</c> puede venir nulo</b>, y eso significa «no consta» — los
    /// cursos que ya estaban en el overlay durable antes de que el campo existiera. La UI lo
    /// lee (<c>normalizeInstructorCourse</c>) y hoy no lo pinta en ninguna columna; se emite
    /// igual porque es la clave que declara su modelo y porque es cierto. Lo que de verdad lo
    /// consume está de este lado: <c>sort=newest</c>.</para>
    /// </remarks>
    public sealed record InstructorCourseDto(
        CourseDto Course,
        string Id,
        string Title,
        string Status,
        DateOnly? PublishedAt,
        decimal Price,
        string PriceFormatted,
        int StudentCount,
        int Students,
        decimal Revenue,
        string RevenueFormatted,
        double Rating);

    /// <summary>
    /// Un alumno matriculado, tal como lo lista la consola del instructor.
    /// </summary>
    /// <remarks>
    /// <para><b>No lleva el correo</b> (#107), y el seam que lo produce
    /// (<see cref="CourseRosterEntry"/>) tampoco lo tiene, así que no es un olvido del mapeo.
    /// La consola pinta cuatro columnas —Alumno / Curso / Progreso / Inscrito— y no tiene una
    /// sola acción que use una dirección: no hay contactar, ni exportar, ni escribir. Lo que se
    /// estaría emitiendo a cambio de una segunda línea de texto es la lista de correos de la
    /// escuela entera en un <c>GET</c>. Es la decisión de #47 (el comprador viaja seudonimizado)
    /// y la de <c>GovController</c> con <c>OpenedBy</c> («no tiene por qué salir del servidor»),
    /// aplicada acá.</para>
    ///
    /// <para><b><see cref="Id"/> es ese seudónimo</b> —SHA-256 del correo normalizado, el mismo
    /// que Tienda, Eventos y la visita al inmueble— y <b>no se pinta</b>: no es una de las
    /// columnas. Ésa es la otra mitad de #47, donde el identificador opaco del comprador acabó
    /// en pantalla; acá se evita antes de que pase. Hace falta igual, porque el normalizador
    /// descarta la fila sin <c>id</c> y porque dos personas se llaman igual.</para>
    ///
    /// <para><b>Consecuencia declarada:</b> la sub-línea que la celda «Alumno» pinta bajo el
    /// nombre queda vacía. Es lo correcto — el dato no está — y lo que sobra es el hueco, que se
    /// limpia del lado de la UI.</para>
    /// </remarks>
    public sealed record InstructorStudentDto(
        string Id,
        string Name,
        string CourseId,
        string CourseTitle,
        int Percent,
        string EnrolledAt);

    /// <summary>
    /// GET /api/academy/instructor/courses — { courses, students } + totales del panel.
    /// </summary>
    /// <remarks>
    /// <b><c>students</c> va en la raíz</b>: el normalizador lo lee ahí, y con las tres listas
    /// vacías (<c>courses</c>, <c>students</c>, <c>questions</c>) devuelve <c>null</c> y la
    /// consola entera cae al mock con el cartel de «datos de ejemplo» encendido.
    /// </remarks>
    public sealed record InstructorCoursesResponse(
        string Instructor,
        IReadOnlyList<InstructorCourseDto> Courses,
        IReadOnlyList<InstructorStudentDto> Students,
        int TotalStudents,
        decimal TotalRevenue,
        string TotalRevenueFormatted);

    /// <summary>Una fila de «mi aprendizaje»: el curso y por dónde va el alumno.</summary>
    public sealed record EnrolledCourseDto(
        string EnrollmentId,
        CourseDto Course,
        int Percent,
        int LessonCount,
        int CompletedCount,
        DateTimeOffset LastActivityAt,
        bool Completed);

    /// <summary>
    /// Una ruta de aprendizaje: varios cursos con un hilo. <b>Hoy no se emite ninguna</b> — el
    /// tipo existe para que la forma de la respuesta sea estable el día que haya de dónde
    /// sacarlas.
    /// </summary>
    public sealed record LearningPathDto(
        string Id,
        string Title,
        string Description,
        IReadOnlyList<string> CourseIds,
        int Percent);

    /// <summary>GET /api/academy/learning — { enrollments, paths }.</summary>
    public sealed record LearningResponse(
        IReadOnlyList<EnrolledCourseDto> Enrollments,
        IReadOnlyList<LearningPathDto> Paths);

    /// <summary>POST /api/academy/course — { courseId } del curso publicado.</summary>
    public sealed record PublishCourseResponse(string CourseId);
}
