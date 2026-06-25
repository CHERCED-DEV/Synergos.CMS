# ADR 0096 — Grandes funcionalidades como módulos Angular completos (patrón "module-mount")

- **Status:** Proposed (proyección de arquitectura — patrón rector para los módulos grandes; nada construido aún)
- **Date:** 2026-06-25
- **Deciders:** Arquitecto + agente, fase SynergosLabs. Diseño verificado por workflow multi-agente (4 exploradores + diseño + auditoría adversaria + síntesis contra código vivo).
- **Prerequisito de:** ADR 0097 (Dashboard) y ADR 0098 (Healthcare).

## Context

El arquitecto pide funcionalidades grandes (Dashboard analítico, módulo Healthcare)
y fija una restricción nueva: **"grandes funcionalidades deben ser con Angular,
como módulo completo"**. Esto introduce una tensión real con el modelo híbrido
existente:

- Hoy el híbrido monta **componentes** (`elementSyn*` → `<synergos-*>`): bloques
  de contenido configurados en el CMS y renderizados por la CDN. Son piezas,
  no apps.
- Un Dashboard o un Healthcare son **aplicaciones**: estado, ruteo interno, CRUD,
  auth-gated, datos operacionales. No son contenido editorial.

La pregunta de este ADR: ¿cómo se montan **apps Angular completas** sin romper
ninguno de los 10 principios (grafo de dependencias, no-SaaS, CDN consumida,
framework-agnóstico, schema-via-uSync)?

**Hechos verificados contra código vivo** (el workflow leyó los archivos, no asumió):

1. `BundleDescriptor` ya tiene el campo `Tier` (incluye `module` / `experience`)
   y `ISynHostEmitter` (`SynHostEmitRequest(BlockAlias, Props, ConfigOverrideJson,
   Culture)`) es 100% genérico — no nombra Angular. **No hace falta cambiar el
   contrato CDN.**
2. El fallback offline **YA EXISTE**: `DefaultSynHostEmitter` emite
   `data-synergos-cdn-offline` + `<div class="syn-cdn-offline-fallback">` y
   `SynHostEmitResult.RegistryResolved` cuando el registry no resuelve. Lo único
   que falta es el `<noscript>`.
3. `IBundleRegistryClient` expone **solo** `TryResolveAsync(string elementKey)` —
   **no hay enumeración**. Un "picker dinámico alimentado por el CDN" es
   irrealizable sin añadir enumeración a un contrato **consumido, no owned**
   (violaría el principio 7).
4. **Ningún store actual particiona por `siteRoot`** (audit/forms/2FA escriben a
   `App_Data/syn-*/` plano). Inventar un eje `siteRootKey` solo en los módulos
   nuevos sería incoherente con el resto y rozaría tenant-isolation (principio 8).
5. El precedente de stores (`FileSystemMemberTwoFactorStore`, `FileSystemAuditTrailWriter`)
   usa `File.WriteAllText` / `File.AppendAllText` — **escritura NO atómica**.

## Decision

Se adopta el patrón **module-mount**: una app Angular completa se monta con el
**mismo mecanismo SynHost** que cualquier componente, marcando el descriptor con
`Tier="module" | "experience"`. El CMS sigue siendo el COMPOSITOR; Angular renderiza
la app; los datos fluyen por seams.

### 1. `elementSynModuleMount` — host universal (uno parametrizado, no uno por módulo)

Un **único** ElementType (schema uSync) monta TODOS los módulos. Hereda
`compIntegration` (configOverride), `compMemberGating` (requiresAuth/allowedRolesCsv)
y `compDom*` (wrapper). Props propios:

- `moduleAlias` — qué módulo montar → es el `BlockAlias` que va a `ISynHostEmitter`
  → `<synergos-{alias}>` y bundle `synergos-{alias}` resuelto por el registry.
- `moduleInitialRoute` (TextBox) — ruta interna inicial (ej. `/patients`).
- `moduleHeightHint` (DropDown: auto/viewport/fixed) — evita layout shift.

> Filtro 3-preguntas (composition-design): el comportamiento "montar app" es
> idéntico; solo cambia el alias → **NO** se crea `elementSynHealthcare` /
> `elementSynDashboard`. La config operacional viaja por API, no por schema.

### 2. `moduleAlias`: catálogo owned-by-CMS + validación, NO enumeración del CDN

El registry no enumera (hecho verificado #3). Por tanto: `moduleAlias` es un
`DropDown.Flexible` con los aliases conocidos (catálogo **owned-by-CMS**) **+
validación server-side** vía `IBundleRegistryClient.TryResolveAsync(alias)` en el
emitter/handler. Añadir un módulo = editar este DataType + publicar el bundle al
CDN (operación rara, detrás de un ADR de todos modos).

Esto **no** viola el principio 6: el schema **no** hornea el framework (Angular se
resuelve en runtime); solo lista el catálogo de módulos, que es propiedad del CMS.
Y **no** viola el 7: el CMS **nunca** construye la ruta del bundle —
`ModuleMount.cshtml`/`DefaultSynHostEmitter` resuelven `BundleDescriptor.MainEntryUri`
+ `Integrity` por `TryResolveAsync`. **Prohibido** cualquier string literal
`angular/latest/main.js` o `C:\LOCAL_CDN` en código Web/Application (esas rutas
solo viven del lado publisher). Un test debe fallar si el HTML emitido contiene una
ruta no proveniente del descriptor.

### 3. Los datos fluyen por seams; el módulo NUNCA toca la DB

```
<synergos-{module}> (Angular, browser)
   │ fetch() con cookie de sesión Umbraco (HttpOnly, SameSite=Lax)
   ▼
{Module}ApiController (Web) — auth-gate por endpoint (primer paso)
   ▼
Seam (Interfaces) → Store/Adapter (Web)
```

Grafo (principio 1): seams puros en **Interfaces**; lógica de orquestación **pura**
en **Application** (sin Umbraco/AspNetCore); adaptadores (stores, `IDataProtectionProvider`,
filesystem, locks atómicos), API controllers, composers, Razor partial y
notification handlers en **Web**. Verificado contra el precedente
`DefaultSynHostEmitter` (vive en Application sin ILogger por esta razón).

### 4. Persistencia de módulo: store dedicado + escritura ATÓMICA

Los datos operacionales **no** son contenido CMS → store dedicado bajo `App_Data/`,
detrás de un seam. **Regla nueva (corrige hecho #5):** escritura atómica real
(escribir a temp + `File.Move`/rename), no `WriteAllText`/`AppendAllText`. **No
propagar** el bug de no-atomicidad del precedente 2FA/audit (deuda documentada).

### 5. Degradación: reusar el fallback existente + `<noscript>` nuevo + error boundary

- Reusar el fallback YA EMITIDO (hecho #2): `ModuleMount.cshtml` consume
  `SynHostEmitResult.RegistryResolved == false` para mostrar el div de fallback.
  **Único entregable nuevo**: el `<noscript>` con link a una superficie SSR de
  degradación (cada módulo decide su destino).
- Módulos stateful son **CSR-only por diseño** (declarado).
- **Error boundary del WC**: contrato `syn:<module>:error` (CustomEvent
  bubbles+composed, outcome=failure) para un throw en `connectedCallback` — el
  marker actual solo cubre "bundle no resolvió", no constructor-throw.

### 6. Routing interno sin colisión con el árbol Umbraco

El router del módulo usa **hash** (`#/patients`) o `<base href>` confinado a la
URL de la página-host. El CMS resuelve solo la página-host; el sub-path lo maneja
el router del WC client-side.

### 7. `siteRootKey` / aislamiento por origen — single-origin en Fase 1 (Decisión D1)

Dado el hecho #4, **Fase 1 = un deploy = un origen, SIN partición por `siteRootKey`**,
coherente con todos los stores vivos. Si llega un deploy real multi-siteRoot, se
escribe un ADR transversal que aplique el scoping **uniformemente a TODOS los
stores** (audit/forms/2FA/módulos) derivando el origen del PublishedRequest
server-side — **nunca** de header/cliente/bridge, **nunca** un `ITenantContext`.
No se inventa un eje de aislamiento que el resto del sistema no tiene. (Ver
Decisiones abiertas.)

### 8. POC obligatorio: tokens `--syn-*` a través del Shadow DOM

Las CSS custom properties heredan a través del shadow boundary **solo** si se
setean en `:root`/host. **Gate de Fase 1**: un POC mínimo que confirme que el
bridge/partial proyecta los tokens al host del custom element. Si no heredan →
inyectar vía constructable stylesheet en `connectedCallback`. De esto depende todo
el theming por-siteRoot (ADR 0094) dentro de los módulos.

### 9. Wiring centralizado (principio 3)

Todos los registros nuevos (seams, impls, API controllers, notification handlers,
hosted services) se cablean en `SeamComposer.cs` — donde hoy vive
`AddSingleton<IAnalyticsTracker, LoggerAnalyticsTracker>`. Ningún `IComposer` en
Application.

## Phases

| Fase | Entregable | Verificable |
|---|---|---|
| **0** | Este ADR ratificado + índice §11.2 actualizado. | ADR mergeado. |
| **1** | `elementSynModuleMount` + DataType del picker (uSync, GUID quad-check) + `SynHost/ModuleMount.cshtml` + wrapper blockgrid + `<noscript>` que consume `RegistryResolved` + contrato `syn:<module>:error` + **POC tokens→Shadow DOM**. Un "hello module" Angular publicado al LOCAL_CDN. | Una página monta `<synergos-helloModule>` que hidrata, lee `window.synergos` y aplica tokens; con registry simulado no-resuelto se ve el fallback + noscript. |

(Fases 2+ viven en los ADRs de cada módulo: 0097 dashboard, 0098 healthcare.)

## Consequences

**Positivas**
- "Grandes features = módulo Angular" se cumple sin tocar ningún principio: es una
  extensión del modelo SynHost, cero contrato CDN nuevo.
- Un solo mecanismo (probado primero con el dashboard, de menor riesgo) sirve a
  todos los módulos futuros.
- El grafo, el "no SaaS" y el "CDN consumida" se mantienen; los datos siempre por
  seams con tests.

**Costos / riesgos**
- Módulos stateful son CSR-only → sin JS solo hay degradación SSR mínima (aceptado).
- La escritura atómica y el POC de tokens son trabajo nuevo, no reuso.
- Tentación de `ITenantContext` para multi-origen → **prohibido**, documentado como
  anti-patrón.

## Decisiones abiertas (para el arquitecto)

- **D1 — `siteRootKey`**: recomendado single-origin en Fase 1 (no partición);
  diferir multi-siteRoot a un ADR transversal uniforme. *Confirmar.*
- **D7 — `moduleAlias` DropDown vs TextBox**: recomendado `DropDown.Flexible`
  (catálogo owned-by-CMS) + validación runtime; añadir módulo = editar DataType +
  publicar bundle. *Confirmar.*
- **Tokens→Shadow DOM**: el POC de Fase 1 decide si basta herencia o hace falta
  constructable stylesheet.

## Addendum (2026-06-25) — Fase 0/1 ejecutada + distribución (ADR 0099)

- **Lado CMS construido** (importado por el arquitecto): `elementSynModuleMount`
  + `DTSelectModuleAlias`/`DTSelectModuleHeight` + `SynHost/ModuleMount.cshtml`
  (+ `<noscript>`) + wrapper blockgrid + registro en `DTBlockGridSections`
  ("Syn — Module Mount", grupo Syn (CDN)) + CSS de degradación.
- **Validado end-to-end** con un `hello-module` **vanilla** self-contained
  publicado a `C:\LOCAL_CDN\synergos\hello-module/vanilla/{0.1.0,v0,latest}`
  + entrada en `registry.json`. El CMS resuelve `moduleAlias="hello-module"`
  → emite `<synergos-hello-module config=...>` que hidrata. Prueba el
  mecanismo + el **POC tokens→Shadow DOM** (el bundle estiliza con
  `var(--syn-color-brand-500)`; las custom properties heredan a través del
  shadow boundary por spec — verificar visualmente al montar el bloque).
- **La distribución a CDN (framework-compatible + import map Angular +
  hot-reload + SSR) se gobierna en ADR 0099**, sobre el desacople CMS↔UI de
  **ADR 0083** (repos/deploys separados; solo CDN + contratos; cero shared
  code). El import map del runtime Angular **ya se emite** en el `<head>`
  (`_SynHostRuntime.cshtml`), así que un módulo **Angular** se monta igual
  que cualquier `elementSyn*` Angular — solo requiere su bundle publicado en
  el CDN (lado UI). El primer módulo de prueba es vanilla por ser el proof
  más simple (self-contained), no por bloqueo.
- `moduleAlias`: dado que el registry **no enumera** (`TryResolveAsync`
  solo), el catálogo es owned-by-CMS (DropDown) + validación runtime — no un
  picker que liste el CDN. Confirmado contra `IBundleRegistryClient`.

## Relación con otros ADRs

Extiende: 0009 (seams), 0011 (config tipada), 0012/0089 (CDN consumida), 0015
(SynHost framework-agnóstico), 0023/0025 (Global Component / gating), 0094
(identidad/tokens). **Distribución a CDN: ADR 0099.** Prerequisito de 0097 y 0098. Sinergia con 0095 (un módulo puede
anidar `<synergos-chatbot>` como WC hijo, coordinando por CustomEvents).
