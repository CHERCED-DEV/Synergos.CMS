# ADR 0102 — Verticales #7 Educación (scholar) + #8 Booking (meridian): dos núcleos de negocio nuevos, 100% composables, con identidad de tema propia

- **Status:** Accepted
- **Date:** 2026-06-26
- **Deciders:** Arquitecto + agente, fase SynergosLabs (OLA 4.6). Aplica la RECIPE de tema de ADR 0101 y el patrón de siembra composable de `DevContentFiller` (SeedVertical/SeedShop/SeedBlog/SeedEducacion). Verificado contra código vivo (`DevContentFiller`, `DevMediaFactory`, `DefaultShopQuery`, `DTSelectPageThemeVariant.config`, `DropdownOptions`, `elementSynCalendar` + los demás SynHost partials de los `elementSyn*`).
- **Relacionados:** ADR 0101 (contrato de identidad + recipe de tema), ADR 0094 (design tokens como única fuente de verdad), ADR 0042 (DTSelect mirror en `DropdownOptions`), ADR 0022/0023 (page composition standard + componentización por capas), ADR 0010/0020 (branding vía provider, sin `if (brand.Key == "X")`), ADR 0015 (framework-agnóstico CDN — `elementSyn*` + `<synergos-*>`), ADR 0008 (schema vía uSync), ADR 0013 (sin seeders automáticos; tooling tras `DevSeed`), ADR 0028 (Shop runtime + `IPriceFormatter` es-CO). "Componer, nunca hardcodear" (memoria `feedback_compose_never_hardcode`).

---

## Context

La promesa del producto es "un motor, mil productos": un mismo core componible
que se convierte en marca, blog, tienda, healthcare… sin reescribir nada. Ya
viven varios verticales (Entidad, Blogs, Tienda, Healthcare) sembrados de forma
composable. OLA 4.6 estrena **dos núcleos de negocio nuevos** que ejercen el
patrón de extremo a extremo y validan que añadir un vertical es un procedimiento,
no un proyecto:

- **#7 Educación** — academia online (catálogo de cursos, curso detalle,
  inscripción).
- **#8 Booking** — plataforma de reservas/citas, registro **enterprise**
  (catálogo de servicios/recursos reservables, calendario, registro multipaso de
  la reserva).

> **Pivote (revisión 2026-06-26):** la versión inicial de OLA 4.6 traía un
> vertical #8 "Restaurante" (tema `ember`). El arquitecto lo **elimina** y lo
> reemplaza por **Booking** (tema `meridian`). Se retira por completo: la siembra
> de Restaurante (siteRoot + menú + páginas), su tarjeta del launcher y toda
> mención en docs. Educación se **mantiene** intacto.

La premisa sagrada del producto es **0 hardcode**: las páginas no se escriben en
Razor; se **componen** como nodos de contenido cuyo body es un Layout Composer
(BlockGrid de secciones) con bloques nativos + componentes CDN `elementSyn*`,
estilados por clases (`compDomClass`/`Variant`) y publicados por `IContentService`.
El look es código (CSS/JS); el contenido no. ADR 0101 dejó la RECIPE para
estrenar un tema; este ADR la aplica para `scholar` y referencia `meridian`
(definido por otro agente) y suma la siembra composable de los dos núcleos.

## Decision

### 1. Temas: `scholar` (Educación) y `meridian` (Booking)

Cada vertical pinta su propia identidad por `siteRoot` mediante el contrato 1:1
`brandKey` ↔ `pageThemeVariant` ↔ `data-theme` ↔ bloque `[data-theme="X"]`:

- **`scholar`** (Educación) — académico, sobrio y cálido. `color-scheme: light`.
  Base marfil claro `#FAF7EF`, surface `#FFFFFF`, ink verde-bosque `#142F29`,
  primary teal `#0E7C7B` (CTA principal), accent ámbar/gold `#D9A441` (rol
  emphasis), info indigo/azul académico `#36638C`, danger `#C0492F`. Registro
  propio (NO clon de `terraLux`). Definido en `wwwroot/css/syn-tokens.css` +
  `DTSelectPageThemeVariant.config` + `DropdownOptions`.
- **`meridian`** (Booking) — identidad enterprise para reservas/citas. **Lo
  define otro agente** (bloque `[data-theme="meridian"]` en
  `wwwroot/css/syn-tokens.css`, item en `DTSelectPageThemeVariant.config` y
  mirror en `DropdownOptions`). Este ADR/seeder **solo referencia el LITERAL
  `meridian`** como `brandKey` + `pageThemeVariant` del siteRoot de Booking
  (RECIPE ADR 0101). El seeder no toca `syn-tokens.css`, `DTSelectPageThemeVariant`
  ni `DropdownOptions`.

No se toca `_BrandThemeStyle.cshtml`, ni el resolver, ni el layout: el contrato
es genérico y brand-agnóstico (ADR 0010+0094); un tema nuevo no requiere código
condicional por marca. Que un solo seeder + RECIPE pinte un vertical entero es la
prueba de la promesa "añadir un vertical es un procedimiento".

### 2. Siembra composable de cada núcleo (0 hardcode)

Cada núcleo = un `siteRoot` por path bajo el `platformRoot` + Home + 2 páginas
clave, **todo como nodos de contenido** (`siteRoot.sections` / `pageBase.sections`
= BlockGrid JSON construido con `BlockGridJsonBuilder`), publicados por
`IContentService`. Idempotente por nombre. No-op con grace si falta schema
(blocks Angular no importados → fallback SSR; Shop no importado → nota/cards).

**#7 Educación** (`/educacion`, tema `scholar`):
- **Home** — hero + cursos destacados (`feature-grid`) + value props
  (`feature-grid`) + testimonios (`testimonial-section` CDN) + **planes**
  (`pricing-table` composable, precio = DataType editable) + FAQ (`faq-section`
  CDN) + CTA.
- **Cursos** — catálogo filtrable: `search-box` (CDN) + `data-grid` (CDN,
  `dataSource` = endpoint GET, `columnsJson` con sortable/filterable) — fallback
  a `feature-grid` del catálogo si el data-grid no está importado.
- **Curso detalle** — temario/lecciones (`accordion` CDN, items {title,content})
  + instructor (`media-text` split) + **CTA inscribirse** (`member-login` SSR
  para contenido gated + `form-stepper` CDN multi-paso → POST a Forms).

**#8 Booking** (`/booking`, brandKey + pageThemeVariant = `meridian`):
- **Home** — hero (propuesta + CTA "Reservar") + **cómo funciona / servicios**
  (`feature-grid`) + value props (`feature-grid`) + **planes** (`pricing-table`
  composable, precio = DataType editable) + testimonios (`testimonial-section`
  CDN) + FAQ (`faq-section` CDN) + CTA.
- **Servicios** (catálogo de servicios/recursos reservables) — `search-box`
  (CDN) + `data-grid` filtrable (columnas categoría/duración/precio) →
  **fallback `feature-grid`** si el data-grid no resuelve. Si Shop está
  importado, además muestra el precio **real por categoría** vía
  `elementShopProductGrid` filtrado (Consultoría / Espacios / Bienestar), con el
  precio formateado por **`IPriceFormatter` (es-CO)** desde `productPriceBase`
  NUMÉRICO. **CERO precio hardcodeado.** `DefaultShopQuery` scopea por el
  `siteRoot` del request, así que el catálogo de Booking **no se cruza** con la
  Tienda.
- **Reservar** — **`calendar`** (CDN `elementSynCalendar`, `eventsEndpoint` =
  disponibilidad GET JSON) para elegir fecha/slot + **`form-stepper`** (CDN, 2
  pasos: reserva + datos) → POST a Forms + **confirmación** (mission con el qué
  sigue) + (opcional) `map-embed` (SSR) + horarios (`accordion` CDN) + FAQ + CTA.

Todo con **fallback con grace** si falta un ElementType (cada helper Syn es
no-op si su tipo no está importado; el llamador degrada a SSR/cards/nota).

### 3. Tarjetas del launcher como composición

Las dos tarjetas (Educación, Booking) se añaden en `SeedPlatformLauncher` con el
**mismo patrón composable** que las existentes: `elementCompCard` dentro de la
Section del `platformRoot.introBody` (`cssClass = syn-launcher__card …`, icono +
estado `--live` + `ctaLink` a su siteRoot). La tarjeta de Restaurante se
**elimina**. Cero hardcode en `.cshtml`: el look PS3 vive en clases CSS
(`.syn-launcher*`) y el contenido se publica por `IContentService` (editable en
backoffice).

### 4. Reuso de componentes ya existentes

La siembra reutiliza los componentes vivos (CDN `elementSyn*` que hidratan desde
`C:\LOCAL_CDN` + SSR nativos): `feature-grid`, `faq-section`,
`testimonial-section`, `pricing-table`/`pricing-plan`, `accordion`, `carousel`,
`lightbox-gallery`, `search-box`, `data-grid`, `form-stepper`,
`countdown-digital`, **`calendar`** (nuevo helper), `member-login` (SSR),
`map-embed` (SSR), `shop-product-grid` (SSR), `hero`, `cta`, `media-text` split,
`mission`. Helpers en `DevContentFiller`: los previos (`AddSynAccordion`,
`AddSynCarousel`, `AddSynGallery`, `AddSynSearchBox`, `AddSynDataGrid`,
`AddSynFormStepper`, `AddSynCountdown`, `AddMemberLogin`, `AddMapEmbed`,
`AddProductGridFiltered`) + **`AddSynCalendar`** nuevo (emite
`<synergos-calendar>` con `eventsEndpoint` + `initialMonth` opcional, no-op con
grace si `elementSynCalendar` no está importado). `DevMediaFactory` se **reusa**
sin cambios (`GetOrCreatePickerValue` / `GetOrCreateMediaUrl` — gradiente limpio
on-brand, sin texto).

## Consequences

**Positivas**

- **Dos núcleos de negocio nuevos, 100% composables**: prueban el patrón
  "un motor, mil productos" de extremo a extremo. Añadir un vertical es seguir
  `SeedVertical` + RECIPE de tema, no escribir Razor.
- **0 hardcode confirmado**: precios vía `IPriceFormatter` (es-CO), chrome-text
  vía Dictionary, contenido como nodos publicados por `IContentService`, look
  como CSS/clases. Nada baked en `.cshtml`.
- **Booking = registro enterprise reservas/citas**: calendario + catálogo de
  servicios con precio real (Shop scoped) + registro multipaso → un vertical
  completo sin un módulo a medida.
- **Catálogo sin colisión**: reusar Shop scoped-al-siteRoot da el catálogo de
  Booking con precios reales sin un módulo nuevo y sin contaminar la Tienda.
- **Sin código por marca**: contrato genérico (ADR 0010+0094); `meridian` se
  referencia, no se cablea condicionalmente.

**Negativas / trade-offs**

- **Doble fuente de verdad CMS↔UI** (heredada de ADR 0094/0101): `scholar` y
  `meridian` deben espejarse en `Synergos.UI/_brand.scss` para que el render
  hidratado por web component iguale al SSR. Diferido al build de los verticales.
- **Mirror dual `DropdownOptions`** (ADR 0042): cada variante toca dos archivos
  (XML + C#); costo conocido y aceptado. (`meridian` lo gestiona otro agente.)
- **`meridian` dependiente de otro agente**: el seeder publica el siteRoot con
  `pageThemeVariant = meridian` aunque el item DTSelect/mirror aún no exista. Si
  el dropdown no ofrece `meridian`, el siteRoot igual lo lleva en el valor
  (FlexibleDropdown almacena el string), pero el tema solo pinta cuando el bloque
  CSS `[data-theme="meridian"]` y el item DTSelect estén importados.

**Neutras**

- **0 GUIDs nuevos en este cambio**, 0 paquetes NuGet/npm. El ElementType
  `elementSynCalendar` ya existía en uSync (no se crea schema). Cambios de este
  agente concentrados en `DevContentFiller.cs` (helper `AddSynCalendar` +
  vertical Booking + tarjeta launcher; eliminación total de Restaurante) y los
  docs (`0102-*.md`, `00-current-state-synergos-cms.md`). `scholar` (CSS +
  DTSelect + mirror) y `meridian` (los define otro agente).
- **uSync Import requerido** (item DTSelect `scholar`, y `meridian` del otro
  agente). **Recompila C#** (mirror + `DevContentFiller`). El CSS sirve en
  caliente. **Re-seed** (`POST /dev/fill-synergos-pages`, gated por
  `Synergos:DevSeed:Enabled`) para crear los siteRoots, páginas, el catálogo de
  servicios y las tarjetas del launcher (y para que la tarjeta de Restaurante
  desaparezca al re-publicar el launcher).

## Alternatives considered

- **Renderizar las páginas de los verticales en Razor (baked)** — rechazado:
  viola la premisa sagrada 0 hardcode. Todo se compone como contenido.
- **Un módulo "Booking" propio para el catálogo de servicios** — rechazado: el
  catálogo es un conjunto de servicios con precio; la infra de Shop (scoped al
  siteRoot por `DefaultShopQuery`) ya lo resuelve sin un módulo nuevo y con
  `IPriceFormatter` garantizando 0 hardcode de precio. (Un módulo de motor de
  reservas — disponibilidad real, locking de slots — es una evolución futura,
  fuera del alcance de esta siembra demostrativa.)
- **Hardcodear los precios de los servicios en el JSON de los bloques** —
  rechazado: el precio es dato editorial; vive en `productPriceBase` (numérico) y
  se formatea en runtime. Hardcodearlo rompería es-CO y la edición en backoffice.
- **Mantener Restaurante junto a Booking** — rechazado por decisión del
  arquitecto: el vertical #8 pivota de Restaurante a Booking; Restaurante se
  retira por completo (siembra + launcher + docs).

## References

- ADR 0101 — Contrato de identidad + recipe de tema (eventsNight/terraLux).
- ADR 0094 — Design tokens como única fuente de verdad + identidad por-siteRoot.
- ADR 0042 — DTSelect mirror en `DropdownOptions`.
- ADR 0022/0023 — Page composition standard + componentización por capas.
- ADR 0010/0020 — Branding vía provider (sin conditional por marca).
- ADR 0015 — Framework-agnóstico CDN (`elementSyn*` + `<synergos-*>`).
- ADR 0028 — Shop runtime + `IPriceFormatter` es-CO (precios del catálogo).
- `Services/DevContentFiller.cs` — `SeedEducacion`/`SeedBooking` + helpers
  (`AddSynCalendar` nuevo).
- `Services/DevMediaFactory.cs` — `GetOrCreatePickerValue`/`GetOrCreateMediaUrl`
  (gradiente limpio on-brand) — reusados sin cambios.
- `Services/DefaultShopQuery.cs` — scoping por siteRoot (catálogo sin colisión).
- `uSync/v9/ContentTypes/elementsyncalendar.config` — `<synergos-calendar>`
  (`eventsEndpoint` + `initialMonth`).
- `wwwroot/css/syn-tokens.css` — bloque `[data-theme="scholar"]`; `["meridian"]`
  lo define otro agente.
- `uSync/v9/DataTypes/DTSelectPageThemeVariant.config` — item `scholar`;
  `meridian` lo añade otro agente.
- `feedback_compose_never_hardcode` (memoria — componer, nunca hardcodear).
- `IPriceFormatter` / `EsCoPriceFormatter` — formato es-CO de precio del catálogo.
