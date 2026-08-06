using Synergos.CMS.Web.Middlewares;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lleva el identificador de correlación del CMS al siguiente servicio (HU #28).
/// </summary>
/// <remarks>
/// <para><b>El CMS es donde nace el rastro.</b> <see cref="CorrelationIdMiddleware"/> ya le ponía
/// uno a cada petición y lo devolvía en la respuesta, pero se quedaba ahí: cuando el checkout
/// llamaba a <c>Bff.Tienda</c>, el orquestador generaba el suyo y a partir de ese salto había dos
/// historias distintas de la misma compra. Este handler cierra ese corte.</para>
///
/// <para><b>Va sobre los clientes NOMBRADOS</b> —los que registra cada composer por destino— y no
/// sobre uno global, porque son esos y solo esos los que salen del CMS hacia el árbol de
/// servicios. Un handler global se lo pegaría también a las llamadas al CDN, donde no significa
/// nada y donde lo que se manda a un tercero conviene que sea lo mínimo.</para>
///
/// <para><b>Duplicado a propósito respecto de <c>Synergos.Shared</c>.</b> Allá vive el mismo
/// handler para las capacidades y los orquestadores; acá vive el del CMS. No se comparte el
/// código porque el CMS no referencia ese ensamblado y no debe hacerlo (<c>CLAUDE.md</c> §11): lo
/// que comparten los dos árboles es <b>el nombre de la cabecera</b>, que es un contrato de una
/// cadena y el único acople legítimo entre ellos.</para>
/// </remarks>
public sealed class CorrelationForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _contexto;

    public CorrelationForwardingHandler(IHttpContextAccessor contexto) => _contexto = contexto;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Sin petición en curso no hay nada que propagar: pasa en el arranque y en los servicios
        // de fondo. No es un fallo, y forzar uno inventado ahí sería peor — un identificador que
        // no corresponde a ninguna petición ensucia el grep sin ayudar a nadie.
        var id = _contexto.HttpContext?.Items[CorrelationIdMiddleware.ContextKey] as string;

        if (!string.IsNullOrWhiteSpace(id)
            && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, id);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
