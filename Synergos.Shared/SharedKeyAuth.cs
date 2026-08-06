using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
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

    /// <summary>Rutas exentas siempre: un chequeo de vida que exige credenciales no sirve de chequeo.</summary>
    private static readonly string[] AlwaysOpen = { "/health" };

    /// <summary>
    /// Exige <see cref="HeaderName"/> en toda petición salvo las de <see cref="AlwaysOpen"/>.
    /// </summary>
    /// <param name="app">El pipeline.</param>
    /// <param name="expectedKey">
    /// La llave esperada. Si viene vacía, lo que pasa depende del entorno:
    /// <b>en Development se abre con un aviso a gritos; en cualquier otro, el arranque falla</b>.
    /// Ver <see cref="UseSharedKeyAuth"/> para por qué esa asimetría no es una comodidad.
    /// </param>
    /// <param name="alsoOpen">
    /// Rutas adicionales que quedan fuera de la llave. <b>Se pasan una por una y a la vista</b>,
    /// nunca por comodín: existen porque quien las llama es un tercero que no tiene la llave —el
    /// webhook de un proveedor de correo o de pagos— y lo que las protege es su propia firma. Una
    /// exención que no lleva verificación detrás es un endpoint abierto que escribe.
    /// </param>
    public static IApplicationBuilder UseSharedKeyAuth(
        this IApplicationBuilder app, string? expectedKey, params string[] alsoOpen)
    {
        var logger = app.ApplicationServices
            .GetService(typeof(ILoggerFactory)) as ILoggerFactory;

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            var entorno = app.ApplicationServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;

            // ── El aviso a gritos era insuficiente, y lo dijo la HU #19 ───────────────────
            //
            // Degradar a ABIERTO con un LogWarning tiene sentido exacto en UN sitio: un
            // `dotnet run` recién clonado, donde exigir la llave empuja a ponerla en el repo
            // —que es peor que no tenerla—. En cualquier despliegue alcanzable es otra cosa:
            // veintidós capacidades abiertas y un renglón de log que nadie va a leer, porque
            // el sitio FUNCIONA. Un agujero que no se nota es el que se queda.
            //
            // Así que el degradado se queda, pero atado al único entorno donde la razón que
            // lo justifica es cierta.
            if (entorno is null || !entorno.IsDevelopment())
            {
                // Se falla CERRADO también cuando no hay entorno que preguntar: si esto se
                // monta fuera de un host, lo desconocido no puede resolverse a favor de abrir.
                throw new InvalidOperationException(
                    "La llave compartida NO está configurada y el entorno es " +
                    $"'{entorno?.EnvironmentName ?? "(sin IHostEnvironment)"}'. Arrancar así dejaría " +
                    "la API abierta a quien la alcance.\n" +
                    "  · En un despliegue: configurá la llave (en compose es SYNERGOS_API_KEY).\n" +
                    "  · En tu máquina: ASPNETCORE_ENVIRONMENT=Development, que es donde el " +
                    "modo abierto está permitido a propósito.");
            }

            logger?.CreateLogger(typeof(SharedKeyAuth)).LogWarning(
                "La llave compartida NO está configurada: la API queda ABIERTA. " +
                "Se permite porque el entorno es Development; en cualquier otro esto no arranca.");
            return app;
        }

        // Se resuelve una vez al montar, no por petición: la lista es de configuración y no
        // cambia, y evaluarla en cada request es trabajo repetido sobre el camino caliente.
        var abiertas = AlwaysOpen.Concat(alsoOpen ?? Array.Empty<string>()).ToArray();

        return app.Use(async (ctx, next) =>
        {
            if (IsOpen(ctx.Request.Path, abiertas))
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

    /// <summary>Si la ruta está exenta de la llave sin que nadie lo haya pedido.</summary>
    public static bool IsAlwaysOpen(PathString path) => IsOpen(path, AlwaysOpen);

    /// <summary>Si la ruta está en una lista de exentas.</summary>
    /// <remarks>
    /// Compara por <b>segmentos</b> y no por prefijo de texto: con un prefijo suelto, exentar
    /// <c>/v1/webhooks</c> abriría también <c>/v1/webhooksfalsos</c>. Es la clase de detalle que
    /// no se nota hasta que alguien lo busca a propósito.
    /// </remarks>
    public static bool IsOpen(PathString path, IReadOnlyList<string> open)
    {
        for (var i = 0; i < open.Count; i++)
        {
            if (path.StartsWithSegments(open[i])) return true;
        }
        return false;
    }

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
