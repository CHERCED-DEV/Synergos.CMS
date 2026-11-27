using System.Globalization;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>
/// El catálogo de la red con las reacciones de cada publicación (012-blogs-social, I4): cuántas hay de cada tipo y, con
/// sesión, la de quien lee. Envuelve a <see cref="CatalogoDeBlogs"/>, que es el mismo para todos.
/// </summary>
/// <remarks>
/// <para><b>Sin la reacción de quien lee, la tarjeta mentiría</b>: le diría «no reaccionaste» a quien sí lo hizo, y al
/// pulsar quitaría la reacción que creía poner. Es lo que la interina ponía en cada publicación
/// (<c>ToPostDto</c>), por petición y con el mismo servicio.</para>
///
/// <para>Va sólo a quien lee: el lector sale de la sesión, nunca de la petición, y la ruta no se cachea.</para>
/// </remarks>
public sealed class LecturaDelCatalogoDeBlogs : ILecturaDelCatalogo
{
    /// <summary>El atributo con las reacciones por tipo: <c>like:3,love:1</c>, sin los tipos en cero.</summary>
    public const string AtributoReacciones = "reacciones";

    /// <summary>El atributo con la reacción de quien lee; no viaja si no tiene una o no hay sesión.</summary>
    public const string AtributoMiReaccion = "miReaccion";

    /// <summary>El atributo con el avatar de quien firma, como lo pone <see cref="CatalogoDeBlogs"/>.</summary>
    private const string AtributoAutorImagen = "autorImagen";

    private readonly ILecturaDelCatalogo _catalogo;
    private readonly IReactionService _reacciones;
    private readonly IMemberAccessGate _sesion;

    /// <summary>Sobre el catálogo de la red, las reacciones y la sesión de quien lee.</summary>
    public LecturaDelCatalogoDeBlogs(ILecturaDelCatalogo catalogo, IReactionService reacciones, IMemberAccessGate sesion)
    {
        _catalogo = catalogo ?? throw new ArgumentNullException(nameof(catalogo));
        _reacciones = reacciones ?? throw new ArgumentNullException(nameof(reacciones));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    /// <inheritdoc />
    public string Vertical => _catalogo.Vertical;

    /// <inheritdoc />
    public async Task<ListadoDelCatalogo> ListarAsync(
        ConsultaDelCatalogo consulta,
        CancellationToken cancellationToken = default)
    {
        var listado = await _catalogo.ListarAsync(consulta, cancellationToken).ConfigureAwait(false);
        return listado with { Elementos = await ConReaccionesAsync(listado.Elementos, cancellationToken).ConfigureAwait(false) };
    }

    /// <inheritdoc />
    public async Task<FichaDelCatalogo?> FichaAsync(string id, CancellationToken cancellationToken = default)
    {
        var ficha = await _catalogo.FichaAsync(id, cancellationToken).ConfigureAwait(false);
        if (ficha is null)
        {
            return null;
        }

        var elementos = await ConReaccionesAsync([ficha.Elemento], cancellationToken).ConfigureAwait(false);
        return ficha with
        {
            Elemento = elementos[0],
            Imagenes = ficha.Imagenes.Select(i => ImagenesDeBlogs.Portada(i) ?? i).ToList(),
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ElementoDelCatalogo>> ElementosAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default)
        => await ConReaccionesAsync(
            await _catalogo.ElementosAsync(ids, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    /// <remarks>
    /// En secuencia, como la interina: la implementación viva de las reacciones lee de disco (ver
    /// <c>ResolveReactionWeightsAsync</c>).
    /// </remarks>
    private async Task<IReadOnlyList<ElementoDelCatalogo>> ConReaccionesAsync(
        IReadOnlyList<ElementoDelCatalogo> elementos,
        CancellationToken cancellationToken)
    {
        var lector = _sesion.IsAuthenticated && !string.IsNullOrWhiteSpace(_sesion.CurrentMemberEmail)
            ? _sesion.CurrentMemberEmail
            : null;
        var conReacciones = new List<ElementoDelCatalogo>(elementos.Count);
        foreach (var elemento in elementos)
        {
            var estado = await _reacciones.GetStateAsync(elemento.Id, lector, cancellationToken).ConfigureAwait(false);
            var atributos = new Dictionary<string, string>(elemento.Atributos, StringComparer.Ordinal)
            {
                [AtributoReacciones] = string.Join(
                    ',',
                    estado.CountsByType
                        .Where(tipo => tipo.Value > 0)
                        .OrderBy(tipo => tipo.Key, StringComparer.Ordinal)
                        .Select(tipo => string.Create(CultureInfo.InvariantCulture, $"{tipo.Key}:{tipo.Value}"))),
            };
            if (!string.IsNullOrWhiteSpace(estado.MyReaction))
            {
                atributos[AtributoMiReaccion] = estado.MyReaction;
            }

            // Las fotos al ancho en que se pintan, con la misma URL que /api/blogs y el respaldo SSR (012-blogs-social).
            // El catálogo de Application guarda la original: es la del item del stream.
            if (atributos.TryGetValue(AtributoAutorImagen, out var avatar))
            {
                atributos[AtributoAutorImagen] = ImagenesDeBlogs.Avatar(avatar) ?? avatar;
            }

            conReacciones.Add(elemento with { Imagen = ImagenesDeBlogs.Portada(elemento.Imagen), Atributos = atributos });
        }

        return conReacciones;
    }
}
