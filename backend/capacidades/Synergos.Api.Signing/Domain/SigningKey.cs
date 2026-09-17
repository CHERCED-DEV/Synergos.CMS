
namespace Synergos.Api.Signing.Domain;

/// <summary>
/// Una llave de firma, con propósito y ciclo de vida.
/// </summary>
/// <param name="Id">Identificador. Viaja DENTRO del token para poder verificar con la llave correcta.</param>
/// <param name="Purpose">Para qué firma: <c>eventos.entrada</c>, <c>gov.certificado</c>.</param>
/// <param name="Secret">El material, en base64.</param>
/// <param name="CreatedAtUtc">Cuándo nació.</param>
/// <param name="RetiredAtUtc">Cuándo dejó de firmar. Sigue verificando.</param>
/// <remarks>
/// <para><b>Una llave por propósito.</b> Con una sola llave para todo, quien consigue firmar una
/// entrada de evento puede firmar un certificado gubernamental — y la separación entre los dos
/// deja de existir en el momento en que hay una filtración.</para>
///
/// <para><b>Retirar NO invalida lo ya firmado.</b> Una llave retirada deja de emitir pero sigue
/// verificando: si dejara de verificar, retirar una llave invalidaría de golpe todas las
/// entradas vendidas. La rotación consiste en emitir con la nueva mientras la vieja verifica lo
/// que ya circula.</para>
/// </remarks>
public sealed record SigningKey(
    string Id, string Purpose, string Secret, DateTimeOffset CreatedAtUtc, DateTimeOffset? RetiredAtUtc = null)
{
    /// <summary>Si todavía puede EMITIR firmas.</summary>
    public bool CanSign => RetiredAtUtc is null;
}
