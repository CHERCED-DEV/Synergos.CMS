# Arrancar los dos árboles en tu máquina

De dos clones a **una portada que hidrata en el navegador**. Medido de punta a
punta el 2026-09-16; cada número de este documento sale de correrlo, no de
recordarlo.

`new-developer-setup.md` te deja un Umbraco corriendo, y ahí para. Eso **no es
el producto**: es el CMS sin schema, sin contenido y sin CDN, o sea las tres
cosas que hacen que la página se vea. Este documento es el resto.

---

## Lo que tiene que ser verdad al final

Una página del CMS que trae, en el mismo HTML:

1. sus `<synergos-*>` pintados por el SSR,
2. un `<script type="importmap">` que resuelve **los dos runtimes** (Angular y
   Preact), y
3. un `<script type="module">` por elemento, con su SRI, apuntando al CDN.

Si falta el (2), la página sale **200, con todo el SSR, y no hidrata nada** — ni
lo que funcionaba. Ése es el defecto #126 y es la razón de que exista
`tools/humo-conectado.mjs`.

---

## 0. Qué necesitás

| | |
|---|---|
| **.NET SDK** | el de `global.json` (hoy 10.0.202). Los tests además piden los **runtimes 8.0** — `dotnet-install.sh --runtime dotnet` y `--runtime aspnetcore` |
| **Node** | 20+ |
| **Los dos clones**, hermanos | `Synergos.CMS/` y `Synergos.UI/` en la misma carpeta padre. Si no lo están, todo acepta `--cms-path` / `--cdn-path` |

---

## 1. El CDN, primero

El CMS **consume** el CDN (ADR 0012): sin él no hay bundles que servir, y
arrancar el CMS primero sólo sirve para verlo a medias.

```bash
cd Synergos.UI
npm install && npm install --prefix platforms/angular && npm install --prefix platforms/preact
npm run build:cdn          # compila las dos plataformas, sus runtimes, publica y mide
```

Termina en `✓ listo en <repo>/public`. Ahí dentro está `synergos/registry.json`,
un `synergos/<elemento>/<framework>/…` por bundle y `synergos/runtime/<framework>/`
por plataforma.

> **`npm run build:cdn` corre el presupuesto de tamaño al final y sale con 1 si
> algo se pasó.** No es un aviso: es el gate. Si se queja, mirá **qué** creció
> antes de tocar un techo — la razón está en `tools/lib/cdn-size-budget.mjs`.

## 2. El schema del CMS

```bash
cd Synergos.CMS
dotnet build Synergos.CMS.sln        # 0 errores; el warning NU1902 es conocido (ADR 0001)
```

El schema vive como XML en `Synergos.CMS.Web/uSync/v9/` y **NO se importa al
arrancar** (ADR 0008: `ImportAtStartup` está en `None`, y hay gate que lo
vigila). Se importa a mano, una vez, desde el backoffice:

1. `dotnet run --project Synergos.CMS.Web`
2. entrás a `/umbraco` (`admin@synergos.local` / `Synergos2026!`)
3. sección **uSync** → **Import**

> **Esperá a que el import TERMINE antes de sembrar nada.** El log dice
> `uSync: Startup Complete`. Sembrar a mitad crea la portada con los DataTypes a
> medio importar: un desplegable guarda una cadena plana y **toda página de
> contenido contesta 500**, con una traza que apunta a la vista y no al orden de
> arranque. Medido — costó tres corridas averiguarlo.

## 3. La portada

`uSync/v9/Content/` está vacía a propósito (ADR 0129: el agente no autora
contenido) y nada siembra al arrancar (ADR 0013). La portada de arranque la
crea una herramienta, detrás del flag de desarrollo:

```bash
curl -X POST http://localhost:<puerto>/dev/seed-portada
```

Contesta `{"outcome":"Created"}` la primera vez y `AlreadyAuthored` después —
**no pisa lo que hayas ajustado**, que es el daño que importa, no duplicar.

## 4. Enchufar el CMS al CDN

**Si corrés en `Development` y clonaste los dos repos hermanos, esto ya está
hecho y podés saltar al paso 5.** El default apunta a `../../Synergos.UI/public`
—relativa, resuelta contra la raíz de contenido del proyecto Web, así que vale
igual en Windows, en Linux y en CI— y el modo es `FileSystem`: con el paso 1
hecho, el sitio hidrata sin una sola variable de entorno.

Hasta el 2026-09-16 ese default decía `C:\LOCAL_CDN` y **no fallaba en ninguna
otra máquina**: servía la portada en 200, con el SSR entero, sin import map y sin
un solo `<script type="module">` — o sea idéntica a una que funciona, y muerta
(#132). Hoy `Mode=FileSystem` sin esa carpeta —o con una carpeta que no trae
`synergos/registry.json`, que es el hermano clonado y sin construir— **no
arranca**, y el mensaje dice contra qué ruta absoluta resolvió y las dos salidas:

```bash
# levantar el CMS SIN CDN (backoffice, contenido, todo menos hidratar)
Synergos__BundleRegistry__Mode=Stub
```

El resto de esta sección es para el otro camino: servir el CDN por HTTP, que es
lo que hace falta con `npm run dev:cdn` (el ciclo editor→navegador) y lo que usa
el despliegue. Y acá está la trampa que cuesta una tarde:

```bash
# ✅ LO QUE FUNCIONA — son claves de configuración de .NET
Synergos__BundleRegistry__Mode=Http
Synergos__BundleRegistry__PublicBaseUrl=http://127.0.0.1:4321
```

```bash
# ❌ LO QUE NO — sólo existe dentro de compose.yml, que las TRADUCE a las de arriba
SYNERGOS_CDN_MODE=Http
SYNERGOS_CDN_URL=http://127.0.0.1:4321
```

Medido con la misma portada, cambiando sólo el nombre de la variable:

| | bytes servidos | import map |
|---|---|---|
| `Synergos__BundleRegistry__Mode` | 21 595 | **23 entradas** |
| `SYNERGOS_CDN_MODE` | 19 785 | **no hay** |

Las dos sirven la portada con sus dos `<synergos-*>`. La segunda **no hidrata
nada** y no se queja de nada.

Para servir el CDN mientras desarrollás tenés dos caminos:

```bash
# (a) el watch del repo hermano, que rehace y sirve — el ciclo editor→navegador
cd Synergos.UI && npm run dev:cdn        # sirve en :4321

# (b) lo ya publicado, con cualquier estático
cd Synergos.UI && npx http-server public -p 4321
```

## 5. Comprobarlo sin abrir el navegador

```bash
cd Synergos.CMS
node tools/humo-conectado.mjs            # levanta el CDN él solo desde ../Synergos.UI/public
```

Hace los pasos 2-4 sobre una base temporal y falla nombrando la causa. Lo que
mira, y por qué cada cosa:

```
✓ / sirve la portada (21595 bytes)
✓ import map con 23 entradas
✓ resuelve hacia los 2: angular, preact     ← con uno solo, medio sitio no hidrata
✓ los 2 tags tienen su bundle               ← un tag sin <script> es un hueco que el SSR disimula
✓ los 2 bundles contestan 200 en el CDN     ← un <script> a un 404 se ve igual que uno bueno
```

Los otros dos gates que arrancan la aplicación siguen valiendo y miran otra cosa:

| | qué prueba |
|---|---|
| `node tools/usync-rebuild-check.mjs` | base vacía + XML = entorno completo |
| `node tools/humo-portada.mjs` | base vacía + XML + siembra = portada **servida** |
| `node tools/humo-conectado.mjs` | …y además **conectada al CDN** |

## 6. Verlo hidratar de verdad

Lo único que prueba que un elemento hidrata es pedir la página en un navegador
—las vistas de este repo se compilan **siempre en caliente**, así que un
`dotnet build` verde no dice nada de ellas—. Abrí la portada y comprobá en la
consola:

```js
customElements.get('synergos-hero-banner')   // una función, no undefined
```

Del lado del UI, `npm run dev:cdn` y `/probar/<elemento>` monta un elemento
suelto con su import map y valores de muestra. **No es una vista previa del
producto** y la página lo dice: ahí no hay tema del CMS ni contenido real.

---

## Lo que se rompe seguido

| Síntoma | Causa | Arreglo |
|---|---|---|
| La página se ve bien y nada responde | falta el import map | El paso 4 — casi siempre el nombre de la variable |
| 500 en toda página de contenido | sembraste antes de que terminara el import | Base nueva, esperá `uSync: Startup Complete`, sembrá |
| `/` dice «No published content» | no hay portada | El paso 3 |
| `MissingContentTypes` al sembrar | el import no terminó | Lo mismo |
| Un elemento hidrata y otro no | el import map no trae el runtime de ese framework | `npm run build:cdn` de nuevo; `humo-conectado` lo nombra |
| `npm run build:cdn` sale con 1 | el presupuesto de tamaño | Mirá **qué** creció, no el techo |
| El CMS no arranca y habla de `LocalPath` | `Mode=FileSystem` y el hermano no está construido ahí | `npm run build:cdn`, o `Synergos__BundleRegistry__Mode=Stub` |

## Lo que este camino NO cubre

- **Publicar contenido de verdad.** La portada de arranque es andamiaje; el
  contenido lo autora una persona en el backoffice y uSync lo exporta al
  guardar.
- **El árbol de servicios** (las 20 capacidades y los orquestadores). Arranca
  aparte con `compose`; el CMS habla con nueve de ellas y **todos los
  interruptores están apagados por defecto**, así que el producto levanta sin
  ninguna. Ver `docs/despliegue/00-montar-el-entorno.md`.
- **El CDN público.** Esto es todo local. El de verdad lo sirve Cloudflare
  Workers desde `Synergos.UI/public`.
