using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Proxies.Impl;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Composers;

public sealed partial class SeamComposer
{
    private void ComposeSocial(IUmbracoBuilder builder)
    {
        var services = builder.Services;

        // OLA 3 Blogs — red social (doc blogs-app-spec). Seams stub-first, aditivos
        // (no tocan Booking/Travel/Shop). ADR 0002 (Application pura, sin Umbraco) +
        // ADR 0075 (seam con tests canónicos). Reusa ICommentRepository EXISTENTE
        // para los comentarios del post (BlogsController deriva un nodeId estable del
        // id string del post) — no se crea otro seam de comentarios.
        //   - IContentStream: ABSTRACCIÓN REUSABLE de feed/contenido (Actor-Verb-Object,
        //     ActivityStreams 2.0). Genérica por Kind (post|article|lesson…) para que
        //     EDUCACIÓN la reuse por polimorfismo (filtrando Kind=lesson) sin instanciar
        //     Blogs ni copiar su schema. El stub compone ISocialGraphService (feed
        //     "Siguiendo") + IReactionService (métricas) — DIP, no duplica estado.
        //   - ISocialGraphService: grafo dirigido asimétrico (follow/unfollow idempotente,
        //     followers/following + conteos). Estado en memoria del proceso.
        //   - IReactionService: reacciones/likes por item (toggle idempotente por
        //     (actor,objeto), conteos por tipo + estado-por-usuario). Estado en memoria.
        //   - ISocialProfileProjection: Member → perfil social (handle/bio/banner) para
        //     el header del perfil. Stub sobre el catálogo sembrado.
        // Durabilidad (ADR 0105): el grafo, las reacciones y los posts creados viven tras el
        // store con namespace propio. El stub de ContentStream pide el StubReactionService
        // concreto para leer conteos en UNA pasada: registramos el concreto y lo exponemos
        // bajo la interfaz (composición manual).
        //
        // La siembra NO se escribe en boot (ADR 0013): un documento ausente cae al seed, y la
        // primera mutación escribe uno que desde entonces GANA sobre él. Es lo que hace que
        // dejar de seguir a alguien sembrado sobreviva un reinicio en vez de re-sembrarse.
        //
        // INotificationFeed y ISocialProfileProjection siguen igual a propósito: ninguno
        // guarda estado propio. El feed de notificaciones se DERIVA del grafo y de las
        // reacciones, así que hacer durables esos dos lo hizo durable por composición; darle
        // store propio duplicaría justo el estado que existe para no duplicar.
        services.AddSingleton(sp =>
            new StubReactionService(
                sp.GetRequiredService<IJsonEntityStore>(),
                StubReactionService.DefaultResourceType));
        services.AddSingleton<IReactionService>(sp => sp.GetRequiredService<StubReactionService>());
        services.AddSingleton<ISocialGraphService>(sp =>
            new StubSocialGraphService(
                sp.GetRequiredService<IJsonEntityStore>(),
                StubSocialGraphService.DefaultResourceType));
        services.AddSingleton<IContentStream>(sp =>
            new StubContentStream(
                sp.GetRequiredService<ISocialGraphService>(),
                sp.GetRequiredService<StubReactionService>(),
                null,
                sp.GetRequiredService<IJsonEntityStore>(),
                StubContentStream.DefaultResourceType));
        services.AddSingleton<ISocialProfileProjection, StubSocialProfileProjection>();

        // OLA 6 Blogs (doc 21 §2.3) — app social completa sobre el motor social ya
        // vivo. Aditivo (no toca los otros dominios). El único seam NUEVO es el
        // centro de notificaciones; el resto reusa lo existente:
        //   - INotificationFeed: DERIVA notificaciones dirigidas (follow/reacción/
        //     mención) del grafo + reacciones vivos — sin store paralelo (compone el
        //     StubReactionService concreto para leer las reacciones por objeto, misma
        //     técnica que StubContentStream). Stateless → singleton.
        //   - DMs reusan IMessagingService (contexto "dm"); guardados reusan
        //     IUserCollection (colección "saved"); explore/trending + long-form
        //     (Kind=article) reusan IContentStream + IReactionService; el studio
        //     compone grafo + reacciones. Todo vía BlogsController.
        // La data de demo de DMs/guardados vive en seams GENÉRICOS compartidos con
        // otros dominios, así que se siembra al boot desde un hosted service (no en
        // el ctor de la mensajería genérica) — idempotente.
        services.AddSingleton<INotificationFeed>(sp =>
            new StubNotificationFeed(
                sp.GetRequiredService<ISocialGraphService>(),
                sp.GetRequiredService<StubReactionService>()));
        services.AddHostedService<BlogsDemoSeedHostedService>();

    }
}
