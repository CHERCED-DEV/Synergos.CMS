namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Configuración del vertical Educación — sección <c>Synergos:Academy</c>.
/// </summary>
public sealed class AcademySettings
{
    /// <summary>
    /// Secreto con el que se deriva el id de los certificados de finalización.
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
    /// </remarks>
    public string CertificateSigningSecret { get; init; } = string.Empty;
}
