using System.Globalization;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El contenido de la red de Blogs en las lecturas genéricas (012-blogs-social, I4): lo que autoró el CMS y lo que
/// publican los miembros, sobre <see cref="IContentStream"/>, como catálogo neutro. Se busca por el título, el cuerpo y
/// el autor, se refina por categoría, autor y etiqueta, y sale de lo más reciente a lo más antiguo, como Explorar.
/// </summary>
/// <remarks>
/// <para><b>Un miembro sale por su cara pública</b> (I0): su alias en la faceta y en los atributos y su llave en
/// <c>autorId</c>, nunca el correo, que es como lo guarda el stream. La semilla de la demo y los autores del CMS no son
/// miembros y salen como están, igual que en <c>BlogsController</c>.</para>
///
/// <para><b>Lo de quien lee no está aquí</b>: su reacción y los conteos de reacciones los pone por petición
/// <c>LecturaDelCatalogoDeBlogs</c> (Web). Este catálogo es el mismo para todos.</para>
/// </remarks>
public static class CatalogoDeBlogs
{
    /// <summary>La clave de Blogs en <c>api/lecturas/{vertical}</c>.</summary>
    public const string Vertical = "blogs";

    /// <summary>El campo de la faceta de categoría (<c>?filtro.categoria=</c>): la página de categoría del artículo.</summary>
    public const string CampoCategoria = "categoria";

    /// <summary>El campo de la faceta de autor (<c>?filtro.autor=</c>): su alias.</summary>
    public const string CampoAutor = "autor";

    /// <summary>El campo de la faceta de etiqueta (<c>?filtro.etiqueta=</c>): un hashtag del cuerpo, sin <c>#</c>.</summary>
    public const string CampoEtiqueta = "etiqueta";

    /// <summary>
    /// El contenido de la red como lectura genérica.
    /// </summary>
    /// <param name="comentarios">Cuántos comentarios aprobados tiene el hilo de una publicación, o <c>null</c> si no se
    /// sabe: sin él, el atributo <c>comentarios</c> no viaja (no se afirma un cero).</param>
    public static ILecturaDelCatalogo Lectura(
        ICatalogSource<ContentStreamItem> fuente,
        IIdentidadPublica identidad,
        CatalogSettings ajustes,
        Func<ContentStreamItem, int>? comentarios = null)
    {
        ArgumentNullException.ThrowIfNull(identidad);
        ArgumentNullException.ThrowIfNull(ajustes);
        return new LecturaDelCatalogo<ContentStreamItem>(
            Vertical,
            fuente,
            new InMemoryCatalogIndex<ContentStreamItem>(Descriptor(identidad), ajustes),
            Proyeccion(identidad, comentarios),
            ajustes.MaxTake);
    }

    /// <summary>Lo que Blogs declara de su catálogo.</summary>
    internal static CatalogDescriptor<ContentStreamItem> Descriptor(IIdentidadPublica identidad) => new(
        idOf: i => i.Id,
        searchFields: new[]
        {
            new CatalogSearchField<ContentStreamItem>(5, i => i.CmsTitle),
            new CatalogSearchField<ContentStreamItem>(3, i => i.Body),
            new CatalogSearchField<ContentStreamItem>(2, i => Autor(i.Author, identidad).Nombre),
        },
        defaultOrder: publicaciones => publicaciones.OrderByDescending(i => i.CreatedUtc),
        filters: new CatalogFilter<ContentStreamItem>[]
        {
            new CatalogTermFilter<ContentStreamItem>(
                CampoCategoria,
                "Categoría",
                i => string.IsNullOrWhiteSpace(i.Category) ? Array.Empty<string>() : new[] { i.Category }),
            new CatalogTermFilter<ContentStreamItem>(CampoAutor, "Autor", i => new[] { Autor(i.Author, identidad).Alias }),
            new CatalogTermFilter<ContentStreamItem>(CampoEtiqueta, "Etiqueta", EtiquetasDe),
        });

    /// <summary>Una publicación en la forma neutra: la tarjeta con lo que la app pinta, y la ficha con el cuerpo entero.</summary>
    internal static ProyeccionDelCatalogo<ContentStreamItem> Proyeccion(
        IIdentidadPublica identidad,
        Func<ContentStreamItem, int>? comentarios) => new(
        Id: i => i.Id,
        Elemento: i => Elemento(i, identidad, comentarios),
        Ficha: i => new FichaDelCatalogo(
            Elemento: Elemento(i, identidad, comentarios),
            Descripcion: i.Body,
            Imagenes: string.IsNullOrWhiteSpace(i.MediaUrl) ? [] : [i.MediaUrl],
            Opciones: [],
            Preguntas: []));

    /// <summary>Quien firma una publicación tal como sale: un miembro por su cara pública; los demás, como están.</summary>
    /// <summary>
    /// Las etiquetas de una publicación: las <c>#etiquetas</c> de su cuerpo y, si es de un artículo del CMS, las que le
    /// puso el editor, sin repetir (012-blogs-social, I6). Con ellas se filtra y se arma la página de una etiqueta.
    /// </summary>
    internal static IReadOnlyList<string> EtiquetasDe(ContentStreamItem i)
        => PublicacionesDeLaRed.Etiquetas(i.Body)
            .Concat(i.Tags ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static AutorDeLaRed Autor(ContentAuthor autor, IIdentidadPublica identidad)
    {
        if (CaraPublica.EsCorreo(autor.Id))
        {
            var cara = identidad.De(autor.Id);
            return new AutorDeLaRed(cara.Llave, cara.Alias, cara.Nombre, null, false);
        }

        return new AutorDeLaRed(autor.Id, autor.Handle, autor.DisplayName, autor.AvatarUrl, autor.Verified);
    }

    private static ElementoDelCatalogo Elemento(
        ContentStreamItem i,
        IIdentidadPublica identidad,
        Func<ContentStreamItem, int>? comentarios)
    {
        var autor = Autor(i.Author, identidad);
        var atributos = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tipo"] = i.Kind,
            ["cuerpo"] = i.Body,
            ["autor"] = autor.Nombre,
            ["autorId"] = autor.Id,
            ["alias"] = autor.Alias,
            ["fecha"] = DateTime.SpecifyKind(i.CreatedUtc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture),
            ["republicaciones"] = i.Metrics.Reposts.ToString(CultureInfo.InvariantCulture),
        };
        Poner(atributos, "autorImagen", autor.Imagen);
        if (autor.Verificado)
        {
            atributos["verificado"] = "si";
        }

        Poner(atributos, CampoCategoria, i.Category);
        Poner(atributos, "etiquetas", string.Join(',', EtiquetasDe(i)));
        Poner(atributos, "menciones", string.Join(',', PublicacionesDeLaRed.Menciones(i.Body).Distinct(StringComparer.Ordinal)));
        Poner(atributos, "pagina", i.CmsUrl);
        if (!string.IsNullOrWhiteSpace(i.MediaUrl))
        {
            // El de quien publicó gana; el recorte del cuerpo es la red para lo que se publicó sin él, como en la interina.
            atributos["imagenAlt"] = string.IsNullOrWhiteSpace(i.MediaAlt)
                ? PublicacionesDeLaRed.TextoAlternativo(i.Body)
                : i.MediaAlt.Trim();
        }

        if (comentarios is not null)
        {
            atributos["comentarios"] = comentarios(i).ToString(CultureInfo.InvariantCulture);
        }

        return new ElementoDelCatalogo(
            Id: i.Id,
            Titulo: string.IsNullOrWhiteSpace(i.CmsTitle) ? PublicacionesDeLaRed.Resumen(i.Body) : i.CmsTitle.Trim(),
            Imagen: string.IsNullOrWhiteSpace(i.MediaUrl) ? null : i.MediaUrl,
            Precio: null,
            PrecioAntes: null,
            Calificacion: null,
            Disponibles: null,
            Atributos: atributos);
    }

    private static void Poner(Dictionary<string, string> atributos, string clave, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
        {
            atributos[clave] = valor.Trim();
        }
    }
}

/// <summary>Quien firma una publicación de la red, tal como sale en público.</summary>
/// <param name="Id">Su llave pública (un miembro) o su id (la semilla, un autor del CMS).</param>
/// <param name="Alias">Su alias, sin <c>@</c>.</param>
/// <param name="Nombre">Su nombre público.</param>
/// <param name="Imagen">Su avatar, o <c>null</c>.</param>
/// <param name="Verificado">Si consta que alguien lo verificó.</param>
public sealed record AutorDeLaRed(string Id, string Alias, string Nombre, string? Imagen, bool Verificado);

/// <summary>
/// El contenido de la red como fuente del catálogo: todo el feed «para ti», en una sola lectura del stream.
/// </summary>
/// <remarks>
/// <para>Es lo que leía Explorar (las cien primeras del mismo feed), sin el corte en cien. El tope de páginas sólo evita
/// que un stream que no deja de devolver cursores deje colgada la lectura.</para>
/// <para><b>Una lectura, no una por página</b> (012-blogs-social, el catálogo lento): el stream del CMS lee todo su
/// almacén en cada página, así que leerlo de cien en cien costaba una lectura entera por cada cien publicaciones. Con las
/// 1336 de la base real, Explorar y las tendencias tardaban de 14 a 27 s por llamada.</para>
/// </remarks>
public sealed class FuenteDeLaRed : ICatalogSource<ContentStreamItem>
{
    /// <summary>Cuántas publicaciones pide cada lectura: todas las de una red de hoy, de una vez.</summary>
    internal const int PorPagina = 5000;

    private const int TopeDePaginas = 20;

    private readonly IContentStream _stream;

    /// <summary>Sobre el stream de la red.</summary>
    public FuenteDeLaRed(IContentStream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContentStreamItem>> GetAllAsync(
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var publicaciones = new List<ContentStreamItem>();
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var pagina = 0; pagina < TopeDePaginas; pagina++)
        {
            var leida = await _stream
                .GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou, Cursor: cursor, PageSize: PorPagina), cancellationToken)
                .ConfigureAwait(false);

            // Si alguien publica entre dos páginas, el desplazamiento repite una: sale una vez.
            publicaciones.AddRange(leida.Items.Where(i => vistas.Add(i.Id)));
            cursor = leida.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        return publicaciones;
    }
}
