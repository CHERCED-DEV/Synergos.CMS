namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Configuración del almacén de ficheros privados (T6) — sección
/// <c>Synergos:PrivateFileStore</c>.
/// </summary>
/// <remarks>
/// <b>El default de <see cref="StorageRoot"/> es la propiedad de seguridad más
/// importante de este POCO:</b> <c>App_Data/</c> está fuera de <c>wwwroot</c>, así que
/// ningún middleware de ficheros estáticos lo sirve. Apuntarlo a <c>wwwroot/</c> haría
/// PÚBLICOS todos los documentos subidos, que es exactamente lo que este almacén evita.
/// </remarks>
public sealed class PrivateFileStoreSettings
{
    /// <summary>
    /// Raíz de almacenamiento, relativa al ContentRoot. Default <c>App_Data/</c> —
    /// fuera de wwwroot, no servido por static-files.
    /// </summary>
    public string StorageRoot { get; init; } = "App_Data/";

    /// <summary>Tamaño máximo por fichero. Default 10 MB.</summary>
    public long MaxFileSizeBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>
    /// Content-types aceptados al subir. Default: PDF + JPEG + PNG, que es lo que la
    /// UI de Gobierno ofrece en su <c>accept</c>.
    /// </summary>
    public IReadOnlyList<string> AllowedContentTypes { get; init; } = new[]
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
    };
}
