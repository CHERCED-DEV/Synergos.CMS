using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>Una tarjeta del listado: el nombre, el dato que va debajo, la imagen si la hay y las iniciales si no.</summary>
public sealed record TarjetaDelListado(string Id, string Titulo, string? Subtitulo, string? Imagen, string Iniciales);

/// <summary>
/// Lo que pinta el bloque «Listado del catálogo» (<c>elementCompCatalogList</c>, 003-salud-cita D-003-V2): la lectura
/// genérica de un vertical (la misma de <c>api/lecturas/{vertical}/catalogo</c>) en tarjetas. «Equipo» de Salud compone
/// así el directorio de <c>professionalPage</c>, en vez de tres médicos escritos a mano.
/// </summary>
/// <remarks>
/// Sin nada que listar el bloque no pinta nada, y su sección tampoco (se degrada por AUSENCIA). Una lectura que falla
/// tampoco pinta nada: no se dice «no hay profesionales» de lo que no se pudo leer.
/// </remarks>
public static class ListadoDelCatalogoEnLaPagina
{
    /// <summary>Cuántos se muestran si el editor no dice cuántos.</summary>
    public const int MaximoPorDefecto = 12;

    /// <summary>El tope: una página del listado de la lectura genérica.</summary>
    public const int MaximoPermitido = 48;

    /// <summary>El máximo que escribió el editor (texto con dígitos), dentro de 1…<see cref="MaximoPermitido"/>.</summary>
    public static int Maximo(string? escrito)
        => int.TryParse(escrito, out var maximo) && maximo > 0 ? Math.Min(maximo, MaximoPermitido) : MaximoPorDefecto;

    /// <summary>Las tarjetas de una página del listado, con <paramref name="atributo"/> como dato bajo el nombre.</summary>
    public static IReadOnlyList<TarjetaDelListado> Tarjetas(ListadoDelCatalogo listado, string? atributo)
    {
        ArgumentNullException.ThrowIfNull(listado);
        var clave = atributo?.Trim();
        return listado.Elementos
            .Where(e => !string.IsNullOrWhiteSpace(e.Titulo))
            .Select(e => new TarjetaDelListado(
                e.Id,
                e.Titulo.Trim(),
                !string.IsNullOrEmpty(clave) && e.Atributos.TryGetValue(clave, out var valor) && !string.IsNullOrWhiteSpace(valor)
                    ? valor.Trim()
                    : null,
                string.IsNullOrWhiteSpace(e.Imagen) ? null : e.Imagen,
                Iniciales(e.Titulo)))
            .ToList();
    }

    /// <summary>
    /// Las iniciales del nombre, sin el título que lo antecede («Dra. Laura Méndez» → «LM»): las tarjetas de personas
    /// van sin foto (D-016-C18).
    /// </summary>
    public static string Iniciales(string? nombre)
    {
        var palabras = (nombre ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !p.EndsWith('.') && char.IsLetter(p[0]))
            .ToList();
        return palabras.Count switch
        {
            0 => string.Empty,
            1 => char.ToUpperInvariant(palabras[0][0]).ToString(),
            _ => string.Concat(char.ToUpperInvariant(palabras[0][0]), char.ToUpperInvariant(palabras[1][0])),
        };
    }

    /// <summary>
    /// Lee el listado de <paramref name="vertical"/> y lo vuelve tarjetas. Un vertical que no existe, uno sin nada
    /// publicado o una lectura caída dan la lista vacía, y el bloque no pinta nada.
    /// </summary>
    public static async Task<IReadOnlyList<TarjetaDelListado>> LeerAsync(
        IEnumerable<ILecturaDelCatalogo> lecturas,
        string? vertical,
        string? atributo,
        int maximo,
        ILogger? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lecturas);
        if (lecturas.DelVertical(vertical) is not { } lectura)
        {
            return [];
        }

        try
        {
            var consulta = new ConsultaDelCatalogo(
                Texto: null,
                Filtros: new Dictionary<string, IReadOnlyList<string>>(),
                Orden: null,
                Pagina: 1,
                PorPagina: Math.Clamp(maximo, 1, MaximoPermitido));
            return Tarjetas(await lectura.ListarAsync(consulta, cancellationToken), atributo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.LogWarning(ex, "El listado del catálogo de {Vertical} no se pudo leer; el bloque no se pinta.", vertical);
            return [];
        }
    }
}
