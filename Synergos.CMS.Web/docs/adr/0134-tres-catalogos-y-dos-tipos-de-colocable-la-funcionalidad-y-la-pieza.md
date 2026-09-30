# ADR 0134 — Tres catálogos y dos tipos de colocable: la funcionalidad y la pieza (y nada se retira por defecto)

- **Estado:** Aceptado
- **Fecha:** 2026-09-29
- **Decidido por:** el arquitecto, el 2026-09-29, al cerrar la auditoría de reutilización «que todo
  sea Lego» (#169 · UI#78). La redacción parte de la síntesis de esa auditoría (informe 20).
- **Parte de:** [#172](../../../../../issues/172) · épica [#139](../../../../../issues/139)
- **Conserva:** ADR 0012 y 0132 (registry por CDN), ADR 0015 (SynHost), ADR 0094 (tokens por
  siteRoot), ADR 0099 (import map + SRI), ADR 0113 (un elemento publicado es una app y no se importa)
- **Abre:** ADR 0135 a 0139 (propuestas), que traen de NewShore cómo funciona por dentro

## Contexto

La fábrica genera un vertical desde un spec **reusando piezas**, y su paso S11 pregunta *«¿existe
algo publicado que haga esto?»*. El #163 midió lo que cuesta contestarla por nombre: 1,2 h de 3,5 h.
La auditoría #169 / UI#78 midió por qué esa respuesta no es confiable, y su primera propuesta fue la
salida de siempre: **retirar lo que no tiene consumidor** (informe 10 §2: «RETIRAR 12 · CABLEAR 8»
sobre las 22 piezas inalcanzables del design system).

El arquitecto la rechazó, y la medición posterior le dio la razón.

> Marcas de certeza de la síntesis de la auditoría (informe 20): ✔ comprobado en el disco ·
> ◐ medido por un agente con dos derivaciones o ejecutando, sin re-derivar · ○ cifra de un agente
> sin verificar. «Re-leído» = línea abierta al escribir esta ADR (CMS `9ec00d81`, UI `98b9233`).

### Lo que no tiene consumidor no es lo que sobra

| hecho | cifra | fuente |
|---|---|---|
| Piezas del DS (`libs/shared`) | 55 = 23 primitives + 16 compositions + 12 patterns + 4 states ✔ | informe 20 §2.4 |
| Alcanzables / inalcanzables | 33 / 22 (6 patterns · 8 compositions · 8 primitives · 0 states) ✔ | informe 20 §2.4; la regla 39 del UI dice «9 de 12 patterns» y es falsa |
| De las 12 «a retirar», las que tuvieron alguna vez un consumidor | **0** en 492 commits (`git log --all -S`) ◐ | informe 11 §0 |
| Sitios donde la necesidad existe y se resolvió a mano | **80** ◐ | informe 11 §0 (`05-sitios`: 80 filas re-verificadas) |
| Veredictos pieza por pieza | FUSIONAR 3 · MEJORAR y USAR 5 · USAR 2 · DECLARAR 2 · **RETIRAR 0** ◐ | informe 11 §0 |
| Capacidades del backend sin segundo consumidor | 15; propuesta **RETIRAR 0** ◐ | informe 02 §1 |

No son piezas que perdieron su uso: son una biblioteca escrita antes de tener necesidades que **nadie
buscó cuando la necesidad llegó**. Dos pruebas escritas (◐, informe 11 §0): el doc 22 pidió
**crear** un resumen de revisión que ya existía como `syn-detail-summary`, y en vez de arreglar
`segmented-control` se creó un gemelo nuevo, `syn-segmented` (vivo en
`libs/shells/src/map/results-map.ts:145`, re-leído). La fusión ya está hecha: sobrevive
`syn-segmented` y la montan cuatro pantallas más (CHERCED-DEV/Synergos.UI#83).

Retirar por defecto habría borrado el vocabulario justo antes de que alguien lo pidiera.

### El mismo concepto vive en dos pisos y no se hablan

- De **33 conceptos** que existen como elemento publicado **y** como pieza del DS, **14** montan su
  pieza y **19 no** ◐ (informe 14 §1; prototipo de gate con 6/6 mutaciones).
- Por nombre, 13 pares: montan su pieza `carousel`, `data-table`, `badge` y `heading` (alias de
  `text-block`) ✔. **`card` no monta la suya**: importa `Badge`, `Button` y `Heading` y rehace la
  tarjeta ✔ (`card.ts:33`, re-leído). Y la clase del elemento se llama `CardComponent` (`:39`),
  igual que la del DS: un conteo por nombre lo daba por montado.
- `stepper` son **dos conceptos** con un nombre: el elemento es un indicador de pasos; `syn-stepper`,
  un `+/-` numérico. El indicador de pasos está rehecho **7 veces** y el DS no lo tiene ◐ (informe 14 H8).
- El piso Razor también: 79 piezas SSR, **36** con un gemelo Angular publicado que no se usa ◐
  (informe 16 §6); y `Elements/Corp/TabGroup.cshtml` **no cambia de pestaña** ✔ (estático, informe 14 H2).
- En el backend el patrón se repite: **12 de las 15** capacidades tienen una implementación local del
  mismo concepto en el CMS ◐ (informe 02 §1). Las ADR 0106, 0107 y 0109 citan la regla «no se
  implementa dos veces» y tienen su gemela en `Api.Notifications`, `Api.Catalog` y `Api.Documents`
  (informe 02 H6). Para S11 son dos respuestas a la misma pregunta.

### Qué hace NewShore (el proyecto previo del arquitecto)

- El editor coloca **macros nombradas por funcionalidad**: 168 macros ✔, 139 montan Angular ◐,
  mediana de **2 parámetros** por macro ◐ (informe 15 §3.1).
- Una funcionalidad es **grande por dentro y chica hacia el CMS**: `PaymentContainer` tiene 309
  ficheros y 57 componentes por dentro y **un** tag hacia el CMS ✔ (informe 20 §1).
- Las piezas chicas también se colocan, pero **siempre** a través de un contenedor que las registra
  (`ReadMoreContainer`, `TitleHeading`…); una pieza de su DS no se puede montar sola (informe 15 §7.5).
- **No hay marcador explícito** entre pieza y funcionalidad: la frontera real es técnica (informe
  15 §7.4, confirmado leyendo). Y la pureza de sus piezas no se sostuvo: el 20 % inyecta el bus.

Synergos ya es mejor en varias cosas (registry agnóstico con versión, SRI y `tier`; diccionario una
vez por página; tema en runtime; montar una pieza sin envoltorio) y le falta cómo funciona NewShore
por dentro. El arquitecto lo dijo así: *«no quiere decir que vamos a pasar a como trabajan allá,
porque aquí ya hemos hecho un gran trabajo»*.

## Decisión

### 1. Tres catálogos

| catálogo | qué es | para qué |
|---|---|---|
| **Piezas Razor del CMS** | SSR puro | casos específicos de contenido y SEO |
| **Piezas Angular del DS** (`libs/shared`) | el vocabulario chico | con él se arman las funcionalidades por dentro |
| **Funcionalidades / módulos Angular** | apps de vertical, flujos | grandes por dentro, un tag hacia el CMS |

### 2. Dos tipos de colocable

Lo que el editor coloca en el Block Grid es una de dos cosas:

| tipo | qué es | qué recibe del CMS |
|---|---|---|
| **Funcionalidad** | nombrada por **lo que hace** (`eventos` = cartelera y entradas; `gov` = trámites), grande por dentro | **sólo cableado**: sus secciones de diccionario, la configuración de negocio que el editor no toca, 3-4 decisiones del editor y la identidad de la sesión |
| **Pieza** | colocable suelta, **cuando le sirve al editor** (`rating-stars`, `accordion`, `kpi-card`…) | contenido y decisiones del editor + una sección de diccionario; **monta su pieza del DS** |

Una funcionalidad **no expone su configuración interna al editor**. Hoy la frontera sólo se infiere
(27 funcionalidades / 64 piezas por un criterio medible, ◐ informe 16 §7.1: habla HTTP, recibe una
clave de despliegue o de runtime, o es shell de vertical). Cómo se marca en el registry (un `kind`
junto al `tier` que ya existe) no se decide aquí: es parte del manifiesto por elemento (plan de la
auditoría, ola 7).

Quién decide qué, en el modelo objetivo (informe 20 §1):

| decisión | editor | diccionario | config de negocio | código / runtime |
|---|---|---|---|---|
| qué va, dónde, con qué espaciado | ✔ Block Grid + Layout Composer | | | |
| contenido por instancia | ✔ | | | |
| decisiones editoriales (variante, mostrar, página destino) | ✔ como **selector**, nunca texto libre | | | |
| textos de la funcionalidad (labels, errores, aria) | | ✔ | | qué claves usa |
| catálogos, límites, moneda, comisión, endpoints | | | ✔ (despliegue / siteRoot) | |
| identidad, ruta, sesión | | | | runtime (`window.synergos.member`) |
| ensamble interno y coordinación | | | | ✔ |

### 3. Nada se retira por defecto

- **Una pieza sin consumidor es vocabulario del catálogo de la fábrica**, no deuda. Se queda.
- **Si está duplicada, se FUSIONA**: la que sobrevive absorbe lo mejor de las dos (así quedaron
  `segmented-control` → `syn-segmented`, `panel` ⇄ `syn-alert`, `configurable-form` ⇄ el motor de
  campos de `syn-dynamic-form`, informe 11).
- **Retirar** queda para un único caso: **un duplicado inferior cuya fusión no aporta nada**, con la
  evidencia escrita.
- **Que un colocable salga del CMS no es retirar la pieza.** Un elemento puede dejar de ser colocable
  y su pieza del DS seguir en el catálogo. Para que salga hace falta evidencia contra dos preguntas
  (informe 14): *¿lo coloca algún DocType?* y *¿tiene efecto lo que el editor elige?*. Con esa vara
  hay tres candidatos con evidencia (`range-slider`: su evento no lo escucha nadie; `skeleton`:
  `aria-busy` perpetuo; `pagination`: toma `currentPage` del editor y nunca lee `?page`) — y
  **los tres siguen pendientes de decisión de producto**, igual que si `<synergos-dropdown>` sale o
  queda como host delgado.

### 4. Regla de los dos pisos

**Un elemento publicado que tiene gemela en el DS la monta; nunca la reimplementa.**

- Si el elemento es mejor que la pieza (pasa con `tooltip`, `avatar`, `progress-bar`, `modal-trigger`
  según el informe 14), **el DS absorbe lo mejor** y el elemento queda como host delgado.
- Si comparten nombre y son **dos conceptos** (`stepper`), no se fusionan: se renombra el que cuesta
  menos renombrar.
- `dropdown`: **gana el DS** (decidido el 2026-09-29). `syn-dropdown` absorbe lo que el elemento
  tiene de correcto para un menú (informe 13).

### 5. Es un refinado: lo agnóstico se conserva entero

Registry por CDN (ADR 0012 / 0132), import map y SRI (ADR 0099), Preact como segunda plataforma,
SynHost (ADR 0015), Layout Composer, tokens `--syn-*` por siteRoot (ADR 0094). De NewShore se toma
**cómo funciona por dentro**, en ADRs separadas y propuestas:

| ADR | qué trae |
|---|---|
| 0135 | el resolver tipado por elemento |
| 0136 | el diccionario por secciones y la regla de i18n |
| 0137 | la configuración de negocio fuera del editor |
| 0138 | la coordinación de página, como eventos DOM |
| 0139 | un bundle con varias entradas colocables |

## Alternativas consideradas

| alternativa | por qué no |
|---|---|
| **Retirar por defecto lo que no tiene consumidor** (la primera propuesta de la auditoría: «RETIRAR 12») | Las 12 nunca tuvieron consumidor porque **nadie las buscó**, no porque sobraran: hay 80 sitios que las necesitaban. Se habría borrado `detail-summary` en la misma época en que el doc 22 pedía crearlo. |
| **Copiar el modelo de NewShore tal cual** | Su `data-module-path="ruta#NgModule"` con `SystemJsNgModuleLoader` ata el contrato a Angular; un `AppModule` raíz compartido ata todos los widgets a una versión; cada pieza suelta necesita un contenedor que la registre (Synergos monta cualquier elemento del registry sin envoltorio); sus variantes son herencia de componentes + `fileReplacements`; su theming es SCSS de compilación con 0 custom properties (informe 15 §10.c). Se toma el cómo, no el esqueleto. |
| **Un solo catálogo: todo es elemento publicado** | Es lo que produjo los 19 incumplimientos de los dos pisos: el elemento y la pieza evolucionan por separado y divergen. |
| **Declarar el tipo (funcionalidad/pieza) sólo por convención de nombre** | NewShore no lo marca y su frontera quedó técnica e implícita. Aquí el tipo se decide en esta ADR; dónde se escribe (registry o manifiesto) es de la ola del manifiesto. |

## Consecuencias

**A favor**

- S11 tiene un modelo con el que contestar: *¿es una funcionalidad o una pieza?, ¿qué cableado pide?*
- Ningún agente vuelve a proponer borrar por defecto (lección A.8 del informe 20): la regla está
  escrita.
- Un duplicado encontrado tiene dirección (fusionar) en vez de dos salidas a elegir.

**En contra, y dicho de frente**

- **El vocabulario cuesta bytes.** Las 22 piezas inalcanzables viajan en `sg-shared.js` (809.386 B)
  y son el 43,2 % de la fuente del DS ✔. Esta ADR acepta ese coste mientras no haya partición del
  bundle compartido.
- **Los 19 incumplimientos de los dos pisos son deuda desde hoy.** Y un host delgado sobre el DS
  actual **pierde cosas**: 7 piezas usan colores fijos que no siguen los temas por siteRoot ◐ y el DS
  trae ≥33 textos en inglés ◐ (informe 14 H3-H4). Tokens e i18n del DS son prerrequisito de cada
  conversión, no un detalle.
- **Los pares capacidad ↔ implementación local no se resuelven aquí.** Esta ADR dice «fusionar, no
  borrar»; cuál de cada par es la buena (Engagement, Documents #26, la ñ del catálogo) sigue siendo
  decisión de producto (informe 20 §4.2 punto 8).
- **El piso Razor necesita su propio contrato de interacción** (TabGroup mudo) y una decisión por
  concepto sobre si gana el SSR o el elemento (36 gemelos sin usar).

**Qué la vigila**

- **Hoy, en parte:** el gate de UI#78 vigila que la deuda de piezas inalcanzables no crezca (línea
  base 22). Mide si alguien alcanza una pieza, **no** si el elemento que debería montarla la monta:
  `TabsComponent` está viva por `academy` mientras `tabs` la reimplementa, y #78 sigue verde.
- **Propuesto, no construido:** `gemelas-del-design-system` (informe 14 §5): para cada par
  elemento → clase del DS, las dos señales (tag + clase importada) por cierre transitivo; línea base
  de 19 vigilada en los dos sentidos; un elemento nuevo que se llame como una pieza obliga a
  clasificarlo. Prototipo con 6/6 mutaciones cazadas.

## Relación con otras ADRs

- **0012, 0015, 0094, 0099, 0132** — se conservan sin cambios: son lo agnóstico.
- **0096** (module-mount) — es el camino de las funcionalidades más grandes; sigue igual.
- **0113** — coherente: la pieza compartida vive en una lib y el elemento es un bootstrap fino que la
  monta. La regla de los dos pisos es su lado del DS. La ADR 0139 propone aclararla para bundles con
  varias entradas.
- **0126 / 0127** — una pieza puede estar embebida por otro bundle **y** ser colocable (`seat-map`):
  el tipo de colocable no excluye el embebido.
- **0106, 0107, 0109** — la regla «no se implementa dos veces» se cumplió dentro de cada árbol y no
  entre los dos (informe 02 H6). Esta ADR no decide qué lado gana; sí que la salida es fusionar.

## Referencias

- Informes de la auditoría (locales, no versionados): 20 (síntesis), 11 (DS: usar o fusionar),
  13 (dropdown), 14 (dos pisos), 15 (NewShore), 16 (cableado), y de la otra sesión 02
  (capacidades) y 10 (síntesis de las fases 0-3).
- NewShore: código local del arquitecto, no versionado en este repo.
