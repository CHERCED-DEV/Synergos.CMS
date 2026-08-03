using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Synergos.Shared;

/// <summary>
/// Autenticación por llave compartida en una cabecera — el borde mínimo de un servicio interno.
/// </summary>
/// <remarks>
/// <para><b>Qué hace esto aquí y no en cada API.</b> Salió tal cual de
/// <c>Synergos.Api.Sessions</c>, donde ya estaba escrito y probado con los dos procesos hablando
/// (ADR 0130). No es código que <i>podría</i> compartirse: es el código que la segunda API iba
/// a copiar, con sus tres decisiones sutiles —comparación de tiempo fijo, exención de
/// <c>/health</c>, degradación a gritos— que copiadas a mano se pierden una por una.</para>
///
/// <para><b>No es autenticación fuerte y no pretende serlo.</b> Es suficiente para un servicio
/// interno del que solo hablan otros procesos nuestros. El día que una API tenga borde público
/// —tráfico de Angular directo, sin pasar por el CMS— necesita otra cosa, y esa decisión no se
/// toma escondida dentro de un middleware.</para>
/// </remarks>
public static class SharedKeyAuth
{
    /// <summary>Cabecera con la llave compartida. Una sola para todos los servicios.</summary>
    public const string HeaderName = "X-Synergos-Key";

    /// <summary>Rutas exentas: un chequeo de vida que exige credenciales no sirve de chequeo.</summary>
    private static readonly string[] AlwaysOpen = { "/health" };

    /// <summary>
    /// Exige <see cref="HeaderName"/> en toda petición salvo las de <see cref="AlwaysOpen"/>.
    /// </summary>
    /// <param name="app">El pipeline.</param>
    /// <param name="expectedKey">
    /// La llave esperada. Si viene vacía la autenticación queda <b>abierta</b> y se registra un
    /// aviso: sin esto, un <c>dotnet run</c> recién clonado no levantaría, y eso empuja a poner
    /// la llave en el repo — que es peor que no tenerla. Pero queda dicho.
    /// </param>
    public static IApplicationBuilder UseSharedKeyAuth(this IApplicationBuilder app, string? expectedKey)
    {
        var logger = app.ApplicationServices
            .GetService(typeof(ILoggerFactory)) as ILoggerFactory;

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            logger?.CreateLogger(typeof(SharedKeyAuth)).LogWarning(
                "La llave compartida NO está configurada: la API queda ABIERTA. " +
                "Sirve para desarrollo; en cualquier despliegue alcanzable es un agujero.");
            return app;
        }

        return app.Use(async (ctx, next) =>
        {
            if (IsAlwaysOpen(ctx.Request.Path))
            {
                await next();
                return;
            }

            if (!FixedTimeEquals(ctx.Request.Headers[HeaderName].ToString(), expectedKey))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        });
    }

    /// <summary>Si la ruta está exenta de la llave.</summary>
    public static bool IsAlwaysOpen(PathString path)
        => AlwaysOpen.Any(open => path.StartsWithSegments(open));

    /// <summary>
    /// Compara en tiempo constante. Una comparación normal filtra el prefijo correcto por
    /// tiempo de respuesta, que es como se adivina una llave sin saberla.
    /// </summary>
    public static bool FixedTimeEquals(string? sent, string? expected)
    {
        // Una llave vacía NO valida contra una llave vacía, aunque los arrays sean iguales.
        // Sin esto, un servicio con el secreto sin configurar aceptaría peticiones sin
        // cabecera como si estuvieran AUTENTICADAS — "abierto" disfrazado de "validado", que
        // es peor que abierto porque no se nota. Lo destapó un test: el caso de la llave
        // ausente se resuelve arriba, decidiendo no montar el middleware; aquí solo se
        // compara, y comparar nada con nada no es una coincidencia.
        //
        // Ramificar sobre si el secreto está vacío no filtra nada del secreto: que exista o
        // no es un hecho de configuración, no su contenido.
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(sent)) return false;

        var a = Encoding.UTF8.GetBytes(sent);
        var b = Encoding.UTF8.GetBytes(expected);
        // La longitud sí se filtra —es inevitable comparando arrays— y no es el secreto.
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
