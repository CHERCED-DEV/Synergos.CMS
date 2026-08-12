namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Quién custodia la llave que identifica un certificado (HU #45) — sección
/// <c>Synergos:Academy</c>.
/// </summary>
/// <remarks>
/// <para><b>Lo que se gana es la CUSTODIA, no el algoritmo.</b> El id del diploma ya era un HMAC
/// con llave del servidor y opaco (ADR 0124); lo que <c>HmacCertificateIdSigner</c> no sabe hacer
/// es <b>retirar una llave</b> ni decir con cuál se emitió cada cosa. <c>Api.Signing</c> sí:
/// crear, listar y retirar, y su <c>verify</c> prueba <b>todas</b> las llaves del propósito
/// —retiradas incluidas— para que rotar no invalide un diploma ya impreso.</para>
///
/// <para><b>Y va a <c>/v1/seals</c>, no a <c>/v1/signatures</c></b> (hallazgo #45). Aquel token
/// vence, no es determinista y publica su payload; las tres cosas son correctas para lo que ese
/// endpoint hace y ninguna sirve para identificar un diploma, que no vence, se re-emite igual y
/// lleva impreso a su titular dentro del contenido sellado.</para>
///
/// <para><b>El default es la llave local y no es una transición.</b> Un clon limpio emite y
/// verifica diplomas sin levantar nada.</para>
///
/// <para><b>⚠️ Encenderlo cambia el id de los certificados NUEVOS</b>, porque el algoritmo del
/// sello no es el del HMAC local. Los ya emitidos <b>siguen verificando</b> — el firmante
/// mantiene la llave local como verificador de los ids viejos, que es lo que impide que un QR
/// impreso deje de valer. Lo que no resuelve solo es que un alumno con diploma viejo que vuelva a
/// pedirlo reciba un id nuevo: ver la nota de despliegue en <c>.env.example</c>.</para>
///
/// <para><b>Con la capacidad caída no se puede verificar un diploma nuevo</b>, y hoy eso es una
/// operación local que no falla nunca. Es el precio de la custodia y se paga a conciencia: no se
/// cae a «lo doy por bueno», porque comprobar el sello contra el sujeto es justo lo que impide
/// que quien escriba en el almacén fabrique una credencial con el nombre que quiera.</para>
/// </remarks>
public sealed class AcademySettings
{
    /// <summary>
    /// Secreto con el que se deriva el id de los certificados de finalización, en modo
    /// <c>Stub</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Vacío NO significa "no firmar".</b> Si no se configura, el host genera una
    /// llave aleatoria la primera vez y la guarda CIFRADA en el almacén de entidades JSON,
    /// así que los ids siguen siendo infalsificables y —esto es lo que importa para un
    /// diploma— <b>el mismo certificado sigue verificando tras un reinicio</b>. Poblarlo es
    /// lo correcto en producción: permite rotar la llave y compartirla entre instancias.</para>
    /// <para><b>Por qué no reusa <c>Synergos:Events:TicketSigningSecret</c>:</b> son dos
    /// ciclos de vida distintos. Rotar el secreto con el que se firman los QR de las
    /// entradas —algo que se hace tras un incidente de puerta, o por temporada— invalidaría
    /// de paso todos los diplomas emitidos. Un diploma dura años; un QR de evento, una
    /// noche.</para>
    /// <para>No se pone un default literal a propósito: un secreto escrito en el repo es un
    /// secreto conocido, y un id derivado de una llave pública es calculable por cualquiera
    /// mientras aparenta lo contrario. Eso es exactamente el defecto que ADR 0124 corrige.</para>
    /// <para><b>En modo <c>Api</c> sigue haciendo falta</b>, y no como reliquia: es con lo que
    /// se verifican los ids emitidos ANTES del cableado. Borrarlo dejaría sin valer cada QR ya
    /// impreso.</para>
    /// </remarks>
    public string CertificateSigningSecret { get; init; } = string.Empty;

    /// <summary>
    /// <c>Stub</c> (default, la llave local) o <c>Api</c> (contra <c>Synergos.Api.Signing</c>).
    /// </summary>
    public string Mode { get; init; } = "Stub";

    /// <summary>Dónde vive la capacidad.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5218/";

    /// <summary>La llave compartida servicio↔servicio. Sin ella todo responde 401.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// El propósito bajo el que viven las llaves de este dominio en <c>Api.Signing</c>.
    /// </summary>
    /// <remarks>
    /// <b>Propio y no compartido con Eventos.</b> Rotar el secreto de las entradas invalidaría
    /// los diplomas si compartieran propósito, y son dos ciclos de vida que no tienen por qué ir
    /// atados — el mismo razonamiento por el que ADR 0124 no reusó <c>ITicketSigner</c>.
    /// </remarks>
    public string SealPurpose { get; init; } = "academy.certificate";

    /// <summary>
    /// Segundos de espera. Corto: sellar es una función pura y no deja nada a medias.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 10;
}
