using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Semilla compartida de la demo de la red social (dominio Blogs — OLA 3). Centraliza
/// los actores, posts, follows y reacciones sembrados para que los stubs
/// (<see cref="StubContentStream"/>, <see cref="StubSocialGraphService"/>,
/// <see cref="StubReactionService"/>, <see cref="StubSocialProfileProjection"/>)
/// rindan un feed COHERENTE: el mismo autor aparece en su perfil, su feed, sus
/// seguidores y sus reacciones. Datos puros/deterministas (ADR 0002) — el adapter
/// real reemplaza cada seam por su store sin tocar esta semilla.
/// </summary>
internal static class SocialDemoSeed
{
    /// <summary>Marca temporal base de la demo (determinista, no <c>DateTime.Now</c>).</summary>
    public static readonly DateTime Epoch = new(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>Perfiles sociales sembrados (actores de la demo).</summary>
    public static readonly IReadOnlyList<SocialProfile> Profiles = new[]
    {
        new SocialProfile(
            ActorId: "act-elena",
            Handle: "elena.dev",
            DisplayName: "Elena Restrepo",
            Bio: "Arquitecta de software · me obsesiona el diseño de sistemas y el café bien hecho.",
            AvatarUrl: "/media/blogs/avatars/elena.webp",
            BannerUrl: "/media/blogs/banners/elena.webp",
            Verified: true,
            JoinedUtc: new DateTime(2025, 1, 14, 0, 0, 0, DateTimeKind.Utc)),
        new SocialProfile(
            ActorId: "act-mateo",
            Handle: "mateo.design",
            DisplayName: "Mateo Gil",
            Bio: "Diseñador de producto. Atomic design, design systems y tipografía.",
            AvatarUrl: "/media/blogs/avatars/mateo.webp",
            BannerUrl: "/media/blogs/banners/mateo.webp",
            Verified: false,
            JoinedUtc: new DateTime(2025, 3, 2, 0, 0, 0, DateTimeKind.Utc)),
        new SocialProfile(
            ActorId: "act-sofia",
            Handle: "sofia.data",
            DisplayName: "Sofía Camargo",
            Bio: "Data & ML. Cuento historias con datos. Bogotá ↔ remoto.",
            AvatarUrl: "/media/blogs/avatars/sofia.webp",
            BannerUrl: "/media/blogs/banners/sofia.webp",
            Verified: true,
            JoinedUtc: new DateTime(2025, 2, 20, 0, 0, 0, DateTimeKind.Utc)),
        new SocialProfile(
            ActorId: "act-andres",
            Handle: "andres.cloud",
            DisplayName: "Andrés Quintero",
            Bio: "Infra y plataforma. Kubernetes, observabilidad y costos.",
            AvatarUrl: "/media/blogs/avatars/andres.webp",
            BannerUrl: null,
            Verified: false,
            JoinedUtc: new DateTime(2025, 4, 11, 0, 0, 0, DateTimeKind.Utc)),
    };

    /// <summary>Proyección liviana del perfil a <see cref="ContentAuthor"/> para el feed.</summary>
    public static ContentAuthor ToAuthor(SocialProfile p) => new(
        Id: p.ActorId,
        Handle: p.Handle,
        DisplayName: p.DisplayName,
        AvatarUrl: p.AvatarUrl,
        Verified: p.Verified);

    /// <summary>Resuelve el autor sembrado por id, o un autor "desconocido" estable.</summary>
    public static ContentAuthor AuthorById(string actorId)
    {
        var profile = Profiles.FirstOrDefault(p =>
            string.Equals(p.ActorId, actorId, StringComparison.OrdinalIgnoreCase));
        return profile is not null
            ? ToAuthor(profile)
            : new ContentAuthor(actorId, actorId, actorId, null, false);
    }

    /// <summary>
    /// Posts sembrados (más reciente primero). Cada uno con autor, cuerpo, kind y
    /// un offset en minutos respecto al <see cref="Epoch"/> para un orden estable.
    /// </summary>
    public static readonly IReadOnlyList<SeedPost> Posts = new[]
    {
        new SeedPost("post-001", "act-elena", "post", 0,
            "Tres años después sigo convencido: la mejor arquitectura es la que te deja borrar código sin miedo. Acoplamiento bajo > cleverness.",
            "/media/blogs/posts/clean-arch.webp"),
        new SeedPost("post-002", "act-mateo", "post", -35,
            "Acabo de migrar todo el design system a tokens semánticos. Cambiar el brand entero es ahora un solo archivo. Atomic design pagando dividendos.",
            null),
        new SeedPost("post-003", "act-sofia", "article", -90,
            "Escribí sobre por qué tus dashboards mienten: agregaciones que esconden la cola, promedios sin percentiles y el sesgo de supervivencia. Hilo 🧵",
            "/media/blogs/posts/dashboards-lie.webp"),
        new SeedPost("post-004", "act-andres", "post", -150,
            "PSA: revisar el costo de egress ANTES de elegir multi-región. Nos ahorramos un susto este mes mirando los data-transfer charges.",
            null),
        new SeedPost("post-005", "act-elena", "post", -240,
            "El patrón stub-first es subestimado. Defines el seam, escribes el stub determinista, y el resto del equipo construye encima desde el día 1. Cero bloqueos.",
            null),
        new SeedPost("post-006", "act-mateo", "post", -310,
            "Recordatorio de que la grilla de 8pt no es dogma, es disciplina. El ritmo vertical consistente es lo que separa un UI 'ok' de uno que se siente premium.",
            "/media/blogs/posts/8pt-grid.webp"),
        new SeedPost("post-007", "act-sofia", "post", -420,
            "Pasé el finde reproduciendo un paper de embeddings. El 80% del tiempo fue limpiar datos. El otro 20% también. Bienvenidos a ML real.",
            null),
        new SeedPost("post-008", "act-andres", "article", -540,
            "Guía: cómo instrumentar trazas distribuidas sin volverte loco. OpenTelemetry, sampling con cabeza, y por qué el contexto de propagación lo es todo.",
            "/media/blogs/posts/otel-tracing.webp"),
    };

    /// <summary>
    /// Grafo sembrado: <c>follower → [followees]</c>. Asimétrico (no recíproco).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Follows =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["act-elena"] = new[] { "act-mateo", "act-sofia" },
            ["act-mateo"] = new[] { "act-elena" },
            ["act-sofia"] = new[] { "act-elena", "act-mateo", "act-andres" },
            ["act-andres"] = new[] { "act-elena", "act-sofia" },
        };

    /// <summary>
    /// Reacciones sembradas: <c>objectKey → (actorId → tipo)</c>. Da contadores
    /// realistas y un estado-por-usuario inicial para el optimistic UI.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Reactions =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["post-001"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["act-mateo"] = "like",
                ["act-sofia"] = "love",
                ["act-andres"] = "like",
            },
            ["post-002"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["act-elena"] = "celebrate",
                ["act-sofia"] = "like",
            },
            ["post-003"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["act-elena"] = "love",
                ["act-mateo"] = "like",
                ["act-andres"] = "like",
            },
        };

    /// <summary>Post sembrado (forma interna de la semilla).</summary>
    public sealed record SeedPost(
        string Id,
        string AuthorId,
        string Kind,
        int OffsetMinutes,
        string Body,
        string? MediaUrl);
}
