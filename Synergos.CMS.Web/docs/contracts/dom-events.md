# DOM events contract — `<synergos-*>` ↔ host

- **Contract version:** v2 — suma el protocolo `synergos:` del coordinador de flujos (ADR 0140 F4, ver al final)
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

- v1: canon inicial cap-220.
- v2 (2026-10-09, ADR 0140 F4): el protocolo `synergos:` de `<synergos-flujo>` (sección siguiente). Es
  aditivo: los eventos `syn:` de arriba no cambian.
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

## v2 — El protocolo `synergos:` del coordinador de flujos (ADR 0140 F4)

Un coordinador sin render, `<synergos-flujo flujo="…">` (`display: contents`), envuelve a los elementos
que participan de un flujo de negocio y lleva sus pedidos a la puerta del CMS
(`/api/flujos/{flujo}/{operacion}`). Lo coloca el CMS —hoy la vista del bloque de Eventos, con la
clave del flujo escrita por el servidor— y lo DEFINE cada participante al cargar su bundle
(`definirCoordinador()`, idempotente: la primera definición gana). El prefijo es `synergos:`, el de las
ADR 0138 y 0140, no el `syn:` de v1: en este protocolo hablan elementos entre sí, no un elemento con el
host, y v1 no tenía ningún emisor.

| Evento | Quién lo despacha, y dónde | `detail` |
|---|---|---|
| `synergos:register` | el participante, desde sí mismo; `bubbles`, `composed` | `{ protocolo, flujo, responder? }` — el coordinador más cercano del mismo flujo llama `responder` de forma SÍNCRONA y corta la propagación |
| `synergos:submit-request` | el participante, desde sí mismo; `bubbles`, `composed` | `{ protocolo, flujo, solicitud, operacion, consulta?, cuerpo?, llave? }` — lo atiende el ancestro más cercano del MISMO flujo, que lo marca `atendida` al vuelo y corta la propagación |
| `synergos:submit-result` | el coordinador, EN el solicitante (no burbujea) | `{ protocolo, solicitud, operacion, outcome: 'success' \| 'failure', resultado }` — `resultado` es `{ ok: true, valor, estado, correlacion }` o `{ ok: false, rechazo: { code, status, transient, detail, origen, extra }, correlacion }` |
| `synergos:flujo-ocupado` | el coordinador, en cada participante registrado | `{ ocupado }` — sólo en los bordes; el coordinador refleja lo mismo en `aria-busy` |
| `synergos:flujo-presente` | el coordinador, en `document`, al conectarse | `{ protocolo, flujo }` — el re-anuncio para un participante que esperó a un coordinador definido tarde |

Reglas:

- **El atributo es una CLAVE de flujo, nunca una ruta**: el DOM no es frontera de confianza, y una clave
  que la tabla generada de la puerta no conoce no sale a la red (`cliente.flujo_desconocido`).
- **`protocolo`** viaja en cada pedido (hoy `1`). Uno distinto se rechaza sin tocar la red
  (`cliente.protocolo_distinto`): con dos bundles en la página gana la primera definición, y un cambio
  incompatible sube el número para convivir con el viejo.
- **Single-flight**: dos pedidos IDÉNTICOS en vuelo (operación, consulta, llave y cuerpo) son una llamada.
- **Sin coordinador**, el pedido contesta al instante `cliente.sin_coordinador`; no se cuelga.
- **Lo que el coordinador NO decide**: el orden de las operaciones (lo secuencia el participante y lo hace
  cumplir el servidor), la `Idempotency-Key` (la pone el participante: la de la intención), reintentar una
  escritura, traducir ni navegar. Los rechazos se leen por `code` y `transient`, nunca por `title`.
- **Declarados y sin construir**: `synergos:ready` y `synergos:navigate`. No tienen productor mientras
  redirigir-al-pago sea del #183; se construyen en el piloto de la ADR 0138 (dos funcionalidades, un botón
  y un participante Preact), con el orden y «todos listos».

Lo vigila `vitals/core/src/flujos/flujos.spec.ts` del UI (jsdom): ancestro del mismo flujo, anidados,
flujo desconocido (con un pedido del MISMO flujo que el atributo, que es el que llega a la guarda),
protocolo distinto, single-flight y `aria-busy`, el coordinador que llega tarde, que importar el módulo no
defina la etiqueta, y los nombres de esta tabla escritos en crudo —un participante que habla con las
cadenas, sin la constante, es atendido—. Que cada participante DEFINA el coordinador al cargar, antes de
registrarse, lo vigila `tools/lib/coordinador-de-los-participantes.mjs` del UI (ADR 0140, endurecimiento
de la F4).

## References

- ADR 0015 — SynHost framework-agnostic.
- `host-bridge.md` — cómo el CMS se conecta al UI runtime.
