using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Synergos.Shared;

/// <summary>
/// El hilo que permite seguir una compra a través de seis procesos (HU #28).
/// </summary>
/// <remarks>
/// <para><b>El problema que cierra:</b> una compra toca CMS → <c>Bff.Tienda</c> → Inventory →
/// Payments → Orders → Notifications. Seis procesos, seis registros, y hasta hoy ninguna forma de
/// saber que las seis líneas son la misma compra. Cuando alguien dijera «pagué y no me llegó
/// nada», la única respuesta honesta era <i>no sé en qué paso se rompió</i>.</para>
///
/// <para><b>Deliberadamente NO es OpenTelemetry.</b> Un colector es otro proceso que mantener en
/// un servidor con veintitrés contenedores. Una cabecera y un campo en el registro resuelven la
/// pregunta que de verdad se hace —«mostrame todo lo de esta compra»— con un <c>grep</c>. El día
/// que eso deje de alcanzar, ese día se justifica el colector, con la evidencia delante.</para>
///
/// <para><b>Vive en <c>Synergos.Shared</c> y no en <c>Synergos.Core</c></b>: es fontanería de
/// host —cabeceras HTTP, middleware, registro— igual que la llave compartida y el libro de
/// idempotencia. <c>Core</c> no sabe qué es ASP.NET y no debe empezar a saberlo por esto.</para>
///
/// <para><b>Y la regla del segundo consumidor se cumple sola</b> (<c>CLAUDE.md</c> §17): acá los
/// consumidores son veintitrés desde el primer día. Conviene decirlo igual, porque «lo pongo en
/// Shared» es exactamente la frase con la que esa regla se rompe.</para>
/// </remarks>
public static class Correlation
{
    /// <summary>
    /// La cabecera por la que viaja. <b>Cabecera y no ruta</b>, a propósito.
    /// </summary>
    /// <remarks>
    /// <para>Un identificador en la URL acaba en las cachés intermedias, en los registros del
    /// proxy y en el historial del navegador — tres sitios que nadie repasa cuando decide qué se
    /// guarda y por cuánto tiempo. En una cabecera muere con la petición.</para>
    ///
    /// <para><b>Este nombre es EL contrato entre los dos árboles, y lo único que comparten.</b>
    /// El CMS ya emitía <c>X-Correlation-Id</c> desde su propio middleware, con su propia
    /// generación y su propio almacenamiento en <c>HttpContext.Items</c>. Se reusa ese nombre en
    /// vez de inventar un segundo: dos cabeceras habrían significado dos identificadores para la
    /// misma compra, que es el problema de partida disfrazado de solución.</para>
    ///
    /// <para><b>Y se comparte el NOMBRE, no el código</b>, porque no puede compartirse el código:
    /// el CMS no referencia <c>Synergos.Shared</c> y no debe hacerlo (<c>CLAUDE.md</c> §11 —
    /// ninguna API referencia el CMS ni al revés). Un contrato de una cadena es exactamente el
    /// tipo de acople que sí está permitido entre los dos árboles.</para>
    /// </remarks>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>Cómo se llama el campo en el registro. Es lo que se busca con <c>grep</c>.</summary>
    public const string LogField = "Correlacion";

    /// <summary>
    /// El <i>scope</i> con el que se abre cada petición.
    /// </summary>
    /// <remarks>
    /// <para><b>Una cadena de formato, y NO un diccionario</b> — y esto lo destapó un proceso
    /// vivo, no un test. Con <c>BeginScope(new Dictionary&lt;string, object&gt;{ … })</c> el
    /// identificador entra al scope, el gate de cableado pasa, y en el registro sale impreso
    /// <c>System.Collections.Generic.Dictionary`2[System.String,System.Object]</c>: el formateador
    /// de consola renderiza el scope con su <c>ToString()</c>, y el de un diccionario es el nombre
    /// de su tipo.</para>
    ///
    /// <para>O sea: todo cableado, todo en verde, y el <c>grep</c> devolviendo cero líneas — que
    /// es exactamente el estado del que partíamos, con un identificador de más que nadie ve. Con
    /// una cadena de formato el estado es un <c>FormattedLogValues</c>, que se imprime <i>y</i>
    /// sigue siendo estructurado para quien algún día lea los registros con algo mejor que un
    /// <c>grep</c>.</para>
    /// </remarks>
    public const string ScopeFormat = LogField + ":{" + LogField + "}";

    /// <summary>El identificador de la petición en curso, o <c>null</c> fuera de una.</summary>
    /// <remarks>
    /// Sale de <see cref="Activity"/> y no de un <c>AsyncLocal</c> propio porque el runtime ya
    /// mantiene ese contexto a través de <c>await</c>, hilos del <c>ThreadPool</c> y tareas de
    /// fondo. Reimplementarlo sería tener dos contextos que se desincronizan.
    /// </remarks>
    public static string? Current => Activity.Current?.GetBaggageItem(LogField);

    /// <summary>
    /// Uno nuevo, opaco y no adivinable.
    /// </summary>
    /// <remarks>
    /// <para><b>Aleatorio, no un contador ni una marca de tiempo.</b> Un identificador adivinable
    /// deja pedir por los registros de otro; uno derivado del reloj revela cuántas compras hubo
    /// entre dos instantes.</para>
    ///
    /// <para><b>Y NUNCA lleva datos de la persona.</b> Ni su correo, ni su documento, ni su
    /// número de pedido: los registros se copian, se pegan en tickets y se mandan por chat. Un
    /// identificador opaco se puede compartir sin pensarlo; una dirección de correo no.</para>
    /// </remarks>
    public static string Nuevo() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    /// <summary>
    /// Registra lo que hace falta para propagar la correlación hacia afuera.
    /// </summary>
    /// <remarks>
    /// Se llama en el arranque de cada host. Enciende además los <i>scopes</i> del registro: sin
    /// eso el identificador existe pero no sale impreso, y una correlación que no se ve no
    /// correlaciona nada.
    /// </remarks>
    public static WebApplicationBuilder AddCorrelation(this WebApplicationBuilder builder)
    {
        builder.Logging.AddSimpleConsole(o =>
        {
            // LO QUE HACE QUE APAREZCA EN CADA LÍNEA. El formateador por defecto ignora los
            // scopes, así que sin esto el middleware abriría uno que nadie imprime.
            o.IncludeScopes = true;
            o.TimestampFormat = "HH:mm:ss ";
        });

        builder.Services.AddTransient<CorrelationHandler>();
        return builder;
    }

    /// <summary>
    /// Toma la correlación de la petición —o crea una— y la deja puesta para todo lo que siga.
    /// </summary>
    /// <remarks>
    /// <para><b>Va ANTES que la llave compartida</b>, y no es un detalle de orden: un 401 también
    /// tiene que quedar correlacionado. Si el rechazo por credencial saliera sin identificador,
    /// justo el caso que más cuesta diagnosticar —«a mí no me llega nada»— sería el único sin
    /// rastro.</para>
    ///
    /// <para><b>Sin cabecera de entrada se genera una nueva</b> en vez de fallar o registrar en
    /// blanco. Que un tercero no la mande es lo normal —un navegador, un webhook del proveedor de
    /// correo— y perder el rastro de esas peticiones sería perder justo las que vienen de fuera.</para>
    /// </remarks>
    public static IApplicationBuilder UseCorrelation(this IApplicationBuilder app)
        => app.Use(async (ctx, next) =>
        {
            var entrante = ctx.Request.Headers[HeaderName].FirstOrDefault();
            var id = string.IsNullOrWhiteSpace(entrante) ? Nuevo() : Recortar(entrante!);

            // ⚠️ `Activity.Current` puede venir en NULO. ASP.NET solo crea uno cuando hay alguien
            // escuchando el DiagnosticSource, y en un despliegue sin telemetría no lo hay. Sin
            // esto, el equipaje no se pegaría a ningún sitio, el handler de salida no encontraría
            // nada que propagar, y la correlación se cortaría en el primer salto — en silencio y
            // solo en producción, que es la peor combinación posible.
            Activity? propia = null;
            if (Activity.Current is null)
            {
                propia = new Activity("synergos.peticion").Start();
            }

            // SetBaggage y no AddBaggage: el segundo APILA. Con un middleware que corre una vez
            // por petición da igual, pero el día que alguien lo llame dos veces el equipaje
            // tendría dos valores y `GetBaggageItem` devolvería el primero — el viejo.
            Activity.Current!.SetBaggage(LogField, id);

            // Y de vuelta en la respuesta, para que quien llamó pueda citarlo sin adivinarlo.
            ctx.Response.Headers[HeaderName] = id;

            var log = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Synergos.Correlacion");
            try
            {
                using (log.BeginScope(ScopeFormat, id))
                {
                    var desde = Stopwatch.GetTimestamp();
                    await next().ConfigureAwait(false);

                    // UNA línea propia por petición, y hace falta aunque el scope ya esté puesto.
                    //
                    // Las de «Request starting/finished» las emite la capa de hosting POR FUERA de
                    // este scope, así que no la llevan. Y una petición que la llave corta —un 401—
                    // no registra nada más: sin esta línea, el caso que más cuesta diagnosticar
                    // («a mí no me llega nada») sería el ÚNICO sin rastro. Lo destapó levantar dos
                    // procesos y pedir sin credencial.
                    log.LogInformation(
                        "{Metodo} {Ruta} → {Estado} en {Ms}ms",
                        ctx.Request.Method,
                        ctx.Request.Path.Value,
                        ctx.Response.StatusCode,
                        (int)Stopwatch.GetElapsedTime(desde).TotalMilliseconds);
                }
            }
            finally
            {
                propia?.Stop();
                propia?.Dispose();
            }
        });

    /// <summary>
    /// Una cabecera de fuera no se copia tal cual.
    /// </summary>
    /// <remarks>
    /// Lo que llega de fuera es entrada, no dato: alguien puede mandar mil caracteres, saltos de
    /// línea que parten el registro en dos, o texto que se lea como otra cosa al pegarlo. Se
    /// recorta y se limpia. Si no queda nada usable, se genera uno propio.
    /// </remarks>
    private static string Recortar(string entrante)
    {
        var limpio = new string(entrante.Where(char.IsAsciiLetterOrDigit).Take(32).ToArray());
        return limpio.Length == 0 ? Nuevo() : limpio;
    }
}

/// <summary>
/// Pega la correlación en cada salto de salida.
/// </summary>
/// <remarks>
/// <para><b>Es la mitad que hace que esto sirva.</b> Nacer con un identificador y no pasarlo al
/// siguiente servicio deja seis rastros aislados con seis identificadores distintos, que es
/// exactamente el problema de partida con un paso más de trabajo.</para>
///
/// <para>Se engancha a los clientes <b>nombrados</b> —los que ya existen por capacidad— para que
/// una capacidad nueva lo herede al registrarse, sin que nadie se acuerde de añadirlo.</para>
/// </remarks>
public sealed class CorrelationHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Correlation.Current is { Length: > 0 } id && !request.Headers.Contains(Correlation.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(Correlation.HeaderName, id);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
