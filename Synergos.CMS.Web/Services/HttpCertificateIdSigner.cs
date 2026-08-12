using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="ICertificateIdSigner"/> cuya llave la custodia <c>Synergos.Api.Signing</c>
/// (hallazgo #45).
/// </summary>
/// <remarks>
/// <para><b>Lo que cambia es quién guarda la llave, no qué se guarda.</b> El id del diploma ya
/// era un HMAC opaco con llave del servidor (ADR 0124). Lo que la llave local no sabe hacer es
/// <b>retirarse</b>: hoy no hay forma de rotar sin invalidar todos los diplomas emitidos, ni
/// registro de con cuál se firmó cada uno. La capacidad tiene las tres operaciones, y su
/// <c>verify</c> prueba todas las llaves del propósito —retiradas incluidas— para que rotar no
/// deje sin valer un QR ya impreso.</para>
///
/// <para><b>Va a <c>/v1/seals</c> y no a <c>/v1/signatures</c></b>, que es el hallazgo entero:
/// aquel token vence (≤365 d), no es determinista y publica su payload sin llave. Las tres cosas
/// son correctas para lo que ese endpoint hace y ninguna sirve para un diploma, que no vence, se
/// re-emite igual y lleva a su titular dentro del contenido sellado.</para>
///
/// <para><b>El firmante local se conserva como VERIFICADOR de los ids viejos</b>, y es lo que
/// hace que este cambio no rompa nada impreso. El sello y el HMAC local no producen el mismo
/// valor —distinto algoritmo, distinta llave—, así que sin esto cada diploma emitido antes del
/// cableado dejaría de verificar el día del despliegue. No es deuda: es exactamente para lo que
/// sirve poder retirar una llave sin invalidar lo que firmó.</para>
///
/// <para><b>Lo que NO resuelve solo</b>, y va dicho en <c>.env.example</c>: un alumno con diploma
/// viejo que vuelva a pedirlo recibe un id NUEVO, porque <see cref="Sign"/> deriva del sello. Su
/// QR impreso sigue valiendo; lo que queda es un segundo registro del mismo curso y alumno. Es la
/// decisión de migración que el hallazgo dejó abierta a propósito, y no se toma desde acá.</para>
///
/// <para><b>Con la capacidad caída no se emite ni se verifica un id nuevo, y NO se cae al
/// firmante local.</b> Caer ahí produciría ids que el despliegue no reconocería mañana, y —peor—
/// daría por bueno un certificado sin comprobarlo: <see cref="Matches"/> es justo lo que impide
/// que quien consiga escribir en el almacén fabrique una credencial con el nombre que quiera.</para>
/// </remarks>
public sealed class HttpCertificateIdSigner : ICertificateIdSigner
{
    /// <summary>Cabecera de la llave compartida. La misma que exige toda capacidad.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string ClientName = "synergos-api-signing";

    /// <summary>Prefijo del id. El mismo que el local: se reconoce a simple vista.</summary>
    public const string Prefix = "cert-";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _factory;
    private readonly ICertificateIdSigner? _heredado;
    private readonly AcademySettings _settings;

    /// <param name="factory">De dónde sale el cliente HTTP.</param>
    /// <param name="settings">La sección <c>Synergos:Academy</c>.</param>
    /// <param name="heredado">
    /// El firmante con el que se emitieron los ids ANTERIORES al cableado. Se usa <b>sólo para
    /// verificar</b>: sin él, cada diploma ya impreso dejaría de valer el día del despliegue.
    /// <c>null</c> en un despliegue que nunca emitió con llave local.
    /// </param>
    public HttpCertificateIdSigner(
        IHttpClientFactory factory,
        IOptions<AcademySettings> settings,
        ICertificateIdSigner? heredado = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _heredado = heredado;
    }

    /// <summary>
    /// Deriva el id sellando <c>(curso, alumno)</c> en la capacidad.
    /// </summary>
    /// <remarks>
    /// <b>Determinista, y de eso vive la idempotencia de la emisión</b>: el sello no lleva
    /// vencimiento ni azar, así que el mismo sujeto da siempre el mismo id mientras no rote la
    /// llave activa. Se normaliza igual que el firmante local —recortar y minúscula invariante—
    /// porque el motor de matrícula ya compara sin distinguir mayúsculas, y sin eso el mismo
    /// alumno tendría dos credenciales del mismo curso según cómo viniera escrito el correo.
    /// </remarks>
    public string Sign(CertificateSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (string.IsNullOrWhiteSpace(subject.CourseId) || string.IsNullOrWhiteSpace(subject.Student))
        {
            throw new ArgumentException(
                "El certificado necesita curso y alumno para derivar su id.", nameof(subject));
        }

        var sello = Bloquear(() => SellarAsync(Contenido(subject)));
        return Prefix + sello;
    }

    /// <summary>
    /// Comprueba que el id le corresponde al sujeto — en la capacidad, o con la llave vieja.
    /// </summary>
    /// <remarks>
    /// <para><b>Primero lo heredado, y no por preferencia: por forma.</b> Un id viejo no puede
    /// cuadrar contra el sello, así que preguntárselo a la capacidad sería una llamada garantizada
    /// a fallar por cada diploma antiguo que alguien verifique. Se reconoce por su forma —32
    /// caracteres hex— y ni sale a la red.</para>
    ///
    /// <para><b>Y un id que no cuadre con ninguno de los dos es <c>false</c>, no una excepción</b>:
    /// el contrato de <c>VerifyAsync</c> dice que id malformado, desconocido y registro fabricado
    /// devuelven todos lo mismo, sin distinguirse. Lo que sí sube es que la capacidad NO CONTESTE,
    /// porque eso no es «no cuadra» — es «no sé», y darlo por falso diría que un diploma bueno es
    /// falso.</para>
    /// </remarks>
    public bool Matches(string? certificateId, CertificateSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (string.IsNullOrWhiteSpace(certificateId)) return false;

        if (EsHeredado(certificateId))
        {
            return _heredado?.Matches(certificateId, subject) ?? false;
        }

        var sello = certificateId.Trim();
        sello = sello.StartsWith(Prefix, StringComparison.Ordinal) ? sello[Prefix.Length..] : sello;

        return Bloquear(() => ComprobarAsync(Contenido(subject), sello));
    }

    /// <summary>
    /// Lo que se sella: la MISMA forma que usaba la llave local, normalizada igual.
    /// </summary>
    /// <remarks>
    /// El separador es <c>|</c> y va entre dos valores normalizados; el contenido viaja dentro del
    /// cuerpo de la petición, nunca en la URL, porque ES el sujeto — un curso y un alumno escritos
    /// en el log de cada proxy sería exactamente lo que el sello existe para no publicar.
    /// </remarks>
    private static string Contenido(CertificateSubject subject)
        => $"{subject.CourseId.Trim().ToLowerInvariant()}|{subject.Student.Trim().ToLowerInvariant()}";

    /// <summary>Si el id tiene la forma del esquema anterior (<c>cert-</c> + 32 hex).</summary>
    private static bool EsHeredado(string id)
    {
        var cuerpo = id.Trim();
        if (cuerpo.StartsWith(Prefix, StringComparison.Ordinal)) cuerpo = cuerpo[Prefix.Length..];

        return cuerpo.Length == 32 && cuerpo.All(Uri.IsHexDigit);
    }

    private async Task<string> SellarAsync(string contenido)
    {
        using var http = Client();
        using var respuesta = await http.PostAsJsonAsync(
            "v1/seals", new SealRequest(_settings.SealPurpose, contenido), Json);

        await GritarSiFalla(respuesta);

        var sello = await respuesta.Content.ReadFromJsonAsync<SealResponse>(Json)
            ?? throw new InvalidOperationException("Api.Signing devolvió un sello vacío.");

        return sello.Seal;
    }

    private async Task<bool> ComprobarAsync(string contenido, string sello)
    {
        using var http = Client();
        using var respuesta = await http.PostAsJsonAsync(
            "v1/seals/verify", new VerifySealRequest(_settings.SealPurpose, contenido, sello), Json);

        // Que no cuadre es una respuesta, no un fallo: es el caso normal de una verificación
        // pública, donde cualquiera puede preguntar por un id inventado.
        if (respuesta.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest) return false;

        // Que no haya NINGUNA llave del propósito es un despliegue a medio configurar, no un
        // sello falso. Confundirlos manda a buscar un ataque donde falta un paso de despliegue.
        await GritarSiFalla(respuesta);
        return true;
    }

    private HttpClient Client()
    {
        var http = _factory.CreateClient(ClientName);
        if (http.BaseAddress is null)
        {
            http.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
        }
        if (!http.DefaultRequestHeaders.Contains(ApiKeyHeader) && !string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeader, _settings.ApiKey);
        }
        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds));
        return http;
    }

    private static async Task GritarSiFalla(HttpResponseMessage respuesta)
    {
        if (respuesta.IsSuccessStatusCode) return;

        var motivo = "sin motivo legible";
        try
        {
            var problema = await respuesta.Content.ReadFromJsonAsync<ProblemDto>(Json);
            if (!string.IsNullOrWhiteSpace(problema?.Detail)) motivo = problema!.Detail!;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
            // Un cuerpo que no es un problema tampoco es un motivo.
        }

        throw new InvalidOperationException(
            $"Api.Signing no pudo atender la credencial ({(int)respuesta.StatusCode}): {motivo}");
    }

    /// <summary>
    /// El seam es SÍNCRONO, así que acá se espera. Y se traduce lo que no es «no cuadra».
    /// </summary>
    /// <remarks>
    /// <b>Cambiar <c>ICertificateIdSigner</c> a asíncrono cruza cuatro consumidores</b> y no es lo
    /// que este cableado viene a decidir. Bloquear es feo y está acotado: son dos llamadas de una
    /// operación que ya era de red en todo lo demás del vertical.
    /// </remarks>
    private static T Bloquear<T>(Func<Task<T>> llamada)
    {
        try
        {
            return llamada().GetAwaiter().GetResult();
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(
                "Api.Signing no responde, así que la credencial no se emite ni se da por buena. "
                + "Comprobar el sello contra el sujeto es lo que impide fabricar un diploma; darlo "
                + "por bueno sin poder comprobarlo vaciaría esa garantía.");
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException(
                "Api.Signing tardó demasiado en contestar. La credencial no se emite ni se da por buena.");
        }
    }

    // ── Lo que viaja ────────────────────────────────────────────────────────

    private sealed record SealRequest(string? Purpose, string? Payload);

    private sealed record VerifySealRequest(string? Purpose, string? Payload, string? Seal);

    private sealed record SealResponse(string Seal, string KeyId);

    private sealed record ProblemDto(string? Title, string? Detail, string? Code);
}
