using Microsoft.AspNetCore.Http;
using Synergos.Core;

namespace Synergos.Shared;

/// <summary>
/// Traduce un <see cref="Rejection"/> a una respuesta HTTP, una sola vez para todas las APIs.
/// </summary>
/// <remarks>
/// <para><b>Esta clase es la razón por la que <c>Shared</c> referencia a <c>Core</c>.</b> El doc
/// 06 había fijado que ninguno referenciara al otro; al bajar a los tipos apareció el costo: este
/// mapeo es fontanería de host que necesitan las dieciséis capacidades, y con la regla simétrica
/// había que copiarlo dieciséis veces o meter dominio en <c>Shared</c>. Queda <b>una flecha, no
/// dos</b>: <c>Shared → Core</c>, nunca al revés. <c>Core</c> sigue sin saber qué es un host, que
/// es lo que importaba.</para>
///
/// <para><b>Por qué el mapeo vive en un solo sitio.</b> Si cada API eligiera su código, "cupo
/// lleno" sería 409 en una y 400 en otra, y el orquestador tendría que aprenderse las dos. Peor:
/// el <c>503</c> es el único que un cliente debería reintentar, y esa distinción se pierde a la
/// primera API que lo mapee distinto.</para>
/// </remarks>
public static class RejectionResults
{
    /// <summary>El código HTTP que corresponde a cada clase de rechazo.</summary>
    public static int StatusCodeFor(RejectionKind kind) => kind switch
    {
        RejectionKind.Invalid => StatusCodes.Status400BadRequest,
        RejectionKind.NotFound => StatusCodes.Status404NotFound,
        RejectionKind.Conflict => StatusCodes.Status409Conflict,
        RejectionKind.Forbidden => StatusCodes.Status403Forbidden,
        // 410 y no 404: "existía y se venció" es información útil — le dice al llamador que
        // rehaga desde el principio en vez de buscar un identificador que escribió mal.
        RejectionKind.Expired => StatusCodes.Status410Gone,
        RejectionKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>
    /// Convierte el rechazo en un <c>ProblemDetails</c> (RFC 7807).
    /// </summary>
    /// <remarks>
    /// El <see cref="Rejection.Code"/> viaja como una extensión propia y no dentro del texto: es
    /// lo que un cliente compara, y meterlo en <c>detail</c> obligaría a parsear prosa.
    /// </remarks>
    public static IResult ToProblem(this Rejection rejection)
    {
        var status = StatusCodeFor(rejection.Kind);
        return Results.Problem(
            detail: rejection.Message,
            statusCode: status,
            title: rejection.Kind.ToString(),
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = rejection.Code,
                ["transient"] = rejection.IsTransient,
            });
    }

    /// <summary>
    /// Sirve el valor con <c>200</c>, o el rechazo con su código.
    /// </summary>
    /// <remarks>
    /// Es el único punto donde un endpoint desempaqueta un <see cref="Result{T}"/>. Tenerlo aquí
    /// evita el olvido que importa: devolver <c>200</c> con un cuerpo vacío cuando en realidad
    /// hubo un rechazo.
    /// </remarks>
    public static IResult ToHttp<T>(this Result<T> result)
        => result.Match(ok => Results.Ok(ok), rejected => rejected.ToProblem());
}
