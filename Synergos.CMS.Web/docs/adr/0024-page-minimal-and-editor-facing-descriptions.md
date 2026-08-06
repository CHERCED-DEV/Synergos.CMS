# ADR 0024 — Pages mínimas + descripciones editor-facing (refinamiento Ola 51)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 51
- **Supersede parcial:** ADR 0022 §1 (extiende, no contradice)

## Context

Ola 49 introdujo el perfil "Standard" (`pageBase`) con 6 propiedades
de contenido baked-in: `heading`, `subheading`, `summary`,
`featuredImage`, `sections`, `sectionsAfterBody`. Ola 49 también
introdujo `compPageOrchestration.showIntro` para renderizar
automáticamente subheading/summary/featured como un "intro block".

Tras la primera revisión real del editor en backoffice (Ola 51) se
detectaron dos fricciones:

1. **Subheading/summary/featuredImage son decisiones editoriales por
   página, no estructurales.** Para una página el editor quiere un
   hero con CTAs; para otra solo título; para otra una galería.
   Hornearlos en el page type obliga a TODAS las páginas Standard a
   verlos siempre, mezcla decisiones de presentación con definición
   del DocType, y duplica lo que ya puede armarse con bloques
   (`elementCompHero` + `compContentMedia` + etc.).
2. **Las descripciones de los campos referencian aliases internos**
   (`compPageOrchestration.showTitle=true`, `Variations=Culture`,
   `IGlobalComponentResolver`, `Reusa DT.X`, `ADR 0022`). El editor
   no entiende el contexto y la columna de descripción del backoffice
   las trunca al ser largas.

## Decision

### Parte A — Pages mínimas (slim Standard + Landing)

Las propiedades de contenido en pages se reducen al mínimo
imprescindible:

| Page type | Propiedades de contenido finales |
|---|---|
| `pageBase` (Standard) | `heading`, `sections`, `sectionsAfterBody` |
| `pageBasic` (Canvas) | `sections` |
| `pageBare` | `sections` |
| `pageLanding` | `heading`, `sections` |

Lo eliminado:
- `pageBase.subheading`, `pageBase.summary`, `pageBase.featuredImage`
- `pageLanding.summary`, `pageLanding.featuredImage`
- `compPageOrchestration.showIntro`
- `PageRenderContext.ShowIntro`
- `Views/Shared/_PageIntro.cshtml`

El equivalente funcional se compone con bloques arrastrables dentro
de `sections`: hero con `elementCompHero` (compone `compContentHeading`
+ `compContentText` + `compContentMedia` + ctaItems), banner con
`elementCompCtaBanner`, etc.

**Heading sigue como prop** porque es metadato editorial reusable
(SEO, breadcrumbs, navegación) — no contenido visual del cuerpo. Si
está vacío, el renderer hace fallback al nombre del nodo.

### Parte B — Descripciones editor-facing (style guide forzado)

Toda descripción XML que el editor pueda ver en el backoffice
cumple, sin excepción:

1. **Una frase clara**, en español, ≤ 120 caracteres.
2. **Cero referencias a internals**:
   - Sin nombres de aliases internos (`compXxx`, `cfgXxx`,
     `pageXxx`, `*Mode`, etc.) en el cuerpo de la descripción.
   - Sin nombres de clases C# (`IFeatureGate`,
     `IGlobalComponentResolver`, `BlockGridModel`, ...).
   - Sin `Variations=Culture`, `Reusa DT.X`, `Ola N`, `ADR XXXX`,
     `member-gated`, `lowercase-hyphen`, `DocType`.
3. **Vocabulario editorial**, no técnico: "alerta global", "ruta
   de navegación", "imagen para redes sociales", "miembros
   autenticados". Nada de "OG image", "auth required", "DOM class",
   "HTTP middleware".
4. **Names en español** cuando son visibles al editor. Inglés solo
   para términos técnicos universales (CTA, JSON, SEO, OpenGraph).
5. **El valor del campo se explica con su efecto, no con su
   alias**. Ejemplo: en lugar de "Si compPageOrchestration.
   showTitle=true, renderiza el heading", escribir "Renderiza el
   título principal arriba del cuerpo. Apágalo si tu primer bloque
   ya trae el título."

## Consequences

**Positivas:**

- **Page Standard pierde 3 props redundantes** (subheading/summary/
  featuredImage). El tab "Contenido" pasa de 6 props a 3. Editor
  se enfoca: título + Layout Composer + cierre.
- **Una sola fuente para hero/intro/featured**: `elementCompHero`.
  Se elimina la duplicación page-prop ↔ block.
- **Backoffice más legible**: descripciones cortas no se truncan en
  la columna; el editor entiende qué hace cada campo sin contexto
  arquitectónico.
- **Standard y Landing convergen estructuralmente** (heading +
  sections). La diferencia ahora vive en la decisión editorial:
  Standard usa bloques editoriales (artículos, casos), Landing usa
  bloques de conversión (hero + beneficios + CTA).

**Negativas:**

- **Migración de contenido pre-Ola 51**: si un nodo `pageBase`
  existente tenía valor en `subheading`/`summary`/`featuredImage`,
  esos valores quedan huérfanos al re-importar el schema. Mitigado
  por el reset de Content que el arquitecto hizo antes del refactor.
- **Editor que esperaba el "intro auto"** ahora debe arrastrar un
  blockHero. Trade-off favorable: gana flexibilidad y consistencia
  con el resto del sitio (que ya usa bloques para todo).
- **`showIntro` sale de `compPageOrchestration`**. El campo que un
  editor pudo haber tocado en Ola 49-50 desaparece. Sin impacto
  funcional (la prop nunca llegó a usarse en producción).

**Neutras:**

- `_PageIntro.cshtml` eliminado. El H1 se renderiza inline en
  `PageBase.cshtml` y `PageLanding.cshtml` (3 líneas Razor cada uno).
- `PageRenderContext.ShowIntro` eliminado del record. El resolver
  C# pierde una computación y queda más SOLID.
- Descripciones reescritas en 11 archivos. Estructura del schema sin
  cambios — solo strings.

## Style guide (memoria del agente — compacto)

> **Patrón obligatorio para `<Description>` y `<Name>` editor-facing.**
>
> Antes (mal):
> `<Description><![CDATA[Renderiza el heading principal automáticamente si compPageOrchestration.showTitle=true. Vacío usa el nombre del nodo.]]></Description>`
>
> Después (bien):
> `<Description><![CDATA[Renderiza el título principal arriba del cuerpo. Apágalo si tu primer bloque ya trae el título.]]></Description>`
>
> Antes (mal):
> `<Name>Allowed Roles CSV</Name>` + `<Description><![CDATA[Roles permitidos separados por coma (ej. "premium,staff"). Vacío = cualquier miembro autenticado.]]></Description>`
>
> Después (bien):
> `<Name>Roles permitidos</Name>` + `<Description><![CDATA[Roles permitidos separados por coma (ej. "premium,staff"). Vacío = cualquier miembro.]]></Description>`

Aplica a TODAS las composiciones, page DocTypes, settings DocTypes y
cfg* Element Types nuevos o modificados a partir de Ola 51.

## Alternatives considered

- **Mover `showIntro` a un toggle "Show hero"** en compPageOrchestration.
  Descartado. El "hero" no es una decisión binaria — varía por
  layout, copy y media. Un block dedicado da mucho más control.
- **Mantener `subheading`/`summary` por compatibilidad**. Descartado.
  Content estaba vacío al momento del refactor; preservar campos sin
  uso real es deuda técnica gratis.
- **Hacer las descripciones bilingües (ES/EN)**. Descartado. El
  Variations=Culture del DocType no aplica a `<Description>` (es
  metadato del schema, no del Content). Fijar idioma único.
- **Custom CSS en App_Plugins para ensanchar la columna de
  descripción**. Descartado. Hackear el backoffice por culpa de
  texto largo es revertir el principio. La fix correcta es texto
  corto.

## References

- ADR 0022 — Page Composition Standard (Ola 49) — referenciada y
  refinada
- ADR 0023 — Componentization Layered Architecture (Ola 50) — pattern
  de cfgAlert que sigue el principio
- `feedback_editor_description_style` — memoria existente del agente
  (refinada con esta ADR)
- `refactor-docs/architecture/05-componentization-audit-and-refactor-plan.md`
  — auditoría que detectó la deuda
