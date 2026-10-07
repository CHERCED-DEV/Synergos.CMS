using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
///
/// <para><b>Devuelve tipos concretos y no <c>IResult</c> (ADR 0140, F2).</b> El documento OpenAPI
/// de cada pieza se infiere del TIPO DE RETORNO de sus endpoints: con <c>IResult</c> toda
/// operación salía «200 OK» sin esquema. Con <c>Results&lt;Ok&lt;T&gt;, ProblemHttpResult&gt;</c> el
/// esquema de la respuesta es el que el compilador comprueba, y un endpoint que devuelva otra
/// forma no compila — un <c>.Produces&lt;T&gt;()</c> declarado a mano sí, y deja el documento
/// mintiendo en verde. El cuerpo HTTP no cambia: <c>Results.Problem</c> ya delegaba en
/// <c>TypedResults.Problem</c>, y los que sólo necesitan un <c>IResult</c> lo siguen teniendo,
/// porque los tres tipos lo implementan.</para>
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
    public static ProblemHttpResult ToProblem(this Rejection rejection)
    {
        var status = StatusCodeFor(rejection.Kind);
        return TypedResults.Problem(
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
    public static Results<Ok<T>, ProblemHttpResult> ToHttp<T>(this Result<T> result)
        => result.Match<Results<Ok<T>, ProblemHttpResult>>(ok => TypedResults.Ok(ok), rejected => rejected.ToProblem());

    /// <summary>
    /// Sirve el valor con <c>201</c> y su ubicación, o el rechazo con su código.
    /// </summary>
    /// <remarks>
    /// Es <see cref="ToHttp{T}"/> para lo que CREA: sin él, cada alta escribía su
    /// <c>Match(… Results.Created(…), bad =&gt; bad.ToProblem())</c> y el documento la publicaba como
    /// un 200, que es lo que no devuelve.
    /// </remarks>
    /// <param name="result">El resultado del servicio.</param>
    /// <param name="location">La ruta del recurso creado, a partir del valor.</param>
    public static Results<Created<T>, ProblemHttpResult> ToCreated<T>(this Result<T> result, Func<T, string> location)
        => result.Match<Results<Created<T>, ProblemHttpResult>>(
            ok => TypedResults.Created(location(ok), ok), rejected => rejected.ToProblem());
}
