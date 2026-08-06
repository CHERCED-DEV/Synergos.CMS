# ADR 0113 — Un elemento publicado es una *app* y NO puede importarse: el componente compartido vive en una lib y la app queda como bootstrap fino (Tienda)

- **Status:** Accepted
- **Date:** 2026-07-20
- **Deciders:** Arquitecto (elección explícita entre tres opciones ofrecidas: dejar el lint rojo y documentar / hacer la extracción ahora / excepción en la config de eslint — eligió la extracción) + agente. Originado al intentar cerrar los 13 errores no-a11y del lint de `Synergos.UI` tras la ola de accesibilidad.
- **Relacionados:** ADR 0083 (contratos CMS↔UI: la UI es la fuente de verdad del nombre), ADR 0099 (pipeline CDN: cada elemento se publica como su propio bundle versionado), ADR 0012 (el CMS consume la CDN vía registry, no cablea paths), ADR 0107 (lo que nadie cumple se borra — aquí aplicado a un `build` que nadie consume).

---

## Context

El encargo era "deja el lint limpio". La premisa venía con **tres errores de medición**,
todos corregidos antes de tocar nada:

1. **El comando del encargo corría el workspace equivocado.** `npx nx run-many -t lint --all`
   desde `Synergos.UI/` corre **21** proyectos y da **cero** errores de accesibilidad. Los 142
   del reporte son el workspace Nx **anidado** en `platforms/angular/` — otro `nx`. La cifra
   "142 vs 21" es la pista.
2. **El lint no tenía 14 errores: tenía 28.** Los 14 rotulados como a11y eran la mitad; los
   otros 13 (7 `enforce-module-boundaries`, 4 `no-output-native`, 1
   `no-unused-private-class-members`, 1 `no-unused-expressions`) no los mencionaba nadie. La
   tesis del encargo —"un lint que nadie mira es donde se esconde el error de verdad"— ya se
   estaba cumpliendo, y más fuerte de lo que creía quien la escribió.
3. **7 de esos 13 no eran un fallo de lint.** Eran el síntoma de un problema de arquitectura.

Este ADR trata solo del punto 3. `cart-summary` compone `cart-item`; `product-detail` compone
`price-display` / `quantity-selector` / `variant-picker`; `cart-item` compone
`quantity-selector` y el `cart.store`. Lo hacían por ruta relativa hacia **dentro** de otra
app: `import … from '../../../quantity-selector/src/quantity-selector/quantity-selector'`.

La composición **es deliberada** y el propio grafo la contempla: los tags `tier:` declaran que
una `composition` puede depender de un `primitive`, y eso es exactamente lo que pasa. Lo que
estaba mal era el **mecanismo**.

**Premisa falsa que se descartó midiendo:** que bastaba con darles un alias `@synergos/*` para
satisfacer la regla ("debe empezar por un scope npm"). Se probó con el cambio más pequeño
posible —un alias, un import— y Nx respondió con **otro** error, no con verde:

```
Projects cannot be imported by a relative or absolute path…   (antes)
Imports of apps are forbidden                                  (con alias)
```

Es decir: el problema no es *cómo* se escribe el import, es *qué* se importa. Y los seis son
elementos CDN publicados de verdad — `element-registry.json` los lista con su `tag`
(`synergos-quantity-selector`, …) y cada uno tiene su `main.ts` con `customElements.define` —
así que **deben seguir siendo apps**. No se pueden convertir en libs y ya.

## Decision

### 1. El componente vive en la lib; la app se queda como bootstrap fino

```
libs/shop/src/lib/<componente>/     ← el componente (ts + html + scss)
apps/domains/shop/<elemento>/src/main.ts  ← solo createApplication + customElements.define
```

El `main.ts` importa el componente desde `@synergos/shop`. Así el componente se compone **por
nombre** desde otros elementos, y además sigue publicándose como su propio bundle CDN. La app
deja de ser algo que otros importan y pasa a ser lo único que debe ser: un punto de montaje.

### 2. Solo sube a la lib lo que comparten VARIOS elementos

Movidos: `quantity-selector`, `price-display`, `variant-picker`, `cart-item` y `cart.store`.
`cart-summary` y `product-detail` **se quedan en su app**: nadie los compone. La regla evita
que `libs/shop` se convierta en el vertedero del dominio.

### 3. Una sola lib, no cinco

Es el patrón de la casa: `libs/shells` contiene los 10 shells, `libs/shared` todo lo
compartido. Una lib por componente habría multiplicado por cinco el `project.json` /
`tsconfig` / alias sin comprar nada.

### 4. `libs/shop` es lib SOLO-FUENTE: sin target `build`

Las apps la compilan **desde fuente** vía el path de `tsconfig`; nadie consume un paquete
compilado de `@synergos/shop`. Y el executor `@angular/build:ng-packagr` está **roto en este
repo desde antes de esta ola**: `shells:build` ya falla con
`Cannot destructure property 'pos' of 'file.referencedFiles[index]'` —verificado revirtiendo a
HEAD limpio, el fallo es idéntico sin ningún cambio mío— y `shop:build` reproducía el mismo
error. Como `@angular/build:application` trae `dependsOn: ["^build"]`, añadir ese target no
solo habría sumado un rojo: habría **bloqueado el build de los seis elementos consumidores**.

Un target que nadie consume y que rompe a quien depende de él es deuda, no consistencia
(ADR 0107).

## Consequences

**Positivas:**

- Los 7 errores desaparecen **por la razón correcta**: ya no hay app importada por nadie.
- El grafo de Nx queda honesto: `apps → libs`, nunca `apps → apps`. Las `depConstraints` de
  `tier:` vuelven a significar algo, porque ahora se evalúan sobre una arista real.
- El tree-shaking aguanta el barrel compartido, **medido**: `quantity-selector` pesa 33.8 kB;
  si el barrel no se podara arrastraría `cart-item`, que él solo pesa 62 kB. Sin avisos de
  budget en ninguno de los seis.
- Queda un lugar obvio donde poner la próxima pieza compartida de Tienda, y un precedente
  reutilizable para los otros dominios cuando les pase lo mismo.

**Negativas o trade-offs:**

- **Estrena un patrón**: hasta ahora los 142 proyectos eran apps de elemento + libs
  transversales; este es el primer "componente de elemento que vive en una lib". Si los demás
  dominios repiten la composición, habrá `libs/<dominio>` y hay que decidir si eso escala o si
  pide una convención (`libs/domains/*`).
- `libs/shop` no tiene `build`, a diferencia de las otras libs. Es deliberado y está
  justificado arriba, pero es una asimetría que el próximo lector notará.
- Los bundles de Tienda hay que **re-publicar** a la CDN para que el cambio llegue a runtime.
  No se corrió en esta ola: otro agente estaba editando el pipeline de publish
  (`tools/publish*.mjs`, `element-registry.json`) en el mismo checkout, y CDN/CMS son
  singletons que exigen serializar.

**Notas de implementación:**

- Los `.scss` movidos usan `@use 'scss' as syn`, que resuelve por
  `stylePreprocessorOptions.includePaths` declarado en `nx.json` **solo** para el executor
  `@angular/build:application`. Al compilarse desde fuente dentro de cada app, siguen
  resolviendo. (Si algún día `libs/shop` recupera un `build` de ng-packagr, necesitará
  `styleIncludePaths` en su `ng-package.json`: probado y funciona.)
- El `cart.store` es estado mutable compartido entre elementos que se montan por separado.
  Este ADR **no** cambia esa semántica: solo la mueve de un fichero suelto en `apps/` a la lib.
  Sigue siendo un singleton de módulo por bundle, no un store compartido entre bundles.

### Dos defectos que destapó la mudanza (y que el refactor no buscaba)

1. **Los 7 `project.json` de Tienda no lintean sus plantillas.** Sus `lintFilePatterns` son
   `apps/domains/shop/<x>/src/**/*.ts` — **solo `.ts`**. Sus `.html` no los miraba nadie. Al
   mover 4 componentes a una lib que sí lintea `.html` (convención de `shells`/`shared`),
   apareció al instante un error real que llevaba ahí desde siempre.
2. **Ese error tenía una trampa: el arreglo LITERAL que pedía la regla habría roto el
   componente.** `price-display.html:19` decía `@if (currentPrice() != null)` y
   `@angular-eslint/template/eqeqeq` exige `!==`. Pero `currentPrice()` es
   `computed(() => this.priceInput() ?? this.price())` con ambos `number | undefined`: es
   `number | undefined` y **nunca** `null`. Con `!= null` funcionaba bien; pasarlo a
   `!== null` habría dado `true` con `undefined` y pintado `formatPrice(undefined)`. El
   arreglo correcto por tipo es `!== undefined`. Misma familia que el `aria-hidden` en el
   backdrop del doc 22: una regla de estilo aplicada al pie de la letra introduciendo el bug
   que dice prevenir.

## Alternatives considered

- **Alias `@synergos/*` apuntando dentro de la app** — *rechazado por medición, no por
  opinión*: solo cambia el mensaje a `Imports of apps are forbidden`. Se probó y se revirtió.
- **Añadir las 7 rutas al `allow` de `@nx/enforce-module-boundaries`** (hay precedente en la
  config: ya permite `vitals/core/src/mappers/block.mapper` y `…/models/*`) — rechazado:
  deja el lint verde **institucionalizando** `app → app`. Es declarar que la composición
  intra-dominio es deliberada sin haber movido nada, y desactiva la regla justo donde después
  haría falta que avisara.
- **Cinco libs, una por componente** — rechazado: contradice el patrón de la casa
  (`libs/shells` = 10 shells en una lib) y quintuplica la configuración.
- **Convertir los cuatro en libs y dejar de publicarlos** — rechazado: los cuatro están en
  `element-registry.json` con `tag` propio; son elementos que el CMS puede montar.
- **Dar a `libs/shop` un target `build` de ng-packagr "por consistencia"** — rechazado: está
  roto repo-wide y, por `^build`, habría bloqueado a los seis consumidores.

**Criterio de reapertura:** si un segundo dominio necesita el mismo patrón, no crear
`libs/<dominio>` a ojo — decidir entonces la convención (`libs/domains/*` vs. plano) y
anotarla. Si `libs/shop` pasa de ~8 componentes o empieza a recibir piezas que solo usa un
elemento, la regla del §2 se está violando y toca partirla.

## References

- Commit `d729eb6` (Synergos.UI) — la extracción completa: 23 ficheros, renames preservados.
- Commit `3bd0e1f` (Synergos.UI) — los otros 6 errores no-a11y de la misma ola
  (4 outputs que colisionaban con eventos DOM nativos + 2 triviales).
- Commit `7817f6d` (Synergos.UI) — la ola de a11y previa: 9 falsos positivos silenciados con
  el motivo al lado, y los 6 errores de los 3 fallos REALES dejados en rojo a propósito.
- Verificación: lint sobre **143** proyectos, los 13 errores no-a11y en **cero**; los 6
  elementos de Tienda compilan. `0 GUIDs, 0 NuGet, 0 npm, 0 schema, 0 Import`.
