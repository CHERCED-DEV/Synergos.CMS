# ADR 0009 — Extension seams son obligatorios

- **Status:** Accepted
- **Enmienda (2026-08-02):** `IDictionaryCache` **se eliminó**. La regla de este ADR sigue en
  pie; lo que se cumplió fue su propio riesgo declarado abajo — *«abrir seams innecesarios para
  consumidores imaginados»*. El seam nunca tuvo un solo lector, y por construcción no podía
  tenerlo: `Get` es la única lectura y la interfaz **no tiene `Set`**, así que el registro DI
  —tipado como la interfaz— dejaba inalcanzable el `Set` de la clase concreta. Era un cache que
  no podía guardar nada. El i18n del proyecto se resolvió con el helper nativo de Umbraco, en
  233 sitios. Se borraron la interfaz, `DictionaryCache`, `DictionaryCacheInvalidator`, sus dos
  suites de tests y los registros del composer. **No re-crear el seam sin un lector real**: si
  algún día se mide que las búsquedas de Dictionary cuestan, la decisión no es este contrato
  sino si vale poner un cache propio delante del de Umbraco.
- **Date:** 2026-04-18
- **Deciders:** Project owner
- **Source:** promoted from `refactor-docs/adr-drafts/0009-extension-seams-mandatory.md`

## Context

Synergos CMS es base de un producto que debe recibir branding, verticales
y módulos diferidos (Flow, Blog, Multi-tenancy) sin reabrir el núcleo.

En los proyectos fallidos previos, cuando surgía la necesidad de bifurcar
comportamiento, se resolvía con copy-paste o con composers adicionales
en nuevas ubicaciones, erosionando la arquitectura.

## Decision

Todo concepto que pueda tener **más de una implementación ahora o en el
futuro próximo** se declara como **interfaz en `Synergos.CMS.Interfaces`**
antes de escribir su primera implementación.

- Los defaults viven en `Synergos.CMS.Application`.
- Las variantes por marca, vertical o tenant viven en la **capa custom
  futura** (otro repo / otra solución).

Conceptos iniciales bajo esta regla (no exhaustiva):

- `IBrandingProvider`
- `IElementViewModelMapper<TIn,TOut>`
- `ICompositionReader<T>`
- `IBundleRegistryClient`
- `IFeatureGate`
- `IContentContextAccessor`
- `ISchemaHealthProbe`
- `IDictionaryCache`

**Filtro obligatorio de 3 preguntas** para declarar un seam nuevo:

1. ¿Podría un cliente, marca o vertical necesitar otra implementación?
2. ¿El core tiene una opinión por defecto que podría no aplicar a todos
   los casos?
3. ¿La decisión involucra I/O, configuración o identidad?

Si alguna respuesta es "sí": es seam. Si las tres son "no": es clase
concreta en `Synergos.CMS.Application`.

## Consequences

**Positive**

- La capa custom futura se integra registrando `IComposer` sin editar
  el core.
- Tests pueden sustituir cualquier seam por un doble.
- El core permanece brand-neutral y vertical-neutral.

**Negative**

- Costo upfront de declarar el contrato antes del primer uso real.
- Riesgo de abrir seams innecesarios para consumidores imaginados.
  Mitigado por el filtro obligatorio.

## Alternatives considered

- **Seams por demanda** (crearlos cuando aparezca el segundo
  consumidor) — rechazado: introduce churn cuando el consumidor
  segundo llega con urgencia.
- **Seams para todo** — rechazado: contradice el principio
  "no premature abstraction" del README de docs/.

## Anti-reglas explícitas

- Prohibido declarar interfaz para concepto que **sólo** tendría una
  implementación en core + custom combinados.
- Prohibido declarar interfaz "por simetría" con otro seam similar
  sin pasar el filtro de 3 preguntas.
