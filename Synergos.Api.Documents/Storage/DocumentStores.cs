using Microsoft.Extensions.Options;
using Synergos.Api.Documents.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Documents.Storage;

/// <summary>Dónde vive el almacén de esta capacidad, y con qué llave firma sus enlaces.</summary>
public sealed class DocumentStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "documents");

    /// <summary>
    /// Llave con la que se firman los enlaces de descarga.
    /// </summary>
    /// <remarks>
    /// Sin configurar, el servicio genera una <b>por arranque</b>: los enlaces emitidos dejan de
    /// valer al reiniciar. Es molesto y es lo correcto — la alternativa sería una llave fija en
    /// el repositorio, que convierte cada enlace firmado en un enlace público permanente.
    /// </remarks>
    public string? SigningKey { get; set; }
}

public interface IDocumentStore
{
    Document? Find(string id);
    IReadOnlyList<Document> ForOwner(Ref owner);
    void Put(Document document);
}

/// <summary>El contenido binario, aparte del metadato.</summary>
/// <remarks>
/// Separado porque los patrones de acceso no se parecen: el metadato se lista y se filtra, el
/// binario se lee entero una vez. Meterlos juntos en un JSON haría que listar diez documentos
/// cargara diez ficheros completos en memoria.
/// </remarks>
public interface IBlobStore
{
    byte[]? Read(string id);
    void Write(string id, byte[] content);
    void Delete(string id);
}

public sealed class FileSystemDocumentStore : IDocumentStore
{
    private readonly JsonCollectionStore<Document> _store;

    public FileSystemDocumentStore(IOptions<DocumentStorageOptions> options)
        => _store = new JsonCollectionStore<Document>(options.Value.Root, "documents", d => d.Id);

    public Document? Find(string id) => _store.Find(id);
    public IReadOnlyList<Document> ForOwner(Ref owner) => _store.Where(d => d.Owner == owner);
    public void Put(Document document) => _store.Put(document);
}

public sealed class FileSystemBlobStore : IBlobStore
{
    private readonly string _root;

    public FileSystemBlobStore(IOptions<DocumentStorageOptions> options)
    {
        _root = Path.Combine(options.Value.Root, "blobs");
        Directory.CreateDirectory(_root);
    }

    public byte[]? Read(string id)
    {
        var path = PathFor(id);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public void Write(string id, byte[] content)
    {
        var tmp = PathFor(id) + ".tmp";
        File.WriteAllBytes(tmp, content);
        File.Move(tmp, PathFor(id), overwrite: true);
    }

    public void Delete(string id)
    {
        var path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
    }

    // El identificador lo genera este servicio (un GUID sin guiones), así que no puede traer
    // separadores de ruta. Aun así se valida: un id que viniera de otro lado y trajera '..'
    // convertiría este almacén en lectura y escritura arbitraria del disco.
    private string PathFor(string id)
    {
        if (id.Any(c => !char.IsAsciiLetterOrDigit(c)))
        {
            throw new ArgumentException("Un identificador de blob solo puede ser alfanumérico.", nameof(id));
        }
        return Path.Combine(_root, id + ".bin");
    }
}
