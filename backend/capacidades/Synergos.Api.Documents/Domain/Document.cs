using Synergos.Core;

namespace Synergos.Api.Documents.Domain;

/// <summary>
/// Un fichero guardado, con su metadato.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="Owner">De quién es — opaco.</param>
/// <param name="Name">Nombre visible.</param>
/// <param name="ContentType">Tipo MIME.</param>
/// <param name="Size">Tamaño en bytes.</param>
/// <param name="Sha256">Huella del contenido, en hexadecimal.</param>
/// <param name="UploadedAtUtc">Cuándo entró.</param>
/// <param name="Deleted">Si se retiró.</param>
/// <remarks>
/// <para><b>La huella se guarda y se devuelve</b> para que quien descargue pueda comprobar que
/// recibió lo mismo que se subió. En un resultado clínico o un certificado, "el fichero se
/// corrompió en tránsito" y "el fichero dice otra cosa" son indistinguibles sin ella.</para>
///
/// <para><b>Borrar marca, no destruye el metadato.</b> "Nunca existió" y "existió y se retiró"
/// son cosas distintas para quien reclama y para quien audita. El contenido sí se borra — eso es
/// lo que pide el derecho al olvido; lo que queda es el rastro de que hubo algo.</para>
/// </remarks>
public sealed record Document(
    string Id,
    Ref Owner,
    string Name,
    string ContentType,
    long Size,
    string Sha256,
    DateTimeOffset UploadedAtUtc,
    bool Deleted = false);
