using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// La semilla de la red social (<see cref="SocialDemoSeed"/>) como lo que es: datos de DESARROLLO, encendidos con
/// <c>Synergos:DevSeed:Enabled</c> (ADR 0013). Apagada, la red sirve sólo lo real: lo que el editor autoró en el CMS y
/// lo que publican los miembros.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe</b> (012-blogs-social, I0, P2 A). La semilla no se escribía al arrancar, pero se servía
/// SIEMPRE: «un documento ausente cae al seed» valía también en producción, así que el inicio de la red mostraba 8
/// publicaciones de 4 personas que no existen, con sus seguidores y sus reacciones.</para>
///
/// <para><b>Una sola pieza decide</b>, y cada stub la recibe: el stream, el grafo, las reacciones, los perfiles y las
/// notificaciones. Repartir la pregunta por cinco clases es dejar que una se olvide.</para>
///
/// <para>Los constructores de siempre de los stubs la reciben <see cref="Encendida"/>: es lo que hacían, y lo que
/// esperan las pruebas que los usan. El composer la pasa según DevSeed.</para>
/// </remarks>
public sealed class SemillaDeLaRed
{
    private static readonly IReadOnlyDictionary<string, string[]> SinSeguidos =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SinReacciones =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

    /// <summary>La semilla servida: la demo de siempre.</summary>
    public static readonly SemillaDeLaRed Encendida = new(true);

    /// <summary>La semilla sin servir: la red sólo con lo real.</summary>
    public static readonly SemillaDeLaRed Apagada = new(false);

    private SemillaDeLaRed(bool encendida) => EstaEncendida = encendida;

    /// <summary>Si se sirve la demo.</summary>
    public bool EstaEncendida { get; }

    /// <summary>La semilla que toca según <c>Synergos:DevSeed:Enabled</c>.</summary>
    public static SemillaDeLaRed Segun(bool devSeedEncendido) => devSeedEncendido ? Encendida : Apagada;

    internal IReadOnlyList<SocialDemoSeed.SeedPost> Posts
        => EstaEncendida ? SocialDemoSeed.Posts : [];

    internal IReadOnlyDictionary<string, string[]> Follows
        => EstaEncendida ? SocialDemoSeed.Follows : SinSeguidos;

    internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Reactions
        => EstaEncendida ? SocialDemoSeed.Reactions : SinReacciones;

    internal IReadOnlyList<SocialProfile> Profiles
        => EstaEncendida ? SocialDemoSeed.Profiles : [];

    /// <summary>
    /// El autor por su id: el perfil sembrado, si la semilla está encendida y lo conoce; si no, el autor «desconocido»
    /// de siempre, con el id en sus tres campos.
    /// </summary>
    /// <remarks>
    /// Ese desconocido es lo que guarda el stream al crear una publicación, y por eso el id va en los tres campos: el
    /// stream lo compara por <see cref="ContentAuthor.Id"/>. Que no salga así en público lo asegura quien arma la
    /// respuesta, con <see cref="IIdentidadPublica"/>.
    /// </remarks>
    internal ContentAuthor AuthorById(string actorId)
        => EstaEncendida ? SocialDemoSeed.AuthorById(actorId) : new ContentAuthor(actorId, actorId, actorId, null, false);
}
