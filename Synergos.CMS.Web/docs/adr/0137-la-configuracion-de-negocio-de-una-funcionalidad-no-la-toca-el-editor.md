# ADR 0137 — La configuración de negocio de una funcionalidad vive en el despliegue, no en el editor

- **Estado:** Propuesto — se acepta o se descarta con el piloto (ver al final)
- **Fecha:** 2026-09-29
- **Propone:** la síntesis de la auditoría de reutilización (informe 20 §5.C.4-5 y §4.2 punto 7,
  «pide ADR»). El arquitecto fijó número y estado el 2026-09-29.
- **Parte de:** [#172](../../../../../issues/172) · épica [#139](../../../../../issues/139)
- **Depende de:** ADR 0135 (el resolver es el único camino hacia el elemento)
- **Convive con:** ADR 0011 (feature flags tipadas): aquello son interruptores, esto son parámetros

## Contexto

> Marcas de certeza (informe 20): ✔ comprobado en el disco · ◐ medido por un agente con dos
> derivaciones o ejecutando, sin re-derivar · ○ cifra de un agente sin verificar. «Re-leído» = línea
> abierta al escribir esta ADR (CMS `9ec00d81`, UI `98b9233`).

### No hay canal: la regla de negocio sale de una constante o del editor

- **No existe un canal de configuración de negocio hacia la UI** ◐. Comisión, tasa, copago, moneda,
  alcance: o son constantes del componente (290 ◐) o llegan por el JSON libre del
  editor (**37 claves** que las 11 funcionalidades verticales leen y que **sólo** ese JSON puede
  darles ◐) (informe 20 §2.2, informe 16 §3-§4).
- Ejemplo: la comisión de `eventos` es `DEFAULT_FEE_PERCENT = 12` (`eventos.ts:147`, re-leído) y
  sólo la cambia un `config.feePercent` (`:276`, re-leído) — es decir, un editor tecleando JSON.
  `realty` y `ehr` repiten el patrón con la tasa y el copago (informe 16 §3).
- El JSON libre es `configOverride`: está en 90 de 91 vistas ◐, se fusiona encima de todo y, si no
  parsea, se descarta **en silencio** (`DefaultSynHostEmitter.cs:113-131`, re-leído).
- **`apiBase` está triplicado**: campo del editor, default escrito en 10 vistas Razor y
  `DEFAULT_API_BASE` en 10 componentes ○ (informe 16 §4).
- **La identidad también entra por ahí**: `SynHost/Ehr.cshtml:17-18` (re-leído) pone
  `patient = "pat-jorge-medina"` si el editor no escribe otro, aunque `window.synergos.member` ya viaja
  (informe 16 §3, H5, severidad no calibrada).

### Lo que existe en el servidor no llega

- Las secciones `*Settings.cs` del CMS son del servidor (modo, URL y llave de cada BFF, `Cart.Currency`…)
  y **ninguna se expone al navegador** ○ (informe 16 §4).
- `IFeatureGate` existe (ADR 0011) y `FeatureGateKeys` declara **0** gates (re-leído). La propiedad
  `featureFlagsSettings.flagsJson` por siteRoot no la lee nadie ○ (informe 16 H4).

### Lo que hace NewShore

- Cada funcionalidad tiene su **JSON de negocio** (`IBusinessConfigRetriever<T>`, 42 ficheros) ✔ que
  su resolver lee; el editor no lo ve (informe 15 §5.3).
- Y su deuda está exactamente ahí: decisiones repartidas en **siete canales** sin chequeo de
  coherencia; la variación por cliente es **reemplazo de fichero entero por `xcopy`** —incluidas
  fuentes `.cs`—; hay **JSON dentro de strings** de entorno (`Payment_Confirmation_Strategy`); la
  configuración del front se **compila** en 26 `environment.*.ts`; y la caché de esos JSON no cachea
  porque el servicio está registrado Transient ◐ (informe 15 §5, D7-D9).

## Decisión (propuesta)

### 1. Una sección por funcionalidad

`Synergos:Features:<X>`, con un POCO tipado por funcionalidad, leído con **`IOptionsMonitor<T>`**
(recarga sin reinicio, como la resiliencia de los webhooks, ADR 0064).

### 2. Override por siteRoot, por fusión de claves

Un siteRoot puede cambiar **claves sueltas** de la sección; lo que no cambia, lo hereda. **Nunca**
reemplazo de fichero. Cómo se nombra el siteRoot dentro de la sección se decide en el piloto; la
regla es la fusión.

### 3. Tres «nunca»

- **Nunca JSON dentro de un string.** La sección es estructura de configuración, no un texto a
  deserializar.
- **Nunca compilada en el bundle.** Nada de `environment.*.ts`: cambiar una comisión no puede exigir
  recompilar y republicar.
- **Nunca desde el editor.** `configOverride` sale de las funcionalidades (ADR 0135 §6).

### 4. Llega a la funcionalidad por el resolver

El resolver de la ADR 0135 copia al record **sólo las claves que la funcionalidad declara leer**. El
navegador no ve la sección entera, y lo que es secreto se queda en el servidor, como ya hace
`SeatMapProjection` con el inventario (ADR 0127).

### 5. Qué va a cada sitio

Con la clasificación del informe 16 §7.2 (juicio del agente, clave por clave ○):

| tipo de clave | hoy | destino |
|---|---|---|
| de despliegue (endpoints, fuentes) | 28 | esta sección |
| de negocio | 12 | esta sección (funcionalidad) o decisión del editor como selector (pieza) |
| de ajuste (intervalos, umbrales) | 7 | constante del componente |
| de runtime (sesión, ruta) | 6 | servidor o sesión: la identidad, por `window.synergos.member` |

## Alternativas consideradas

| alternativa | por qué no |
|---|---|
| **Copiar NewShore tal cual** | Siete canales sin coherencia, `xcopy` de ficheros enteros, JSON en strings, 26 `environment.*.ts` compilados, caché anulada. Se toma la idea (JSON de negocio por funcionalidad, fuera del editor), no el reparto. |
| **Dejar el JSON libre del editor como canal** (`configOverride`) | Es «pasar la configuración completa desde el CMS», lo que el arquitecto no quiere: un editor teclea la comisión en un textarea, y un JSON mal escrito se descarta sin avisar. |
| **Constantes en el componente** (lo de hoy) | Cambiar una regla es recompilar y republicar el bundle, y no puede variar por siteRoot. |
| **Contenido por siteRoot en uSync**, como `themeSettings` | Lo edita el editor en el backoffice, y esto es lo que el editor no toca. Además el precedente está muerto: `flagsJson` por siteRoot no tiene lector. |
| **Un servicio de configuración externo** (el Azure App Configuration de NewShore) | Añade una dependencia de runtime antes de la primera regla real; es la misma trampa que la ADR 0011 evitó con las flags. |

## Consecuencias

**A favor**

- La regla de negocio tiene **un** sitio, tipado, por despliegue y por siteRoot.
- El editor deja de poder romper una funcionalidad con un JSON.
- Las secciones nuevas quedan bajo `SeccionesDeConfiguracionTests` (#154), que ya impide dos
  secciones a una edición de distancia.

**En contra**

- **Una clave mal escrita no falla.** El binder de .NET descarta en silencio lo que no mapea (la
  lección del #138, que el propio `SeccionesDeConfiguracionTests` cita). El piloto tiene que traer
  su validación, o esta ADR mueve el fallo silencioso de un JSON del editor a un `appsettings`.
- **Otra fuente para la misma regla.** Si una regla que muestra la UI (la comisión) la aplica también
  un servicio del backend, tiene que haber **una** fuente, no dos (el principio que el doc 25 aplica
  a las capacidades). Esta ADR no lo resuelve.
- **El override por siteRoot es nuevo.** Hoy lo que varía por siteRoot vive en el contenido
  (`themeSettings`, `siteConfigSettings`), y `Synergos:Branding` en `appsettings.json` es una sola
  marca por defecto. Hay que diseñar cómo se parte por siteRoot una sección de configuración.
- **Sacar `configOverride` de un ElementType es cambio de schema** (ADR 0008) y lo importa el
  arquitecto.

**Qué la vigilaría:** `SeccionesDeConfiguracionTests` para los nombres. Por construir: un gate que
falle si la vista de una funcionalidad pasa `ConfigOverrideJson`, y la validación de la sección al
arrancar.

## Qué hace falta para aceptarla (el piloto)

Una funcionalidad: `eventos`, con las claves que el informe 16 §7.3 le asigna (`apiBase`,
`currency`, `feePercent`, `scope`). Se mide:

1. Las constantes de negocio salen del bundle y la funcionalidad se comporta igual con los valores
   por defecto.
2. **Dos siteRoots con valores distintos** para una clave, y el resto heredado.
3. **Recarga sin reinicio**: cambiar el valor en configuración y verlo en la siguiente petición.
4. **Una clave mal escrita falla al arrancar**, no en silencio (mutación comprobada).
5. `configOverride` fuera de esa funcionalidad, con el contenido que ya lo usaba medido antes.

## Relación con otras ADRs

- **0011** — sigue igual: las flags son interruptores; `Synergos:Features:<X>` son parámetros.
- **0013** — si `configOverride` sobrevive para desarrollo, va detrás de `DevSeed`.
- **0064** — precedente de `IOptionsMonitor` con recarga en caliente.
- **0096** — module-mount ya decía que la configuración operacional viaja por API y no por schema.
- **0127** — el secreto se queda en el servidor; al navegador llega la proyección.
- **0135** — el resolver es el único camino. **0134** — sólo las funcionalidades reciben
  configuración de negocio; una pieza recibe decisiones del editor.

## Referencias

- Informes locales de la auditoría: 20 §2.2 y §5.C.4-5, 16 §3-§4 y §7, 15 §5 y §9.
