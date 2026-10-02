# ADR 0137 — La configuración de negocio de una funcionalidad vive en el despliegue, no en el editor

- **Estado:** Aceptado (2026-10-02) — el arquitecto la ratificó con los seis cambios que pidió el
  piloto ([#194](../../../../../issues/194)), que pasan a ser parte de la decisión (ver «Resultado del
  piloto»). Se escala a las funcionalidades en [#196](../../../../../issues/196)
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

## Decisión

> **Aceptada con los seis cambios del piloto** (ver «Resultado del piloto»). Donde un apartado de abajo
> y uno de esos cambios digan cosas distintas, manda el cambio.

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

## Resultado del piloto (#194, 2026-10-02)

Piloto en `eventos`, rama `lego/integracion` de CMS (`7c966f9d` código y tests, `1a281aab` schema) y
UI (`444cb84`). Medido en vivo con el CMS del piloto en `:5194` contra una **copia** de la base real,
con dos dominios asignados en la copia (`eventos-a.localhost` → siteRoot Eventos,
`tienda-b.localhost` → siteRoot Tienda) y un CDN propio con el bundle nuevo; la base real y
`C:\LOCAL_CDN` no se tocaron.

**Lo que había, leído en el código antes de tocar nada.** La comisión era `DEFAULT_FEE_PERCENT = 12`
en el bundle y **ningún camino del servidor la cobraba**: ni el motor en proceso (el total era la suma
de las entradas) ni el orquestador (autorizaba el total de `Api.Pricing`). Con ese código, dos VIP se
mostraban en 940.800 y se cobraban 840.000. La «Comisión plataforma (10%)» del payout estaba escrita en la
plantilla, y `/api/eventos` en cuatro sitios del módulo además del campo del editor y del default de
la vista. El campo `config` (JSON libre) estaba vacío en el único bloque, y **ningún** bloque usa
`configOverride`.

**La lista heredada del informe 16 §7.3 estaba mal en las dos direcciones**, y se midió antes de
ejecutarla:

- `currency` **no** es configuración: es un dato del precio. La decide el catálogo
  (`Currency = "COP"` en las dos fuentes, CMS y demo) y viaja con cada importe; el elemento ya la
  prefería (`tier.currency || currency()`), salvo en el carrito. Una moneda de configuración habría
  sido una segunda fuente para un dato que ya tiene dueño — justo el «En contra» de esta ADR.
- `scope` es el prefijo de las rutas por hash (`#/eventos/e/<id>`): de runtime, se queda en el
  componente.
- Faltaba `platformFeePercent`, el 10 % compilado en el payout.

**Los criterios:**

1. **Las constantes de negocio salen del bundle** — `DEFAULT_FEE_PERCENT`, `DEFAULT_CURRENCY`,
   `DEFAULT_API_BASE` y sus tres copias en la estrategia, y el 10 % de la plantilla. Con los valores
   base, la página emite `feePercent: 12`, `platformFeePercent: 10`, `apiBase: "/api/eventos"`: se
   ve lo mismo que antes. **Lo que cambia a propósito es el cobro**: ahora cobra lo que se mostraba.
   Sin configuración el elemento no inventa una comisión, y sin `apiBase` no llama a nada (degrada a
   su muestra, visible).
2. **Dos siteRoots con valores distintos y el resto heredado, en vivo.** Overrides `Sitios:{Key}`:
   Eventos `FeePercent: 8`; Tienda `FeePercent: 15, PlatformFeePercent: 5`. La página por
   `eventos-a` emite `feePercent: 8` con `apiBase` y `platformFeePercent` **heredados**; el control
   sin dominio, 12. El checkout del mismo carrito (2 × 180.000) cobra **28.800** por `eventos-a`,
   **43.200** sin dominio y **54.000** por `tienda-b`.
3. **Recarga sin reinicio, en vivo.** Con el CMS corriendo, 8 → 9,5 en `appsettings`: la petición
   siguiente cobra 34.200 y la página emite 9,5 (un solo arranque en el log). La caché de salida
   sólo cubre sitemaps y RSS, así que página y cobro cambian juntos. **Una recarga inválida**
   (`FeePercnt`) **no tumba la venta**: sigue rigiendo 9,5 en la página y en el cobro, el error sale
   **una vez** con la ruta exacta de la clave, y al corregirla rige el valor nuevo (7 % → 385.200).
4. **Una clave mal escrita no deja arrancar, en vivo y mutado.** Con `FeePercnt` el CMS no llega a
   escuchar: `OptionsValidationException` con la ruta de la clave y las que sí se leen. Validador por
   sección (`ValidadorDeNegocioDeEventos`, `ValidateOnStart`) sobre una pieza genérica
   (`ClavesDeConfiguracion`: lo que el binder descarta en silencio). Rechaza también sitios que no
   son GUID, porcentajes fuera de 0–100 o con más de dos decimales, y una `ApiBase` que no es ruta del
   sitio ni `http(s)` (`//host` y `/\host` incluidos). Medido: el binder **no** lee `1,5` como 15 —lo
   rechaza al convertir—, así que eso ya fallaba al arrancar.
5. **`configOverride` y el JSON libre, fuera.** `SolicitudSynHost` ya descartaba `configOverride` en
   una funcionalidad; el schema quita además `apiBase`, `config` y la composición `compIntegration`
   de `elementSynEventos` (contenido medido antes: nada que regía se pierde). Import quirúrgico en la
   copia: 1 ítem, 0 ERR; el tipo queda con `heading`, `subheading` y `role`. `usync-rebuild-check`
   1331/1331. **En la base real**, import quirúrgico delegado por el arquitecto el 2026-10-02 (respaldo
   `*.bak.20261002-095324`): 1 ítem, 0 ERR, el tipo queda igual que en la copia.
6. **(Propio) Lo que se muestra es lo que se cobra, en los dos caminos.** Motor en proceso, **en el
   navegador**: el carrito de `eventos-a` pinta «Cargos por servicio (8 %) $ 67.200» y total
   907.200; `POST /api/eventos/checkout` abre la sesión por `"amount": 907200`. Contra el
   orquestador, **por tests**: el CMS manda `serviceFeePercent` y `Bff.Eventos` autoriza el total de
   la cotización más la comisión **sobre el subtotal** (con impuesto en la cotización, para que
   calcularla sobre el total no pase en verde). **No se verificó en vivo**: el árbol de servicios
   local no tiene precios ni aforo de eventos sembrados.

**La fórmula vive en tres sitios** —carrito, motor en proceso, orquestador— y en dos lenguajes, así
que la cruzan unos **vectores de oro** en la superficie de acople
(`docs/contracts/service-fee-vectors.json`, como la hipoteca del #167): `NegocioDeEventosTests`,
`ComisionDeServicioTests` y, en la UI, G-12. La regla es la de la casa —al par, la de
`Synergos.Core.Money`— y no la del `Math.round` que tenía el carrito; los vectores de medio centavo
están elegidos para que mitad-arriba dé otro número.

**Gates y mutaciones.** 10 mutaciones en el CMS y 5 en la UI (más G-12), todas compilaron y todas
rojas: el motor contra el orquestador sin `negocio:`, sin `ValidateOnStart`, el motor en proceso sin sumar, el
orquestador sobre el total, mitad-arriba en cada implementación, el validador sin claves, el lector
sin último válido, el orquestador sin revisar el porcentaje, el cuerpo sin la comisión, el carrito
inventando un 12, el sanitizador tirando `platformFeePercent`, el cliente llamando sin base, y la cara
decidida en el constructor. Suites: CMS 2776 + 682 + 465 = **3923**; UI 760 + 65 + 1.853 + 11;
`usync-audit` y `compilan-las-vistas` (401) en verde; G-10 ve las 115 rutas.

**Encontrado por el camino:**

- **El `role` del editor no se respetaba nunca.** El constructor decidía la cara y lanzaba la primera
  carga, y en un custom element los inputs llegan **después** del constructor: siempre abría la de
  asistente. Arreglado (`ngOnInit`) con test y mutación. Sin la base compilada, la primera cartelera
  habría salido sin API: el piloto lo destapó. **Hay que medir el patrón en las otras funcionalidades.**
- **G-10 se quedó ciego** con la primera forma del guardia (un helper en la URL): busca el marcador
  literal `${apiBase}` y dejó de ver las nueve rutas de eventos; lo delató su censo. El guardia quedó
  en `request()`.
- **Se venden entradas de un evento pasado**: «Festival Estéreo 2026» es del 15 de agosto, la ficha
  dice «El evento ya comenzó» y el checkout lo cobra. Previo al piloto; ticket aparte.
- **En modo orquestador el impuesto no se muestra**: si `Api.Pricing` cotiza impuesto, se autoriza y
  el carrito no lo pinta. Previo; sin medir en vivo.
- **Centavos en COP**: con un porcentaje de dos decimales la comisión puede tener centavos
  (22.500,12); el carrito ahora los pinta en vez de redondearlos. Si COP se cobra sin centavos es una
  decisión de negocio que no tomó el piloto.

**Los cambios que el piloto pidió a esta ADR** (ratificados por el arquitecto el 2026-10-02; son parte
de la decisión):

1. **§2 — el sitio es el del hostname** (`SitioDeLaPeticion`, la regla del router de Umbraco), no el
   de la página: la API del checkout no tiene página, y lo que se muestra y lo que se cobra tienen que
   salir de la misma regla. Sin dominio rigen los valores base. **El override se nombra por la `Key`
   del siteRoot**: `Synergos:Features:<X>:Sitios:{Key}:{Clave}`.
2. **§1 — la validación es parte de la decisión, no del piloto**: cada sección trae su
   `IValidateOptions` sobre `ClavesDeConfiguracion` más sus rangos, con `ValidateOnStart`; en
   caliente, quien la lee sigue con el **último valor válido** y lo dice una vez.
3. **«Otra fuente para la misma regla» queda resuelto así**: una regla que se muestra y se cobra se
   **cobra en el servidor** desde la misma sección (una costura, `INegocioDe<X>`, con dos lectores:
   el resolver y los motores); a un servicio del backend le llega como **parámetro** desde el CMS, no
   de su propia configuración; y la fórmula, si vive en más de un sitio, la cruzan **vectores de oro**
   versionados con el redondeo de la casa.
4. **§5 — la clasificación de claves se mide antes de mudarlas**: un dato que ya tiene dueño (la
   moneda del precio) no se vuelve configuración, y una constante de negocio que la lista no ve (el
   10 % del payout) entra.
5. **El contrato declara el origen `negocio`** (`OrigenDelCampo.Negocio`) y **sólo una funcionalidad
   lo lleva**, con gate en los dos repos (`ContratoSynHostTests` y el generador del UI).
6. **En el elemento, una regla de negocio no tiene atributo suelto** —volvería a separar lo que se
   muestra de lo que se cobra— y **la funcionalidad lee su `config` en `ngOnInit`**, nunca en el
   constructor.

**Para escalar** ([#196](../../../../../issues/196): 25 funcionalidades sin record, medidas el
2026-10-02 —las verticales y las acotadas—): por cada una, medir su lista de claves contra lo que el
servidor ya decide, su sección con validador, los motores que cobran o aplican la regla leyendo la
misma costura, y su par de vectores si la fórmula vive en dos lenguajes. `realty` repite el patrón
con la tasa del simulador. `ehr` **no**, y lo dice el cambio 4: su copago es un precio que el servidor
ya decide (el motor en proceso cobra 80.000 mientras la UI pinta «Sin costo»), así que llega de la
API y no de una sección. `app-launcher` conserva `compIntegration` aunque como funcionalidad no la
recibe: sale en la misma pasada.

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
