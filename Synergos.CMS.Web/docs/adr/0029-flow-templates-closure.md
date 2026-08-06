# ADR 0029 — Flow templates closure (Ola 58)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 58
- **Cierra:** Flow Engine runtime diferido desde Ola 41 (último módulo
  agente-able pendiente)
- **Extiende:** Ola 41 runtime (`FlowResolver` + `FlowController`)

## Context

El Flow Engine quedó parcialmente cerrado en Ola 41:

- Schema completo (Ola 36): `flowDefinition` + `flowStep` con `flowKey`,
  `flowTitle`, `stepLabel`, `stepContent`, `isTerminal`, `maxSteps`.
- Runtime backend (Ola 41): `FlowResolver` resuelve definition+steps por
  `flowKey`, `FlowController` maneja `/start`, `/next`, `/cancel` con
  cookie `syn-flow-{flowKey}` para tracking de step index.
- Renderers de embebido: `Views/Partials/Elements/Flow/Trigger.cshtml` y
  `Progress.cshtml` para insertar el flow en cualquier página.

**Gap detectado en auditoría Ola 58**: ambos DocTypes tenían
`<DefaultTemplate></DefaultTemplate>` vacío. `FlowController.Start()`
redirige a `descriptor.Steps[0].Url()` — sin DefaultTemplate, esa URL
devuelve 404. **El flow no era navegable públicamente**, solo funcionaba
si el editor embeba un Trigger en otra página y el visitante nunca veía
el step renderizado.

Mismo patrón de gap que Olas 56 (Blog: `postPage` + `postCategoryPage`
sin templates) y 57 (Shop: `productPage` + `productCategoryPage`
sin templates) — y mismo procedimiento de cierre.

## Decision

Crear los dos templates faltantes y asignarlos como `DefaultTemplate`
de sus respectivos DocTypes.

### `Views/FlowDefinition.cshtml`

Landing pública del flow. Modelo:

- `flowKey` + `flowTitle` (default a `Name`) + `flowDescription` del nodo
- `FlowResolver.Resolve(flowKey)` para contar steps publicados
- Header (`h1` + descripción opcional)
- Si `stepCount > 0`: link `/flow/{flowKey}/start` (dispara
  `FlowController.Start`); muestra el conteo de pasos.
- Si `stepCount == 0` o `flowKey` vacío: mensaje editor-facing
  ("Este flow aún no tiene pasos publicados") usando dictionary key
  `flow.empty`.

### `Views/FlowStep.cshtml`

Render del paso individual. Modelo:

- `step.Parent.Value<string>("flowKey")` (asume parent es
  `flowDefinition` — defensa con `string.Equals` del alias)
- `stepLabel` + `stepContent` (TinyMCE rendered) + `isTerminal`
- `FlowResolver.Resolve(flowKey)` para `totalSteps`
- Búsqueda manual del `currentIndex` recorriendo
  `descriptor.Steps[i].Id == step.Id`
- Progress bar `role="progressbar"` con ARIA + label "Paso X de N"
  (dictionary keys `flow.progress.step` + `flow.progress.of`)
- Botón Next como `<form method="post" action="/flow/{flowKey}/next">`.
  Label cambia a "Finalizar" cuando `isTerminal` o último step.
- Botón Cancel como `<form method="post"
  action="/flow/{flowKey}/cancel">`. Omitido cuando `isTerminal=true`
  (no tiene sentido cancelar el step terminal).

### Templates uSync

- `uSync/v9/Templates/flowdefinition.config` (GUID `a6af84da-8312-42f4-9321-0695d9fd6128`)
- `uSync/v9/Templates/flowstep.config` (GUID `8a517f23-5c45-41bb-bee5-bd0e0e5c639b`)

Ambos GUIDs frescos, verificación cuádruple OK (grep en
`Synergos.CMS/`).

### ContentType updates

- `flowdefinition.config`: `<DefaultTemplate>FlowDefinition</DefaultTemplate>`
  + `<AllowedTemplates><Template>FlowDefinition</Template></AllowedTemplates>`
- `flowstep.config`: análogo con `FlowStep`.

## Consequences

**Positivas:**

- **Flow runtime end-to-end**: arquitecto crea siteRoot →
  `flowDefinition` (con `flowKey` + título) → N `flowStep` hijos
  (con `stepKey` + `stepLabel` + `stepContent`) → publica → el
  visitante puede:
  - Visitar la URL del `flowDefinition` y ver la landing con botón
    Start.
  - O acceder a un Trigger embebido en otra página (Ola 36).
  - Click → `FlowController.Start` redirige a `Steps[0].Url()` que
    ahora renderiza correctamente con `FlowStep.cshtml`.
  - Click "Siguiente" → POST a `/flow/{flowKey}/next` → cookie
    incrementa → redirect al siguiente step.
  - Click "Cancelar" → POST a `/flow/{flowKey}/cancel` → cookie limpia.
  - Step `isTerminal` → "Finalizar" → cookie limpia + redirect a
    returnUrl o `/`.
- **Progress consistente**: los renderers `Progress.cshtml` (embebido)
  y `FlowStep.cshtml` (template) leen el mismo cookie y la misma fuente
  (`FlowResolver`), garantizando que el indicador de progreso es
  coherente entre vistas.
- **Editor-facing fallbacks**: los mensajes "no hay pasos publicados"
  y la lógica de `isTerminal` no requieren JS — son SSR puros.

**Negativas:**

- **Sin persistencia más allá de cookies**: como en Ola 41, el step
  index vive solo en cookie `syn-flow-{flowKey}`. Si el visitante
  borra cookies o cambia de dispositivo, pierde el progreso.
  Aceptable para el MVP del producto; futura "Flow v2" podría adoptar
  `IDistributedCache` o member properties cuando aplique.
- **Sin captura de respuestas por step**: el cuerpo de cada step es
  contenido editorial (`stepContent` TinyMCE) — no hay un mecanismo
  built-in para que el visitante responda preguntas o llene un
  formulario por step. Para encuestas reales, integrar con
  `ISurveyEmitter` (Ola 33 schema) o el módulo Forms (`compForms` +
  `ContactFormController`).

**Neutras:**

- 2 GUIDs nuevos. Verificación cuádruple OK.
- `Views/Partials/Elements/Flow/Trigger.cshtml` aún tiene
  `data-pending="flow-runtime-wiring"` como hint heredado de Ola 36.
  El runtime ya está wired desde Ola 41 — el atributo se puede limpiar
  en una micro-ola futura (no urgente, no funcional).

## Alternatives considered

- **Renderizar el step inline desde el `flowDefinition` template**.
  Descartado. Romper el modelo "una URL por nodo" complica
  bookmarking, métricas y deep-links a un step específico.
- **Step content como Layout Composer block** (en lugar de TinyMCE).
  Diferido. El schema actual usa `Umbraco.TinyMCE` (Ola 36) — migrar
  a Block Grid sections sería ADR aparte. Por ahora, TinyMCE es
  suficiente para flujos de texto + imagen embebida.
- **Botón Next como link `<a href="...">`**. Descartado. POST forma
  garantiza que cada avance es deliberado del usuario y no se dispara
  por prefetch / preload del navegador.

## Implementation summary (Ola 58, 1 commit)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-58.1)` | `1e35e14` | `FlowDefinition.cshtml` + `FlowStep.cshtml` + 2 uSync Templates + DefaultTemplate asignado |

## References

- ADR 0017 — Layout system (paralelo: Block Grid sections como contenido)
- ADR 0027 — Blog runtime (mismo patrón de cierre: template + DefaultTemplate)
- ADR 0028 — Shop runtime (mismo patrón)
- `refactor-docs/migration/05-legacy-refinement-inventory.md` — Flow
  Engine en backlog (item #16)
- Ola 41 — Flow runtime (`FlowResolver` + `FlowController` + partials
  Trigger/Progress)
