# ADR 0008 — uSync hybrid source-of-truth

- **Status:** Accepted
- **Date:** 2026-04-18
- **Deciders:** Project owner
- **Source:** promoted from `refactor-docs/adr-drafts/0008-usync-hybrid-source-of-truth.md`

## Context

Dos intentos previos (`Synergos.CMS.epicfail` y `epicfail2`) gobernaban el
schema Umbraco (Data Types, Element Types, Document Types, Compositions,
Dictionary) mediante **39 initializers code-first** orquestados en 12 fases
sobre `UmbracoApplicationStartedNotification`. Los problemas documentados:

- Interdependencia frágil entre fases: si una falla a medias, la DB queda
  parcialmente mutada.
- Orden del pipeline no es explícito — depende del conocimiento tácito del
  autor.
- Recovery manual ante fallos; mitigación con `SqliteAutoBackup` que sólo
  evidencia el problema de fondo.
- uSync existía como backup pero no como fuente.

La nueva arquitectura requiere arranque determinista, schema versionable
por PR, y recovery = `git revert`.

## Decision

1. El **schema Umbraco** (Data Types, Element Types, Document Types,
   Compositions, Dictionary, MediaTypes) se define en archivos **uSync XML**
   versionados en git bajo `Synergos.CMS.Web/uSync/v9/`.
2. **uSync es la fuente de verdad** del schema; el backoffice y el código
   son consumidores.
3. **Los imports automáticos al arranque permanecen desactivados.** En
   uSync 13.x la propiedad real es
   `uSync:Settings:ImportAtStartup` y su valor es un **string enum**
   (`"None"` | `"All"` | nombre de handler group), no un booleano. El
   default del paquete es `"None"`, que es exactamente el
   comportamiento deseado: confiar en el default y **no escribir la
   clave en `appsettings`** mantiene la superficie mínima. Los
   imports se disparan manualmente o por un proceso CI/CD gobernado
   por ADR separado.
4. El código C# puede tocar schema **sólo** para:
   - Health checks (`ISchemaHealthProbe`) que validen consistencia uSync/DB.
   - Resolvers que consultan tipos por GUID o alias.
   - Tooling one-off detrás de flag explícito (nunca auto-run).
5. El **registro de GUIDs** (`ContentTypeKeys`, `DataTypeKeys`,
   `MediaTypeKeys`) vive en
   `Synergos.CMS.Application/Dto/Constants/` como **referencia cruzada**;
   no es fuente de creación.
6. **Prohibido** reintroducir initializers code-first que mutan schema en
   boot, o handlers que muten schema / content en
   `UmbracoApplicationStartedNotification`.

## Consequences

**Positive**

- Diffs de schema visibles en PR.
- Boot determinista; no hay mutaciones al arranque.
- Rollback = `git revert` + re-import manual.
- Recovery explícito y auditable.

**Negative**

- Requiere disciplina editorial: cualquier cambio en backoffice debe
  exportarse a uSync antes de commitear.
- Import manual es un paso operativo extra en dev y en despliegue.
- Primera activación obliga a decidir estructura de carpeta uSync
  (estándar vs tree custom).

## Alternatives considered

- **Re-implantar los 39 initializers legacy** — rechazado: reintroduce
  acoplamiento de fases.
- **uSync total sin código de soporte** — rechazado: se pierde la red
  de seguridad de health checks que detectan drift.
- **Backoffice manual sin uSync** — rechazado: impide versionado por PR.

## Guardrails operativos

- `appsettings.*.json`: **no se escribe** `uSync:Settings:ImportAtStartup`.
  El default del paquete `"None"` ya cumple la decisión; escribirlo
  amplía superficie sin valor (ver `feedback_compose_over_appsettings`
  en memory). Si en el futuro se requiere importar un handler group
  específico al boot, llega con ADR sucesor + override en composer
  (`services.Configure<uSyncSettings>(opts => opts.ImportAtStartup = "Settings")`),
  nunca via edición ciega de appsettings.
- `/_health` incluye `usync_folder_readable` y `schema_version_match`
  como probes obligatorias.
- Pre-commit (futuro): validar que un cambio que tocó C# de schema trae
  también el XML uSync correspondiente.

## Corrections

- **2026-04-18** (editorial, no-decisional): se corrigió la referencia
  a `ImportOnStartup = false` (nombre y tipo de valor incorrectos) por
  `ImportAtStartup = "None"` (string-enum), tras verificar la API real
  de uSync 13.3.2 contra la documentación oficial de Jumoo. La
  **decisión** (imports automáticos desactivados; uSync como SoT)
  permanece inalterada. Esta nota preserva la inmutabilidad del ADR
  registrando explícitamente la diferencia entre error terminológico
  y cambio de decisión.
