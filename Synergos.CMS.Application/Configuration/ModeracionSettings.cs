namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Sección <c>Synergos:Moderation</c>: dónde guarda el CMS las denuncias y sus decisiones (012-blogs-social, I5).
/// </summary>
/// <remarks>
/// <para>Con <c>Mode = Api</c> los casos viven en <c>Api.Moderation</c>: denunciar, la cola y decidir pasan por la
/// capacidad, y retirar lo sigue haciendo el CMS. Sin <c>BaseUrl</c> no se adivina un puerto: la moderación no
/// contesta, que es fallar cerrado — denunciar dice que no se pudo, y la cola no se pinta vacía.</para>
///
/// <para>Con <c>Local</c> (el valor por defecto) los casos viven en el almacén común del CMS: es la vuelta atrás y el
/// desarrollo sin la capacidad. Producción la enciende al desplegar.</para>
/// </remarks>
public sealed class ModeracionSettings
{
    /// <summary><c>Local</c> o <c>Api</c>.</summary>
    public string Mode { get; init; } = "Local";

    /// <summary>Dónde vive <c>Api.Moderation</c>.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>La llave compartida del árbol de servicios.</summary>
    public string? ApiKey { get; init; }

    /// <summary>El techo de cada llamada.</summary>
    public int TimeoutSeconds { get; init; } = 5;
}
