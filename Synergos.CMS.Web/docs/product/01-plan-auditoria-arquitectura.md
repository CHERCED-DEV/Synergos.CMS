# Plan de auditoría de arquitectura — SOLID, clean architecture, segregación

> **Qué es esto.** El plan de trabajo para auditar, calificar y refinar la arquitectura del
> CMS, pedido por el arquitecto al cierre de la fase de verticales: *"auditar el proyecto,
> calificar qué tan aplicado al SOLID está, qué tan clean architecture se ha formado, refinar,
> segregar, mejorar"*. No es un documento aspiracional: la Fase 0 ya está ejecutada y sus
> números están abajo. Cada fase siguiente produce PRs, no solo diagnóstico.
>
> **Regla del plan entero:** ninguna calificación sin evidencia citable (archivo:línea o
> medición reproducible), y ningún refactor sin su gate en verde antes y después. La prosa
> segura no es evidencia — esta sesión lo demostró tres veces.

## Fase 0 — Línea base (EJECUTADA, 2026-08-02)

Medida sobre `claude/repo-access-zqabsl` con 1580 tests en verde.

### La regla de capas se cumple — verificado, no asumido

| Verificación | Resultado |
|---|---|
| `Synergos.CMS.Interfaces` → referencias | **cero** (ni proyectos ni paquetes) |
| `Synergos.CMS.Application` → referencias | solo `Interfaces` |
| `using Umbraco.*` / `Microsoft.AspNetCore.*` en Interfaces+Application | **0 archivos** |

El ADR 0002 no es una intención: hoy es un hecho. **Riesgo**: nada lo vigila — un `using`
accidental compilaría si alguien agrega el paquete. Candidato a gate barato (F1).

### Hotspots por tamaño (top, LOC)

| Archivo | LOC | Lectura preliminar |
|---|---|---|
| `Web/Services/DevContentFiller.cs` | 4 581 | Tooling dev tras flag — riesgo bajo, pero 4.5k LOC sin tests es donde un bug se esconde años |
| `Web/Composers/SeamComposer.cs` | 1 323 | Composition root: tamaño esperable, PERO es un solo archivo para ~15 verticales — punto de colisión de merges entre equipos |
| `Web/Controllers/ShopCatalogController.cs` | 1 216 | Controller-gordo: routing + reshape + reglas en un archivo |
| `Application/Dto/Constants/ContentTypeKeys.cs` | 1 158 | Constantes generables — ¿por qué se mantiene a mano? |
| `Web/Controllers/AdminController.cs` | 1 144 | Agrega métricas de N dominios: acoplamiento de lectura transversal |
| `Web/Controllers/EhrController.cs` | 1 024 | **Corregido en F2**: NO es el hotspot PHI que la primera lectura sugirió — es demo DEV-ONLY tras `[DevSeedOnly]` (404 fuera de dev, datos fabricados; el PHI real va por `HealthcareApiController`, que sí tiene tests). El riesgo real era que la PARED —el atributo— tenía cero tests; cerrado en F4 con `DevSeedOnlyAttributeTests` (filtro on/off, default fail-closed, y que el controller LLEVA el atributo) |

Los 7 controllers más grandes suman **6 790 LOC** — más de la mitad de los 12 346 LOC de los
37 controllers.

### Asimetría de cobertura — el hallazgo principal de la línea base

| Área | Cobertura |
|---|---|
| Services / seams (Application) | 118 archivos de test — es donde vive el patrón por-seam del ADR 0075, y se nota |
| Reglas puras (`*ContentRules`) | 4 suites, patrón consolidado |
| **Controllers** | **21 de 37 sin NINGUNA referencia en tests** — incluidos AdminController, EhrController, SearchController. (Corregido: una primera medición dijo 27 buscando solo `<X>ControllerTests.cs`; el conteo real busca la clase referenciada en cualquier test — BlogsController y AcademyController SÍ tienen cobertura con otros nombres) |
| Notifications / Composers | 0 |

La lectura honesta: el proyecto testea **lo que la arquitectura hizo fácil de testear** (los
seams puros) y no testea lo que quedó Umbraco-dependiente (controllers). Eso es a la vez un
elogio a la seam-architecture y la deuda más clara. La mitigación parcial existe —los
controllers delegan reshape a reglas puras testeadas y el drift de claves lo vigila
`validate-cms-contracts`— pero autorización, ruteo y códigos de estado no los prueba nadie.

### Otras señales

- 110 interfaces / 80 impls en Application / 131 services en Web / 37 controllers.
- 24 `TODO|HACK` en total — bajo para 67k LOC; ninguno en Interfaces.
- Gates existentes: usync-audit, css-parity, contratos Vitest, build+test, **usync-rebuild
  (ADR 0128, nuevo)**. Ninguno vigila las capas.

## Rúbrica de calificación (F2)

Cada principio se califica por capa con veredicto `CUMPLE / CUMPLE CON COSTO / VIOLA`, y toda
nota cita evidencia. Sin promedios: un promedio esconde el archivo que importa.

| Principio | Qué se busca en ESTE codebase (no en abstracto) |
|---|---|
| **S**RP | Controllers que además de rutear hacen reshape/reglas; el SeamComposer como archivo único; clases >800 LOC con más de una razón de cambio |
| **O**CP | ¿Agregar un vertical toca archivos existentes o solo agrega? (hoy: toca SeamComposer siempre) |
| **L**SP | Stubs vs implementaciones reales: ¿algún stub miente sobre el contrato? (precedente: StubBundleRegistryClient retorna null-siempre y eso ES el contrato documentado — bien) |
| **I**SP | Interfaces gordas: ¿algún consumer usa 2 de 8 métodos? Precedente ya cerrado: `IDictionaryCache` borrado por seam-sin-lector (ADR 0009 enmendado) |
| **D**IP | Ya verificado estructuralmente (capas limpias). Queda lo fino: ¿algún service de Web instancia concreto en vez de recibir seam? |

Clean architecture además: dirección de dependencias (hecho), pureza del dominio (hecho),
**y la pregunta que el arquitecto ya contestó a futuro**: separar responsabilidades en
"circuitos de aplicativo" propios (API de sesión fue el primer candidato declarado).

## Fases

| Fase | Entrega | Tamaño |
|---|---|---|
| **F0. Línea base** | este documento, sección anterior | ✅ hecha |
| **F1. Gate de capas** | ✅ hecha en la misma ola: `Tests/Architecture/LayerRuleTests.cs` — 4 tests sobre las referencias reales de los ensamblados compilados. La regla del ADR 0002 pasa de costumbre a invariante que corre con la suite | ✅ hecha |
| **F2. Boleta SOLID** | ✅ [`02-boleta-solid.md`](02-boleta-solid.md) — 7 hotspots + 3 controles, veredicto y evidencia archivo:línea | ✅ hecha |
| **F3. Mapa de segregación** | ✅ [`03-mapa-segregacion.md`](03-mapa-segregacion.md) — 14 verticales mapeados, núcleo compartido y enredos identificados, CODEOWNERS borrador | ✅ hecha |
| **F4. Refinar** | ✅ 2 defectos de SEGURIDAD cerrados (IDOR Shop, downcast 2FA), código muerto borrado (ContentTypeKeys 1158 LOC, deps muertas Gov), pared del demo EHR testeada. Cada uno commit atómico con tests | ✅ hecha |
| **F5. Cierre** | ✅ [`04-cierre-auditoria.md`](04-cierre-auditoria.md) — boleta final, backlog por riesgo real, CODEOWNERS apéndice | ✅ hecha |

### Qué NO va a pasar en este plan

- **No habrá refactor sin gate.** Igual que el uSync rebuild: primero el detector, después el
  cambio.
- **No se parte nada en microservicios/proyectos nuevos todavía.** La segregación en
  "circuitos" (deseo declarado del arquitecto) necesita el mapa de F3 primero; partir antes de
  mapear es como el multi-tenant que el principio 8 prohíbe: topología antes que necesidad.
- **No se refactoriza `DevContentFiller`** salvo que F2 encuentre riesgo real: es tooling
  detrás de flag, y 4.5k LOC feas que funcionan valen menos que cualquier hotspot con PHI.

## Registro de decisiones del plan

- 2026-08-02 — F0 ejecutada. Retención de search analytics bajada a 30 días (aprobación del
  arquitecto). Descartado sacar Templates del import uSync: rompería la propiedad derivable
  del ADR 0128 (una DB fresca necesita los registros de template para renderizar); el ruido de
  BOM queda vigilado por el gate en su lugar.
