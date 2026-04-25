# ADR 0023 — Componentization Layered Architecture (5 capas + global components)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 50
- **Antecedente:** `refactor-docs/architecture/05-componentization-audit-and-refactor-plan.md`

## Context

Ola 49 cerró el inventario de page types pero la auditoría
post-Ola 49 (doc 05) destapó cuatro problemas estructurales:

1. **15 prefijos `element*`** sin regla unificadora (`action`, `comp`,
   `corp`, `info`, `media`, `text`, `struct`, `layout`, `syn`, `form`,
   `flow`, `nav`, `member`, `shop`, `int`).
2. **Duplicados conceptuales**: 3 heros, 2-3 alerts, 2 familias de
   layout primitives (`elementLayout*` × 14 vs `elementStruct*` × 7).
3. **Falta pattern genérico de "componente global configurable"**.
   Ola 49 resolvió el caso *Alert* ad-hoc vía `compAlex` por página,
   sin abrir la puerta a Modal/Banner/etc.
4. **Settings tree solo cargaba escalares**, no piezas editoriales
   compartidas (alertas, modales, banners). El tab "Alex" en cada
   page reflejaba esa ausencia de pattern.

## Decision

Adoptar una **arquitectura de 5 capas estancas** con responsabilidad
única, naming y folder discriminantes, y un pattern transversal de
componente global.

### L1 — Settings (configuración del sitio)

- Vive en el árbol Settings (`platformRoot/settingsRoot/...`) +
  `siteRoot`.
- Edita arquitecto/admin.
- Aliases: `siteConfigSettings`, `themeSettings`,
  `featureFlagsSettings`, `siteRoot`, `cfg*`.
- **Las piezas editoriales globales** (Alerta, Modal, Banner, Footer
  Note, ...) viven aquí como Element Types `cfg*` embebidos en
  `siteConfigSettings.globalComponents` (BlockList).
- **No se renderizan directo**. Se consumen por resolver L5.

### L2 — Schema Mixins (compositions)

- `comp*` reusables. Solo declaran shape. **Nunca renderizan.**
- Subfolders en uSync: `Compositions/Content/`, `Compositions/Dom/`,
  `Compositions/Page/`.
- Aliases conservados.

### L3 — Blocks (Element Types renderizables)

- `IsElement=true`, droppables en BlockGrid/BlockList.
- Cada uno tiene 1 partial Razor.
- **Aliases conservados (Ola 50 no renombra)**, pero los
  `<Folder>` y `<Name>` se unifican a categorías reales:
  Layout, Content, Marketing, Info, Media, Action, Form, Flow,
  Member, Nav, Shop, Int, Misc, Syn (CDN).
- Las descripciones de origen histórico ("Corp —", "Comp —",
  "Struct —") se reemplazaron por categorías reales en el display
  Name del block picker.

### L4 — Pages (Document Types con Layout Composer)

- siteRoot + 4 page types canónicos (Standard/Canvas/Bare/Landing) +
  verticales (postPage, productPage, etc.).
- **Las pages NO componen `cfg*`**. Si necesitan override, lo hacen
  vía un campo opcional o un toggle suppress (ej.
  `compPageOrchestration.suppressGlobalAlerts`).

### L5 — Wiring (resolvers, providers, emitters)

- `IBrandingProvider`, `IBrandThemeProvider`, `IBundleRegistryClient`,
  `ISynHostEmitter`, `IPageRenderContextResolver` (Ola 49) — sin
  cambios.
- **NUEVO**: `IGlobalComponentResolver` — generalización del pattern
  Ola 49 a piezas globales. Expone `GetActiveAlert()` hoy; cuando
  aterricen `cfgModal`/`cfgBanner` se añaden métodos hermanos.

### Pattern transversal: Settings → Resolver → Template

```
Editor configura cfgAlert en siteConfigSettings.globalComponents (1 vez)
    ↓
IGlobalComponentResolver.GetActiveAlert() filtra:
    - alertActive=true
    - dentro de schedule (UTC)
    - page.suppressGlobalAlerts=false
    ↓
_Layout.cshtml renderiza _GlobalAlert.cshtml automáticamente
    en cualquier página
```

Una página puede suprimir la alerta global con un toggle
(`suppressGlobalAlerts`). No necesita componer schema propio.

## Consequences

**Positivas:**

- **Transversalidad real**: cualquier template recibe la alerta
  global sin componer schema. El pattern se replica para
  Modal/Banner agregando un nuevo método al resolver y un nuevo
  `cfg*` ContentType — sin tocar el resto.
- **Editor UX mejorada**: el block picker muestra categorías
  semánticas (Marketing, Info, Layout, ...) en lugar de prefijos
  históricos (Corp, Comp, Struct).
- **Seguridad arquitectónica**: el pattern L1↔L5 evita que el
  schema de page tenga que conocer las piezas globales.
- **Eliminación de duplicados**: 3 BlockGrid pilots retirados,
  5 elementStruct duplicados de elementLayout retirados, compAlex
  retirado (ya no aparece tab "Alex" en page).
- **Preservación de Keys**: cero rompimiento. Aliases de element*
  conservados (uSync match-by-Key + property-by-alias) — solo
  descripciones y carpetas evolucionan.

**Negativas:**

- **Aliases siguen mostrando prefijos históricos** (`elementCorpAlertBar`,
  `elementCompHero`, etc.). Decisión deliberada: rename masivo de
  aliases es alto-riesgo (tocaría composition references, BlockList
  allowedTypes JSON, ContentTypeKeys.cs constants). Diferido a una
  ola posterior cuando haya capacidad para coordinar.
- **No todos los blocks heredan `compContent*` con disciplina total**.
  La auditoría F4 mostró que la mayoría (Card, MediaTextSplit,
  MissionBlock, Feature, GalleryItem, MemberGate, ...) componen
  correctamente. Los pocos gaps (ContactInfo, Stat, AlertBar,
  BannerSlider) tienen semántica propia que justifica fields
  específicos. Documentado, no bloqueado.
- **Solapes SSR↔CDN sin canon decidido**: Hero (Layout/Comp/Syn),
  Stat (Info/Syn), Tabs (Corp/Syn), Accordion (Info/Syn) — quedan
  como decisión de producto en una ola posterior. Hasta entonces el
  editor escoge según preferencia.

**Neutras:**

- `compPageOrchestration` gana un campo `suppressGlobalAlerts`
  (TrueFalse). KISS: un toggle por página, sin override granular
  por tipo de componente global.
- `cfgAlert` reusa los DataTypes `DTSelectAlertVariant` y
  `DTSelectAlertTone` creados en F2. La estética es la misma que
  tenía la cintilla Ola 49 — solo cambia la fuente (Settings vs
  page).

## Alternatives considered

- **Mantener `compAlex` con typo `Alex` y hacer rename in-situ a
  `compAlert`**. Descartado. Era seguir mezclando configuración
  global con composición page-level. La decisión correcta es mover
  a Settings (L1), no renombrar el alias.
- **Hacer `IGlobalComponentResolver` genérico con `Get<T>()` y
  marker interface `IGlobalComponent`**. Descartado por
  sobre-diseño. Hoy hay 1 método (`GetActiveAlert`); cuando lleguen
  Modal/Banner se añaden 2 métodos más. KISS.
- **Renombrar todos los aliases element* → block* en esta ola**.
  Descartado por riesgo. Implicaría tocar BlockList JSONs, todas
  las composiciones que referencian estos types, ContentTypeKeys.cs.
  Diferido a ola posterior con sweep dedicado.
- **Hacer el suppress por-componente** (suppressGlobalAlerts +
  suppressGlobalModals + suppressGlobalBanners). Descartado por
  KISS. Si en el futuro se necesita granularidad, se agrega un solo
  campo `globalComponentsSuppress` (BlockList o multi-select). Hoy
  un solo toggle cubre el 99% de casos.

## Implementation summary (Ola 50, 4 commits)

| Commit | Hash | Foco |
|---|---|---|
| `refactor(ola-50.1.1)` | 49c40a7 | Kill compAlex de pages + cleanup PageRenderContext (sin tab Alex en pages) |
| `refactor(ola-50.1.2)` | b4f3618 | Retira 3 BlockGrid legacy (Basic/Editorial/SynPilot) + 5 elementStruct duplicados |
| `feat(ola-50.2)` | c7c3b3d | IGlobalComponentResolver + cfgAlert + extender siteConfigSettings + suppressGlobalAlerts + _GlobalAlert.cshtml + _Layout bridge |
| `refactor(ola-50.3)` | 209bb76 | UX masiva: Folder/Name de Element Types unificados a categorías reales (Marketing, Info, Layout, ...) |

## References

- `refactor-docs/architecture/05-componentization-audit-and-refactor-plan.md` — auditoría
- ADR 0017 — Layout system per-block compositions
- ADR 0020 — Platform/Settings split + multi-brand
- ADR 0021 — DataType semantics by intent
- ADR 0022 — Page Composition Standard (Ola 49)
