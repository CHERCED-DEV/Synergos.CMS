namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Configura el ASP.NET Core Data Protection master keyring para
/// soportar deployments multi-instance. Sección
/// <c>Synergos:DataProtection</c>. Cap-260 Batch C (Olas 257-258).
/// </summary>
/// <remarks>
/// Default por ASP.NET Core es ephemeral (in-memory) en runtime
/// scenarios genéricos o per-app filesystem dentro de IIS/Kestrel.
/// Para deployments multi-instance (LB con N pods, container
/// orquestación, blue/green), todos los nodos deben compartir el
/// keyring para que un secret cifrado en pod A pueda descifrarse
/// en pod B. Si no, los <c>FileSystemMemberTwoFactorStore</c> emiten
/// <c>CryptographicException</c> en el pod equivocado y fallback al
/// legacy plaintext path (Olas 178-180 + 221-224).
///
/// Settings vacío (default) preserva el comportamiento per-instance
/// (no breaking change). Activar SHARED-RING cuando llegue scale-out.
/// </remarks>
public sealed class DataProtectionSettings
{
    /// <summary>
    /// Path absoluto a un directorio compartido entre instancias
    /// donde se persisten los keys del Data Protection. Vacío
    /// (default) = no override; ASP.NET Core usa su default (ephemeral
    /// o per-app filesystem según runtime). Recomendado para
    /// container deployments: bind mount o volume compartido en
    /// <c>/var/syn/dp-keys/</c> o equivalente.
    /// </summary>
    public string KeyringPath { get; init; } = string.Empty;

    /// <summary>
    /// Application name discriminator. Si dos apps comparten el mismo
    /// keyring filesystem pero tienen ApplicationName distinto,
    /// generan keys aisladas (encrypted en una NO descifra en la
    /// otra). Default <c>"Synergos.CMS"</c>. Cambiar si se ejecutan
    /// múltiples instalaciones del CMS sobre el mismo storage físico
    /// (ej. staging + prod montados en el mismo NFS).
    /// </summary>
    public string ApplicationName { get; init; } = "Synergos.CMS";

    /// <summary>
    /// Lifetime de cada key generado. Default 90 días — ASP.NET Core
    /// auto-rotata creando un key nuevo y marca el antiguo como
    /// "no se usa para nuevas operaciones pero sigue válido para
    /// descifrar". Bump a más largo si los secrets persistidos viven
    /// más que ese window (ej. 2FA secrets de members duran toda la
    /// vida del member).
    /// </summary>
    public int KeyLifetimeDays { get; init; } = 90;
}
