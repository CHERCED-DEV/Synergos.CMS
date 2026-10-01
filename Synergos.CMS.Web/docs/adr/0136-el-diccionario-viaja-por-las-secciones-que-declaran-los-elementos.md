# ADR 0136 — El diccionario viaja por las secciones que declaran los elementos, y la funcionalidad traduce

- **Estado:** Propuesto — pilotada en #186 (2026-10-01); el piloto recomienda **aceptarla con
  seis cambios** (ver «Resultado del piloto»). La ratificación es del arquitecto.
- **Fecha:** 2026-09-29
- **Propone:** la síntesis de la auditoría de reutilización (informe 20 §5.C.2-3). El arquitecto
  fijó número y estado el 2026-09-29. Lo había pedido como diferido el 2026-06-28 —*«mirar cómo lo
  hace NewShore»*— y esa mirada es la auditoría.
- **Parte de:** [#172](../../../../../issues/172) · épica [#139](../../../../../issues/139)
- **Cambiaría:** la regla de publicación por subconjunto de `docs/contracts/i18n-bridge.md` (ADR 0083)
- **Depende de:** ADR 0135 (el record declara las secciones) y ADR 0134 (funcionalidad / pieza)

## Contexto

> Marcas de certeza (informe 20): ✔ comprobado en el disco · ◐ medido por un agente con dos
> derivaciones o ejecutando, sin re-derivar · ○ cifra de un agente sin verificar. «Re-leído» = línea
> abierta al escribir esta ADR (CMS `9ec00d81`, UI `98b9233`).

### El cableado existe y no lo usa nadie

| hecho | cifra | fuente |
|---|---|---|
| Ítems de diccionario en uSync | **481** en 35 secciones ✔ | informe 20 §2.1 |
| Claves que el bridge publica en cada página | **176** (≈7 KB) ✔, filtrando por **11 prefijos fijos** | `HostBridgeSettings.cs:15-28` (re-leído) |
| Prefijos que no casan ninguna clave | **3**: `Comments.`, `Cart.`, `Account.` (0 ítems cada uno, frente a `Common.` 55, `Shop.` 47, `Form.` 31, `Search.` 13) ✔ | informe 20 §2.1 |
| Llamadores de `t()` en la UI | **0** — sólo su definición ✔ | `vitals/core/src/bridge/synergos-bridge.ts:32` (re-leído) |
| Textos visibles escritos a mano en la UI | **3.235** (±3 %; 1 falso positivo en 154 muestras), el 75 % en las 11 verticales ◐ | informe 16 §2 |
| …que ya tienen su texto exacto en una clave de uSync | **386** ◐ | informe 16 §2 |
| Claves de `GetDictionaryValue` en Razor que no existen en uSync | **81** (61 `Account.*`): sale siempre el respaldo escrito en la vista ◐ | informe 16 §1.2 |
| Componentes que ya leen `config.translations`, al estilo NewShore | **15**, y nadie se lo manda ◐ | informe 16 §1.3 |

El presupuesto del contrato para **todo** el bridge es < 4 KB (`docs/contracts/host-bridge.md:185`,
re-leído); el i18n solo ya lo supera, y lo publica en cada página aunque nadie lo lea.

El caso que resume el defecto es `kpi-card`: la vista traduce la tendencia para el respaldo SSR
(`SynHost/KpiCard.cshtml:35-37`, re-leído: `Synhost.Kpi.Trend.Up` → «Tendencia al alza») y el
elemento, al hidratar, la reemplaza por su literal (`kpi-card.ts:218`, re-leído: `'al alza'`). **El
diccionario llega al HTML que se ve antes de hidratar y se pierde después.**

### El contrato promete lo que el código no hace

`i18n-bridge.md` declara un `defaultCulture` *«for fallback when current key missing»*, pero el bridge
filtra por la cultura activa y no hay fallback por clave (informe 15 §10.b.7). Su `t()` devuelve la
**clave cruda** como último recurso. Dice «369+ keys» (son 481) y que `Account.*` no se publica (el
código lo publica, y casa 0).

### Lo que hace NewShore

- Casi todo su texto visible pasa por el diccionario: ~1.079 claves usadas y ≈33 literales en 395
  plantillas ◐ (informe 15 §4.1, §4.7).
- Cada resolver pide **a mano** sus secciones (93 distintas, de 1 a 14 por widget) y **cada host
  embebe su copia**: en una página de pasajeros `common` viaja 10 veces (informe 15 §4.5; la
  composición de la página es inferencia).
- Las **hojas reciben texto ya traducido**; las piezas intermedias, el diccionario entero ◐. Eso
  obliga al resolver a conocer las secciones que usan **por dentro** las piezas que su contenedor
  monta — `Payment` pide `CardPaymentForm` y `StoredPayment` para piezas de su DS, y ningún
  manifiesto lo declara (informe 15 §4.6). Un acoplamiento invisible.
- Sus deudas: 174 bindings `[translations]=` de mano en mano ◐; **no hay fallback por clave**: la
  clave cruda sale en pantalla; y usa el diccionario como **catálogo de datos maestros**
  (`'Station.' + code`) (informe 15 D1, D11, §4.1).

## Decisión (propuesta)

### 1. Las secciones las declara cada elemento

`I18nKeyPrefixes` deja de ser una lista fija y pasa a ser **la unión de las secciones declaradas por
los elementos de la página**. Cada elemento las declara en su record (ADR 0135). Una pieza del DS que
una funcionalidad monta por dentro aporta sus secciones a través de la declaración de esa
funcionalidad: el acoplamiento invisible de NewShore queda escrito.

Se sigue publicando **una vez por página** en `window.synergos.i18n`: en el transporte, Synergos ya
es mejor que NewShore.

### 2. Quién traduce

- **La funcionalidad traduce** con `t()` (el helper que ya existe).
- **Las hojas —las piezas del DS— reciben strings**, nunca claves ni el diccionario entero.
- Una **pieza colocable** (ADR 0134) declara su sección y traduce lo suyo con `t()`, igual que una
  funcionalidad.

### 3. Fallback por clave, en el servidor

Si una clave no tiene traducción en la cultura activa, el bridge publica la de la **cultura por
defecto**. Se resuelve al construir el bridge, no en el cliente. Ninguno de los dos proyectos lo
tiene hoy.

### 4. Lo que rompe el gate

- Una **clave referenciada que no existe** en uSync: error, no aviso.
- Un **prefijo declarado que no casa ninguna clave**: error (hoy serían `Comments.`, `Cart.`,
  `Account.`).
- **Literales visibles** en las funcionalidades: con **línea base**, no a cero de golpe (hoy son
  3.235).

### 5. El diccionario no es un catálogo de datos maestros

Nombres de ciudades, estaciones, categorías: van en su propio origen (la API o el contenido). Ya hay
un precedente sano: las etiquetas de ficha de Realty, Stay y Gobierno se traducen en el servidor con
`ICultureDictionary` y viajan resueltas en la respuesta de la API (informe 16 §1.2).

### Transición

El resolver de la ADR 0135 puede llenar `translations` desde la sección para los 15 componentes que
ya lo leen, mientras migran a `t()`.

## Alternativas consideradas

| alternativa | por qué no |
|---|---|
| **Copiar NewShore tal cual**: secciones pedidas a mano en cada resolver y embebidas por widget | Duplica (`common` ×10 por página), esconde el acoplamiento pieza → sección, pasa el diccionario de mano en mano (174 bindings), deja la clave cruda como fallback visible y usa el diccionario de catálogo de datos maestros. Además su diccionario no está versionado: 25 ítems en uSync frente a ~1.079 claves usadas (informe 15 D12). |
| **Mantener la lista fija y ampliarla** | Publica todo en todas las páginas: hoy son 176 claves (≈7 KB) que nadie lee. Crecer la lista es crecer ese coste, y la lista no dice quién necesita qué. |
| **Mandar el diccionario dentro del `config` de cada elemento** (el `data-initial-value.translations` de NewShore) | Pierde el transporte de una vez por página y vuelve a duplicar por widget. |
| **Traducir todo en el servidor y mandar strings en las props** | Sirve para datos de API (ya se hace con `ICultureDictionary`). Para el texto propio de una funcionalidad infla cada payload y lleva la presentación a C#. |

## Consecuencias

**A favor**

- El bridge publica lo que la página usa, y puede volver al presupuesto del contrato.
- El SSR y la hidratación dicen lo mismo: la clave que usó la vista es la que usa el elemento.
- Una clave que falta en inglés sale en español en vez de salir como `Contact.email`.
- El texto de la UI se traduce en uSync, donde ya vive el diccionario (ADR 0008).

**En contra**

- **Es mucha migración**: 3.235 literales, y 81 claves de Razor sin crear (la mayoría del área de
  cuenta). La línea base del gate evita que crezca; no lo arregla.
- **El bridge pasa a depender de qué hay en la página.** Hoy se construye igual para todas; con esto,
  cada página publica su unión, y la caché de salida tiene que contar con eso.
- **Las 386 coincidencias de texto no son todas la misma intención** («Total» de carrito no es
  «Total» de reserva): no está revisado caso a caso (informe 16 §9). Reutilizar una clave por su
  texto puede ser un error.
- **Cambia el contrato `i18n-bridge.md`**: la forma de `keys` no cambia, la regla de qué se publica
  sí. Sube de versión y hay que corregir de paso sus cifras.

**Qué la vigilaría:** los gates de §4, a construir. `tools/lib/bridge-consumers.spec.mjs` (UI) ya
registra la deuda de `t()` sin consumidores como *«DEUDA, y medida»* (re-leído); sería el sitio
natural de la línea base.

## Qué hace falta para aceptarla (el piloto)

Los cinco elementos del piloto de la ADR 0135 más una funcionalidad (ola 2 del plan de la
auditoría). Se mide:

1. **Tamaño del bridge por página**, antes y después (hoy ≈7 KB en todas).
2. **`t()` con llamadores** en el piloto, y los 15 de `config.translations` alimentados.
3. **Fallback por clave**: una clave que sólo existe en es-CO, pedida en en-US, publica el valor de
   es-CO y no la clave cruda.
4. **Gates mutados**: referenciar una clave inexistente → rojo; declarar un prefijo vacío → rojo; un
   literal nuevo en una funcionalidad → rojo. Y que cada mutación entró.
5. Los 3 prefijos vacíos y las 81 claves de Razor, resueltos o con decisión.

## Resultado del piloto (#186, 2026-10-01)

Piloto en los cinco elementos del piloto 0135 —`kpi-card`, `tag`, `rating-stars`, `dropdown`,
`carousel`— más **una funcionalidad**, `app-launcher`, y `hero-banner`, que ya declaraba su sección y
no la leía (el gate nuevo lo exigió). Ramas `lego/0136` de CMS y UI. Medido sobre una **copia** de
la base local con el CMS del piloto en `:5194` y un CDN propio; la base real no se tocó.

**Por qué `app-launcher`**: es una funcionalidad (presenta y lanza las apps; búsqueda, tres filtros
derivados de los datos, contador) que no carga configuración de negocio —no espera a la ADR 0137—,
tenía 18 textos de interfaz escritos a mano, cinco de ellos (`searchLabel`, `ctaLabel`…) sólo
editables tecleando JSON en `configOverride`, y está colocada en cuatro páginas reales, entre ellas la
portada. Recibió su record (`AppLauncherProps`, tipo Funcionalidad: sin `configOverride`).

**Los cinco criterios:**

1. **Tamaño del bridge, en 98 páginas reales** (las que alcanza el sitio desde `/`, sin duplicar la
   barra final): **antes**, 97 con bridge y las mismas 176 claves en todas —mediana 7.755 B, máx.
   7.801 B; 680.380 B de claves sumando las páginas—; la portada no tenía bridge. **Después**, las
   98 con bridge: 93 sin ninguna clave (331–445 B), `/propiedades` con las 9 de `Slider` (731 B) y
   las cuatro del lanzador con 27 (1.585–1.602 B): **mediana 401 B, máx. 1.602 B, 5.269 B de claves
   en total (−99,2 %)**. Todas dentro del presupuesto de < 4 KB de `host-bridge.md`.
2. **`t()` con llamadores: de 0 a 38 llamadas en 7 elementos**; los textos a mano de esos elementos,
   de 41 a 0 (con el mismo detector del gate). **Los 15 de `config.translations` no se alimentan**:
   ninguno lo monta hoy una vista del CMS (medido por dos caminos: las 91 vistas SynHost y todo el CMS
   buscando su tag o su nombre) y ningún contenido los coloca; piden 85 claves, 34 existentes. La
   «Transición» de esta ADR no tiene a quién servir: cada uno migra a `t()` cuando tenga record.
3. **Fallback por clave en el servidor**: con test (`DiccionarioDelBridgeTests`) y **en vivo con
   control** — en la copia de la base se borró la traducción en-US de `Common.Buttons.Search`. Antes
   (código anterior), el bridge en en-US traía 175 claves y esa no: `t()` sin respaldo habría
   devuelto la clave cruda. Después trae «Buscar» (es-CO) junto a «Save» (`Common.Buttons.Save`, que
   sí tiene en-US). La cultura por defecto la da Umbraco, no el literal `"es-CO"` del builder.
4. **Gates mutados, 19 mutaciones, todas entraron (contenido comprobado) y todas rojas**:
   clave inexistente, clave de una sección que el record no declara, clave no literal, `t()` en una
   hoja del DS, `t()` en un elemento sin record y sección declarada sin usar (`gate:diccionario`,
   UI); literal nuevo en la funcionalidad y deuda pagada sin bajar la línea base (`gate:literales`,
   UI); prefijo vacío en el contrato (UI) y en el record (`ContratoSynHostTests`, CMS); sin fallback,
   sin secciones en la solicitud, sin anotar en el emitter, funcionalidad con `configOverride`,
   sección sin el límite del punto y CSP sin secciones (CMS); y una clave de Razor nueva y una de la
   línea base que ya existe (check 12 de `usync-audit`). La línea base de literales arranca en
   **2.931 textos en 86 de 127 elementos**.
5. **Los 3 prefijos vacíos, resueltos**: la lista fija ya no existe y `Comments.`, `Cart.` y
   `Account.` siguen sin un solo ítem, en uSync y en la base; una sección declarada que no casa ninguna
   clave con texto es rojo en los dos repos. **Las 81 claves de Razor, con decisión medida**: son 81
   sin mayúsculas —145 contando mayúsculas exactas, la cifra del comentario del check 11; Umbraco las
   busca sin ellas (`COLLATE NOCASE`)—, 91 llamadas en 23 vistas, 61 de `Account.*`, y **no existen
   tampoco en la base** (la sospecha del informe 16 queda refutada). No viajan por el bridge (Razor
   puro, ADR 0061/0073) y ningún documento se publica hoy en en-US (95 de 95 sólo en es-CO), así que
   no se ve nada mal: el defecto es que parecen cableadas. **Quedan congeladas** en
   `tools/usync-audit.claves-razor.baseline.json`, vigilada en los dos sentidos; crearlas, con su
   copia en inglés revisada, es trabajo aparte.

**Los cambios que el piloto pide a esta ADR:**

1. **§1 — el bridge se escribe al FINAL del `<body>`**, y la unión la junta el **emitter** (cada
   `SynHostEmitRequest` lleva las secciones del record; un decorador en Web las anota), no un
   recorrido del contenido. En la cabecera aún no se emitió el chrome ni los globales; los módulos
   son diferidos, así que llega a tiempo. Y en las **cuatro** plantillas que montan elementos: tres
   (`PageBare`, `PlatformRoot`, `Error`) no emitían bridge.
2. **§1/0087 — el modo CSP estricto recibe las secciones Y la cultura en la URL.** El endpoint es
   otra petición: sin ellas no publica nada, y medido, `/synergos-bridge.js` salía **en en-US** para
   una página es-CO.
3. **§2 — `t()` acepta marcadores con nombre**: el diccionario usa `{n}`, `{max}`, `{count}` en 20 de
   sus 25 claves con marcador, y el helper sólo rellenaba `{0}`.
4. **§4 — los gates, como quedaron**: la clave referenciada se cruza en el UI contra las `claves` que
   el CMS proyecta al contrato desde uSync (`gate:diccionario`, sin el hermano), y en Razor contra una
   línea base (check 12); el prefijo vacío, en los dos repos; los literales, con línea base **por
   elemento** y en los dos sentidos. Y dos que la ADR no nombraba: **sección declarada que el elemento
   no usa** (se publicaría para nadie) y **`t()` en una librería** (las hojas reciben strings).
5. **Transición — fuera**: no hay componente de `config.translations` montado al que alimentar.
6. **Contexto — las cifras**: 481 ítems eran 446 claves y 35 contenedores; hoy son 511 (473 y 38).

**Hallazgos** (para ticket aparte, detalle en el informe del piloto): el import map del runtime sale
con rutas relativas a la raíz (`--base=/synergos`) y `LeerImports` sólo reubica las absolutas, así que
con el CDN en otro origen o bajo `/cdn-bundles` **nada hidrata** (medido en vivo; el humo conectado
no lo ve); 38 ítems de uSync no están importados en la base del arquitecto (`Synhost.*`,
`Realty.Spec.*`, `Stay.*`, `Gov.Step.*`, `Admin.AccessDenied.*`); `/blog/tag/*` pinta su bridge en
en-US; la sección se publica entera (38 de las 62 claves que publican los siete elementos se usan);
y la descripción de `elementSynAppLauncher` promete `slug`/`displayName` donde el contenido y el
elemento usan `id`/`name`.

**Recomendación: aceptar con los seis cambios.**

## Relación con otras ADRs

- **0083** — el contrato del bridge se conserva; cambia la regla de subconjunto. **0087** — el modo
  CSP estricto sirve el mismo payload por controlador: le aplica igual.
- **0008** — las claves nuevas se proponen primero en uSync.
- **0061 / 0073** — el diccionario del admin es Razor puro y sigue fuera del bridge.
- **0134** — define quién es hoja y quién funcionalidad. **0135** — el record es donde se declaran
  las secciones.

## Referencias

- Informes locales de la auditoría: 20 §2.1 y §5.C.2-3, 16 §1-§2 y §7.4, 15 §4, §7.2, §9, §10.b.
- `docs/contracts/i18n-bridge.md` y `host-bridge.md`.
