# Inventario funcional de Synergos

- **Fecha del barrido:** 2026-08-01
- **Alcance:** los dos repos — `Synergos.CMS` (Umbraco 13 / .NET 8, SSR) y
  `Synergos.UI` (Nx multi-framework, Web Components a CDN).
- **Método:** seis barridos paralelos por dominio, cada uno **abriendo el
  código**, más un séptimo hecho a mano (Social). Las afirmaciones de
  seguridad y las tres más citadas se re-verificaron una por una.
- **Detalle por dominio:** `inventario/*.md` (7 ficheros, ~1.650 líneas).

> **Estado del barrido.** Este documento describe lo que el barrido encontró
> **antes** de la tanda de olas que salió de él. Lo que ya se cerró va marcado
> ✅ con su ADR; lo tachado dejó de ser cierto. Los anexos de `inventario/`
> **no** se actualizaron y siguen describiendo el estado original — léelos como
> el diagnóstico, no como el estado de hoy.

---

## Cómo leer esto

El documento está ordenado **por capacidad, no por nicho**, porque eso es lo
que el barrido demostró: lo que se repite entre negocios son las capacidades,
y los nichos son una capa fina encima. La Parte 2 trae igualmente una ficha por
vertical para el uso comercial ("qué tengo hoy para un restaurante").

### La escala de madurez

| Rótulo | Significa |
|---|---|
| **VIVO** | Implementación real sobre datos reales, con persistencia durable |
| **DEMO** | Lógica real, datos sembrados, y/o estado que **no sobrevive un reinicio** |
| **INERTE** | Devuelve siempre vacío / null / cero |
| **SÓLO SEAM** | Interfaz declarada, sin implementación consumida |

**DEMO no es un insulto**: varias piezas DEMO tienen motores correctos y sirven
para vender. Lo que no hacen es aguantar producción.

---

## Parte 1 — El núcleo transversal

### 1.1 El activo central: un contrato, N negocios

Éste es el hallazgo que sostiene la tesis del producto, y **ya está construido**.
No es una intención en un ADR: son consumidores reales en el código.

| Seam | Verticales que ya lo consumen | Evidencia |
|---|---|---|
| `IUserCollection` | Tienda (wishlist), Propiedades (shortlist), Blogs (guardados) | `ShopCatalogController`, `RealtyController`, `BlogsController` |
| `IMessagingService` | Blogs (DM), Gobierno (radicado), Tienda (comprador↔vendedor), **Salud (In Basket clínico)** | `SeamComposer.cs:734,750,763,947` |
| `IContentStream` | Blogs (feed), Educación (lecciones, `Kind=lesson`) | `SeamComposer.cs:419-422,445` |
| `IJsonEntityStore` | Órdenes, pagos, reservas, expedientes, tickets, matrículas | ADR 0105 |
| `ICatalogIndex<T>` | Motor de facetas transversal | ADR 0107 |
| `IOrderTrackingService` | Tienda (envío) y Eventos (pipeline propio) | `SeamComposer.cs:788` |

Dos ejemplos de que el patrón se aplica bien y no de boquilla:

- `ISavedSearchService` **no tiene almacén propio**: se compone sobre
  `IUserCollection` (`SeamComposer.cs:878`).
- `StubContentStream` **compone** `ISocialGraphService` + `IReactionService` en
  vez de duplicar estado (`SeamComposer.cs:385-388`).

**Regla de la casa, ya escrita:** *ninguna capacidad transversal se implementa
dos veces* (ADR 0107). Este inventario confirma que se cumple.

**Hueco:** `IMessagingService` nombra 5 verticales en su contrato y sólo 4 lo
consumen — Booking y Propiedades tienen la promesa sin cumplir.

### 1.2 Plataforma — lo que un negocio hereda gratis

| Capacidad | Madurez | Nota |
|---|---|---|
| Identidad + 2FA (TOTP, códigos de recuperación) | **VIVO** | Otp.NET real, cifrado en reposo |
| Consola de administración | **VIVO** | 27+ acciones, rol `admin,moderator,editor` |
| Auditoría | **VIVO** | JSONL append-only, con export CSV |
| Notificaciones (Email + Slack/Discord/Teams/Webhook) | **VIVO** | Los 4 canales opt-in: no-op si la URL está vacía |
| Branding y temas por hostname | **VIVO** | 8 variantes, resuelve sobre contenido real |
| Salud del sistema | **VIVO** | `/healthz`, `/healthz/ready`, `/admin/health` |
| Dashboard / métricas | **VIVO** | …pero ver el hueco de ventas en §1.4 |
| Realtime (SSE) | **VIVO** | Sólo **1 canal** en uso: `eventos:checkin:*` |
| Ficheros privados | **VIVO** | Sólo Gobierno lo usa |
| Retención + GDPR | **PARCIAL** | Cubre 6 de ~20 almacenes |
| Feature flags | **VIVO** | Estático en appsettings, sin toggle en runtime |

**La consola admin** cubre: moderación de comentarios (con bulk + undo),
formularios (listar/detalle/borrar/CSV), miembros (lock/unlock/reset 2FA/borrar/
roles/GDPR-erase), analítica de búsqueda, harness de webhooks, auditoría y
salud. Todo lo que sea **contenido o schema sigue exigiendo el backoffice de
Umbraco**.

### 1.3 Composición de páginas — el feature más maduro

El **Layout Composer** sostiene el título, verificado y no asumido:

- **14 presets** de layout, con defaults en **doble capa** (JS antes del drop +
  C# al guardar) y consistentes entre sí.
- **170 tipos de bloque** combinables **sin escribir código**.
- Plugin de backoffice vivo, con thumbnails y previews.
- **SSR puro**: no depende del CDN, que es la dependencia bloqueada.

Es, hoy, la respuesta más fuerte a "armá cualquier negocio sin programador".

### 1.4 Comercio como motor transversal

| Capacidad | Madurez |
|---|---|
| Catálogo (conmutable `demo`↔`cms` por flag **real**) | **VIVO** |
| Carrito (cookie HMAC, rehidrata contra el CMS) | **VIVO** |
| Checkout y órdenes (durables, ownership por member) | **VIVO** |
| Reseñas con gate de comprador verificado | **VIVO** |
| Pagos (motor y persistencia reales, **PSP simulado**) | **DEMO** |
| Devoluciones / RMA | **DEMO** |
| Tracking de envío | **DEMO** |

Dos incoherencias que valen su línea:

- **El dashboard de ventas siempre muestra $0.** `DefaultDashboardReadModel.cs:44`
  lee `ICheckoutRecorder.GetCheckouts()`, y **nadie llama nunca `Record()`**.
- **Un pedido nunca pasa de "pagado".** Nada invoca
  `IOrderTrackingService.AdvanceAsync` más allá de esa etapa.

### 1.5 La capa UI / CDN

**153 filas de registro = 141 tags DOM únicos** (62 module / 51 composition /
40 primitive). Reparto por dominio (inferido por nombre — *el dominio no existe
como campo en el repo*):

| Dominio | Elementos |
|---|---|
| Contenido / marketing genérico | ~100 |
| Comercio | 10 |
| Social | 10 |
| Dashboard | 8 |
| Viajes, Eventos | 2 c/u |
| Educación, Salud, Gobierno, Inmobiliaria | 1 c/u (el *shell* del vertical) |

**Los 4 frameworks:** Angular es el motor (136 apps, 89% del registro). React,
Svelte y Vanilla tienen 4/4/3 apps — **no son andamios vacíos**: son un programa
deliberado de canarios cross-framework (Svelte reimplementa `avatar` y
`accordion` con los mismos tags DOM que Angular, como prueba de paridad).

**Sistema de diseño: sano.** ~950 tokens `--syn-*`, fuente única en el CMS,
espejo auto-generado en el UI con gate que lo vigila, 8 rutas de tema con
auditoría de contraste propia. **Un solo fichero** con hex hardcodeado en todo
`apps/elements` (`academy.scss`).

---

## Parte 2 — Ficha por nicho

| Nicho | Qué cierra hoy | Qué falta |
|---|---|---|
| **Tienda** | Catálogo real → carrito → checkout → orden durable → devolución con reembolso → reseña verificada | PSP real; que el dashboard vea las ventas; mover el pedido más allá de "pagado" |
| **Contenido / Blog** | Blog editorial, comentarios con hilos y moderación, búsqueda (Examine), SEO con JSON-LD, formularios | — (es el más completo) |
| **Eventos** | Checkout → e-ticket con **QR firmado** → check-in verificado, durable y auditado. ✅ El catálogo **sale del CMS**: `eventPage` modela localidades, aforo, agenda y zonas (ADR 0117) | El "quedan N" arranca lleno: el contenido declara cuánto hay, no cuánto queda |
| **Educación** | Matrícula → pago → progreso, durable | La verificación **pública** del certificado es un enlace muerto |
| **Gobierno** | Radicar → revisar → decidir, durable, con ownership real | Catálogo de trámites sembrado |
| **Salud** | API PHI con cifrado real, escritura atómica, RTBF. ✅ **Portal del paciente abierto** con `GET /api/healthcare/me` (ADR 0120). ✅ La auditoría de acceso ya no se purga a los 90 días (ADR 0121) | Un paciente no puede corregir sus propios datos — necesita su propio flujo |
| **Viajes** | Checkout / confirm / cancel durables | Catálogos sembrados; cancelación por URL-capacidad |
| **Inmobiliaria** | ✅ Catálogo **desde el CMS** con `propertyListing` (ADR 0118). ✅ Visitas, leads y alertas **durables** (ADR 0105) | — |
| **Social** | 17 endpoints: feed, follow, reacciones, perfiles, DM, notificaciones. ✅ **Todo persiste** (ADR 0105) | Sin caché: el feed relee dos espacios por página |
| **Booking (hoteles)** | Demo puro | Sin rebanada CMS y **el único controller sin tests** |

---

## Parte 3 — Los patrones de deuda

Lo valioso del barrido no son los 30 hallazgos sueltos: son **cuatro patrones**
que se repiten entre dominios. Atacar el patrón cierra muchos a la vez.

### 3.1 Durabilidad a medias

La decisión ya está tomada (`IJsonEntityStore`, ADR 0105) y aplicada en órdenes,
pagos, reservas, expedientes, tickets y matrículas. **No** se aplicó en: los 8
seams sociales, Inmobiliaria entera, devoluciones, tracking de envío, el índice
de certificados, y los eventos/cursos publicados en caliente.

Todos guardan en `ConcurrentDictionary` de proceso. Es trabajo mecánico y
acotado, no un rediseño.

> ✅ **Cerrado casi entero.** Devoluciones y tracking de envío primero; después
> los 8 seams sociales (incluido `IUserCollection`, que no era "social": lo
> comparten la wishlist de Tienda, los favoritos de Propiedades y los guardados
> de Blogs — un reinicio borraba las tres listas de un golpe) e Inmobiliaria
> completa. Dos hallazgos que el barrido no vio: `INotificationFeed` y
> `ISocialProfileProjection` **no necesitan store** —se derivan de los otros
> seams, y hacerlos durables los hizo durables por composición—, y
> `BlogsDemoSeeder` habría re-añadido los mismos DM en **cada** reinicio en
> cuanto la mensajería se volvió durable, porque `StartThreadAsync` es
> idempotente en el hilo pero no en el mensaje.
>
> **Sigue pendiente:** el índice de certificados y los cursos publicados en
> caliente.

### 3.2 El guard está bien escrito; el call site no lo usa

**Dos casos, en direcciones opuestas — y por eso es un patrón, no dos accidentes.**

- **Falla ABIERTO — Tienda.** `POST /api/shop/return/{rmaId}/advance`
  (`ShopCatalogController.cs:720`) **no comprueba nada**: ni auth, ni rol, ni
  ownership, mientras su vecino de la línea 703 sí llama `DenyIfForeignMember`.
  Un anónimo con un `rmaId` puede llevar la devolución a `refunded`, lo que
  dispara `IPaymentProvider.RefundAsync`. Hoy no mueve dinero porque el PSP es
  un stub. **Se vuelve dinero real el día que se conecte el PSP.**
- **Falla CERRADO — Salud.** `HealthcareApiController.cs:135` construye
  `new AccessCheckRequest(resourceType, action, targetPatientKey)` — posicional,
  así que `TargetOwnerMemberKey` queda `null` siempre. Esa es justo la rama que
  habilita el autoacceso en `DefaultPhiAccessGuard.cs:92`. Resultado: **ningún
  paciente puede leer su propio expediente**.

En ambos casos el guard existe y está bien. El defecto vive en cómo lo llaman.

> ✅ **Los dos cerrados.** El de Tienda pasó a exigir ownership; el de Salud lo
> resuelve ahora el propio guard, acotado a `read` —sin eso, un paciente podría
> reescribir sus propias notas clínicas.
>
> Y el de Salud tenía **una segunda mitad que este barrido no vio**: con el
> guard arreglado el portal seguía sin existir, porque todos los endpoints se
> direccionan por `patientKey`, que es distinta del `MemberKey` a propósito y
> que nadie le dice al paciente. El permiso estaba concedido y era inalcanzable.
> Lo cierra `GET /api/healthcare/me` (ADR 0120).

### 3.3 Seams registrados sin consumidor

| Seam | Estado |
|---|---|
| `ICheckoutRecorder` | En DI, nunca invocado → dashboard en $0 |
| `IDictionaryCache` | Impl + invalidador + tests, **cero consumidores** (el i18n usa la API nativa en 233 sitios) |
| `ICatalogSource<EventSummary>` | Registrado condicionalmente, nadie lo inyecta |
| `ICertificateService.VerifyAsync` | Sin controller que lo exponga |
| `SearchAnalyticsRetentionPolicy` | Purga un directorio que nadie escribe |

Hay una regla de la casa para esto (ADR 0107: *lo que nadie cumple se borra*).
Cada fila es una decisión pendiente: cablear o quitar.

### 3.4 Documentación que se adelanta o se atrasa

- CLAUDE.md decía 148 bloques; son **170**.
- El ADR 0114 se aceptó declarando el seam de reseñas *sin consumir*; **ya está
  cableado** (rancio en el lado optimista, el error menos común).
- `BUILD_PIPELINE.md` documenta 7 comandos npm que **no existen**.
- Retención: `HealthcareRetentionPolicy.cs:11` promete auditoría PHI indefinida
  "por obligación legal"; `AuditRetentionPolicy.cs:34` la purga a los 90 días.
  **Gana la segunda.**
  ✅ Cerrado (ADR 0121) — y era bastante más que un desfase de documentación:
  el registro de quién miró qué historia clínica desaparecía a los 90 días, en
  silencio y sin que nada fallara. Ahora los eventos `phi.*` viven en su propia
  familia de archivos con su propia retención, que por defecto **no purga
  nunca**. Queda una acción manual: lo ya escrito sigue mezclado.

---

## Parte 4 — Qué se puede vender hoy

**Sin reservas:** un sitio de contenido con blog, comentarios moderados,
búsqueda, SEO serio y formularios, compuesto por un editor con 170 bloques y 14
layouts sin tocar código, con 8 temas y branding por hostname, más consola de
administración, auditoría y 2FA.

**Con una demo convincente y camino claro:** tienda completa (falta el PSP),
eventos con ticketing firmado (falta que el catálogo salga del CMS), educación
con matrículas (falta la verificación pública del certificado), y gobierno con
expedientes durables.

**Todavía no:** Inmobiliaria y Social (nada persiste), Booking (demo sin tests),
y el portal del paciente en Salud (bloqueado por §3.2).

> ✅ **Se movieron cinco.** Eventos ya no espera al CMS (ADR 0117). Inmobiliaria
> pasó de "todavía no" a vendible: tiene su DocType y todo persiste (ADR 0118).
> Social persiste entero. El portal del paciente existe (ADR 0120). El PSP de
> Tienda sigue siendo el bloqueo real de esa fila, y sigue esperando
> credenciales de sandbox.
>
> **Lo que sigue en "todavía no":** Booking, la verificación pública del
> certificado en Educación, y los catálogos sembrados de Viajes y Gobierno.

**Transversal a todo:** los 87 bloques CDN emiten placeholder fuera de la
máquina con `C:\LOCAL_CDN` — `HttpBundleRegistryClient` no existe.

---

## Siguiente fase

Investigación profunda por nicho, contrastando cada vertical con las mejores
experiencias del mercado, para definir los flujos completos. Este documento es
la línea base contra la que se medirá qué hay que construir.

Antes de eso hay tres arreglos que no deberían esperar a ninguna investigación:
el guard de RMA (§3.2), el autoacceso PHI (§3.2), y `ICheckoutRecorder` (§3.3).
