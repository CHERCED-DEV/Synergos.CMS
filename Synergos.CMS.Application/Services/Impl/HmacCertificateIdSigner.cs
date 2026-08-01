using System.Security.Cryptography;
using System.Text;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ICertificateIdSigner"/> — el id del certificado es un HMAC-SHA256
/// truncado del sujeto <c>(curso, alumno)</c>, en hex minúscula y con prefijo
/// <c>cert-</c>.
/// </summary>
/// <remarks>
/// <para><b>Formato:</b> <c>cert-{32 hex}</c> — 128 bits del MAC. Al contrario que el
/// token de una entrada (<c>HmacTicketSigner</c>), aquí el payload NO viaja: el id es
/// OPACO. Es deliberado y es la diferencia de fondo entre los dos casos — el payload de
/// una entrada dice de qué evento es; el de un certificado diría de qué alumno es, y ese
/// id se imprime en un diploma y viaja en cada verificación pública.</para>
///
/// <para>128 bits bastan: no hay nada que "adivinar" (el atacante tendría que acertar un
/// valor uniformemente distribuido en 2^128), y el id sigue cabiendo cómodo en un QR y en
/// una línea impresa. El esquema anterior tenía 31 bits Y era calculable sin secreto.</para>
///
/// <para><b>Normalización.</b> Curso y alumno se recortan y se pasan a minúscula
/// invariante antes de entrar al MAC. Sin eso, <c>"Juan@X.co"</c> y <c>"juan@x.co"</c>
/// —el mismo alumno para el motor de matrícula, que ya compara case-insensitive—
/// producirían DOS credenciales distintas del mismo curso, y la idempotencia que el
/// contrato promete dependería de cómo viniera escrito el correo esa vez.</para>
///
/// <para><b>Sin llave no se firma.</b> El ctor rechaza una llave vacía, igual que
/// <c>HmacTicketSigner</c>: un firmante con llave vacía produce ids que cualquiera
/// recalcula, que es peor que no firmar porque aparenta lo contrario.</para>
///
/// <para>Lógica pura (ADR 0002): solo <c>System.Security.Cryptography</c> del BCL.</para>
/// </remarks>
public sealed class HmacCertificateIdSigner : ICertificateIdSigner
{
    /// <summary>Prefijo legible del id — no aporta seguridad, aporta que se reconozca a simple vista.</summary>
    public const string Prefix = "cert-";

    /// <summary>Bytes del MAC que se conservan (16 → 32 caracteres hex → 128 bits).</summary>
    private const int IdBytes = 16;

    /// <summary>
    /// Etiqueta de versión DENTRO del payload: si algún día cambia el esquema, cambiar
    /// esta constante basta para que los ids nuevos no colisionen con los viejos.
    /// </summary>
    private const string PayloadVersion = "certid.v1";

    private readonly byte[] _key;

    public HmacCertificateIdSigner(byte[] key)
    {
        if (key is null || key.Length == 0)
        {
            throw new ArgumentException(
                "La llave de firma de certificados es obligatoria: sin ella el id sería calculable por cualquiera.",
                nameof(key));
        }
        _key = key;
    }

    public string Sign(CertificateSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (string.IsNullOrWhiteSpace(subject.CourseId) || string.IsNullOrWhiteSpace(subject.Student))
        {
            throw new ArgumentException(
                "El certificado necesita curso y alumno para derivar su id.", nameof(subject));
        }

        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(BuildPayload(subject)));
        return Prefix + Convert.ToHexString(mac, 0, IdBytes).ToLowerInvariant();
    }

    public bool Matches(string? certificateId, CertificateSubject subject)
    {
        if (string.IsNullOrWhiteSpace(certificateId) || subject is null
            || string.IsNullOrWhiteSpace(subject.CourseId) || string.IsNullOrWhiteSpace(subject.Student))
        {
            return false;
        }

        var expected = Sign(subject);
        var provided = certificateId.Trim();

        // Tiempo constante — nunca `==` de string. Misma regla que HmacTicketSigner y
        // PaymentWebhookVerifier. Longitudes distintas se descartan antes (comparar
        // buffers de distinto tamaño lanza).
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// El payload que entra al MAC. El separador <c>'|'</c> no aparece en ids de curso
    /// ni en correos, así que <c>(a, bc)</c> y <c>(ab, c)</c> no pueden colapsar en el
    /// mismo payload.
    /// </summary>
    private static string BuildPayload(CertificateSubject subject)
        => $"{PayloadVersion}|{Normalize(subject.CourseId)}|{Normalize(subject.Student)}";

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
