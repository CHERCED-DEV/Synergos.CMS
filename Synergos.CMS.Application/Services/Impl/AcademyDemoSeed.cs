using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Semilla compartida de la demo del LMS (dominio Educación — OLA 4). Centraliza
/// los cursos, instructores, módulos y lecciones sembrados para que los stubs
/// (<see cref="StubCourseCatalogProvider"/>, <see cref="StubEnrollmentService"/>)
/// rindan un catálogo COHERENTE end-to-end (catálogo → curso → aula → progreso →
/// certificado). Datos puros/deterministas (ADR 0002) — el adapter real reemplaza
/// cada seam por su store sin tocar esta semilla.
/// </summary>
/// <remarks>
/// <b>Polimorfismo Blogs.</b> Cada <see cref="SeedLesson"/> declara un
/// <see cref="SeedLesson.ContentBody"/>: el contenido editorial (cuerpo/
/// transcripción) que el <see cref="StubCourseCatalogProvider"/> SIEMBRA en el
/// <see cref="IContentStream"/> como item con <c>Kind=lesson</c>, y luego
/// referencia por su id (<see cref="CourseLesson.ContentItemId"/>). El catálogo
/// NO duplica ese cuerpo — sale del mismo motor de feed que usa Blogs (post),
/// vía la abstracción, sin instanciar Blogs ni copiar su schema.
/// </remarks>
internal static class AcademyDemoSeed
{
    public const string Currency = "COP";

    /// <summary>Instructores sembrados (forma autor de Blogs: avatar + bio).</summary>
    public static readonly IReadOnlyList<CourseInstructor> Instructors = new[]
    {
        new CourseInstructor(
            Id: "ins-elena",
            Name: "Elena Restrepo",
            Headline: "Arquitecta de software · 15 años diseñando sistemas",
            Bio: "Lidera plataformas a escala y enseña arquitectura limpia. Le obsesiona el acoplamiento bajo y el código que se puede borrar sin miedo.",
            AvatarUrl: "/media/academy/instructors/elena.webp"),
        new CourseInstructor(
            Id: "ins-mateo",
            Name: "Mateo Gil",
            Headline: "Diseñador de producto · Design systems",
            Bio: "Construye design systems atómicos para equipos de producto. Tipografía, ritmo vertical y tokens semánticos.",
            AvatarUrl: "/media/academy/instructors/mateo.webp"),
        new CourseInstructor(
            Id: "ins-sofia",
            Name: "Sofía Camargo",
            Headline: "Data & ML · Cuenta historias con datos",
            Bio: "Científica de datos. Enseña ML aplicado sin humo: del dato sucio al modelo en producción.",
            AvatarUrl: "/media/academy/instructors/sofia.webp"),
    };

    /// <summary>
    /// Cursos sembrados: 4 cursos en 3 categorías (Desarrollo / Diseño / Datos),
    /// con niveles, precios (incluye uno gratis), instructor, módulos y lecciones.
    /// </summary>
    public static readonly IReadOnlyList<SeedCourse> Courses = new[]
    {
        new SeedCourse(
            Id: "dev-clean-architecture",
            Title: "Arquitectura Limpia en .NET",
            Summary: "Diseña sistemas mantenibles con Clean Architecture, SOLID y seams testeables.",
            Description: "Un recorrido práctico por la arquitectura limpia aplicada a aplicaciones .NET reales: separación de capas, inversión de dependencias, seams stub-first y pruebas. Termina con un servicio modular listo para producción.",
            Category: "Desarrollo",
            Level: "Intermedio",
            InstructorId: "ins-elena",
            CoverImageUrl: "/media/academy/courses/clean-architecture.jpg",
            Price: 320_000m,
            Rating: 4.8,
            Outcomes: new[]
            {
                "Separar dominio, aplicación e infraestructura sin acoplar capas.",
                "Aplicar SOLID a casos reales, no de juguete.",
                "Diseñar seams stub-first y cubrirlos con pruebas.",
            },
            Modules: new[]
            {
                new SeedModule("mod-ca-1", "Fundamentos", 1, new[]
                {
                    new SeedLesson("les-ca-1-1", "¿Por qué arquitectura limpia?", 1, 9, "https://videos.synergos.co/ca/intro", true,
                        "La arquitectura limpia no es dogma: es disciplina para que el dominio no dependa de detalles. Vemos el grafo de dependencias unidireccional y por qué importa.",
                        Array.Empty<CourseResource>()),
                    new SeedLesson("les-ca-1-2", "El grafo de dependencias", 2, 14, "https://videos.synergos.co/ca/deps", false,
                        "Interfaces ← Application ← Web. Cómo leer un grafo de dependencias y detectar fugas de capa antes de que se vuelvan deuda técnica.",
                        new[] { new CourseResource("Diagrama de capas (PDF)", "/media/academy/resources/ca-layers.pdf", "pdf") }),
                }),
                new SeedModule("mod-ca-2", "Seams y pruebas", 2, new[]
                {
                    new SeedLesson("les-ca-2-1", "Stub-first: define el seam", 1, 18, "https://videos.synergos.co/ca/stub-first", false,
                        "El patrón stub-first: defines el seam, escribes el stub determinista y el equipo construye encima desde el día 1. Cero bloqueos.",
                        new[] { new CourseResource("Plantilla de seam (zip)", "/media/academy/resources/ca-seam-template.zip", "zip") }),
                    new SeedLesson("les-ca-2-2", "Los 4 casos canónicos de prueba", 2, 16, "https://videos.synergos.co/ca/tests", false,
                        "Empty, happy, filter, idempotent: la matriz mínima de pruebas que todo seam debería cubrir. Ejemplos en xUnit.",
                        Array.Empty<CourseResource>()),
                }),
            }),

        new SeedCourse(
            Id: "design-systems-atomic",
            Title: "Design Systems con Atomic Design",
            Summary: "Construye un design system escalable: tokens, átomos, moléculas y organismos.",
            Description: "De los tokens semánticos a los organismos: cómo modelar un design system que sobreviva a un rebrand entero cambiando un solo archivo. Incluye la grilla de 8 puntos y el ritmo vertical premium.",
            Category: "Diseño",
            Level: "Principiante",
            InstructorId: "ins-mateo",
            CoverImageUrl: "/media/academy/courses/design-systems.jpg",
            Price: 240_000m,
            Rating: 4.6,
            Outcomes: new[]
            {
                "Modelar tokens semánticos que aguanten un rebrand completo.",
                "Aplicar la grilla de 8pt para un ritmo vertical consistente.",
                "Componer átomos → moléculas → organismos reutilizables.",
            },
            Modules: new[]
            {
                new SeedModule("mod-ds-1", "Tokens y fundamentos", 1, new[]
                {
                    new SeedLesson("les-ds-1-1", "Tokens semánticos vs literales", 1, 11, "https://videos.synergos.co/ds/tokens", true,
                        "Un token literal (#4f6ef7) te ata a un color; uno semántico (brand-500) te deja cambiar el brand entero en un archivo. Por qué la indirección paga.",
                        Array.Empty<CourseResource>()),
                    new SeedLesson("les-ds-1-2", "La grilla de 8 puntos", 2, 13, "https://videos.synergos.co/ds/grid", false,
                        "La grilla de 8pt no es dogma, es disciplina. El ritmo vertical consistente separa un UI 'ok' de uno que se siente premium.",
                        new[] { new CourseResource("Cheat-sheet de espaciados", "/media/academy/resources/ds-spacing.pdf", "pdf") }),
                }),
                new SeedModule("mod-ds-2", "Componiendo el sistema", 2, new[]
                {
                    new SeedLesson("les-ds-2-1", "Átomos, moléculas, organismos", 1, 17, "https://videos.synergos.co/ds/atomic", false,
                        "Atomic design en la práctica: del botón (átomo) a la card (molécula) al header (organismo). Cómo trazar la frontera de cada nivel.",
                        Array.Empty<CourseResource>()),
                }),
            }),

        new SeedCourse(
            Id: "data-ml-applied",
            Title: "Machine Learning Aplicado",
            Summary: "Del dato sucio al modelo en producción, sin humo.",
            Description: "ML real: el 80% es limpiar datos. Recorremos el pipeline completo — exploración, features, entrenamiento, evaluación honesta y despliegue — con foco en lo que de verdad mueve la aguja.",
            Category: "Datos",
            Level: "Avanzado",
            InstructorId: "ins-sofia",
            CoverImageUrl: "/media/academy/courses/ml-applied.jpg",
            Price: 480_000m,
            Rating: 4.9,
            Outcomes: new[]
            {
                "Construir un pipeline de datos reproducible.",
                "Evaluar modelos con percentiles, no solo promedios.",
                "Desplegar un modelo y monitorear su deriva.",
            },
            Modules: new[]
            {
                new SeedModule("mod-ml-1", "Datos antes que modelos", 1, new[]
                {
                    new SeedLesson("les-ml-1-1", "El 80% es limpiar datos", 1, 15, "https://videos.synergos.co/ml/data", true,
                        "Antes del primer modelo: valores faltantes, fugas de datos y el sesgo de supervivencia. Por qué tus dashboards mienten cuando promedian sin percentiles.",
                        new[] { new CourseResource("Dataset de ejemplo (csv)", "/media/academy/resources/ml-dataset.csv", "csv") }),
                    new SeedLesson("les-ml-1-2", "Feature engineering", 2, 20, "https://videos.synergos.co/ml/features", false,
                        "Las features ganan a los modelos. Técnicas de encoding, escalado y creación de variables que de verdad aportan señal.",
                        Array.Empty<CourseResource>()),
                }),
                new SeedModule("mod-ml-2", "Entrenar y desplegar", 2, new[]
                {
                    new SeedLesson("les-ml-2-1", "Evaluación honesta", 1, 18, "https://videos.synergos.co/ml/eval", false,
                        "Métricas que no mienten: precisión/recall por clase, matriz de confusión y por qué un solo número nunca cuenta toda la historia.",
                        Array.Empty<CourseResource>()),
                    new SeedLesson("les-ml-2-2", "De notebook a producción", 2, 22, "https://videos.synergos.co/ml/deploy", false,
                        "Empaquetar el modelo, exponerlo tras un seam y monitorear la deriva. El notebook es el principio, no el fin.",
                        new[] { new CourseResource("Checklist de despliegue", "/media/academy/resources/ml-deploy-checklist.pdf", "pdf") }),
                }),
            }),

        new SeedCourse(
            Id: "dev-git-fundamentals",
            Title: "Git desde Cero",
            Summary: "Domina Git: commits atómicos, ramas y resolución de conflictos. Curso gratuito.",
            Description: "Un curso introductorio y GRATIS para quien empieza con control de versiones. Del primer commit al flujo de ramas, con énfasis en commits atómicos y legibles.",
            Category: "Desarrollo",
            Level: "Principiante",
            InstructorId: "ins-elena",
            CoverImageUrl: "/media/academy/courses/git-fundamentals.jpg",
            Price: 0m,
            Rating: 4.5,
            Outcomes: new[]
            {
                "Hacer commits atómicos con mensajes claros.",
                "Trabajar con ramas y fusiones sin miedo.",
                "Resolver conflictos de merge con calma.",
            },
            Modules: new[]
            {
                new SeedModule("mod-git-1", "Primeros pasos", 1, new[]
                {
                    new SeedLesson("les-git-1-1", "Qué es el control de versiones", 1, 8, "https://videos.synergos.co/git/intro", true,
                        "Por qué Git cambió cómo trabajamos. El modelo mental de snapshots, no de diffs.",
                        Array.Empty<CourseResource>()),
                    new SeedLesson("les-git-1-2", "Tu primer commit", 2, 10, "https://videos.synergos.co/git/commit", true,
                        "add, commit, status: el ciclo básico. Qué hace que un mensaje de commit sea bueno y por qué los commits atómicos te salvan.",
                        Array.Empty<CourseResource>()),
                }),
                new SeedModule("mod-git-2", "Ramas", 2, new[]
                {
                    new SeedLesson("les-git-2-1", "Crear y fusionar ramas", 1, 12, "https://videos.synergos.co/git/branches", false,
                        "branch, checkout, merge: el flujo de trabajo con ramas. Cuándo ramificar y cómo mantener la historia legible.",
                        Array.Empty<CourseResource>()),
                }),
            }),
    };

    /// <summary>Resuelve un instructor sembrado por id, o uno "desconocido" estable.</summary>
    public static CourseInstructor InstructorById(string id)
        => Instructors.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase))
           ?? new CourseInstructor(id, id, string.Empty, string.Empty, null);

    // ── Forma interna de la semilla (no expuesta) ──────────────────────

    public sealed record SeedCourse(
        string Id,
        string Title,
        string Summary,
        string Description,
        string Category,
        string Level,
        string InstructorId,
        string? CoverImageUrl,
        decimal Price,
        double Rating,
        IReadOnlyList<string> Outcomes,
        IReadOnlyList<SeedModule> Modules)
    {
        public bool IsFree => Price <= 0m;

        public int LessonCount => Modules.Sum(m => m.Lessons.Count);

        public int DurationMinutes => Modules.Sum(m => m.Lessons.Sum(l => l.DurationMinutes));
    }

    public sealed record SeedModule(
        string Id,
        string Title,
        int Order,
        IReadOnlyList<SeedLesson> Lessons);

    public sealed record SeedLesson(
        string Id,
        string Title,
        int Order,
        int DurationMinutes,
        string? VideoRef,
        bool IsPreview,
        string ContentBody,
        IReadOnlyList<CourseResource> Resources);
}
