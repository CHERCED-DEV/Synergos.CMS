using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Composers;

/// <summary>
/// Dónde vive una capacidad (o un orquestador) del árbol de servicios, tal como la configura el
/// despliegue: la URL base, la llave compartida y cuánto se le espera.
/// </summary>
/// <param name="BaseUrl">La raíz, siempre con barra final — las rutas <c>v1/…</c> son relativas.</param>
/// <param name="Llave">La llave compartida. Vacía no manda cabecera: el clon limpio, sin llave.</param>
/// <param name="Timeout">
/// El techo de la llamada ENTERA, reintentos incluidos: es el <c>HttpClient.Timeout</c>, que
/// envuelve toda la cadena de handlers.
/// </param>
public sealed record DestinoDelArbol(Uri BaseUrl, string? Llave, TimeSpan Timeout)
{
    /// <summary>
    /// Lee el destino de la sección del vertical: <c>BaseUrl</c>, <c>ApiKey</c> y
    /// <c>TimeoutSeconds</c> — los campos del molde (doc 12 §5.4).
    /// </summary>
    /// <param name="seccion">La sección, p. ej. <c>Synergos:Gob</c>.</param>
    /// <param name="urlPorDefecto">El puerto local del servicio, para el clon limpio.</param>
    /// <param name="segundosPorDefecto">
    /// El techo si la sección no lo trae o trae algo que no es un entero positivo.
    /// </param>
    /// <param name="claveDeLaUrl">
    /// Qué clave lleva la URL. <c>BaseUrl</c> casi siempre; Tienda lleva la de la canasta en
    /// <c>CartBaseUrl</c> porque habla con dos servicios bajo la MISMA llave y el mismo techo.
    /// </param>
    public static DestinoDelArbol De(
        IConfiguration seccion, string urlPorDefecto, int segundosPorDefecto, string claveDeLaUrl = "BaseUrl")
    {
        ArgumentNullException.ThrowIfNull(seccion);

        var url = seccion[claveDeLaUrl];
        if (string.IsNullOrWhiteSpace(url)) url = urlPorDefecto;

        var segundos = int.TryParse(seccion["TimeoutSeconds"], out var s) && s > 0 ? s : segundosPorDefecto;

        return new DestinoDelArbol(
            new Uri(url.EndsWith('/') ? url : url + "/"),
            seccion["ApiKey"],
            TimeSpan.FromSeconds(segundos));
    }
}

/// <summary>
/// La pieza con la que el CMS habla con el árbol de servicios (#178): UNA llamada enchufa el
/// cliente nombrado con toda su cadena, en vez de copiarla.
/// </summary>
/// <remarks>
/// <para><b>Lo que arma, en este orden, y por qué el orden.</b> La URL base, el techo y la llave
/// compartida en el cliente; después la correlación (HU #28), la telemetría y el reintento. La
/// telemetría va ANTES que el reintento —más afuera— porque lo que el operador necesita ver es
/// lo que esperó quien llamó, reintentos incluidos (ADR 0072), no cada intento suelto.</para>
///
/// <para><b>Lo que NO arma, y es de cada cliente</b>: cómo se PRESENTA un fallo. Un 404 es «no
/// existe», un 401 nombra la llave, un rechazo con motivo sube como error de negocio — eso
/// depende del dominio de cada seam y se queda en su clase (<see cref="RechazoDelArbolDeServicios"/>
/// lo dice con más detalle). Lo que el cliente sí recibe hecho es la LECTURA del rechazo: la
/// hace ese mismo tipo, y no una copia privada.</para>
///
/// <para><b>El reintento, y las dos llaves que lo abren.</b> Se repite sólo si la petición es
/// <b>repetible</b> —un método seguro, o una <c>Idempotency-Key</c> que la capacidad resuelve
/// antes que cualquier regla (§0.B.16)— <b>y</b> el fallo es <b>pasajero</b>: no hubo respuesta
/// (la conexión no se abrió o se cortó), o la capacidad contestó <c>transient: true</c>. Esa
/// bandera se LEE, no se deduce del código de estado (#129): un 503 sin bandera —un proxy, una
/// versión anterior— es «no consta» y no se repite. Un POST sin llave no se repite NUNCA, porque
/// es justo el que puede crear dos veces lo mismo: agregar una línea a la canasta, sellar. Y un
/// cliente puede VETAR el reintento de una petición (<see cref="PeticionAlArbol.NoSeRepite"/>)
/// cuando sabe que la capacidad cierra la llave aunque diga pasajero — hoy, autorizar un cobro
/// en <c>Api.Payments</c>, medido con la capacidad viva.</para>
///
/// <para><b>El techo es el de siempre.</b> El <c>TimeoutSeconds</c> de cada capacidad es el
/// <c>HttpClient.Timeout</c>, que envuelve la cadena entera: reintentar nunca hace esperar más
/// de lo que el despliegue ya aceptaba, y un timeout sigue saliendo como la misma
/// <see cref="TaskCanceledException"/> que los clientes ya saben traducir. Por eso NO hay
/// timeout por intento ni cortacircuitos: los dos lanzan excepciones propias de Polly que
/// ninguno de los quince clientes captura, y encenderlos habría convertido «la capacidad no
/// contesta» en un 500 sin traducir.</para>
///
/// <para><b>Se enchufa así</b>, en el composer del vertical y dentro de su interruptor:</para>
/// <code>
/// services.AddClienteDelArbolDeServicios(
///     HttpXService.ClientName,
///     DestinoDelArbol.De(builder.Config.GetSection("Synergos:X"), "http://127.0.0.1:52NN/", 10));
/// </code>
/// <para>Hay gate (<c>ClienteDelArbolTests</c>): la llave compartida se escribe en este fichero y
/// en ningún otro, nadie lee un problem+json por su cuenta, y ningún composer arma a mano un
/// cliente hacia el árbol.</para>
/// </remarks>
public static class ClienteDelArbolDeServicios
{
    /// <summary>La cabecera de la llave compartida. La misma que exige toda capacidad
    /// (<c>SharedKeyAuth.HeaderName</c> del otro árbol; hay gate que las cruza).</summary>
    public const string CabeceraDeLlave = "X-Synergos-Key";

    /// <summary>La cabecera que hace repetible una escritura. La misma que lee
    /// <c>IdempotencyHeader</c> del otro árbol.</summary>
    public const string CabeceraDeIdempotencia = "Idempotency-Key";

    /// <summary>El nombre del tramo de reintento dentro de la cadena de cada cliente.</summary>
    internal const string TramoDeReintento = "reintento";

    private static readonly JsonSerializerOptions LecturaDelRechazo = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Registra el cliente nombrado hacia una capacidad u orquestador, con su cadena entera.
    /// </summary>
    /// <param name="services">El contenedor.</param>
    /// <param name="nombre">El nombre del cliente — la constante <c>ClientName</c> del cliente <c>Http*</c>.</param>
    /// <param name="destino">Dónde vive y con qué llave.</param>
    /// <returns>El constructor del cliente, por si quien lo registra necesita algo más.</returns>
    public static IHttpClientBuilder AddClienteDelArbolDeServicios(
        this IServiceCollection services, string nombre, DestinoDelArbol destino)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentNullException.ThrowIfNull(destino);

        // Lo que la cadena necesita viaja con ella: quien enchufa la pieza no tiene que saber
        // qué más registrar. TryAdd, para no pisar lo que un composer ya haya registrado.
        services.AddHttpContextAccessor();
        services.TryAddTransient<CorrelationForwardingHandler>();
        services.TryAddSingleton<IWebhookTelemetryStore, InMemoryWebhookTelemetryStore>();

        return services
            .AddHttpClient(nombre, http =>
            {
                http.BaseAddress = destino.BaseUrl;
                http.Timeout = destino.Timeout;
                if (!string.IsNullOrWhiteSpace(destino.Llave))
                {
                    http.DefaultRequestHeaders.Add(CabeceraDeLlave, destino.Llave);
                }
            })
            // El hilo de la correlación cruza al árbol de servicios (HU #28). Va en la pieza y
            // no en un handler global: los webhooks salen a terceros, y a un tercero conviene
            // mandarle lo mínimo.
            .AddHttpMessageHandler<CorrelationForwardingHandler>()
            .AddTelemetriaYReintento(ContestoLaCapacidad, EsPasajeroEnElArbol);
    }

    /// <summary>
    /// Telemetría y reintento para un cliente hacia un TERCERO (la pasarela de pago), sin llave
    /// compartida ni correlación.
    /// </summary>
    /// <remarks>
    /// <para><b>Un tercero no emite la bandera <c>transient</c></b>, así que ahí sí se usa la tabla
    /// de códigos — la de la librería (<c>HttpClientResiliencePredicates</c>), no una escrita acá:
    /// sin respuesta, 5xx, 408 y 429. La regla de #129 es sobre el árbol de servicios, donde
    /// quien sabe si un «no» es pasajero es la capacidad y lo dice.</para>
    ///
    /// <para>Lo que NO cambia es la otra llave: sólo se repite lo repetible. Un POST sin
    /// <c>Idempotency-Key</c> a una pasarela —una devolución— no se repite jamás: repetirlo es
    /// devolver dos veces.</para>
    /// </remarks>
    public static IHttpClientBuilder AddResilienciaDeTercero(this IHttpClientBuilder cliente)
    {
        ArgumentNullException.ThrowIfNull(cliente);

        cliente.Services.TryAddSingleton<IWebhookTelemetryStore, InMemoryWebhookTelemetryStore>();
        return cliente.AddTelemetriaYReintento(esExito: null, EsPasajeroEnUnTercero);
    }

    /// <summary>
    /// La telemetría, y DESPUÉS —más adentro— el reintento. El orden es ADR 0072.
    /// </summary>
    private static IHttpClientBuilder AddTelemetriaYReintento(
        this IHttpClientBuilder cliente,
        Func<HttpResponseMessage, bool>? esExito,
        Func<Outcome<HttpResponseMessage>, CancellationToken, ValueTask<bool>> esPasajero)
    {
        var canal = cliente.Name;

        cliente.AddHttpMessageHandler(sp => new WebhookTelemetryHandler(
            canal, sp.GetRequiredService<IWebhookTelemetryStore>(), esExito));

        cliente.AddResilienceHandler(TramoDeReintento, (tramo, contexto) =>
        {
            // En caliente (ADR 0064): un cambio en la sección rehace este tramo sin reiniciar.
            contexto.EnableReloads<ReintentoSettings>();
            var ajustes = contexto.GetOptions<ReintentoSettings>();

            if (ajustes.MaxRetryAttempts <= 0) return;

            tramo.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = ajustes.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(Math.Max(0, ajustes.RetryBaseDelayMs)),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                // La espera es de quien está comprando, no del servidor: un Retry-After largo
                // sólo gastaría el techo esperando.
                ShouldRetryAfterHeader = false,
                ShouldHandle = args => SeReintenta(args.Outcome, args.Context, esPasajero),
            });
        });

        return cliente;
    }

    /// <summary>Las dos llaves del reintento: repetible Y pasajero.</summary>
    internal static async ValueTask<bool> SeReintenta(
        Outcome<HttpResponseMessage> resultado,
        ResilienceContext contexto,
        Func<Outcome<HttpResponseMessage>, CancellationToken, ValueTask<bool>> esPasajero)
    {
        var peticion = resultado.Result?.RequestMessage ?? contexto.GetRequestMessage();

        return EsRepetible(peticion)
            && await esPasajero(resultado, contexto.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Si repetir la petición no puede hacer dos veces lo mismo.
    /// </summary>
    /// <remarks>
    /// Un método seguro, o una llave de idempotencia: la capacidad la resuelve antes que
    /// cualquier regla, así que el segundo intento de una escritura que SÍ llegó devuelve lo que
    /// el primero creó en vez de crear otra cosa. <c>DELETE</c> no entra por su nombre: que el
    /// protocolo lo llame idempotente no dice qué hace una capacidad con él.
    /// </remarks>
    internal static bool EsRepetible(HttpRequestMessage? peticion)
        => peticion is not null
           // El cliente puede vetarlo cuando sabe que la capacidad cierra la llave con un rechazo
           // pasajero (PeticionAlArbol). Medido con Api.Payments viva al autorizar.
           && !PeticionAlArbol.EstaVetada(peticion)
           && (peticion.Method == HttpMethod.Get
               || peticion.Method == HttpMethod.Head
               || peticion.Method == HttpMethod.Options
               || peticion.Headers.Contains(CabeceraDeIdempotencia));

    /// <summary>
    /// Pasajero en el árbol: sin respuesta, o la capacidad dijo <c>transient: true</c>.
    /// </summary>
    /// <remarks>
    /// Sin respuesta no hay bandera que leer, y por eso esa es la única excepción que abre: la
    /// petición no llegó o se cortó en el camino, y siendo repetible, volver a mandarla no puede
    /// duplicar nada. Un timeout NO entra — lo corta el techo, que es el de la llamada entera.
    /// </remarks>
    private static async ValueTask<bool> EsPasajeroEnElArbol(
        Outcome<HttpResponseMessage> resultado, CancellationToken ct)
    {
        if (resultado.Exception is not null) return resultado.Exception is HttpRequestException;

        if (resultado.Result is not { IsSuccessStatusCode: false } respuesta) return false;

        var rechazo = await RechazoDelArbolDeServicios
            .LeerAsync(respuesta, LecturaDelRechazo, ct).ConfigureAwait(false);

        return rechazo?.EsTransitorio == true;
    }

    // La sobrecarga con token de IsTransient es experimental (EXTEXP0001) y la sin token es la
    // estable. No pierde nada acá: la sin token nunca toma una cancelación por pasajera, y sólo
    // mira la excepción de red y los códigos 5xx, 408 y 429.
#pragma warning disable CA2016
    private static ValueTask<bool> EsPasajeroEnUnTercero(Outcome<HttpResponseMessage> resultado, CancellationToken _)
        => ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(resultado));
#pragma warning restore CA2016

    /// <summary>
    /// Qué cuenta como «el canal contestó» en la telemetría de una capacidad.
    /// </summary>
    /// <remarks>
    /// <para>Un 4xx es una RESPUESTA: «no existe», «ya estaba», «no alcanza el cupo». Contarlo
    /// como fallo pintaría en rojo a <c>Api.Identity</c>, que contesta 409 a cada alta a partir de
    /// la segunda — el caso normal. Un 5xx sí es un fallo del canal, y el 401 también: es la
    /// llave compartida mal puesta, y con ella mal puesta fallan todas las llamadas.</para>
    ///
    /// <para><b>No es deducir transitoriedad</b> (#129): esto no decide si se repite nada, sólo
    /// cómo se pinta el panel de salud.</para>
    /// </remarks>
    private static bool ContestoLaCapacidad(HttpResponseMessage respuesta)
        => (int)respuesta.StatusCode < 500 && respuesta.StatusCode != HttpStatusCode.Unauthorized;
}
