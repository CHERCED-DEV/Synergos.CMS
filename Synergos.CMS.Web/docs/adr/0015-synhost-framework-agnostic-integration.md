# ADR 0015 — SynHost: contrato de integración framework-agnóstica CDN↔CMS

- **Status:** Accepted
- **Date:** 2026-04-22
- **Deciders:** Project owner
- **Source:** promoted from `refactor-docs/adr-drafts/0015-synhost-framework-agnostic-integration.md` (Draft 2026-04-20)
- **Authorises:** Olas 8.5 (pilot) + 26-30 (batches A-E, 71/71 `elementSyn*` DONE)
- **Related:** ADR 0008 (uSync hybrid SoT), ADR 0009 (Extension seams mandatory), ADR 0012 (CDN contract consumed), ADR 0013 (No automatic seeders), ADR 0014 (PageBasic first product case)

## Context

El producto Synergos monta bloques de UI hosteados en CDN dentro de
páginas editoriales del CMS. La capacidad de que un editor componga
una página con bloques construidos por frameworks externos (Angular
como first adapter; React/Svelte/Vue solo por necesidad, ver ADR 0012
+ memory `feedback_framework_agnostic_integration`) es **core del
producto**, no integración accesoria.

El legado Epic Fail 2 resolvió esto con **71 Umbraco Macros prefijo
`cdn*`** en `uSync/v9/Macros/` + partials `Views/MacroPartials/
Cdn*.cshtml`. Esa elección tuvo tres problemas:

1. **Mecanismo deprecado**: Umbraco 14+ descontinúa Macros; Umbraco
   13 LTS ya las marca legacy.
2. **Parámetros untyped**: los parámetros de Macro son strings
   sueltos; no componen con `compIntegration` ni heredan
   Variations=Culture ni se testean como DTOs.
3. **Naming acoplado a delivery**: el prefix `cdn*` encodea el
   mecanismo de entrega, no la intención. Además colisiona con el
   análisis que vocabulariza "host runtime" y "artifact", no "CDN".

El roadmap oficial dice explícitamente (§Frente 5 Runtime de host,
§Frente 7 Multi-framework):

> "El contrato y el host deben ser agnosticos; la implementacion
> concreta de cada bloque puede existir solo en el framework que
> convenga; cuando tenga valor real, un bloque puede tener mas de
> un adapter; la decision de framework es por necesidad, no por
> simetria artificial."

## Decision

Se establece el patrón **SynHost** como único mecanismo para
integrar bloques CDN-hosted en el CMS:

### 1. Schema

- **ElementType por bloque**: alias `elementSyn<Block>` (ej.
  `elementSynHero`, `elementSynAvatar`, `elementSynCountdownClock`).
  Variations=Culture por defecto (ver memory
  `feedback_variations_culture_default`).
- **Composition base**: todos componen `compIntegration`, que provee
  `configOverride` (TextArea Nothing) para JSON de config específica
  de instancia.
- **Props tipadas**: cada ElementType declara las props que el
  editor edita con DataTypes tipados (TextBox, MediaPicker, Dropdown,
  etc.). Se serializan al config JSON que viaja al bundle.
- **No Umbraco Macros**: cero archivos en `uSync/v9/Macros/`. Los 71
  `cdn*` del legado quedan clasificados DESCARTAR en
  `05-legacy-refinement-inventory.md` v3+.

### 2. DOM emitido

- **Custom element tag**: `<synergos-<block-kebab-case>>`. Ejemplo:
  `elementSynCountdownClock` → `<synergos-countdown-clock>`.
  Convención framework-agnóstica ya usada en el legado.
- **Script tags**: `<script type="module" defer>` por cada dependencia
  + main entry, en el orden que el registry devuelve. URLs absolutas
  provistas por `IBundleRegistryClient`.
- **Config JSON attribute**: `config='...'` en el custom element tag.
  System.Text.Json escapa `<>'&"` a `\u00XX` por default; el
  attribute es seguro sin encoding adicional.

### 3. Seam y default impl

- **Interface**: `Synergos.CMS.Interfaces.ISynHostEmitter.
  EmitAsync(SynHostEmitRequest, CancellationToken)` devuelve
  `SynHostEmitResult(string ScriptHtml, string ElementHtml, bool
  RegistryResolved)`.
- **Default impl**: `Synergos.CMS.Application.Services.Impl.
  DefaultSynHostEmitter`. Consume `IBundleRegistryClient` (ADR 0012)
  — NO cablea paths CDN. Sin `ILogger<T>` (ADR 0002: Application no
  pulls `Microsoft.Extensions.Logging`).
- **Registrado en `SeamComposer`** (ADR 0005) con lifetime Singleton
  tras `OptionsComposer`.

### 4. Renderer partials

- **Renderer real**: `Views/Partials/SynHost/<Block>.cshtml`, acepta
  `IPublishedElement`, proyecta props, llama
  `ISynHostEmitter.EmitAsync`, renderiza `ScriptHtml` + `ElementHtml`
  con `@Html.Raw`.
- **Block Grid convention wrapper**: `Views/Partials/blockgrid/
  Components/elementSyn<Block>.cshtml`, acepta
  `BlockGridItem<IPublishedElement>`, delega a `SynHost/<Block>` vía
  `@Html.PartialAsync`. Un archivo trivial de una línea de
  delegación.
- **Layout Composer wrapper** (ADR 0017 — Layout Composer Olas 42+):
  contrato común `IPublishedElement` como input; los renderers del
  Layout Composer delegan a los partials SynHost cuando contienen
  `elementSyn*` blocks.

### 5. Integration Element Types (no-CDN)

Tres casos distintos de "integración externa" se separan semánticamente:

- **`elementSyn<Block>`** (CDN-hosted web component): este ADR.
- **`elementIntIframeHost`**: iframe genérico con sandbox. Usa
  `Views/Partials/Int/IframeHost.cshtml` (sin `ISynHostEmitter` —
  iframe ≠ CDN bundle).
- **`elementIntScriptHost`**: script con src de origen allowlisted
  (`Synergos:Cdn:AllowedScriptOrigins` en appsettings, configuración
  pendiente). Renderer diferido hasta entonces.

Los Element Types legacy `elementIntAngularHost`, `elementIntMfHost`,
`elementIntExternalWidget` quedan **DESCARTADOS** — su intent se
cubre por la familia `elementSyn*`. Las compositions `compAngularMount`
y `compMfMount` también: reemplazadas por `compIntegration`.

### 6. Composer-agnóstico (garantía contractual)

El mismo ElementType `elementSyn<Block>` es usable en:

- **Block Grid** (native Umbraco): convención
  `Views/Partials/blockgrid/Components/<alias>.cshtml` o view custom
  configurada en el DataType Block Grid.
- **Layout Composer** (ADR 0017): los renderers SSR iteran areas
  y delegan a los partials SynHost cuando hay `elementSyn*` dentro.
- **Futuros composers**: cualquier mecanismo que sepa entregarnos un
  `IPublishedElement` puede renderizarlo.

El renderer partial es **uno solo por bloque**. La UI de edición
varía por composer; el servidor-side render es único.

## Consequences

**Positive**

- **Framework-agnóstico por diseño**: cambiar el adapter de un
  bloque (ej. Hero de Angular a React) NO requiere tocar ni schema
  ni C# ni renderers del CMS. El bundle CDN publica un manifest
  distinto; `IBundleRegistryClient` devuelve URLs nuevas; el DOM
  tag `<synergos-hero>` se mantiene; el browser monta lo que sea.
- **Alineado con roadmap oficial** y memorias
  (`feedback_framework_agnostic_integration`, `feedback_cdn_integration_is_core`,
  `feedback_synhost_naming_convention`).
- **Testable end-to-end sin Umbraco**: `DefaultSynHostEmitter` se
  testea con `FakeBundleRegistryClient` + `CultureInfo`; los
  renderers se testean con Integration Tests contra
  `WebApplicationFactory`.
- **Un mecanismo menos**: cero Macros, cero seam
  `IMacroHostResolver`, cero `AngularHost`/`MfHost`/`ExternalWidget`
  duplicados. Simplifica mental model.
- **Preparado para `ArtifactManifest`** (Frente 3 del roadmap):
  cuando el contrato CDN publique manifest con `framework`,
  `version`, `integrity`, `ssrCapable`, `dependencies` —
  `IBundleRegistryClient` se evoluciona a `IArtifactResolver` sin
  romper `ISynHostEmitter`.

**Negative**

- **Coste por-bloque más alto que un macro**: cada bloque requiere
  1 ElementType + 1 renderer + 1 BG wrapper (~3 archivos). Un macro
  eran ~2 archivos. Pero el costo se paga una vez; el ahorro en
  testabilidad, tipado y Variations se recupera en días.
- **71 macros del legado NO se migran 1:1**: quedan DESCARTADOS; el
  producto se re-implementa por olas según necesidad real. Para
  pilotar, 3 blocks (`avatar`, `badge`, `divider`) cubren el smoke
  test del contrato.
- **Dependencia del Block Grid DataType**: el editor necesita al
  menos un DataType Block Grid con los `elementSyn*` como bloques
  permitidos. Ola 8.5 crea `DT.BlockGrid.SynPilot` con los 3 blocks
  piloto; olas 26-30 los promueven a `DT.BlockGrid.Editorial` y
  `DT.BlockGrid.Sections` (Ola 42.5).

## Alternatives considered

- **Mantener Macros como mecanismo primario**. Rechazada:
  deprecación en Umbraco 14 + parámetros untyped + colisión con
  roadmap.
- **ElementType con framework en el alias** (`elementAngHero`,
  `elementReactHero`). Rechazada: viola principio framework-
  agnóstico; lock-in implícito; impide que un mismo bloque tenga
  múltiples adapters.
- **Un único ElementType genérico `elementSynHost`** con
  `blockAlias` como prop string. Rechazada: pierde tipado de props
  por bloque; editor escribe strings libres; Block Grid no puede
  mostrar bloques distintos en el picker; pierde Variations
  semánticas.
- **Dual-track: Macros para legacy body RichText + ElementTypes
  para Block Grid**. Rechazada en Ola 8.5. Si en el futuro aparece
  necesidad concreta de embeber un bloque inline en RichText, se
  re-evalúa. No se crea un rail dual por especulación.

## Implementation status (Olas 8.5 + 26-30)

✅ **Completado al 100%**:

- `compIntegration` + `elementIntIframeHost` + `elementIntScriptHost`
  + 71 `elementSyn*` blocks en `uSync/v9/ContentTypes/` (batches
  Pilot + A + B + C + D + E FINAL).
- `DT.BlockGrid.SynPilot` + `DT.BlockGrid.Editorial` con todos los
  synhost blocks.
- `ISynHostEmitter` + `SynHostEmitRequest` + `SynHostEmitResult` en
  `Synergos.CMS.Interfaces`.
- `DefaultSynHostEmitter` en `Synergos.CMS.Application.Services.Impl`.
- `SeamComposer` registra `ISynHostEmitter → DefaultSynHostEmitter`
  con lifetime Singleton.
- `ContentTypeKeys` + `DataTypeKeys` actualizados con constantes.
- 71 renderer partials `Views/Partials/SynHost/<Block>.cshtml` +
  `Views/Partials/Int/IframeHost.cshtml`.
- 71 Block Grid convention wrappers en
  `Views/Partials/blockgrid/Components/`.

🔜 **Diferido** (registrado):

- `elementIntScriptHost` renderer + allowlist CSP config
  (`Synergos:Cdn:AllowedScriptOrigins`).
- Evolución de `IBundleRegistryClient` → `IArtifactResolver` cuando
  CDN publique contrato (ADR 0012, roadmap §Frente 3).
- `HttpBundleRegistryClient` (reemplaza `StubBundleRegistryClient`)
  cuando CDN team publique los 5 puntos del contrato.
