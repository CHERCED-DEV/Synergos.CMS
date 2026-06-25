namespace Synergos.CMS.Interfaces;

/// <summary>
/// Almacén cifrado-at-rest para PHI de Healthcare (ADR 0098). Guarda y lee
/// documentos JSON opacos por <c>resourceType</c> + <c>key</c>; el cifrado y
/// la atomicidad son responsabilidad de la implementación. Es la única puerta
/// al disco para datos clínicos — ningún repo escribe PHI por fuera.
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.FileSystemEncryptedPhiStore</c>: envuelve el
/// JSON con <c>IDataProtector</c> (purpose <c>"Synergos.Healthcare.PHI.v1"</c>),
/// escribe atómico (temp + <c>File.Move</c>) en
/// <c>App_Data/syn-healthcare/{resourceType}/{key}.phi</c>. Disclaimer: cifrado
/// at-rest baseline, NO HIPAA-grade (sin rotación AES-256 dedicada, HSM, FIPS ni BAA).
///
/// <strong>Atomicidad</strong>: nunca <c>File.WriteAllText</c>/<c>AppendAllText</c>
/// directos (no atómicos ante crash) — siempre temp + rename.
/// </remarks>
public interface IPhiStore
{
    /// <summary>Persiste (cifrado, atómico) el JSON bajo resourceType/key. Sobrescribe.</summary>
    Task WriteAsync(string resourceType, Guid key, string json, CancellationToken cancellationToken);

    /// <summary>Lee y descifra el JSON, o null si no existe.</summary>
    Task<string?> ReadAsync(string resourceType, Guid key, CancellationToken cancellationToken);

    /// <summary>Lee y descifra todos los documentos de un resourceType. Vacío si no hay.</summary>
    Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken);

    /// <summary>Borra el documento. True si existía y se borró.</summary>
    Task<bool> DeleteAsync(string resourceType, Guid key, CancellationToken cancellationToken);
}
