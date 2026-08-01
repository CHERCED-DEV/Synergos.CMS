# ADR 0115 — Un gate que no encuentra su entrada FALLA; y la lista de temas tiene una sola fuente

- **Status:** Accepted
- **Date:** 2026-08-01
- **Deciders:** Arquitecto (encargo: "atiende esos issues, repara, comitea" tras la auditoría de los dos repos) + agente.
- **Relacionados:** ADR 0083 (contratos CMS↔UI: la única superficie de acople), ADR 0101 (contrato de identidad `pageThemeVariant`↔`data-theme`, 1:1 verbatim), ADR 0102 (temas `scholar` / `meridian`), ADR 0086 (Husky + CI gateando los contratos), ADR 0107 (una promesa que nadie cumple se borra), ADR 0006 (gobernanza documentation-first).

---

## Context

Una auditoría de `Synergos.CMS` + `Synergos.UI` corriendo los gates —no
leyéndolos— encontró que **tres de los cuatro controles del acople no estaban
controlando nada**. No por estar mal escritos: por no ejecutarse nunca, o por
salir en verde sin haber mirado.

**1. El validador cruzado salía 0 cuando no encontraba el CMS.**
`validate-cms-contracts.mjs` localizaba el repo hermano con un
`resolve(ROOT_UI, '..', 'Synergos.CMS')` cableado: sin flag, sin variable de
entorno. En CI el CMS no está en el checkout, así que imprimía *"Skipping CMS
cross-validation"* y salía **0**. Ese script es parte de `contracts:validate`,
que a su vez es parte de `npm run release`. O sea: **el release verificaba el
acople saliendo en verde sin comprobarlo.**

Corrido de verdad, con los dos repos como hermanos: **6 errores, 79 warnings**.

| | Alias | Qué significa |
|---|---|---|
| E1 | `elementSynPaxSelector`, `elementSynSeatMap` | Publicados al CDN sin DocType. Un editor **no puede colocarlos**. |
| E2 | `elementSynFaqSection`, `elementSynFeatureGrid`, `elementSynMediaText`, `elementSynTestimonialSection` | DocType y partial SynHost vivos, **bundle inexistente**. El editor SÍ los coloca, y como ninguno pasa `FallbackHtml`, el visitante ve el skeleton CDN-offline para siempre. |

**2. El gate de paridad CSS estaba rojo en `master` y nadie lo sabía.**
`.syn-blog-tag` se emitía sin regla. El workflow existía desde el primer commit
y **jamás corrió**: el historial del repo tiene un solo run (el de uSync).

**3. La lista de temas estaba escrita a mano en cinco sitios, y en cinco
estaba rancia.** ADR 0101 ratificó `silverGold` (camelCase) como canónico y
hoy hay ocho variantes. Pero el puente publicaba
`["light","dark","silvergold"]` — tres de ocho, y la tercera en una
ortografía, todo-minúscula, **que no emite ninguna capa**:

- `DefaultHostBridgeContextBuilder.cs` (el runtime)
- `SynergosBridgeController.cs` (el fallback CSP-strict)
- `_SynergosBridge.cshtml` (el fallback inline)
- `docs/contracts/css-tokens.md` y `host-bridge.md` (los contratos)
- `host-bridge.contract.ts` y `synergos-bridge.ts` (el espejo del UI)

`_Layout.cshtml` escribe `data-theme` verbatim, así que el DOM real dice
`silverGold`. Un consumidor que hiciera `available.includes(variant)` obtenía
**`false` justo cuando el tema estaba activo**, y los cinco temas de los
verticales no existían para la UI. El SCSS siempre estuvo bien: mentía el
contrato tipado.

El test que debía cubrirlo montaba `available: ['light','dark','silvergold']`
a mano y sólo comprobaba que `'dark'` estuviera dentro. **Pasaba en verde con
el listado rancio.** Ese fue el agujero.

## Decision

### 1. Un gate que no encuentra su entrada FALLA

`validate-cms-contracts.mjs` sale **1** cuando no localiza el CMS. Tolerarlo
ahora es explícito (`--allow-missing-cms`), para el build local de quien no
tiene el hermano a mano. Y encuentra el CMS por tres vías, en orden:
`--cms-path=`, `SYNERGOS_CMS_PATH`, hermano.

> Un control que se salta su comprobación y reporta éxito es peor que no
> tenerlo: el "no tenerlo" al menos no miente.

### 2. La deuda conocida va a un baseline, no a un `continue-on-error`

`tools/cms-contract-baseline.json` lista los 6 desajustes E1/E2 con `reason`,
`owner` y `action` obligatorios. El validador los **sigue imprimiendo** como
`[BASELINE]` en cada corrida, pero no los cuenta como fallo.

La razón es operativa: un gate permanentemente rojo por deuda vieja deja de
leerse, y entonces no gatea la deriva **nueva**, que es lo único que un gate
puede prevenir. El baseline convierte deriva silenciosa en deuda *nombrada*.
Además reporta `[BASELINE STALE]` cuando una entrada ya no corresponde a un
desajuste vivo: se arregló y toca borrarla de la lista.

Mismo patrón que `css-parity-allowlist.txt` y que el marker de compositions
reservadas — no se inventa mecanismo nuevo.

**Los 6 no se "arreglan" aquí a propósito.** Cerrarlos exige decisiones de
producto que no son del agente: qué schema tiene un selector de pasajeros, si
el mapa de asientos se modela como contenido, y si esos cuatro bloques quieren
bundle Angular o un `FallbackHtml` SSR (para `media-text`, lo segundo es más
barato y probablemente mejor).

### 3. La lista de temas tiene UNA fuente

`DropdownOptions.PageThemeVariant.All` — ocho variantes, casing canónico,
`inherit` fuera por ser el centinela del resolver y no un tema. De ahí salen
el runtime y los dos caminos degradados, que se unifican en
`HostBridgeFallback` (antes eran dos literales JSON distintos, y hasta
discrepaban en el `displayName`).

Agregar un tema = una entrada en esa constante + su bloque en
`syn-tokens.css`. En ningún otro sitio.

### 4. Los contratos se verifican contra el CSS real, en las dos direcciones

`HostBridgeThemeContractTests` lee `syn-tokens.css` del repo y comprueba que
ninguna de las dos listas se adelante a la otra: toda variante publicada tiene
bloque, y todo bloque está publicado. Comparar la constante contra sí misma no
habría atrapado nada.

En el harness Vitest, la invariante que faltaba —`available` **siempre**
contiene `variant`— ahora se verifica sobre las ocho.

### 5. Falta el gate más básico: compilar

El repo tenía tres workflows y **ninguno compilaba la solución ni corría los
976 tests**; los tres filtran por `paths`, así que un cambio puramente C# no
encendía nada. Se agrega `build-test.yml`, sin filtro de paths.

Y el job cross-repo de `design-gates.yml`, que llevaba comentado desde que se
escribió esperando "que exista el remote del UI", se activa: el remote existe,
el runner existe, y el espejo del lado UI ya hacía exactamente eso.

## Consequences

**Bueno.** El acople CMS↔UI pasa a estar verificado por CI en vez de por la
disciplina de correr scripts a mano en Windows. La deriva nueva se bloquea; la
vieja queda nombrada, con dueño. La lista de temas deja de poder pudrirse en
cinco sitios a la vez.

**El coste, dicho en claro.** El baseline es una concesión: seis desajustes
reales siguen en el repo, y cuatro de ellos son visibles para un editor hoy
(coloca el bloque, no pinta nada). Están documentados, no resueltos.

`npm run release` ahora **falla** si el hermano `Synergos.CMS` no está
presente. Es intencional —era el bug— pero cambia el flujo de quien releaseaba
sin el CMS al lado.

**Sin verificar.** Estos cambios se escribieron en un contenedor **sin SDK
.NET**: los gates de Node y el harness Vitest (56 tests) se corrieron y pasan;
`dotnet build` y los 976 tests **no se pudieron ejecutar**. El primer run de
`build-test.yml` es la verificación pendiente.

## Alternatives considered

- **`continue-on-error: true` en el job del validador.** Visible pero no
  bloqueante, y sin obligar a escribir el motivo de cada desajuste. Perdía lo
  único valioso: que alguien tuviera que nombrar la deuda para saltarla.
- **Arreglar los 6 desajustes ahora.** Habría significado inventar schema
  uSync y componentes Angular sin decisión de producto. Contra ADR 0107.
- **Derivar `available` leyendo `syn-tokens.css` en runtime.** Elimina la
  duplicación de raíz, pero mete IO de ficheros en el render de cada página
  para un dato que sólo cambia con un deploy. El test cubre el riesgo sin el
  coste.
