using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.DependencyInjection;

namespace Synergos.CMS.Web.Composers;

/// <summary>
/// Contra qué se valida el avance de un pedido (HU #46).
/// </summary>
/// <remarks>
/// <para><b>Vive en su propio parcial porque los cuatro dominios lo comparten.</b> Tienda,
/// Viajes, Eventos y Educación construyen cada uno su <c>IOrderTrackingService</c> con SU
/// pipeline, y sin un sitio común la decisión de contra qué se valida quedaría copiada cuatro
/// veces — que es exactamente la forma del problema que esta HU quita.</para>
/// </remarks>
public sealed partial class SeamComposer
{
    /// <summary>Registra la sección y el cliente. Una vez, antes que los cuatro dominios.</summary>
    private static void ComposeTracking(IUmbracoBuilder builder)
    {
        // La sección se ENLAZA: sin esto el cliente recibe un TrackingSettings recién construido
        // y el prefijo de la definición se queda en su default en silencio — un despliegue que
        // publicó `tracking2.shop` seguiría validando contra el pipeline viejo sin avisar.
        builder.Services.Configure<TrackingSettings>(builder.Config.GetSection("Synergos:Tracking"));

        if (!EsModoApi(builder.Config["Synergos:Tracking:Mode"])) return;

        var url = builder.Config["Synergos:Tracking:BaseUrl"];
        var key = builder.Config["Synergos:Tracking:ApiKey"];
        var timeout = int.TryParse(builder.Config["Synergos:Tracking:TimeoutSeconds"], out var t) && t > 0 ? t : 10;

        builder.Services.AddHttpClient(HttpOrderTrackingService.ClientName, http =>
        {
            var destino = string.IsNullOrWhiteSpace(url) ? "http://127.0.0.1:5215/" : url;
            http.BaseAddress = new Uri(destino.EndsWith('/') ? destino : destino + "/");
            http.Timeout = TimeSpan.FromSeconds(timeout);
            if (!string.IsNullOrWhiteSpace(key))
            {
                http.DefaultRequestHeaders.Add(HttpOrderTrackingService.ApiKeyHeader, key);
            }
        })
        .AddHttpMessageHandler<CorrelationForwardingHandler>();
    }

    private static bool EsModoApi(string? modo)
        => string.Equals(modo, "Api", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// El seguimiento de UN dominio: su pipeline, su espacio de almacén y su definición.
    /// </summary>
    /// <param name="sp">De dónde salen el store y el cliente.</param>
    /// <param name="pipeline">Las etapas de ESTE dominio, en orden.</param>
    /// <param name="storeNamespace">
    /// Su familia de entidades. El estado guarda el índice de etapa, así que compartir espacio
    /// haría que «enviado» se leyera como «matriculado» sin que nada fallara.
    /// </param>
    /// <param name="domain">
    /// <c>shop</c> · <c>travel</c> · <c>events</c> · <c>academy</c>. Forma la clave de la
    /// definición y el <c>Kind</c> del sujeto — <b>una por pipeline</b>, porque los nombres de
    /// estado se repiten entre dominios y una definición compartida leería la etapa de uno contra
    /// el pipeline de otro.
    /// </param>
    /// <remarks>
    /// <b>El motor local NO se reemplaza: se envuelve.</b> Sigue siendo el modelo de lectura y
    /// quien sella las fechas; la capacidad sólo valida el avance. Por eso con
    /// <c>Api.Workflow</c> caída se sigue viendo dónde va un pedido — al revés que en Gobierno
    /// (#44), donde caer al motor local estaba prohibido porque allá el riesgo era <i>decidir</i>
    /// con un proceso que quizá ya no es el vigente.
    /// </remarks>
    private static IOrderTrackingService Tracking(
        IServiceProvider sp,
        IReadOnlyList<OrderTrackingStageDefinition> pipeline,
        string storeNamespace,
        string domain)
    {
        var local = new StubOrderTrackingService(
            pipeline, null, sp.GetRequiredService<IJsonEntityStore>(), storeNamespace);

        var ajustes = sp.GetRequiredService<IOptions<TrackingSettings>>();
        if (!EsModoApi(ajustes.Value.Mode)) return local;

        return new HttpOrderTrackingService(
            sp.GetRequiredService<IHttpClientFactory>(), local, ajustes, pipeline, domain);
    }
}
