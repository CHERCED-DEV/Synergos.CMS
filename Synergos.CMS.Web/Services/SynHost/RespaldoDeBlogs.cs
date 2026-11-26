using System.Globalization;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// La reserva del servidor para el inicio de la red (D-012-V11, 012-blogs-social): el título del bloque y la primera
/// página del feed «Para ti», con los mismos datos que la app va a pintar, dentro de <c>&lt;synergos-blogs&gt;</c>
/// hasta que hidrata.
/// </summary>
/// <remarks>
/// <para><b>Por qué.</b> El elemento estaba vacío hasta que llegaba su bundle, y debajo el servidor ya pintaba «Lo más
/// reciente». Al montar, la app lo empujaba fuera de la pantalla: CLS 0,83 a 375 px y 0,50 a 1280. Con la reserva, lo
/// de abajo ya empieza fuera de la pantalla.</para>
///
/// <para><b>Los mismos datos.</b> La misma consulta que <c>GET /api/blogs/feed?scope=foryou</c> (un
/// <see cref="FeedQuery"/> por defecto: «Para ti» no depende de quién lee), y cada autor por su cara pública, como en
/// el feed: nunca un correo. Sólo cuando la app lee esta misma API (<see cref="ApiLocal"/>): si el sitio la apunta a
/// otra, la reserva no sería lo que la app pinta, y no hay.</para>
///
/// <para><b>La arma la vista con lo que resolvió el resolver</b> (<see cref="BlogsResolutor"/>), de los mismos props que
/// viajan: el resolver es síncrono, y el feed se lee de forma asíncrona.</para>
///
/// <para><b>Sin la regla <c>:defined</c></b> de <c>syn-ssr-fallback.css</c>: la app vacía el host al crearse y ocupa
/// el sitio de la reserva sin que se vea un cuadro sin ninguna de las dos (medido en la piloto: lo de abajo no se mueve),
/// y entre que el tag queda definido y la app pinta pasan más de 100 ms. Ocultar la reserva al definirse dejaría ese
/// hueco sin ella: el mismo salto que viene a quitar.</para>
///
/// <para><b>Contrato de fallo</b> (el de <see cref="IResolutorSynHost{TProps}"/>): si el feed no se puede leer, el
/// elemento sale sin reserva, como antes, y queda en el log.</para>
/// </remarks>
public sealed class RespaldoDeBlogs
{
    /// <summary>Dónde sirve este CMS el feed que lee la app: la ruta de <c>BlogsController</c>.</summary>
    public const string ApiLocal = "/api/blogs";

    private readonly IContentStream _stream;
    private readonly IIdentidadPublica _identidad;
    private readonly ILogger<RespaldoDeBlogs> _log;

    public RespaldoDeBlogs(IContentStream stream, IIdentidadPublica identidad, ILogger<RespaldoDeBlogs> log)
    {
        _stream = stream;
        _identidad = identidad;
        _log = log;
    }

    /// <summary>
    /// La reserva del inicio, o <c>null</c> si el bloque es de otra vista, si la app lee otra API o si el feed no se
    /// pudo leer.
    /// </summary>
    public async Task<string?> DelInicioAsync(BlogsProps props, CultureInfo cultura, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(cultura);
        if (!EsElInicio(props.Vista) || !LeeEsteCms(props.ApiBase))
        {
            return null;
        }

        ContentStreamPage pagina;
        try
        {
            pagina = await _stream.GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "El inicio de Blogs sale sin reserva del servidor: no se pudo leer el feed.");
            return null;
        }

        return SynHostFallbackBuilder.Blogs(
            props.Heading,
            props.Subheading,
            pagina.Items.Select(Publicacion).ToList(),
            cultura);
    }

    /// <summary>Si el bloque es el inicio: la vista elegida, o ninguna, que es el inicio (<see cref="BlogsResolutor"/>).</summary>
    internal static bool EsElInicio(string? vista)
        => string.IsNullOrWhiteSpace(vista)
           || string.Equals(vista.Trim(), BlogsResolutor.VistaPorDefecto, StringComparison.OrdinalIgnoreCase);

    /// <summary>Si la app lee el feed de este CMS.</summary>
    internal static bool LeeEsteCms(string? apiBase)
        => string.Equals((apiBase ?? string.Empty).Trim().TrimEnd('/'), ApiLocal, StringComparison.OrdinalIgnoreCase);

    private PublicacionDelRespaldo Publicacion(ContentStreamItem item)
    {
        var (autor, alias) = Autor(item.Author);
        // Como la app: el título sólo es de un artículo del CMS (el feed lo llama `postPage`).
        var articulo = string.Equals(item.Kind?.Trim(), "article", StringComparison.OrdinalIgnoreCase);
        return new PublicacionDelRespaldo(
            Autor: autor,
            Alias: alias,
            Fecha: item.CreatedUtc,
            Titulo: articulo ? item.CmsTitle : null,
            Cuerpo: item.Body,
            Pagina: item.CmsUrl,
            Imagen: item.MediaUrl,
            // El de quien publicó gana; el recorte del cuerpo es la red, como en el feed.
            TextoAlternativo: string.IsNullOrWhiteSpace(item.MediaAlt)
                ? PublicacionesDeLaRed.TextoAlternativo(item.Body)
                : item.MediaAlt,
            Etiquetas: PublicacionesDeLaRed.Etiquetas(item.Body));
    }

    /// <summary>Quien firma, tal como sale en el feed: un miembro por su cara pública; si no, como viene.</summary>
    private (string Nombre, string? Alias) Autor(ContentAuthor autor)
    {
        if (CaraPublica.EsCorreo(autor.Id))
        {
            var cara = _identidad.De(autor.Id);
            return (cara.Nombre, cara.Alias);
        }

        return (autor.DisplayName, autor.Handle);
    }
}
