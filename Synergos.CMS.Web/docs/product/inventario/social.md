# Dominio: Social y comunidad

> Barrido hecho a mano (no por agente) para cubrir el hueco de asignación.
> Todo verificado abriendo el código. Fecha: 2026-08-01.

## Resumen ejecutivo

Hay un motor de red social completo a nivel de **contrato y de endpoints**, con
17 rutas HTTP vivas bajo `api/blogs`, pero **toda la persistencia es en memoria
sembrada al boot**: las 8 implementaciones son `Stub*` con `ConcurrentDictionary`
y datos de demo. Funciona de punta a punta en una sesión y se pierde al
reiniciar el proceso.

Lo relevante no es el vertical Blogs en sí: es que **tres de estos seams están
diseñados como genéricos y ya los consumen otros verticales**. Ése es el activo
arquitectónico más fuerte del proyecto para la tesis del "compendio con el que
se arma cualquier negocio", y ya está implementado, no es aspiracional.

## Capacidades

| Capacidad | Seam | Impl | Líneas | Madurez |
|---|---|---|---|---|
| Grafo social (follow asimétrico) | `ISocialGraphService` | `StubSocialGraphService` | 159 | DEMO |
| Perfil social (handle/bio/banner) | `ISocialProfileProjection` | `StubSocialProfileProjection` | 43 | DEMO |
| Reacciones idempotentes | `IReactionService` | `StubReactionService` | 158 | DEMO |
| Stream de contenido (Actor-Verb-Object) | `IContentStream` | `StubContentStream` | 187 | DEMO |
| Feed de notificaciones (derivado) | `INotificationFeed` | `StubNotificationFeed` | 143 | DEMO |
| Mensajería 1:1 con contexto | `IMessagingService` | `StubMessagingService` | 202 | DEMO |
| Colecciones por usuario | `IUserCollection` | `StubUserCollection` | 187 | DEMO |
| Búsquedas guardadas | `ISavedSearchService` | `StubSavedSearchService` | 198 | DEMO |

**DEMO en todos los casos** significa: hay lógica real (idempotencia, toggles,
derivación), hay datos sembrados que hacen la pantalla creíble, y **no hay
durabilidad**. Ninguno usa `IJsonEntityStore` (ADR 0105), que es el seam de
persistencia durable del proyecto.

### Superficie HTTP — `BlogsController.cs`, `[Route("api/blogs")]`

| Verbo | Ruta | Línea |
|---|---|---|
| GET | `feed` | 91 |
| GET | `post/{id}` | 122 |
| POST | `post` | 150 |
| POST | `post/{id}/react` | 185 |
| POST | `follow/{authorId}` | 219 |
| GET | `profile/{handle}` | 252 |
| POST | `article` | 294 |
| GET | `messages` | 327 |
| GET | `thread/{id}` | 350 |
| POST | `message` | 382 |
| GET | `notifications` | 414 |
| GET | `explore` | 440 |
| GET | `saved` | 474 |
| POST | `saved` | 496 |
| DELETE | `saved` | 517 |
| GET | `studio` | 538 |

## El hallazgo que importa: seams genéricos reusados entre verticales

`SeamComposer.cs` documenta esto explícitamente y el grep lo confirma. **No es
una intención escrita en un ADR: son consumidores reales.**

### `IUserCollection` — un contrato, 3 verticales

> "favoritos, wishlist, listas nombradas, shortlists, bookmarks y saved-searches
> son TODAS instancias del mismo contrato (owner + nombre de colección + refs)"
> — `IUserCollection.cs`

| Vertical | Uso | Consumidor |
|---|---|---|
| Tienda | wishlist / listas de regalo | `ShopCatalogController.cs` |
| Propiedades | shortlist / favoritos | `RealtyController.cs` |
| Blogs | guardados | `BlogsController.cs` |
| Propiedades | saved-searches (compuesto encima) | `StubSavedSearchService.cs` (`SeamComposer.cs:878`) |

`ISavedSearchService` **no tiene store propio**: se compone sobre
`IUserCollection` (`SeamComposer.cs:878-880`). Es el patrón bien aplicado.

### `IMessagingService` — un contrato, 4 verticales

> "comprador↔vendedor post-venta (Tienda), huésped↔host (Booking),
> interesado↔agente (Propiedades), DM (Blogs), paciente↔clínico In Basket
> (Healthcare) son el MISMO contrato" — `IMessagingService.cs`

| Vertical | Contexto | Consumidor |
|---|---|---|
| Blogs | `dm` | `BlogsController.cs` |
| Gobierno | `gov`, contextRef = radicado | `GovController.cs` (`SeamComposer.cs:947`) |
| Tienda | comprador↔vendedor | `ShopCatalogController.cs` |
| Salud | `clinical` (In Basket) | `EhrController.cs`, `StubEhrInBasketService.cs`, `StubClinicalMedicationService.cs` (`SeamComposer.cs:734,750,763`) |

Booking y Propiedades están nombrados en el contrato pero **no los encontré como
consumidores** — son los dos que faltan por cablear.

### `IContentStream` — un contrato, 2 verticales

> "ABSTRACCIÓN REUSABLE de feed/contenido (Actor-Verb-Object) ... sin depender de
> Blogs ni copiar su schema" — `SeamComposer.cs:367-371`

| Vertical | Uso | Consumidor |
|---|---|---|
| Blogs | feed, explore, long-form (`Kind=article`) | `BlogsController.cs` |
| Educación | lecciones (`Kind=lesson`) | `AcademyController.cs`, `StubCourseCatalogProvider.cs` (`SeamComposer.cs:419-422,445`) |

El `StubContentStream` además **compone** `ISocialGraphService` + `IReactionService`
en vez de duplicar estado (`SeamComposer.cs:385-388`) — DIP aplicado, no
copiar-pegar.

## Veredicto

**Lo bueno.** El patrón "un contrato, N dominios" está implementado y probado en
3 seams con 9 puntos de consumo reales. Ésta es exactamente la tesis del
producto y es defendible mostrando código.

**El coste.** Los 8 seams sociales son DEMO: nada sobrevive a un reinicio. Para
que dejen de ser demo, el camino ya existe y está decidido —`IJsonEntityStore`,
ADR 0105— y no se ha recorrido. Es un trabajo mecánico y acotado, no un rediseño.

**El hueco de cobertura.** `IMessagingService` nombra 5 verticales en su
contrato y sólo 4 lo consumen: Booking y Propiedades tienen la promesa escrita
sin cumplir. Por ADR 0107 ("lo que nadie cumple se borra") eso es una decisión
pendiente: cablearlos o quitar la mención.

## Huecos concretos

| # | Hueco | Dónde |
|---|---|---|
| 1 | Cero durabilidad: 8 seams sociales en memoria, se pierde todo al reiniciar | `Application/Services/Impl/Stub{SocialGraph,Reaction,ContentStream,Messaging,UserCollection,NotificationFeed,SavedSearch,SocialProfile}*.cs` |
| 2 | `IMessagingService` promete Booking y Propiedades; no hay consumidor | `Synergos.CMS.Interfaces/IMessagingService.cs` (doc-comment) |
| 3 | 17 endpoints sociales sin verificar autorización por dueño en este barrido | `Controllers/BlogsController.cs` — **pendiente**, lo cubre el agente de verticales regulados vía ADR 0112 |
