namespace Synergos.CMS.Interfaces;

/// <summary>
/// Contenido de un fichero privado recuperado del almacén: los bytes + con qué
/// content-type y nombre original se guardó.
/// </summary>
public sealed record PrivateFileContent(
    byte[] Content,
    string ContentType,
    string FileName);

/// <summary>
/// Almacén de FICHEROS PRIVADOS (bytes) — el único sitio del proyecto donde se
/// persiste un binario subido por un usuario (T6). Es el contrapeso de la biblioteca
/// de medios de Umbraco: <c>wwwroot/media</c> es PÚBLICO por construcción (lo sirve
/// <c>UseStaticFiles</c>), así que sirve para fotos de producto, no para la cédula de
/// un ciudadano.
/// </summary>
/// <remarks>
/// <para><b>Tres propiedades que lo hacen privado</b>, y las tres importan:</para>
/// <list type="number">
/// <item><b>Fuera de <c>wwwroot</c></b> (bajo <c>App_Data/</c>): ningún middleware de
///   ficheros estáticos lo alcanza. Es lo que separa este almacén de <c>/media</c>.</item>
/// <item><b>Id opaco</b>: la clave la genera el almacén, no el llamador, y no es
///   adivinable ni enumerable — al revés que el radicado <c>SG-2026-000001</c>, que se
///   cuenta.</item>
/// <item><b>Cifrado at-rest</b>: quien lea el disco no lee la cédula. Mismo criterio
///   que <see cref="IPhiStore"/> (ADR 0098) — dato sensible en reposo va cifrado.</item>
/// </list>
/// <para>Aun así, el id opaco NO es la autorización: <b>quien sirva estos bytes debe
/// comprobar permiso en cada descarga</b> (dueño o funcionario). El id evita la
/// enumeración; el gate evita el acceso. Nunca montar esta carpeta como estática.</para>
/// <para>Guarda bytes, no dominio: la metadata de negocio (a qué expediente pertenece,
/// quién lo subió) vive en el agregado del vertical, no aquí. Mismo reparto que
/// <see cref="IJsonEntityStore"/>.</para>
/// </remarks>
public interface IPrivateFileStore
{
    /// <summary>
    /// Guarda <paramref name="content"/> y devuelve el <b>id opaco</b> con el que se
    /// recupera. El id lo genera el almacén (no se acepta uno del llamador, que podría
    /// elegirlo adivinable o pisar otro fichero).
    /// </summary>
    Task<string> SaveAsync(
        string scope,
        byte[] content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recupera el fichero por su id opaco, o <c>null</c> si no existe o no se pudo
    /// descifrar (fail-safe: nunca devuelve el contenido crudo ilegible).
    /// </summary>
    Task<PrivateFileContent?> ReadAsync(
        string scope,
        string fileId,
        CancellationToken cancellationToken = default);

    /// <summary>Borra el fichero. Devuelve <c>false</c> si no existía.</summary>
    Task<bool> DeleteAsync(
        string scope,
        string fileId,
        CancellationToken cancellationToken = default);
}
