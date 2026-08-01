namespace Synergos.CMS.Interfaces;

/// <summary>
/// A quién y por qué se le emite una credencial: el curso completado y el
/// identificador del alumno. Es lo ÚNICO de lo que depende el id del certificado —
/// de ahí que re-emitir sea idempotente.
/// </summary>
/// <remarks>
/// <paramref name="Student"/> es el identificador de la cuenta (hoy el correo). NO
/// se publica ni se puede recuperar desde el id: entra en el HMAC y sale un valor
/// opaco. Ver <see cref="ICertificateIdSigner"/>.
/// </remarks>
public sealed record CertificateSubject(string CourseId, string Student);

/// <summary>
/// Deriva —y comprueba— el id de un certificado de finalización (dominio Educación).
/// Es la seam que hace que la credencial sea <b>infalsificable</b>: sin la llave del
/// servidor no se puede calcular el id de nadie.
/// </summary>
/// <remarks>
/// <para><b>El problema que resuelve.</b> El id era
/// <c>"cert-" + FNV-1a-32(courseId + "|" + student)</c>: un hash NO criptográfico, sin
/// secreto, enmascarado a 31 bits (~2.100 millones de valores). Cualquiera que supiera
/// el id de un curso —público, sale del catálogo— y adivinara el correo de un alumno
/// calculaba su id de certificado en una línea de código. Y como
/// <c>Certificate.StudentName</c> viaja en la respuesta, abrir esa verificación al
/// público habría convertido el certificado en un padrón consultable de quién estudió
/// qué. Mientras nadie expuso <c>VerifyAsync</c> el agujero no se podía tocar; el
/// endpoint público lo habría abierto.</para>
///
/// <para><b>Por qué NO se reusa <see cref="ITicketSigner"/>,</b> que resuelve el mismo
/// problema para el QR de las entradas: su token lleva el payload <b>legible a
/// propósito</b> (<c>SYN-TKT-{evento}-{ticket}-v{n}</c>), y ahí eso es correcto —un
/// operador quiere leer de qué evento habla un QR—. Aquí el payload es
/// <c>(curso, ALUMNO)</c>: publicar el identificador del titular dentro del id que se
/// imprime en el diploma y viaja en cada verificación es exactamente lo que hay que
/// evitar. Además su llave sale de <c>Synergos:Events:TicketSigningSecret</c>: rotar el
/// secreto de Eventos invalidaría todos los diplomas de Educación, dos ciclos de vida
/// que no tienen por qué ir atados. Se reusa su <b>forma</b> (HMAC-SHA256 del BCL, hex
/// minúscula, comparación en tiempo constante, llave persistida y fail-closed sin
/// llave), no el seam.</para>
///
/// <para>Vive en <c>Interfaces</c> y se implementa en <c>Application</c> con
/// <c>System.Security.Cryptography</c> (BCL puro — no viola ADR 0002).</para>
/// </remarks>
public interface ICertificateIdSigner
{
    /// <summary>
    /// Devuelve el id ESTABLE y opaco de la credencial de <paramref name="subject"/>.
    /// Determinista: el mismo (curso, alumno) da siempre el mismo id —de eso vive la
    /// idempotencia de la emisión—, y sin la llave no se puede calcular.
    /// </summary>
    string Sign(CertificateSubject subject);

    /// <summary>
    /// Comprueba, en tiempo constante, que <paramref name="certificateId"/> es
    /// realmente el id que le corresponde a <paramref name="subject"/>.
    /// </summary>
    /// <remarks>
    /// Es lo que impide que el índice de certificados emitidos sea la autoridad. Quien
    /// consiga escribir en el almacén podría inventar un registro con el id que quiera
    /// y el nombre de quien quiera; al verificar se recalcula el id desde el sujeto
    /// guardado y, si no cuadra, la credencial no vale. La autoridad es la llave.
    /// </remarks>
    bool Matches(string? certificateId, CertificateSubject subject);
}
