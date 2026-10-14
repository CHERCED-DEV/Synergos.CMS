# ADR 0141 — El runtime compartido se publica por contenido: la URL que nombra el import map lleva la huella de sus bytes (enmienda la 0099)

- **Estado:** Propuesta — la ratifica el arquitecto. Construida en los dos repos y verificada en vivo
  sobre la copia piloto (2026-10-14); nada empujado a `master`.
- **Fecha:** 2026-10-14
- **Propone:** el plan del defecto 1 de la verificación en vivo del cupón endurecido
  (`_informes/82-adr-0140-defectos-en-vivo-plan.json`, pasos 1 y 11-17: tres diseños puntuados por un
  juez), a partir de lo medido en `_informes/80-adr-0140-cupon-en-vivo.json` (escenario 7). La revisó un
  segundo agente que intentó refutarla (`_informes/84-adr-0140-defectos-pausa.json`) y la verificación
  en vivo la midió (`_informes/83-adr-0140-defectos-en-vivo-r1.json`, escenarios 1, 1b y 1c).
- **Parte de:** [#201](../../../../../issues/201) · el defecto apareció en el piloto de la
  [ADR 0140](0140-el-flujo-de-negocio-se-declara-en-el-orquestador-y-el-cms-solo-abre-la-puerta.md)
- **Enmienda, sin borrar nada:** [ADR 0099](0099-cdn-distribution-framework-compatible-import-maps-hot-reload.md)
  §3 — el runtime compartido de `runtime/{fw}/{ver}/` y su `import-map.json`
- **Respeta:** [ADR 0012](0012-cdn-contract-consumed.md) y [ADR 0132](0132-el-equipo-del-cdn-eramos-nosotros.md)
  (el CDN se consume: el CMS no publica nada; sólo cambia la cabecera de su montaje local y cuándo
  relee el mapa) · [ADR 0083](0083-cms-ui-alignment-via-contracts.md) (CMS y UI se despliegan por
  separado) · CLAUDE.md §0.C.21 (superset: nada se retira)

## Contexto

> Marcas de certeza, como en la 0140: ✔ comprobado en el disco o en vivo al escribir esta ADR · ◐
> medido por un agente y confirmado por otro método · ○ cifra de un agente sin re-derivar. Código: CMS
> `6b495c9d`, UI `7faa386` (`lego/integracion`).

### Lo que se midió: el navegador se queda con el runtime viejo, y nadie se entera

En la verificación en vivo del cupón endurecido (informe 80, escenario 7) `syn-promo-code` salió **sin
estilos y sin `locked`** en un navegador que ya había cargado el runtime. `performance.getEntriesByType
('resource')` daba `…/runtime/angular/21.1.6/sg-shared.js` con `transferSize` 0 y **801.154 B**,
mientras el servidor servía en la MISMA URL **802.496 B** con `public, max-age=31536000, immutable`.
Vaciar la caché del navegador lo «arreglaba» en la máquina de quien miraba, y en la de nadie más.

### Por qué: la carpeta del runtime lleva la versión del framework, no la de su contenido

- `runtime/angular/21.1.6/` es la versión de **Angular**. `publish:runtime` reescribía los ficheros de
  esa carpeta (`copyFile` encima) cada vez que cambiaba `libs/shared`, que es lo que más cambia.
- Las dos políticas de caché la servían `immutable` por tener forma de versión: la del montaje local
  del CMS (`Program.cs`: sólo `latest/` y `vN/` se revalidaban) y la del Worker del CDN
  (`tools/lib/cdn-cache-policy.mjs`, `VERSION_EXACTA`). Un spec del UI **afirmaba** el defecto: tenía
  `…/runtime/angular/21.1.6/import-map.json` en el bloque «lo que no se mueve».
- El 304 del Worker salía sin la política y sin CORS (`worker/index.js`: `if (!respuesta.ok) return
  respuesta;`).
- Un `immutable` falso no da un rojo: da un año. Además del defecto medido (estilos y una entrada que
  no llega) había uno peor sin medir: un elemento nuevo que importa un export que el `sg-shared` en
  caché no tiene no enlaza, y la app entera deja de cargar para quien vuelve.

### Lo que decidió por dónde ir: el cambio es de un fichero ◐

Por dos métodos (`node:crypto` y `sha256sum`) sobre `dist`, `C:\LOCAL_CDN`, el `public/` del UI y la
CDN piloto: **14 de los 15 ficheros del runtime de Angular eran idénticos** en las cuatro copias. Sólo
`sg-shared.js` difería, con tres contenidos (`18a9decb…`/802.496 B, `3690e94d…`/801.154 B y
`a830b97d…`/800.763 B). Y los ficheros del runtime sólo se importan por especificador desnudo (grep y
`es-module-lexer` sobre los dos runtimes: 279 desnudos, 0 relativos, 0 dinámicos, 0 `import.meta`), así
que renombrar uno sólo exige cambiar su entrada del mapa.

## Decisión (propuesta)

### 1. La URL que nombra el import map lleva la huella de sus bytes

`runtime/<fw>/<ver>/<stem>.<sha256[0:16]>.js`, por ejemplo `runtime/angular/21.1.6/sg-shared.18a9decbae7fffb0.js`.
La forma la fija UNA regex, `^[a-z0-9-]+\.[0-9a-f]{16}\.m?js$`, la misma en el CMS
(`PoliticaDeCacheDelCdnLocal`) y en el UI (`HUELLA_EN_NOMBRE`, `cdn-cache-policy.mjs`). El mapa de
Angular nombra con huella sus 18 entradas y el de Preact sus 5. El alias heredado y su gemelo comparten
URL, y también `rxjs` y `rxjs/operators` (`importsDelRuntime(framework, base, nombrar)`; sin `nombrar`
sale el mapa de antes, byte a byte).

Un navegador con el fichero viejo recibe un mapa que nombra una URL que nunca pidió: la baja y queda
rescatado, sin vaciar nada. Y como va **por fichero**, un cambio de `libs/shared` sólo mueve `sg-shared`
y `sg-core`; Angular sigue en caché.

### 2. `immutable` sólo donde una huella lo prueba

Las dos políticas son gemelas y sus specs llevan la misma tabla de casos. Se aplican en este orden:

1. Un segmento `latest` o `vN` → `public, no-cache, must-revalidate`, como siempre.
2. Bajo la carpeta reservada `runtime`, `immutable` sólo si el último segmento lleva huella. Si no
   —los planos de `<ver>/` y su `import-map.json`—, se revalida.
3. Todo lo demás, `immutable` como siempre: no se invierte el default de `registry.json` ni el de los
   elementos.

El 304 del Worker sale con la misma política y el mismo CORS que el 200; el 404, sin cabeceras. La tabla
de `Cache-Control` de `docs/contracts/cdn-bundle-structure.md` lo dice, con el formato del libro.

### 3. Cada fichero con huella se escribe UNA vez, y el mapa se voltea el último

`publish-runtime` (`publicarRuntime`, `tools/lib/runtime-por-contenido.mjs`), por framework y en orden:

1. el sha256 de cada fichero de `dist`;
2. `<ver>/<stem>.<huella>.js`, una sola vez: con el mismo sha256 entero no escribe, y con otro **para**
   (sobrescribirlo sería volver a mentir con `immutable`);
3. los planos de `<ver>/` y `latest/`, sólo si cambian sus bytes (así conservan el mtime, que es el
   ETag del CMS);
4. `<ver>/import-map.json`;
5. el libro `latest/huellas.json`, fusionado;
6. `latest/import-map.json`, **el último**, por temporal + renombrado y con reintentos cortos ante
   `EPERM`, `EBUSY` o `EACCES` (el CMS lo abre en cada render). Quien lea el mapa a mitad de la
   publicación lee el anterior, entero;
7. el gate `revisarPorContenido` sobre el destino.

### 4. Nada se borra: el libro sólo crece y `build:cdn` conserva las generaciones

- **El libro**, `runtime/<fw>/latest/huellas.json`: `{ framework, entradas: [{ fichero, url, sha256,
  bytes, desde }] }`. La llave de cada entrada es su ruta DENTRO de `runtime/<fw>/`, no la URL entera:
  la misma publicación con otra base es una entrada, no dos. Sólo crece, en orden estable, y conserva el
  «desde» de cada entrada.
- **`build:cdn` conserva**, y lo hace AL EMPEZAR, antes de compilar y de borrar nada: si el origen no
  contesta se sabe en un segundo y la salida anterior queda intacta. Guarda lo publicado en `public/` y
  baja del CDN vivo lo que falte, verificando cada sha256 contra el libro y contra el nombre
  (`tools/lib/conservar-runtime.mjs`). Hace falta porque producción arma su `public/` desde cero: sin
  conservar, el despliegue siguiente daría 404 a quien tenga un mapa anterior.
- **El origen** es por defecto el CDN público del proyecto (`https://synergos-ui.synergos-labs.workers.dev`,
  el mismo que mira el humo); `--conservar-desde` o `SYNERGOS_CDN_CONSERVAR_DESDE` lo sobrescriben. Se
  normaliza a esquema y host, como lo lee el humo: una ruta se dice y se ignora.
- **Todo falla seguro.** Un libro en 404 es el primer despliegue por contenido —se avisa y se sigue—,
  pero sólo si el origen es un CDN de Synergos (sirve su `registry.json`). Sin red o con un 5xx se
  reintenta dos veces, también la lectura del cuerpo, y después se para. Un sha256 que no casa, para.
- **El gate `revisarPorContenido`** corre en `publish-runtime` y en `build:cdn`. Exige que:
  - toda URL del mapa lleve huella, salvo con `por-nombre` declarado;
  - el fichero exista y sus bytes casen con el nombre;
  - cada entrada del libro tenga su fichero;
  - `latest/<plano>` sea el fichero que nombra el mapa;
  - el alias y su gemelo compartan URL;
  - todo fichero que el mapa nombra esté en el libro, porque si no, el siguiente `build:cdn` no lo
    conservaría.
- **El humo del CDN** (`humo-cdn`) exige que la ruta con huella sea `immutable` de un año con CORS, que
  el plano sea corto si hay libro y que cada entrada del libro dé un 200 con su sha256. Contra un CDN
  anterior, sin libro, avisa (⚠) con la expectativa de antes, para no ponerse rojo antes del primer
  despliegue.

### 5. Los interruptores, declarados

| interruptor | qué hace | para qué |
|---|---|---|
| `--urls=por-contenido\|por-nombre` · `SYNERGOS_RUNTIME_URLS` | `por-nombre` publica el mapa plano de antes. Los ficheros con huella y el libro se siguen escribiendo, así que ir y volver no pierde generaciones | La vuelta atrás **del contenido**, no la de los envenenados: quien tiene la URL plana con la política vieja la sigue teniendo un año |
| `--sin-conservar` · `SYNERGOS_CDN_CONSERVAR=no` | `build:cdn` no conserva nada, lo dice en voz alta y deja una marca en la salida (`sin-conservar.json`) | Un CDN que no se despliega: el humo conectado del CMS (`humo-conectado.yml`) |
| `--conservar-desde` · `SYNERGOS_CDN_CONSERVAR_DESDE` | Cambia el origen del que se conserva | Otro CDN de Synergos |

La puerta de `wrangler deploy` (`tools/cdn-desplegable.mjs`, el `build.command` de `wrangler.jsonc`)
**no deja desplegar una salida con la marca**. Corre en todo `wrangler deploy`: el tecleado a mano y
también el de Workers Builds. Una salida construida conservando pasa sin configurar nada.

### 6. En el CMS cambian su montaje local y cuándo relee el mapa (la 0012 sigue en pie)

El CMS no publica nada ni conoce el formato del libro. `ReubicacionDelImportMap` y los dos clientes del
registry ya conservaban la ruta y leen sólo los `imports` de `latest/`, así que el nombre con huella pasa
sin tocar su código. Lo fijan tests con sus tres formas (absoluta, relativa a la raíz y relativa) y con
el alias y el gemelo en la MISMA URL. Cambian dos cosas:

- **`PoliticaDeCacheDelCdnLocal`**, en la que delega `Program.cs`: la regla 2 en el montaje local
  (`/cdn-bundles`).
- **`HttpBundleRegistryClient` relee el import map cuando cambia el snapshot del registry**, no sólo
  cuando vence su propio reloj. Antes, un registry recién leído convivía hasta `RefreshSeconds` con el
  mapa de la publicación anterior. Si el mapa no se puede leer, sirve el último bueno y lo dice.

### 7. En el UI, una puerta de enlace al publicar un elemento

`publish.mjs` no publica un elemento si algo que importa de `@synergos/*` no está en el runtime que
nombra el `latest/import-map.json` PUBLICADO (`tools/lib/puerta-de-enlace.mjs`, que lo enlaza con
esbuild, como lo haría el navegador). En un `build:cdn` real enlazan 125 bundles de Angular y 1 de
Preact. Cierra el caso local de publicar un elemento sin su runtime (`release-cdn --scope=elements`).

## Alternativas consideradas

El juez puntuó los tres diseños en corrección, compatibilidad, simplicidad y rendimiento:

| alternativa | puntaje | por qué no |
|---|---|---|
| **Nombre con huella por fichero** (ésta) | 9 / 9 / 7 / 9 | Es el único mecanismo que alcanza a quien ya tiene la entrada `immutable`: una URL nueva cuando, y sólo cuando, cambian los bytes. |
| **Una carpeta por generación**, `runtime/<fw>/<ver>/<huella-del-build>/` | 8 / 8 / 5 / 8 | Llega al mismo cierre, pero para no volver a bajar Angular necesita «anclar» cada fichero a generaciones anteriores. Eso es reconstruir a mano lo que el nombre con huella da por construcción, y un arrastre parcial da 404. Cada generación pesa ~2,2 MB contra ~0,8 MB, con cinco interruptores y dos índices. Se le tomó: `immutable` SÓLO con huella y los planos cortos, la relectura del mapa en el cliente Http y la puerta de enlace. |
| **Sólo política de caché**: todo `runtime/` `no-cache` con ETag y una época `?epoca=N` (un `?v=`) en el mapa | 6 / 6 / 7 / 4 | La URL sigue sin decir qué contiene. Un CMS con la política vieja —en esta máquina corren varios— vuelve a envenenar la URL con época, y hay que subirla a mano. El ETag local es mtime^longitud, con resolución de segundos, y no deja emitir SRI. Además cuesta 11 GET condicionales (~3 RTT) antes de hidratar, en CADA navegación. Se le tomó: el 304 con la política del Worker y copiar los planos sólo si cambian. |
| **Vaciar la caché tras `publish:runtime`** (lo de antes, la receta de la 0140) | — | Arregla la máquina de quien mira. |

## Consecuencias

**A favor, medido en vivo** (informe 83 ✔):

- **Quien tenía el runtime viejo baja el nuevo sin vaciar nada.**
  - *Antes.* Se envenenó un perfil con la cabecera exacta del CMS viejo y se le sirvió el CMS nuevo con
    el mapa plano. `sg-shared.js` siguió saliendo de la caché (`transferSize` 0, 801.154 B) aunque el CMS
    ya lo servía `no-cache`: la entrada `immutable` de un año gana sin revalidar.
  - *Después.* Tras `publish-runtime` por contenido y un `reload()` sin vaciar, el mapa nombra
    `sg-shared.18a9decbae7fffb0.js` y los 11 ficheros del runtime de la página llegan con `transferSize`
    > 0 (`sg-shared`: 802.796 B transferidos, 802.496 B de cuerpo). El SHA-256 de los bytes casa con la
    huella, y `syn-promo-code` vuelve a tener estilos y `locked`.
  - La URL plana sigue envenenada en esa caché, pero ningún mapa la nombra.
- **La segunda visita no sale a la red**: los 11 ficheros desde la caché, sin un solo 304.
- **Las cabeceras del montaje local son las de la decisión 2**:
  - con huella, `public, max-age=31536000, immutable` y `Access-Control-Allow-Origin: *`;
  - los planos (`<ver>/sg-shared.js` y `latest/sg-shared.js`), `<ver>/import-map.json`,
    `latest/import-map.json` y `latest/huellas.json`, `public, no-cache, must-revalidate`;
  - los seis —el de la huella incluido— contestan 304 a `If-None-Match` y a `If-Modified-Since`;
  - los `main.js` versionados de los elementos siguen `immutable`, y su `latest`, `no-cache`.
- **Republicar sin cambios no escribe nada**: los 66 ficheros de `runtime/` tienen el mismo mtime y el
  mismo hash. La primera publicación escribió 22 ficheros nuevos (20 con huella y 2 libros) y sólo cambió
  el contenido de los 4 mapas.
- **La receta de la 0140** («`build:runtime` + `publish:runtime`, y vaciar la caché») pierde su último
  paso.

**En contra**

- **El CDN sólo crece**: ~0,8 MB (`sg-shared` + `sg-core`) por cada cambio de `libs/shared`, y no se
  poda nada. Los límites de assets de Workers quedan lejos; podar sería una decisión del arquitecto, con
  su ventana.
- **Los navegadores ya envenenados lo siguen estando en la URL PLANA**, hasta un año. Sólo la huella los
  rescata, y `por-nombre` los vuelve a exponer; igual que redesplegar un SHA anterior a este cambio.
- **`build:cdn` necesita red hacia el origen**: sin ella se para (falla seguro, no publica). Si quien
  construye producción no alcanza `workers.dev`, frena el despliegue hasta que se ponga el origen o se
  resuelva la red.
- **Un CDN nuevo y vacío no se puede desplegar sin una decisión** (leído del código). Su primer
  `build:cdn` se para, porque un origen que no sirve `registry.json` no prueba que no haya nada que
  conservar; y una salida construida sin conservar lleva la marca que la puerta de `wrangler deploy`
  rechaza. Hoy hay un solo CDN de producción, y sí sirve su registry.
- **Dos publicadores a la vez sobre el mismo `C:\LOCAL_CDN`** pueden perder una entrada del libro, que
  se lee, se fusiona y se escribe sin cerrojo; uno de los dos gates sale rojo. En producción no aplica:
  cada `build:cdn` arma su propio `public/` en su máquina (la revisión lo refutó como defecto).
- **Tests**: 124 casos nuevos en `test:tools` del UI (de 875 a 999) y 23 en el CMS (22 en el paso 1
  —la tabla de la política, gemela de la del UI, y las tres formas del nombre con huella— y 1 de la
  relectura del mapa).

**Lo que NO cierra, dicho para no presentarlo como cerrado**

- **La ventana del CMS en modo Http para los ELEMENTOS.** El `main.js` de un elemento se publica en una
  ruta que se sobrescribe (en producción, todos en `0.1.0`) y su `meta.json`, con la SRI, se cachea para
  siempre en el CMS. Entre el despliegue del CDN y el refresco del proceso, un navegador puede bajar el
  elemento nuevo con el mapa viejo. Lo cierra publicar los elementos por contenido, como el runtime:
  queda de seguimiento.
- **El gate «los exports sólo crecen»** de `publish-runtime` (F2 del plan): no se construyó.
- **La SRI del runtime**: con la huella ya sería seguro emitirla; queda de seguimiento.
- **#183** (la semántica de los pagos) no se toca.

**Lo que no se midió en vivo** (estaba en el plan 82 y no entró en la ronda 1):

- *El cambio trivial de `libs/shared` publicado en la piloto.* Sí se midió en la prueba de humo local de
  `publish-runtime`, sobre una carpeta propia: un byte de más en `sg-shared` dio UN fichero con huella
  nuevo, dejó el viejo intacto (mismo mtime y sha256) y el libro con las dos.
- *El humo conectado del CMS contra la piloto*, con el CMS nuevo y con uno anterior.
- *El export nuevo con el publicador viejo* como control.
- *La vuelta atrás* con `--urls=por-nombre`.
- *El envenenamiento con el binario del CMS viejo*: se sembró emulando su cabecera exacta con un
  servidor propio.

## Producción: quién construye el CDN público, dicho

- **El CDN público lo construye Cloudflare Workers Builds**, no `despliegue-cdn.yml`: es el check
  «Workers Builds: synergos-ui» de cada push al `master` del UI. **Sale rojo en cada push** ✔
  (`gh api …/commits/<sha>/check-runs`): 60 de las 60 cabezas de `master` que tienen ese check, desde
  la primera (2026-09-17) hasta la última (2026-10-10).
  - Lo que sirve hoy es un build verde del 2026-09-12, de `b951c775`, un commit que no está en la
    historia de `master`: el `meta.json` de `eventos` dice ese commit y `builtAt` 2026-09-12T00:57:45Z.
  - Los cinco últimos pushes de `master` que dispararon `despliegue-cdn.yml`, del 2026-10-01 al
    2026-10-10 (`399febc4`, `8ce86331`, `d408a341`, `2020219c` y `6af94863`), también están rojos en
    Workers Builds.
  - La causa está en los logs de Cloudflare, que desde aquí no se ven.
- **`despliegue-cdn.yml` no tiene secrets** (`CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`). Su paso
  «¿Está configurado el destino?» deja `listo=no` y se salta todos los demás (corridas 37984217938 y
  38007454987 ✔). Encenderlo, y desconectar Workers Builds —dos publicadores sobre el mismo Worker
  dejan sin saber cuál publicó—, **es del arquitecto**. Mientras tanto, el `SYNERGOS_CDN_CONSERVAR_DESDE`
  que ese workflow le pasa a `build:cdn` no se ejerce.
- **El CDN público todavía no tiene libro** (404 ✔) y sí sirve su `registry.json` (200 ✔). El primer
  despliegue con este cambio será el «primer despliegue por contenido»; desde el segundo, `build:cdn`
  bajará y verificará las generaciones vivas. La puerta de `wrangler deploy` también corre dentro de
  Workers Builds.

## Relación con otras ADRs

- **0099** — la enmienda en su §3: el runtime compartido sigue en `runtime/{fw}/{ver}/` con su
  `import-map.json`, pero lo que el mapa nombra lleva la huella de sus bytes. Lleva la nota fechada.
- **0012 / 0132** — el CDN se consume: el CMS no publica, y su cliente Http sólo aprende cuándo releer
  el mapa.
- **0089** — el montaje local del CDN, cuya política de caché cambia para el runtime.
- **0140** — el defecto salió de su piloto; con esta ADR, tras `publish:runtime` ya no hace falta vaciar
  la caché (nota fechada en su «cupón endurecido»).
- **0113** — los elementos siguen siendo apps que externalizan el runtime; publicarlos por contenido es
  el seguimiento.
- **0133** — el despliegue del CMS; el del CDN público tiene su propio dueño (arriba).

## Referencias

- `_informes/80-adr-0140-cupon-en-vivo.json` (escenario 7 y defecto 1),
  `_informes/82-adr-0140-defectos-en-vivo-plan.json` (los tres diseños, el juez y los pasos),
  `_informes/84-adr-0140-defectos-pausa.json` (la revisión) y `_informes/83-adr-0140-defectos-en-vivo-r1.json`
  (la verificación en vivo).
- CMS: `3a79c36f` (la política del montaje local, `Synergos.CMS.Web/Services/PoliticaDeCacheDelCdnLocal.cs`,
  el contrato `docs/contracts/cdn-bundle-structure.md` y el humo conectado que declara que su CDN no se
  despliega) y `c6a9973d` (la relectura del mapa en `HttpBundleRegistryClient`).
- UI: `45609b3` (la política del Worker y el 304), `822e95b` (el mapa que nombra con una función),
  `ce4b73d` (`runtime-por-contenido`), `83f66ae` (`publish-runtime` por contenido), `5985436` (`build:cdn`
  conserva), `60af1ab` (el humo), `6dada50` (la llave del libro), `2fee048` y `0ed9627` (los reintentos,
  también del cuerpo), `eaa9338` (el origen, el 404 que no es del CDN y la puerta de `wrangler deploy`) y
  `ac3e197` (la puerta de enlace de `publish.mjs`). Todos en `lego/integracion`, locales.
