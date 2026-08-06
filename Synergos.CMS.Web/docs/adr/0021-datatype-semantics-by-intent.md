# ADR 0021 — DataType semantics: one type per editorial intent

- **Status:** Accepted
- **Date:** 2026-04-24
- **Deciders:** Arquitecto + agente, durante ola 43

## Context

El schema inicial (olas 1-42) usaba masivamente `Umbraco.TextBox` con
validación regex para representar intenciones editoriales distintas: URLs
navegables, paths de media, valores enumerados acotados, flags booleanos.
Esto entregaba a los editores un input libre sin ayuda visual — debían
escribir strings exactos (`"primary"`, `"16:9"`, `"bottom-right"`,
`https://...`), y el sistema validaba con regex tras el hecho. Los errores
eran tardíos y el UX pobre.

Durante ola 43 el arquitecto pidió textualmente: *"necesito que todo lo
que sea parametrizado o configurado por tipos, sea un dropdown, y que
todo lo que tenga que ver con contenido use correctamente los types, de
mediatypes"*.

## Decision

**Un DataType por intent editorial, no por tipo de almacenamiento.**

Mapping canonical:

| Intent editorial | DataType | Alias uSync |
|---|---|---|
| URL a contenido interno/externo (CTA, link, policy) | `Umbraco.MultiUrlPicker` MaxNumber=1 | `DTUrlPickerSingle` |
| URL a recurso media single | `Umbraco.MediaPicker3` | `MediaPicker` / `ImageMediaPicker` |
| URL a múltiples media | `Umbraco.MediaPicker3` Multiple | `MultipleMediaPicker` |
| URL externa técnica fija (iframe, script, oEmbed, live, endpoint) | `Umbraco.TextBox` | `Textstring` |
| Valor enumerado acotado | `Umbraco.DropDown.Flexible` | `DTSelect<Kind>` |
| Boolean semántico | `Umbraco.TrueFalse` | `True/false` |
| Texto libre corto | `Umbraco.TextBox` | `Textstring` |
| Texto rico | `Umbraco.TinyMCE` | `Richtext editor` |
| Tags con autocomplete | `Umbraco.Tags` | `Tags` |
| Referencia a contenido interno | `Umbraco.ContentPicker` | `ContentPicker` |

**Cuando una prop tiene N valores fijos y N ≤ 12, crear `DTSelect<Kind>`**
en vez de TextBox + regex. Los DTSelect se reutilizan por intent
semántico (ej. DTSelectOrientation se comparte entre chart/splitter/
timeline), no por elemento.

## Consequences

**Positivas:**

- Editor UX: picker nativo de Umbraco (autocomplete media, content tree
  browser, URL picker con anchor/mailto), dropdown con valores legibles,
  no más typos en strings.
- Eliminación de `<Validation>` regex stale: si el DataType acota los
  valores, el regex es redundante.
- Renderers Razor más limpios: leen `Link.Url`, `IPublishedContent.Url()`,
  `IEnumerable<string>`, en vez de parsear strings manualmente.
- Server-side validation automática: picker/dropdown no permiten valores
  fuera del dominio.

**Negativas:**

- Los renderers de aliases que se renombraron (ej. `ctaUrl → ctaLink`,
  `mediaUrl → media`) requirieron actualización. Se hizo en ola 43
  para los 13 casos afectados.
- Los DTSelect son duplicables — hay ~35 DataTypes DropDown acotados
  que podrían crecer con cada nuevo intent enumerado. Mitigación:
  revisar antes de crear un DTSelect nuevo si alguno existente cubre
  el caso (ej. `DTSelectSide` cubre left/right/top/bottom para
  cualquier "side" prop).

**Neutras:**

- TextBox sigue siendo correcto para: slugs, SKUs, keys de provider,
  labels cortos, URLs técnicas externas (iframe src, script src,
  oEmbed URL) y JSON blobs (sin Umbraco.Json nativo).

## Alternatives considered

- **Mantener TextBox con regex validation:** Descartado. Regex valida
  tardío; editor ve input libre; errores aparecen al guardar, no al
  seleccionar.
- **Crear un único DT.Select.Generic con config override por prop:**
  Descartado. uSync no soporta config per-property en DataType. Un
  DataType por enum es la vía idiomática de Umbraco 13.
- **Reusar DTSelectVariantKey para todos los enums:** Descartado. Mezclar
  variantes visuales BEM con orientation/placement/size crea DataType
  monstruoso que el editor ve con items irrelevantes.
- **Custom DataTypes con UI en AngularJS para props dinámicas** (ej.
  `featureFlagKey` que debería listar keys del appsettings): Diferido.
  Requiere build de App_Plugins plugin por cada caso. Keep como TextBox
  mientras tanto.
