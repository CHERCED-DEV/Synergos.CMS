# ADR 0114 — El rating de un producto es UGC DERIVADO tras un seam de prueba social: se calcula de las reseñas, nunca se almacena, y su ausencia no es un cero

- **Status:** Accepted
- **Date:** 2026-07-20
- **Deciders:** Arquitecto (eligió alcance A+B+C completo sobre "solo infraestructura", y el gate de **solo comprador verificado** sobre "cualquier miembro autenticado") + agente. Originado al intentar rematar el "rating compacto" de `product-card` (doc `24`, fila 49) y descubrir que el ítem no era construible.
- **Relacionados:** ADR 0105 (`IJsonEntityStore` es el único store durable), ADR 0112 (degradar por AUSENCIA, nunca por negación), ADR 0107 (un campo que nadie cumple se borra; y el motor de catálogo solo emite facetas CON valores), ADR 0083 (la UI es la fuente de verdad del contrato), ADR 0075 (tests por seam), ADR 0002 (grafo de dependencias), ADR 0013 (cero seeders en boot).

---

## Context

`product-card` debía pintar un "rating compacto" (una estrella + el número). El contrato de la
UI ya tenía el hueco —`Product.rating?: {average, count}` en `@synergos/contracts`— así que a
primera vista era trabajo de tarjeta.

**No lo era. El contrato tenía el hueco y ninguna capa tenía el dato:**

| Capa | Rating |
|---|---|
| `ProductSummary` (`IShopQuery.cs`) — dominio del endpoint que consume la tarjeta | el campo **no existe** |
| `ProductBySkuDto` — respuesta de `GET products/sku/{sku}` | **no se emite** |
| `UmbracoProductCatalogSource` — la fuente viva (catálogo mudado a contenido) | `Rating: 0d` fijo, `Reviews = Array.Empty` |
| `ProductDto.Rating` / `ReviewCount` — search y PDP | emitidos, pero alimentados por lo anterior ⇒ 0 |

Y el motivo estaba escrito en el propio código, como decisión consciente:

> `// Rating y reviews son UGC DERIVADO, no contenido editorial: un editor no autora`
> `// la review de un comprador. Salen en cero hasta que ICatalogSocialProof exista.`

`ICatalogSocialProof` aparecía **una sola vez en todo el repositorio**: en ese comentario. La
decisión era correcta y llevaba tiempo sin cumplirse, así que todo el catálogo valía 0.

Ya se había pagado una vez la consecuencia: con el rating en cero, la faceta "Calificación"
salía como **columna muerta**, y se arregló haciendo que el motor emitiera solo facetas CON
valores (`ICatalogIndex.cs`). Pintar "0,0" en cada tarjeta habría reabierto ese mismo defecto
una capa más arriba, ahora en la vitrina.

## Decision

1. **El rating es una VISTA de las reseñas, no un dato propio.** Se introduce el seam
   `ICatalogSocialProof` con `CustomerReview` (la entidad almacenada) y `ProductSocialProof`
   (el agregado `{average, count}`).
2. **El agregado se DERIVA en lectura y no se almacena nunca.** Un contador persistido se
   desincroniza el día que una reseña se edita o se borra, y entonces miente sin que nada falle.
3. **Ausencia, no cero.** `GetAsync` devuelve `null` cuando no hay reseñas — nunca un agregado
   en cero. Quien pinte esto no debe recibir jamás un "0,0" que parezca la peor nota posible
   (ADR 0112).
4. **Durabilidad sobre `IJsonEntityStore`** con `ResourceType = "reviews"`, **una entrada por
   SKU** con sus reseñas dentro. Leer un producto es UNA lectura, y la idempotencia sale de
   reemplazar dentro de la lista. No se crea un store dedicado (ADR 0105).
5. **La identidad del autor es el `memberKey` server-trusted**, del gate de miembros y **jamás
   del cuerpo de la petición** — un `actorKey` que viaja en el body es un IDOR con otro nombre
   (lección de T2). Es además la clave de idempotencia: **una reseña por comprador y producto**;
   reenviarla la EDITA.
6. **Solo comprador verificado reseña**: autenticado **y** con una orden que contenga ese SKU,
   comprobado contra las órdenes durables. Decisión del arquitecto frente a "cualquier miembro
   autenticado", que daría más volumen a cambio de reseñas de quien no compró.
7. **Un rating fuera de 1..5 se rechaza, no se recorta.** Recortar en silencio convertiría un
   bug del llamante en un 5 estrellas.

## Consequences

**Bueno.** El rating deja de ser un campo que promete y no cumple. La regla "el editor no
autora la reseña del comprador" queda cumplida por construcción. La tarjeta, cuando se cablee,
degrada por ausencia sin ninguna lógica de presentación extra.

**El coste, dicho en claro.** Este ADR se acepta con el seam **construido pero SIN CONSUMIR**:
el cableado (`UmbracoProductCatalogSource` → `ProductSummary` → `ProductBySkuDto`) no se hizo
porque esos ficheros tenían trabajo sin comitear de otro agente en el mismo checkout, que los
worktrees no aíslan. **Hasta que ese cableado ocurra, el catálogo sigue emitiendo `Rating: 0d`
y no se ve absolutamente nada.**

Eso es exactamente la trampa que esta misma ola documentó en el ítem del botón —*construir la
capacidad no la hace alcanzable*— y aquí se asume **a sabiendas y por escrito**, no por
descuido. Si el cableado no llega, esto es código dormido y debe borrarse antes que quedarse:
un seam sin consumidor es la siguiente promesa incumplida, y ya sabemos lo que cuesta
(ADR 0107).

**Pendiente al aceptar:** el cableado · el gate de comprador verificado y su endpoint (Ola B) ·
la moderación · las reseñas de demo tras `Synergos:DevSeed:Enabled` (ADR 0013: nada en boot) ·
la UI de la estrella (Ola C), recordando que `rating-stars` es un elemento publicado, o sea una
app, y **no se importa** (ADR 0113).

## Alternativas descartadas

- **Pintar el 0 que ya emite el catálogo.** Es lo más barato y es exactamente la columna
  muerta que el proyecto ya arregló una vez.
- **Que el editor autore el rating como contenido.** Rompe la premisa: un rating editorial no
  es prueba social, es publicidad.
- **Almacenar el agregado junto al producto.** Rápido de leer y desincronizado a la primera
  edición.
- **Una entrada de store por reseña.** Obligaría a listar y filtrar el universo entero para
  pintar una tarjeta.
- **Reusar el `ProductReview` de `IProductCatalogProvider`.** Es la proyección de LECTURA de la
  PDP: sin SKU y sin identidad. Meterle el `memberKey` mezclaría la entidad con la vista y
  arrastraría la identidad del miembro hasta el DTO. Son dos tipos con dos dueños.
