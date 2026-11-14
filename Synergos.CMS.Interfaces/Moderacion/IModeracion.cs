namespace Synergos.CMS.Interfaces.Moderacion;

/// <summary>
/// La moderación de lo que se publica (012-blogs-social, I5, P8 A): cualquiera con sesión denuncia, y la cola decide
/// mantener o retirar, con quién decidió y por qué. Es genérica: lo que se denuncia es un <see cref="ObjetivoModerable"/>
/// de cualquier vertical (una publicación de la red, un comentario; mañana una reseña de la Tienda o de Academia).
/// </summary>
/// <remarks>
/// <para>Se publica al instante y se modera después: es como funcionaba la red y como funcionan las redes.</para>
///
/// <para><b>Ejecutar la decisión es del CMS</b>, no de quien guarda los casos: retirar oculta el objetivo con el
/// <see cref="ITipoModerable"/> de su tipo, y es idempotente para poder reintentarlo sin saga.</para>
/// </remarks>
public interface IModeracion
{
    /// <summary>
    /// Denuncia el objetivo de <paramref name="denuncia"/>. Si ya tiene un caso abierto, la denuncia se suma a ése: un
    /// caso por objetivo en la cola. La misma persona dos veces es la misma denuncia.
    /// </summary>
    Task<CasoDeModeracion> DenunciarAsync(DenunciaNueva denuncia, CancellationToken cancellationToken = default);

    /// <summary>Los casos abiertos, del más antiguo al más reciente: la cola.</summary>
    Task<IReadOnlyList<CasoDeModeracion>> AbiertosAsync(int cuantos, CancellationToken cancellationToken = default);

    /// <summary>El caso, o <c>null</c> si no existe.</summary>
    Task<CasoDeModeracion?> CasoAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decide el caso. Decidir lo mismo otra vez no cambia nada y deja reintentar la ejecución; decidir lo contrario de
    /// lo decidido es un conflicto visible, no el último que gana.
    /// </summary>
    Task<DecisionTomada> DecidirAsync(
        string id,
        DecisionDeModeracion decision,
        QuienModera quien,
        string porQue,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Quien decide un caso: su llave pública, que es la traza (nunca su correo), y su nombre público, que es lo que se
/// muestra (012-blogs-social, I5).
/// </summary>
public sealed record QuienModera(string Llave, string Nombre);

/// <summary>
/// La moderación no contestó: <c>Api.Moderation</c> caída, o encendida sin dirección. Lo que se pidió no quedó, y no
/// se finge que quedó (012-blogs-social, I5).
/// </summary>
public sealed class ModeracionNoDisponibleException : Exception
{
    /// <summary>Sin detalle.</summary>
    public ModeracionNoDisponibleException()
    {
    }

    /// <summary>Con lo que pasó.</summary>
    public ModeracionNoDisponibleException(string message)
        : base(message)
    {
    }

    /// <summary>Con lo que pasó y lo que lo causó.</summary>
    public ModeracionNoDisponibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Lo que se puede denunciar: su tipo (<c>blogs.publicacion</c>, <c>blogs.comentario</c>…) y su id.</summary>
public sealed record ObjetivoModerable(string Tipo, string Id);

/// <summary>
/// Una denuncia. <paramref name="Resumen"/> es lo que la cola muestra de lo denunciado, leído al denunciar por su
/// <see cref="ITipoModerable"/>; <paramref name="Denunciante"/> es la llave pública de quien denuncia: sirve para que
/// la misma persona no cuente dos veces, y no se publica.
/// </summary>
public sealed record DenunciaNueva(
    ObjetivoModerable Objetivo,
    string Resumen,
    string? Enlace,
    string Denunciante,
    string Motivo);

/// <summary>Mantener o retirar.</summary>
public enum DecisionDeModeracion
{
    /// <summary>Se queda como está.</summary>
    Mantener = 0,

    /// <summary>Se retira: desaparece para todos.</summary>
    Retirar = 1,
}

/// <summary>
/// Un caso de la cola: qué se denunció, por qué, cuántas personas, y cómo se resolvió. <paramref name="Quien"/> es el
/// nombre público de quien decidió.
/// </summary>
public sealed record CasoDeModeracion(
    string Id,
    ObjetivoModerable Objetivo,
    string Resumen,
    string? Enlace,
    string Motivo,
    int Denuncias,
    DateTimeOffset Denunciado,
    DecisionDeModeracion? Decision,
    string? Quien,
    string? PorQue,
    DateTimeOffset? Decidido)
{
    /// <summary>Si sigue en la cola.</summary>
    public bool Abierto => Decision is null;
}

/// <summary>Cómo terminó decidir un caso.</summary>
public enum ResultadoDeDecidir
{
    /// <summary>Quedó decidido (o ya lo estaba igual).</summary>
    Decidido = 0,

    /// <summary>El caso no existe.</summary>
    NoExiste = 1,

    /// <summary>Ya estaba decidido al revés.</summary>
    YaDecididoDistinto = 2,
}

/// <summary>Lo que dejó decidir: el resultado y el caso tal como quedó.</summary>
public sealed record DecisionTomada(ResultadoDeDecidir Resultado, CasoDeModeracion? Caso);

/// <summary>
/// Lo que un vertical sabe de un tipo suyo que se puede denunciar (012-blogs-social, I5): si existe y si quien denuncia
/// puede verlo, cómo se resume en la cola, y cómo se retira.
/// </summary>
public interface ITipoModerable
{
    /// <summary>El tipo, como lo manda quien denuncia: <c>blogs.publicacion</c>.</summary>
    string Tipo { get; }

    /// <summary>
    /// Lo denunciable con ese id, o <c>null</c> si no existe o quien denuncia no lo ve: no se denuncia lo que no se ve.
    /// </summary>
    Task<ContenidoDenunciable?> LeerAsync(string id, string quienDenuncia, CancellationToken cancellationToken = default);

    /// <summary>Lo retira para todos. Idempotente: retirar lo ya retirado no falla.</summary>
    Task RetirarAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>Lo que la cola muestra de lo denunciado: un resumen y, si tiene página, su enlace.</summary>
public sealed record ContenidoDenunciable(string Resumen, string? Enlace);

/// <summary>
/// Lo retirado por moderación que no tiene dónde marcarse en su propio almacén (las publicaciones de la red): un
/// registro aparte que leen quienes las sirven.
/// </summary>
public interface IRegistroDeRetirados
{
    /// <summary>Marca <paramref name="objetivo"/> como retirado. Idempotente.</summary>
    Task RetirarAsync(ObjetivoModerable objetivo, CancellationToken cancellationToken = default);

    /// <summary>Los ids retirados de ese tipo.</summary>
    Task<IReadOnlySet<string>> RetiradosAsync(string tipo, CancellationToken cancellationToken = default);
}
