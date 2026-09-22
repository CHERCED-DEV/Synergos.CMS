---
vertical: social
sustantivo: Post
alias:       [Social, Blog]     # el interruptor dice `Social`, el controller y las vistas dicen `Blog`
epica: 11
oraculo: no            # este spec se escribió ANTES del código, no contra el disco. Ver §9 del doc 13
ejes:
  catalogo:    { doctype: postpage, fuente: UmbracoSocialContentSource, interruptor: "Synergos:Catalog:Sources:Social" }
  transaccion: { forma: ninguna, razon: "publicar un post no compone nada que haya que deshacer: una sola escritura, local, sin plata ni cupo de nadie. El paywall y el cobro recurrente de la épica #11 SÍ tendrían eje 2 y son otra HU" }
  artefacto:   { que: ninguno, razon: "un post no es una prueba que alguien tenga que enseñarle a un tercero: es contenido, y su URL es su identidad. La CUARTA pregunta del doc 12 §3.1 se contesta que NO — nadie de fuera necesita comprobarlo sin creernos" }
preguntas:
  deshacer:      no          # una escritura local, nada que compensar → sin orquestador
  recurso_ajeno: no          # el post lo lleva el árbol de contenido, que es nuestro
  quien_cobra:   nadie       # no se cobra
reusa:
  capacidades: []
  elementos:   [blogs, comments-widget, social-share, share-bar, social-proof, poll]
crea:
  doctypes:    []            # postpage, postcategorypage y authorpage YA existen — ver §2
  seams:       []            # IContentStream e IBlogQuery ya existen; el eje 1 es un DECORADOR
  capacidades: []
  artefacto:   []
  composer:    SeamComposer.Social
ui:
  app: blogs
rechazos:                                 # leídos de SocialContentRules, no inventados
  - "postPage sin excerpt · no se siembra · el cuerpo son sus sections y aplanarlas fabrica"
  - "postPage sin authorName · no se siembra · el feed firmaría con el id del nodo"
  - "postPage sin segmento de URL · no se siembra · no hay handle que derivar"
  - "publishDate ambiguo · se sirve sin fecha · adivinar el mes es peor que no tenerlo"
  - "dos postPage con el mismo slug · se avisa · el feed los re-sembraría en cada vuelta"
---

# Social — el spec del octavo vertical

> **Este spec es el PILOTO 1 de la fábrica** (#146, épica #139). Se escribió **antes** del
> código, que es lo que lo distingue del piloto 0: Eventos se derivó hacia atrás contra el disco
> para medir si el molde daba para generar; éste mide si el molde da para **construir**.
>
> `oraculo: no` a propósito: medir la cobertura de un plan contra un vertical que este mismo spec
> produjo sería `feedback_contract_shape_needs_its_own_test` aplicado a un generador — el plan
> acertaría porque el disco lo copió del plan.

## Qué problema del negocio resuelve

Publicar un post que **la app social vea** sin un despliegue.

## Lo que la cabecera del ticket predijo, y en qué falló

El #146 daba Social por «un sub-spec y tres ajustes», leyendo que no existía un
`Synergos:Catalog:Sources:Social`. Al medir el árbol aparecieron **dos lectores de «un post»**, y
el eje 1 no estaba donde la tabla lo buscaba:

| lector | de dónde salían los posts | quién lo consume |
|---|---|---|
| `DefaultBlogQuery` → `PostSummary` | **`postPage` de Umbraco**, desde siempre | las vistas Razor: listados, `BlogHighlight`, `PostCategoryPage`, RSS, sitemap, tag page |
| `StubContentStream` → `ContentStreamItem` | **`SocialDemoSeed.Posts`: cinco posts en C#** | `BlogsController` entero — el feed, el detalle, explore, guardados: **la app** |

O sea que la mitad editorial ya salía del CMS y la mitad que el producto usa exigía un
despliegue. La predicción acertó el síntoma —falta el interruptor— y erró el sujeto.

## Por qué el eje 1 de Social NO es «un `Umbraco*Source` más»

Los otros siete sirven una colección **propia** y **de sólo lectura**, y se eligen con
`IsCmsSource`. El almacén de éste no cumple ninguna de las dos:

- **Lo comparte Educación.** `IContentStream` tiene un `Kind` (`post` | `article` | `lesson`)
  precisamente para eso, y Educación lo usa: `CatalogCourseCatalogProvider` **siembra** ahí los
  cuerpos de sus lecciones (#100). Cambiar «la fuente de Social» no es cambiar el origen de una
  colección: es cambiar el de **una parte** de un almacén que otro vertical comparte.
- **El producto ESCRIBE en él.** `BlogsController` llama a `IContentStream.CreateAsync` — alguien
  publica desde la app, no sólo desde el backoffice. Las siete fuentes anteriores son de lectura.

**Ése es el sub-spec que el molde no tenía**, y va al doc 13 §5: *un eje 1 cuyo almacén de lectura
está COMPARTIDO con otro vertical y además admite escrituras del producto.*

## Las tres salidas, y por qué se siembra

1. **Un adaptador que sirva el feed leyendo el árbol de contenido.** Encaja con el molde y deja sin
   contestar qué pasa con `CreateAsync`: ¿se pierde lo que alguien publicó desde la app? ¿se
   mezcla? ¿la app deja de poder publicar cuando el origen es `cms`? Esa pregunta es de producto.
2. **Que `postPage` SIEMBRE el stream.** ← **la elegida.** Deja `CreateAsync` intacto, hace
   convivir las dos cosas, y reusa una mecánica ya probada: es exactamente lo que Educación hace
   con el cuerpo de sus lecciones.
3. **Partir el `Kind`**: lo autorado sale del CMS, lo creado por el producto se queda. Lo más
   limpio conceptualmente y lo que más toca.

## Los tres ejes

**Eje 1 · el catálogo.** `postPage` con su `excerpt`, su `heroImage`, su `publishDate` y su
`authorRef`. Lo lee `UmbracoSocialContentSource` (Web) y lo siembra `CatalogContentStream`
(Application). El rollback es `Synergos:Catalog:Sources:Social = demo`, sin redespliegue.

**Eje 2 · la transacción.** No hay, y es una decisión: publicar un post es **una** escritura local.
Las dos capacidades candidatas del dominio —`Api.Moderation` y `Api.Engagement`— siguen sin primer
consumidor, y `CLAUDE.md` §11 dice que conviene que el primero sea real y no un fake.

**Eje 3 · el artefacto.** No hay. La CUARTA pregunta (doc 12 §3.1) —*¿alguien de FUERA tiene que
poder comprobar esto sin creernos?*— se contesta que no: un post es contenido, su URL es su
identidad, y quien quiera verlo lo abre. Sellarlo sería custodiar una llave para nadie.

## Lo que el molde no escribía, y este vertical necesitó

**El AUTOR viaja con el post.** `StubContentStream` resuelve el autor de un item con
`SocialDemoSeed.AuthorById`, que ante un id desconocido devuelve
`new ContentAuthor(actorId, actorId, actorId, null, false)` — o sea **fabrica** el handle y el
nombre. Sembrar un post autorado sin traerse su `authorPage` habría firmado cada tarjeta con
«autor-camila-rios», y eso no se lee como un defecto: se lee como un handle. Por eso
`AuthoredPost` lleva su `AuthoredPostAuthor`, y por eso el decorador **repone el autor al leer** y
no sólo al sembrar — el mapping es durable y la caché de autores no.

## Cómo sabemos que quedó bien

- `Synergos:Catalog:Sources:Social = cms` sirve los `postPage` autorados; `demo` sirve los cinco
  del seed. El rollback es esa línea.
- `SocialWiringTests` (6) y `CatalogContentStreamTests` (11), **mutados uno por uno**.
- `Toda_fuente_de_catalogo_esta_registrada_por_un_composer` — el diente que hizo falta añadir,
  porque el del eje 1 descubría verticales por su interruptor de **transacción** y Social no tiene.

## Predicho contra real — la medición que pedía la HU

El ticket predijo **«un sub-spec y tres ajustes»**. Lo construido, fichero por fichero:

| sub-spec | predicho | real |
|---|---|---|
| S1 DocTypes | nada | **nada** ✓ |
| **S2** fuente + interruptor | *«el delta de verdad»*, un sub-spec | **cuatro ficheros de producción**: `AuthoredPost` (Interfaces) · `UmbracoSocialContentSource` + `SocialContentRules` (Web) · `CatalogContentStream` (Application), más el interruptor en el composer |
| S3–S6 cableado | apagados | **apagados** ✓ |
| S7 artefacto | ajuste | **nada** — `ejes.artefacto: ninguno` |
| S8 sello | apagado | **apagado** ✓ |
| S9 controller | ajuste | **nada** — `BlogsController` no se tocó |
| S10 `<X>WiringTests` | *(no predicho)* | **`SocialWiringTests`, 6 dientes** |
| S11 elementos | reuso | **reuso** ✓ |
| S12 cliente + normalizadores | ajuste | **nada** |

**Dos desviaciones, y en direcciones contrarias.**

1. **S2 costó ~3× lo predicho**, porque el molde sólo tenía escrita la forma A. Eso es el
   hallazgo, y el molde lo gana (doc 13 §5.bis).
2. **Los tres «ajustes» costaron CERO.** Y no fue suerte: el decorador se enchufa detrás de
   `IContentStream`, así que todo lo que está por encima —el controller, sus DTOs, el cliente de
   la app, sus normalizadores— no se entera. La predicción los contó porque la tabla mide «qué
   piezas toca este vertical», no «cuáles cambian»; **una pieza que existe y sirve no es un
   ajuste**. Es el mismo error de medición que el #140 nombró por el otro lado: allá una
   derivación flaca inflaba la lista de hallazgos, acá una lectura floja infla el trabajo.

**Lo que la HU quería saber era si la cabecera mide lo que dice medir. Contestado: no del todo.**
Acierta lo que está apagado —S3–S6, S8— y lo que se reusa —S11—, que es la mitad del valor;
y en lo que queda vivo confunde «toca» con «cambia» y no distingue las dos formas de S2. Las dos
correcciones son mecánicas y ya están escritas, así que el piloto 2 (#147) arranca con ellas.

## Lo que este spec NO hace

- **No valida el comprador.** La épica #11 llama a su sub-segmento «una hipótesis no validada con
  una sola conversación de venta», y construir el eje 1 no la valida.
- **No cablea `Api.Moderation` ni `Api.Engagement`.** Siguen esperando primer consumidor real.
- **No repone el autor en `ISocialProfileProjection`.** El perfil de un autor autorado sigue
  saliendo del seed; el disparador es que alguien abra el perfil de un autor del CMS.
