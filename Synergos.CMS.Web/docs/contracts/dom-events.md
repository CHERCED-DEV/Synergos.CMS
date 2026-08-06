# DOM events contract — `<synergos-*>` ↔ host

- **Contract version:** v1
- **Owner:** Synergos.UI (emits) — CMS subscribes opt-in.

## Premisa

Los Web Components Synergos comunican estado al host (CMS) **solo
via DOM CustomEvents**. No hay shared store, no hay window.* mutations
mutables, no hay TypeScript types compartidos. El host decide si
escucha — los components nunca rompen si nadie escucha.

## Naming convention

```
syn:{component}:{event}
```

- `syn:` prefix garantiza zero-collision con eventos nativos /
  third-party.
- `{component}` matchea el tag sin el prefix `synergos-` (e.g.
  `accordion`, `form-stepper`, `media-text`).
- `{event}` es **past tense** para state changes (`opened`,
  `submitted`) o **imperative** para intents (`request-close`).

## Eventos canónicos

### Lifecycle (todos los components emiten)

| Event | When | `detail` payload |
|---|---|---|
| `syn:component:ready` | Component hidratado y attached al DOM | `{ tag: string, version: string }` |
| `syn:component:error` | Hydration error | `{ tag: string, message: string, error?: any }` |

### Interaction (per component, opt-in)

Ejemplos canónicos por categoría:

**Action components:**
- `syn:button:clicked` — `{ id?: string, label?: string }`
- `syn:cta-group:item-clicked` — `{ index: number, action: string }`

**Form components:**
- `syn:form-stepper:step-changed` — `{ from: number, to: number }`
- `syn:form-stepper:submitted` — `{ values: Record<string, unknown>, outcome: 'success' \| 'failure' \| 'partial' }`
- `syn:form-stepper:validation-failed` — `{ stepIndex: number, errors: Array<{ field: string, message: string }> }`

**Disclosure components:**
- `syn:accordion:opened` — `{ id: string }`
- `syn:accordion:closed` — `{ id: string }`
- `syn:modal:opened` — `{ id: string }`
- `syn:modal:closed` — `{ id: string, reason: 'user' \| 'esc' \| 'backdrop' \| 'programmatic' }`

**Media components:**
- `syn:video-player:play` — `{ currentTime: number }`
- `syn:video-player:ended` — `{ duration: number }`
- `syn:gallery:item-shown` — `{ index: number }`

**Outcome (used by alerts, banners, toasts):**
- `syn:toast:dismissed` — `{ id: string, autoDismissed: boolean }`
- `syn:cookie-consent:decided` — `{ choice: 'all' \| 'necessary' \| 'custom', categories?: string[] }`

## Outcome enum (canónico)

Tri-state — alineado con `IAuditTrailWriter.AuditEvent.Outcome`:

```typescript
type SynOutcome = 'success' | 'failure' | 'partial';
```

- `success` — operación completa, todo OK.
- `failure` — error fatal, nada se completó.
- `partial` — algo ocurrió pero no todo (e.g. bulk action 5/10).

`partial` es **mandatory** en form/bulk components que pueden
fallar parcialmente. Si un component nunca tiene partial, no usar
el campo (event sin `outcome`).

## Bubbling + composition

Todos los CustomEvents synergos:
- `bubbles: true` — para que el host pueda escuchar via delegation
  en un parent común.
- `composed: true` — atraviesa shadow DOM si el component lo usa.
- `cancelable: false` por default — eventos son notificación, no
  command. Si llega un caso command (e.g. `request-close`), opt-in
  a `cancelable: true`.

## Standard listener pattern (CMS-side)

```html
<main id="content" data-synergos-host>
    <synergos-form-stepper id="contact-form">...</synergos-form-stepper>
</main>

<script>
document.addEventListener('syn:form-stepper:submitted', (e) => {
    if (e.detail.outcome === 'success') {
        // Server-side already handles via /api/forms POST.
        // Cliente solo necesita analytics ping.
        navigator.sendBeacon('/api/analytics/track',
            JSON.stringify({ event: 'form.submitted', detail: e.detail }));
    }
});
</script>
```

## Custom events fuera del namespace

Componentes pueden emitir **otros events nativos** (`change`,
`input`, `click`) — siguen el comportamiento DOM estándar. El
namespace `syn:` solo aplica a eventos custom de business logic.

## Versioning

- v1 (este doc): canon inicial cap-220.
- Adición de evento nuevo: minor bump (sin breaking).
- Cambio de payload existente: major bump + nuevo doc + ADR.

## Implementación referencia

UI side, en cualquier component:

```typescript
import { Component, EventEmitter, Output } from '@angular/core';

@Component({...})
export class AccordionComponent {
  @Output() opened = new EventEmitter<{ id: string }>();
  // Angular EventEmitter ya emite syn:accordion:opened cuando
  // se compila como custom element con createCustomElement, IF
  // el output se configura con { bubbles: true, composed: true }.
}
```

Mapping del nombre del Output (`opened`) al CustomEvent name
(`syn:accordion:opened`) lo hace el factory wrapper:
`vitals/runtime/createSynergosElement.ts` (UI side, deferred si
no existe). Convención: `syn:{tag-without-synergos-prefix}:{outputName}`.

## Compliance test

Para verificar contract compliance, el UI puede shippear un
`@synergos/contract-tests` (deferred) que valida cada component:

- `ready` event fires post-hydration.
- `error` event fires en hydration failure simulada.
- Naming convention coincide.
- Bubbles + composed flags.

Hasta entonces, manual smoke test en demo page `/dev/element-grid`.

## References

- ADR 0015 — SynHost framework-agnostic.
- `host-bridge.md` — cómo el CMS se conecta al UI runtime.
