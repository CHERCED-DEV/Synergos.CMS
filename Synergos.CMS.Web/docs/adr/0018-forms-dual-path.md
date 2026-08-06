# ADR 0018 — Forms: dual-path (custom SSR + iframe bridge)

- **Status:** Accepted
- **Date:** 2026-04-22
- **Deciders:** Project owner
- **Source:** promoted from `refactor-docs/adr-drafts/0018-forms-dual-path.md` (Draft 2026-04-21)
- **Authorises:** Ola 23 (Forms family — FormContainer + FormField + FormEmbed + DT.BlockList.FormFields), commit `2a3077d`
- **Related:** ADR 0009 (Extension seams mandatory), ADR 0015 (SynHost framework-agnostic integration)

## Context

El CMS necesita soportar formularios de contenido editorial (contacto,
newsletter, lead gen, encuestas cortas). Se identificaron dos familias
de use-case:

1. **Forms simples con N campos y POST a un endpoint externo**: 90%
   del tráfico editorial. Newsletter signup, formulario de contacto,
   lead capture. El backend puede ser Formspree, Netlify Forms,
   Mailchimp, o Synergos.API futuro.
2. **Forms complejos con branching / lógica condicional / pagos /
   workflows**: menor volumen, pero cuando ocurre es crítico.
   Presupuesto en Typeform / Jotform / Umbraco.Forms. El CMS no debe
   reimplementar esto.

El legado Epic Fail 2 tenía `elementForm*` (2 items: `formEmbed`,
`formField`) pero sin coherencia — el container no existía como
Element Type y no había guardrails a11y.

## Decision

Se adopta un modelo **dual-path** con tres Element Types en Ola 23:

### Path A — Custom SSR (cobertura del 90%)

- **`elementFormContainer`**: emite `<form method="post"
  action="{formEndpoint}">`. Props: `formTitle` (Culture),
  `formEndpoint` (Nothing, https regex-validated), `submitLabel`
  (Culture default "Enviar"), `fields` BlockList mandatory 1+.
- **`elementFormField`**: emite `<label>` + `<input|textarea>` +
  helpText. Props: `fieldLabel` (Culture), `fieldName` (Nothing,
  slug regex `^[a-z][a-zA-Z0-9_-]*$`), `fieldType` (Nothing, enum
  whitelist: `text|email|tel|number|textarea|date|url`),
  `fieldRequired` (Nothing), `fieldPlaceholder` (Culture),
  `fieldHelpText` (Culture + wired via `aria-describedby`).
- **Backing**: `DT.BlockList.FormFields` (mandatory 1+).
- **Exposición**: `elementFormField` **NO aparece en el top-level del
  Editorial BlockGrid** — solo accesible dentro de
  `FormContainer.fields`. Evita campos huérfanos por drop-accidental.
- **Validación client-side**: nativa HTML5 (`required`,
  `type="email"` parsing, `pattern`). No hay JS adicional en el
  primer pase.
- **Submission**: form nativo POST. El endpoint procesa; el CMS no
  maneja respuesta directamente. Si se desea UX tipo SPA (XHR +
  toast), el design-system JS puede interceptar el submit y hacer
  `fetch()`.

### Path B — Bridge iframe (cobertura del 10%)

- **`elementFormEmbed`**: emite `<iframe src="{embedUrl}"
  sandbox="allow-scripts allow-forms allow-same-origin">`. Props:
  `embedUrl` (Nothing, https mandatory), `embedTitle` (Culture
  mandatory — WCAG 4.1.2), `embedHeight` (Nothing, numeric default
  600).
- **Use case**: Typeform, Jotform, Google Forms, encuestas con
  branching lógico. Si el arquitecto adopta Umbraco.Forms, su
  endpoint de render también se consume por este mismo element (no
  requiere nuevo ElementType).

### Umbraco.Forms: decisión diferida

- **NO se adopta** el paquete Umbraco.Forms en esta ola.
- El arquitecto puede adoptarlo más adelante sin breaking changes:
  `elementFormEmbed` ya sirve como bridge apuntando al endpoint de
  render de Umbraco.Forms.
- Si en el futuro se requiere workflow editorial propio (form
  builder, validación server-side avanzada, submissions store), se
  evaluará Umbraco.Forms vs. desarrollo custom en
  `Synergos.CMS.Application` con `IFormSubmissionHandler` seam.

### A11y mandatorio

- `fieldLabel` mandatory: sin label sin formulario. Wire via
  `<label for>` al input id.
- `fieldHelpText` wired a `<input aria-describedby>`: el screen
  reader lee el help text.
- `fieldRequired` emite `required` attr + `aria-required="true"` +
  asterisco visual con `aria-hidden="true"`.
- `embedTitle` mandatory en iframe: obligatorio por WCAG 4.1.2. Si
  falta, el renderer aborta.
- Sandbox iframe restrictivo: `allow-scripts allow-forms
  allow-same-origin` — sin `allow-top-navigation` ni
  `allow-popups-to-escape-sandbox`.

## Consequences

**Positive**

- Cobertura práctica completa del 100% de use-cases sin acoplarse a
  un paquete grande.
- Editor que crea FormContainer puede razonar en términos simples
  ("¿qué campos pido?") sin elegir motor primero.
- A11y es obligatoria por schema, no opcional por buena voluntad.

**Negative / limitaciones conocidas**

- No hay validación custom cross-field (ej. "password matches
  confirmation") en Path A. Workaround: escribir JS en el
  design-system.
- No hay captcha / anti-spam integrado. Se delega al endpoint
  (Formspree, Netlify). Si se necesita en el CMS, agregar
  `elementFormCaptcha` en ola futura.
- No hay form submissions store en el CMS. Los submissions viven
  en el endpoint externo.
- `fieldType` es whitelist cerrada (7 tipos). Si se necesita
  `color`, `range`, `file`, ampliar el regex + renderer en su
  propia ola. Prohibido abrir a free-form.

## Re-evaluación

- Si la tasa de request de "form complejo custom en CMS" supera el
  20% del total, reabrir el ADR para evaluar adopción de
  Umbraco.Forms o custom form builder.
- Si la integración con Synergos.API (futura) requiere auth +
  server-side validation, evaluar `IFormSubmissionHandler` seam en
  `Synergos.CMS.Application`.
