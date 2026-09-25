# Montar el entorno — lo que hace el arquitecto a mano, una sola vez

> Épica [#16](../../../../issues/16). Todo lo de acá se hace **una vez**; después cada `git push`
> a `master` despliega solo.
>
> **Lo que un agente NO puede hacer y por eso está escrito acá:** crear cuentas, pagar, y tener
> las credenciales. El resto lo hace el pipeline.

---

## 0. Antes de empezar: qué NO es cada cosa

Se intentó una vez desplegar el CMS en **Cloudflare Workers** y no podía funcionar. Queda dicho
para que no se repita:

| | Qué ejecuta | Nuestro caso |
|---|---|---|
| **Cloudflare Workers** | JavaScript y WASM en aislados de V8. Sin disco, sin proceso de larga vida, sin .NET | ❌ **no puede correr Umbraco ni las APIs** |
| **Cloudflare Pages** | ficheros estáticos, con cabeceras propias | ✅ los bundles del CDN ([#20](../../../../issues/20)) |
| **Cloudflare (proxy/DNS)** | se pone **delante** de un servidor ajeno | ✅ DDoS, caché, SSL, dominio |
| **GitHub Actions** | trabajos que arrancan, hacen algo y **se mueren** | ✅ construir y empujar. ❌ hospedar |
| **GHCR** | registro de imágenes | ✅ ahí quedan las 23 |
| **Un VPS** | máquinas de verdad, precio fijo | ✅ **ahí corre el producto** |

> **La regla que resume todo:** Actions es el obrero, Cloudflare el portero, el VPS la casa.

---

## 1. El servidor — Hetzner, ~€8,49/mes

### Por qué precio fijo y no cobro por uso

Es la decisión que protege del miedo real:

> En AWS, Azure o Vercel, **un ataque genera factura**. En un VPS de precio fijo, un ataque pone
> el sitio lento o caído — y **nunca** genera factura. Pagás lo mismo el mes del ataque que el
> anterior.

### ⚠️ Lo que este documento decía y era falso

Hasta el 2026-08-04 esta sección mandaba a crear un **CX32 en Ashburn por €6,80**. Las dos mitades
estaban mal, y sólo se vio al ir a comprarlo:

- **La línea CX no existe en EE.UU.** Es exclusiva de Europa. En Ashburn y Hillsboro sólo hay
  CPX y CCX.
- **El CX32 ya no existe en ningún sitio**: Hetzner renombró la línea y su reemplazo es el
  **CX33** (mismas 4 vCPU y 8 GB, 80 GB NVMe, 20 TB de tráfico) a **€8,49/mes**.
- **Los precios de EE.UU. subieron fuerte el 15-jun-2026.** El equivalente allá —CPX31, mismas
  4 vCPU y 8 GB— cuesta **$73,49/mes**.

### La decisión que eso obliga a tomar

|  | Alemania · **CX33** | Ashburn · **CPX31** |
|---|---|---|
| Precio | **€8,49/mes** | **$73,49/mes** |
| CPU · RAM · disco | 4 vCPU · 8 GB · 80 GB | 4 vCPU · 8 GB · 160 GB |
| Latencia a Colombia | ~200 ms | ~80 ms |
| Al año | ~€102 | ~$880 |

> ### Recomendado: **Alemania**. La diferencia son ~$780 al año por 120 ms.
>
> Y esos 120 ms se pagan **una vez por página**, no por recurso: los bundles del CDN ya salen del
> borde de Cloudflare (que tiene presencia en Bogotá) y el HTML lo cachea el proxy en naranja. Lo
> que de verdad viaja a Alemania es la primera petición y las de backoffice.
>
> Si algún día el cliente nota la diferencia, mudarse es recrear el servidor y cambiar un secreto:
> los datos están en volúmenes y las imágenes en GHCR. **No es una decisión difícil de revertir**,
> y por eso no vale la pena pagarla por adelantado.

### Qué crear

1. Cuenta en [console.hetzner.cloud](https://console.hetzner.cloud) (pide verificación; puede
   tardar unas horas la primera vez)
2. **New Project** → `synergos`
3. **Add Server**:

   | | |
   |---|---|
   | **Location** | **Falkenstein** o **Nuremberg** (Alemania) — ver la tabla de arriba |
   | **Image** | Ubuntu 24.04 |
   | **Type** | **CX33** — 4 vCPU · 8 GB · 80 GB · 20 TB |
   | **SSH Key** | pegar la pública. **Sin contraseña**, ver §1.1 |
   | **Backups** | ver §1.2 |

> **Por qué 8 GB y no 4.** Son 23 procesos .NET: Umbraco solo pide 400-600 MB y 22 APIs a
> ~80-100 MB son otros ~2 GB. En 4 GB entra con swap; en 8 GB entra tranquilo. Ahorrarse €3 para
> después depurar por qué el servidor se traba es mal negocio.
>
> **Confirmá el precio en el panel antes de crear.** Los de acá se verificaron contra la
> [tabla oficial](https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/)
> el 2026-08-04, y ya cambiaron una vez desde que se escribió este documento.

### 1.1 La llave SSH

En la máquina del arquitecto:

```bash
ssh-keygen -t ed25519 -C "synergos-deploy"
cat ~/.ssh/id_ed25519.pub          # ESTA se pega en Hetzner
```

> **La privada (`id_ed25519`, sin `.pub`) no se comparte, no se commitea y no se pega en un
> chat.** Va a hacer falta como *secret* de GitHub en el paso §4 — ese es su único destino.

Desactivar el acceso por contraseña en cuanto entres:

```bash
sudo sed -i 's/^#*PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
sudo systemctl restart ssh
```

### 1.1.bis Si dirigís esto desde una tablet — la ruta sin terminal local

Lo de arriba supone un portátil con `ssh-keygen`, `ssh` y `scp`. **Buena parte de este proyecto
se dirige desde una tablet Android**, donde eso es un estorbo. Esta ruta hace exactamente lo
mismo usando sólo el navegador y la **consola web de Hetzner**.

**1. Crear el servidor SIN llave SSH.** Hetzner muestra una contraseña de root al terminar.

**2. Abrir la consola web** (el botón `>_` en el panel del servidor) y entrar como `root`.

**3. Preparar la máquina, de una línea.** El repo es público, así que el script se baja solo —
no hay que pegarlo:

```bash
curl -fsSL https://raw.githubusercontent.com/CHERCED-DEV/Synergos.CMS/master/tools/bootstrap-servidor.sh | bash
```

**4. Generar la llave de despliegue EN el servidor**, que es lo que evita necesitar terminal
propia:

```bash
ssh-keygen -t ed25519 -f /root/deploy_key -N "" -C "synergos-deploy"
install -o despliegue -g despliegue -m 700 -d /home/despliegue/.ssh
cat /root/deploy_key.pub >> /home/despliegue/.ssh/authorized_keys
chown despliegue:despliegue /home/despliegue/.ssh/authorized_keys
chmod 600 /home/despliegue/.ssh/authorized_keys

cat /root/deploy_key        # ← copiar ESTO al secret DEPLOY_SSH_KEY de GitHub
```

**5. Comprobar que la llave nueva funciona ANTES de cerrar la contraseña:**

```bash
ssh -i /root/deploy_key -o BatchMode=yes despliegue@localhost 'echo ok'
```

**6. Borrar la privada del servidor**, ya guardada en GitHub. No tiene por qué quedarse donde
vive la pública:

```bash
shred -u /root/deploy_key /root/deploy_key.pub
```

**7. Cerrar la puerta que abrió el paso 1:**

```bash
sed -i 's/^#*PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
systemctl reload ssh
```

> **El orden importa y no es intercambiable.** Cerrar la contraseña antes de comprobar la llave
> (paso 5) deja fuera de su propio servidor a quien lo monta — recuperable por la consola web,
> pero un rato desagradable. Y dejar la privada en el servidor significa que quien entre ahí una
> vez puede volver a entrar mañana aunque se le quite todo lo demás.
>
> **La privada pasa por el portapapeles UNA vez.** No por un chat, no por un fichero del repo, no
> por correo. Si aparece en algún otro sitio: **se rota** —llave nueva, `authorized_keys` nuevo,
> secret nuevo— en vez de borrar el rastro.

### 1.2 Backups — la decisión honesta

Cuestan **20% del servidor (~€1,4/mes)**. Hoy no hay nada que perder, así que se pueden dejar
apagados. Pero:

> **El día que un cliente tenga pedidos, citas o acuses ahí dentro, hay que encenderlos.** La DB
> del CMS es derivable (ADR 0128) y las imágenes están en GHCR, así que el CMS se reconstruye
> solo. **Los datos de las 20 capacidades, no.** Esos solo existen en el disco del servidor.

### 1.3 El firewall — el paso que hace verdad todo lo demás

En **Hetzner → Firewalls**, o con `ufw` en la máquina. Entrante:

| Puerto | De dónde |
|---|---|
| 22 (SSH) | **solo tu IP**, si es fija. Si no, abierto y con llave |
| 80 y 443 | **solo los rangos de Cloudflare** ← §2.3 |

> **Sin esto, todo lo de Cloudflare es decorativo.** Hay servicios que buscan la IP real detrás
> del proxy; quien la encuentre le pega al origen directo y se salta la protección entera.

### 1.4 Dejar el servidor listo — un script, una vez

Todo lo que el despliegue automático da por hecho —Docker con el plugin `compose` v2, un usuario
que no es root, `/opt/synergos` con los permisos correctos, el firewall, y un `.env` con las dos
llaves largas ya generadas— lo pone esto:

```bash
scp tools/bootstrap-servidor.sh root@<ip>:/tmp/
ssh root@<ip> 'bash /tmp/bootstrap-servidor.sh'
```

Sin terminal local —desde la consola web de Hetzner— es una línea, porque el repo es público:

```bash
curl -fsSL https://raw.githubusercontent.com/CHERCED-DEV/Synergos.CMS/master/tools/bootstrap-servidor.sh | bash
```

Está escrito para poder correrse **dos veces sin romper nada**, porque se va a correr dos veces:
la primera siempre falta algo.

> **Lo que NO hace, y es a propósito:** no escribe el dominio ni las credenciales de correo —esas
> las ponés vos en `/opt/synergos/.env`—, y no cierra 80/443 a los rangos de Cloudflare. Ese paso
> va **después** de que el DNS apunte (§2.3); antes deja el sitio inalcanzable hasta para vos.
>
> **El action entra como el usuario `despliegue`, no como root.** No es ceremonia: la llave de
> despliegue vive en los secretos de GitHub, y una llave que abre root convierte cualquier fuga en
> el servidor entero en vez de en un directorio y un demonio.

---

## 2. Cloudflare — el portero, gratis

### 2.1 El dominio

Comprarlo en **[Cloudflare Registrar](https://domains.cloudflare.com)**: lo vende a precio de
costo (~$10,44/año un `.com`) y **renueva al mismo precio**. Nada del truco de $1 el primer año y
$40 el segundo.

Si ya lo comprás en otro lado, hay que apuntar los *nameservers* a Cloudflare igual.

### 2.2 Los registros DNS

| Tipo | Nombre | Valor | Proxy |
|---|---|---|---|
| A | `@` | la IP del servidor | 🟠 **activado** |
| A | `www` | la IP del servidor | 🟠 **activado** |

> **La nube naranja es lo que importa.** En gris, Cloudflare solo resuelve el nombre y expone tu
> IP al mundo. En naranja, el tráfico pasa por ellos: ahí están el DDoS, la caché y el SSL.

Y en **SSL/TLS → Overview**: modo **Full (strict)**.

### 2.3 Cerrar el origen

Los rangos de Cloudflare están en [cloudflare.com/ips](https://www.cloudflare.com/ips/). Se
cargan en el firewall del §1.3 como las únicas fuentes permitidas en 80/443.

Se comprueba así — y **tiene que fallar**:

```bash
curl -I --connect-timeout 5 http://<IP-DEL-SERVIDOR>     # sin respuesta = bien
curl -I https://<tu-dominio>                             # 200 = bien
```

### 2.4 Lo que NO hay que crear

**Ningún Worker sobre `Synergos.CMS`.** Ya se intentó: Workers no ejecuta .NET, el build muere y
queda un proyecto vacío haciendo ruido en cada push. Si quedó uno, borralo en
**Workers & Pages → el proyecto → Settings → Delete**.

---

## 3. Los bundles del CDN — construido, y **falta una credencial** ([#20](../../../../issues/20) · [#165](../../../../issues/165))

**https://synergos-ui.synergos-labs.workers.dev** — existe y está sano. (Cuántos elementos sirve
se mide, no se copia: el comando está en `CLAUDE.md` §11, que es el único sitio donde va esa
cifra — ver #86.)

> ### ⚠️ Esta fila decía ✅ «ya está hecho» y «no hay nada que montar acá», y era falso
>
> Medido el **2026-09-25**: el CDN servía el commit `b951c77`, un build del **12 de septiembre**
> que **no está en ninguna rama** de `Synergos.UI` — ni en master, ni en el respaldo previo a la
> refirma. Desde entonces `master` del hermano había avanzado **once veces** (78 commits contando
> lo que entró por el merge del 17) sin que el CDN se moviera. Un commit que ninguna rama contiene
> sólo pudo salir de un `wrangler deploy` desde una máquina.
>
> Quien publica es `despliegue-cdn.yml` (de `Synergos.UI`, [#74](https://github.com/CHERCED-DEV/Synergos.UI/issues/74)),
> y **se salta solo** mientras falten `CLOUDFLARE_API_TOKEN` y `CLOUDFLARE_ACCOUNT_ID`.
>
> **Lo que hay que montar, entonces, son esas dos credenciales**, y van en los secrets de
> `Synergos.UI` —no de este repo—: el paso a paso está en
> [`SynergosDocs/DESPLIEGUE_CDN.md`](https://github.com/CHERCED-DEV/Synergos.UI/blob/master/SynergosDocs/DESPLIEGUE_CDN.md).
>
> **Y por qué esta línea importaba más que una desactualizada corriente:** una guía que se queda
> corta se lee como incompleta; ésta **afirmaba de más**, bajo un ✅, en el único documento cuyo
> trabajo es enumerar lo que hay que hacer a mano. Mandaba a no hacer algo que hay que hacer.
> Mientras no estén las credenciales, **lo que hay arriba puede no ser lo que dice el repo, y nada
> se pone rojo**: el humo del CDN pasa igual, porque el CDN viejo está perfecto.

Quedó en **Workers con assets estáticos**, no en Pages como decía este documento. Dos cosas
salieron mejor de lo previsto:

- **La política de caché es código con tests** (`tools/lib/cdn-cache-policy.mjs`), no un fichero
  `_headers`. Hizo falta: Cloudflare **fusiona** las reglas de `_headers` que se solapan, y las
  tres rutas hermanas de cada elemento —`latest/`, la versión exacta, el alias mayor— habrían
  producido `Cache-Control: max-age=60, max-age=31536000, immutable`. Basura.
- **El despliegue es atómico**: todo el directorio cambia de un golpe, así que no existe la
  ventana «registry publicado antes que los bundles» que había que vigilar.

> **Y un defecto que sólo se vio en vivo:** Cloudflare sirve los assets **antes** de invocar al
> Worker, así que las cabeceras no corrían y todo salía `max-age=0` sin CORS. Se arregla con
> `run_worker_first: true`. Ningún test podía verlo — es comportamiento de la plataforma.

Lo que falta de este lado es una línea del `.env`:

```
SYNERGOS_CDN_MODE=Http
SYNERGOS_CDN_URL=https://synergos-ui.synergos-labs.workers.dev
```

Sin eso, los 71 `elementSyn*` emiten un comentario HTML de relleno: degradado y **visible**, que
es mejor que un sitio roto. (El `bootstrap-servidor.sh` del §1.4 ya lo deja puesto.)

---

## 4. Los secretos en GitHub

**Settings → Secrets and variables → Actions.** Ojo con la pestaña: hay dos, y no da igual.

### 4.1 Secrets — sólo lo que abre puertas

| Nombre | Qué es |
|---|---|
| `DEPLOY_HOST` | la IP del servidor |
| `DEPLOY_USER` | el usuario SSH — `despliegue`, el que crea el script del §1.4 |
| `DEPLOY_SSH_KEY` | la llave **privada** del §1.1 |
| `DEPLOY_HOST_KEY` | *(opcional pero recomendado)* la salida de `ssh-keyscan -H <ip>` |

> **Sin `DEPLOY_HOST_KEY`, el action acepta la huella que el servidor presente en el momento.**
> Alcanza para arrancar y **no es equivalente**: quien pueda meterse en medio de ese primer saludo
> recibe la llave de despliegue. Está escrito en el workflow para que sea una decisión y no un
> descuido.

Las credenciales de los servicios (`SYNERGOS_API_KEY`, `SYNERGOS_CART_SECRET`, las tres de Resend)
**no van acá**: viven en `/opt/synergos/.env`, en el servidor, y nunca salen de ahí. El action no
las necesita — sólo copia ficheros y ejecuta un script; quien las lee es `docker compose`.

Los tres de Resend se sacan en [resend.com](https://resend.com) después de verificar el dominio;
el webhook apunta a `https://<tu-dominio>/v1/webhooks/resend`.

### 4.2 Variables — lo que no es secreto

| Nombre | Qué es |
|---|---|
| `SYNERGOS_DOMAIN` | el dominio público, sin `https://` ni barra final |

> **Va como Variable y no como Secret a propósito.** Un dominio no es secreto —está en el DNS—, y
> GitHub **enmascara** los secretos en los logs: puesto como secret, la salida del humo sería
> `✓ el humo pasó contra https://***`, que es exactamente el renglón que uno va a leer el día que
> algo falle.

> **Ninguno de estos va al repo. Nunca.** Y si alguno se pega por error en un commit, en un
> issue o en un chat: **se rota**, no se borra el mensaje. Un secreto que se vio una vez está
> quemado.

> ### Mientras falten, el despliegue se salta solo
>
> No se pone rojo: si faltan `DEPLOY_HOST` o `SYNERGOS_DOMAIN`, el workflow anota qué falta y
> termina en verde. **Un rojo permanente que todos saben ignorar entrena a ignorar los rojos de
> verdad.** Se enciende solo el día que los secretos existan, sin tocar nada.

---

## 5. Qué pasa después

Con eso, un `git push` a `master`:

```
gates (2080 tests + 8 workflows) → 23 imágenes a GHCR → el servidor las baja
  → parada antes de arranque → humo contra la URL pública → verde
```

Y si el humo falla: vuelve a la imagen anterior y **el action se pone rojo**. Un deploy verde con
el sitio caído es peor que uno rojo, porque nadie lo mira.

### Las tres piezas, y dónde corre cada una

| Fichero | Dónde corre | Qué hace |
|---|---|---|
| `.github/workflows/deploy.yml` | runner | espera los gates, copia, ordena, y decide |
| `tools/deploy-remoto.sh` | **el servidor** | baja imágenes, para, arranca, comprueba etiquetas |
| `tools/humo-publico.sh` | **el runner** | prueba la URL pública como un visitante |

> **El humo corre en el runner y no en el servidor, y esa es la parte que se suele hacer mal.**
> Desde el servidor se prueba el último salto; desde fuera se atraviesa lo mismo que atraviesa un
> visitante — DNS, Cloudflare, el certificado, el proxy—, que es donde falla un despliegue.
>
> Por eso vive en su propio fichero: así `DeployPipelineTests` puede exigir que no diga
> `localhost`. Un humo contra el propio runner **pasa siempre**, y el único síntoma es que nunca
> falla.

### Lo que el humo comprueba, y por qué cada cosa

1. **La portada devuelve 200 y es HTML.** `/health` puede contestar con el sitio inservible: no
   toca ni el contenido, ni las plantillas, ni la base.
2. **La versión que contesta es el commit que se subió.** `/_health` publica su SHA (lo inyecta la
   imagen). Es lo que separa «responde» de «se actualizó»: un reinicio que falla en silencio deja
   viva la versión anterior y el despliegue se daría por bueno habiendo desplegado nada.
3. **El árbol de servicios no contesta desde internet.** Hay un gate que vigila el `compose`; esto
   vigila la **realidad** — un firewall mal puesto o un `ports` añadido a mano en el servidor no
   se ven en el repo.

### La caída es a propósito

Se para todo y se arranca todo. No es pereza: 19 capacidades guardan en fichero JSON con un `lock`
de **proceso**, y un despliegue «sin caída» son dos instancias a la vez pisándose el almacén **sin
dar error**. El despliegue que cualquier plataforma moderna hace por defecto rompería esto.

> El día que cambie el almacén (épica #2) se revisa. Hasta entonces, quien lo «mejore» corrompe
> datos sin ver un error.

### El primer arranque tarda, y es normal

Umbraco **se instala** desatendido: crea sus tablas y su usuario sin que nadie toque el
backoffice. Eso sí es automático, y durante ese rato el proxy reintenta en vez de dar 502.

> ### ⚠️ Lo que este documento decía y era falso (#114)
>
> Decía que el arranque *«importa 880 ítems de uSync — **es lo que hace que no haya que correr el
> import a mano**»*. **No lo hace.** ADR 0008 fija que el import NO ocurre al arrancar: el default
> del paquete es `None` y no hay una sola línea en el repo que lo pise — ni en C#, ni en
> `appsettings*.json`, ni en `compose.prod.yml`.
>
> Medido contra una base vacía, el arranque dice `uSync: Startup Complete 0ms` — o sea **cero**
> ítems. La prueba de que ya se sabía está en el propio gate de CI:
> `tools/usync-rebuild-check.mjs` inyecta `uSync__Settings__ImportAtStartup=All` por variable de
> entorno, precisamente **porque la app no lo hace sola**.
>
> Y un Umbraco vacío no falla: sirve su cartel «No published content» con **200 y HTML de
> verdad**. Antes de #114 el humo daba eso por bueno, así que el primer despliegue de un servidor
> nuevo se habría puesto **verde con el sitio en blanco**.

El import es un paso, y está en el §5.bis de abajo.

---

## 5.bis. El estado que hay que sembrar, UNA VEZ

Un servidor recién montado tiene los procesos arriba y **nada dentro**. Esto es lo que hay que
hacer una vez, en orden. Nada de esto lo hace el arranque, y ninguno estaba escrito acá.

| Fichero | Dónde corre | Qué hace |
|---|---|---|
| `tools/importar-schema.sh` | **el servidor** | importa el árbol de uSync en un contenedor efímero |
| `tools/provisionar.sh` | **el servidor** | publica definiciones, recursos y precios; reconcilia |

> **Los dos los COPIA el despliegue**, junto con `tools/provisionar.recursos.json`. No es un
> detalle: los tres del respaldo vivían sólo en el repo y la máquina que había que proteger era la
> única sin con qué (HU #31), y al escribir estos dos volvió a pasar igual — este documento decía
> «corré esto en el servidor» y el servidor no los tenía (#114). Hoy hay gate, y lo que decide qué
> se copia es **la columna «Dónde corre» de las tablas de este documento**: lo que aquí diga «el
> servidor» tiene que llegar al servidor.

### 5.bis.1 El schema de uSync

```bash
tools/importar-schema.sh          # en el servidor, con el stack levantado
```

Para el CMS, importa en un contenedor efímero y lo vuelve a levantar. Es **idempotente** —uSync
compara y aplica lo que difiere— y comprueba lo mismo que el gate de reconstrucción: que termine,
sin líneas `[ERR]`, y que procese al menos tantos ítems como ficheros `.config` hay. Medido en el
stack compuesto: **896 ítems, 355 cambios, 0 errores, ~28 s**.

> **Para el CMS a propósito, y no es prudencia.** La primera versión de este script importaba con
> el CMS arriba y se midió qué pasa: **dos Umbraco sobre la misma base se matan por MainDom**. El
> que entra se lo queda, el que servía el sitio se apaga solo —«Application is shutting down»,
> exit 0, sin un error— y el import se cae con él. Es la misma regla de «una instancia, y parada
> antes de arranque» que el compose ya declara para las capacidades.

#### Por qué no se activa el import en el arranque y ya

Se consideró, y se descartó con dos razones — ninguna de las dos es «lo dice el ADR».

**`ImportAtStartup=All` revierte el trabajo editorial en cada reinicio.** `appsettings.json` tiene
encendidos `ContentHandler` y `MediaHandler` (ADR 0129), así que `All` re-importa el contenido del
repo **en cada arranque**. Un `restart`, un despliegue, un reinicio del servidor — y lo que un
editor publicó vuelve atrás, en silencio y sin error. Se cambiaría «el primer despliegue necesita
un comando» por «cada reinicio es una vuelta atrás editorial», que es peor y además del tipo que
no se nota.

**`ImportAtStartup=Settings` no resuelve el problema que venía a resolver.** Trae el schema y deja
el sitio sin contenido, o sea exactamente el mismo cartel de «No published content» en la portada.
Es la vía de escape que ADR 0008 deja prevista —con ADR sucesor— y no compra nada aquí.

Así que **ADR 0008 se queda como está**, y lo que cambia es que el import deja de ser un ritual de
backoffice imposible en un VPS sin pantalla: es un comando.

### 5.bis.2 El contenido — la portada de arranque

Con el schema importado, la portada **sigue diciendo «No published content»** hasta que alguien
publique un nodo. No es un fallo del import: es que `uSync/v9/Content/` **está vacía en el repo**
—comprobado también contra `master`— así que no hay ni un nodo que importar. Y encender
`Synergos:DevSeed:Enabled` tampoco la crea sola: lo que se siembra en el arranque son datos de
dominio (hilos de blogs, correspondencia de Gobierno), nunca contenido — eso es ADR 0013 y así
tiene que seguir.

Hasta la HU [#119](../../../../issues/119) ahí se acababa el documento: «alguien tiene que
autorarlo a mano». **Hoy hay herramienta**, y el camino entero es éste — cuatro pasos, y **los
tres primeros corren en la máquina de desarrollo, no en el servidor**:

| | Dónde corre | Qué hace |
|---|---|---|
| 1 | **desarrollo** | `POST /dev/seed-portada` crea la portada de arranque |
| 2 | **desarrollo** | el arquitecto la ajusta en el backoffice hasta que sea la suya |
| 3 | **desarrollo** | uSync exporta al guardar → `uSync/v9/Content/` → commit |
| 4 | **el servidor** | `tools/importar-schema.sh` (paso 5.bis.1) la trae con el resto |

> **Por qué el rodeo, y no un comando en el servidor.** El flag de DevSeed **viene apagado en
> producción** y hay gate que lo vigila ([#113](../../../../issues/113)), así que en el servidor
> ese endpoint contesta 404 — y eso es lo correcto, no un obstáculo: son catorce endpoints
> `[AllowAnonymous]` y uno de ellos borra el árbol de contenido entero. Sembrar donde se edita y
> **transportar el resultado como XML versionado** es además lo que hace que el siguiente
> servidor, y el de después, arranquen con la misma portada sin que nadie repita nada.

#### 1 · Sembrarla

Con el CMS de desarrollo levantado y `Synergos:DevSeed:Enabled` en `true`:

```bash
curl -X POST http://localhost:5000/dev/seed-portada
# {"siteRootId":1463,"outcome":"Created","detail":"created"}
```

Crea **un `siteRoot` en la raíz del árbol** —no bajo un `platformRoot`, que es el wrapper
opcional para agrupar varios sitios— y le pone de cuerpo tres bandas del Layout Composer: un
hero, una rejilla de tres tarjetas y un bloque de texto. Las dos primeras son elementos del
catálogo CDN (`elementSynHeroBanner`, `elementSynFeatureGrid`) y **traen fallback SSR** (ADR
0012), así que la portada se ve entera **con el CDN sin configurar**, que es el estado de un
servidor recién montado.

**Es idempotente, y de la forma que importa.** Correrla otra vez no duplica nada — pero lo que
de verdad protege es el paso 2: si el `siteRoot` ya tiene cuerpo, **no lo toca** y contesta
`AlreadyAuthored`. Sembrar encima se llevaría por delante lo que acabás de ajustar, justo antes
de exportarlo.

| outcome | qué pasó |
|---|---|
| `Created` | no había `siteRoot`; se creó con su portada |
| `Filled` | había uno sin cuerpo; se le puso la portada, **sin pisarle la marca** |
| `AlreadyAuthored` | ya había portada. No se tocó nada |
| `RootAlreadyTaken` | la raíz la ocupa otro nodo. **No siembra**, y dice cuál |
| `MissingContentTypes` | el schema no está importado. Dice cuál falta |

> **Lo de `RootAlreadyTaken` costó una medición.** Si en el árbol ya hay un raíz —el
> `platformRoot` del andamio de la vitrina, por ejemplo— crear un `siteRoot` al lado **no pone
> la portada en `/`**: Umbraco resuelve `/` al primer raíz, así que quedaría en `/inicio` y la
> herramienta habría contestado «creada» con la portada invisible. Verificado en vivo, que es
> como se vio. Hoy no siembra y dice qué hay ocupando la raíz.

> **No confundir con `POST /dev/seed-synergos-identity`**, que arma el andamio de la vitrina
> SynergosLabs (`platformRoot` → `siteRoot` → tres páginas que luego puebla
> `POST /dev/fill-synergos-pages`). Es otra cosa y mucho más grande. De paso quedó arreglado:
> **llevaba roto** —no ponía las dos obligatorias de `compBranding` en el `platformRoot`, así
> que el publish se caía y no creaba nada— **y lo contestaba con un 200** y un `success:false`
> adentro, que desde un script no se lee como un fallo. Hoy sale con 409 y es idempotente.

Un fallo sale con **409**, no con 200 — la herramienta anterior contestaba `200` con
`success:false` y por eso llevaba rota sin que nadie lo notara.

#### 2 · Ajustarla

En el backoffice, sobre ese nodo. Es contenido normal: cambiar textos, arrastrar bloques, añadir
páginas hijas, poner la marca. Lo que queda acá es lo que va a ver la gente — la siembra sólo
evita empezar con una página en blanco.

#### 3 · Exportarla y commitearla

`ExportOnSave` está en `All` y el `ContentHandler` encendido (ADR 0129), así que **guardar ya
exporta**: aparece `Synergos.CMS.Web/uSync/v9/Content/<nodo>.config`. Ese fichero **lo commitea
el arquitecto**, no un agente — el contenido editorial no se autora escribiendo XML.

```bash
git add Synergos.CMS.Web/uSync/v9/Content
node tools/usync-audit.mjs      # pasa: el check 9 sólo rechaza el árbol del seeder de pruebas
```

#### 4 · Y el servidor la recibe

Con el `Content/` commiteado, el paso 5.bis.1 deja de traer sólo el schema. Medido contra una
base limpia: el import pasa de **896** ítems a **897**, y `GET /` sirve la portada sin que nadie
siembre nada en el servidor.

> **Qué se verificó de verdad, y con qué.** El ciclo completo se corrió con el proceso vivo,
> perfil `Docker`, base SQLite limpia y el árbol de uSync importado entero: `/` en blanco (200,
> 1926 bytes, cartel de Umbraco) → sembrar → `/` con la portada (200, 19802 bytes) → sembrar otra
> vez (`AlreadyAuthored`, la página no cambia) → export a `Content/inicio.config` → **base nueva
> desde cero importando ese fichero** → `/` sirve la portada sin sembrar. Hay gate
> (`tools/humo-portada.mjs`, workflow `humo-portada.yml`), y es el único del repo que **pide la
> página**.
>
> **Y lo que NO se pudo ejecutar, dicho en vez de afirmado:** el paso 4 se reprodujo
> importando el `Content/` exportado contra una base nueva **con la aplicación directamente**,
> no con `tools/importar-schema.sh` — ese script necesita el demonio de Docker, que el
> contenedor donde se escribió esto no tiene. Lo que hace el script es levantar un contenedor
> efímero para correr ese mismo import, así que lo verificado es el import; lo que queda sin
> ejercitar es su envoltorio, igual que ya decía §5.bis.3.
>
> **Lo que ese gate destapó la primera vez que se corrió, y que ningún otro veía:** con la
> portada puesta, `/` contestaba **500**. `_SynergosBridge.cshtml` usa `LogWarning` sin el
> `@using` de `Microsoft.Extensions.Logging`, y las vistas de este proyecto se compilan
> **siempre en caliente** (`RazorCompileOnBuild=false`, porque `ModelsMode=InMemoryAuto`), donde
> no llegan los implicit usings del SDK. O sea que **ninguna página de contenido renderizaba**
> desde el 2026-09-05, con la suite entera en verde. No lo vio nadie porque no había contenido
> que renderizar: el hueco de abajo tapaba el de arriba.

### 5.bis.3 El estado que las capacidades exigen

```bash
tools/provisionar.sh --verificar   # qué falta, sin escribir nada
tools/provisionar.sh               # lo publica
```

Cuatro cosas, y las cuatro se rechazan con su motivo si faltan —`definition_not_found`,
`resource_not_found`, `price_not_found`— así que **no fallan al arrancar: fallan la primera vez
que alguien intenta usar la función**. El script está en `tools/provisionar.sh` y su cabecera
explica cada una.

| | qué publica | de qué sirve |
|---|---|---|
| Gobierno | la definición del trámite en `Api.Workflow` | sin ella no se decide ningún expediente |
| Seguimiento | las **cuatro** definiciones de pipeline | sin ellas no avanza ningún pedido |
| Salud · Propiedades · Viajes | los recursos de `Api.Booking` | sin ellos no se agenda ni se aparta |
| Viajes | los precios de `Api.Pricing` | sin ellos no se cotiza una oferta |

> **Ojo con las tres de la última fila: son por ENTIDAD, no por producto.** Un recurso por médico,
> uno por inmueble que acepte visitas, uno por oferta de viaje. Eso **no es bootstrap**: es una
> obligación permanente que crece con el catálogo cada vez que se da de alta un médico o se
> publica un inmueble. `provisionar.sh` siembra las que conoce y **reconcilia**, pero el día que
> el catálogo salga de Umbraco en vez de del stub, esto tiene que pasar a ser un seam del alta —
> está escrito en la cabecera del script, con el disparador.

**Las entidades se declaran en `tools/provisionar.recursos.json`**, y de ahí salen los recursos y
los precios. Dos cosas que hay que saber al escribirlo, porque las dos las destapó correr esto
contra las capacidades vivas y **ninguna de las dos falla a la vista**:

- **Una entrada `precio` tiene que traer su `amount`, y numérico.** Si falta, el script se planta
  y lo dice. No se publica a cero: una oferta a cero **se vende gratis** y no hay nada que falle
  —ni al desplegar, ni al cotizar— hasta que alguien compra.
- **Cambiar un precio y volver a correr el script SÍ lo cambia**, porque su llave de idempotencia
  lleva el monto dentro. Antes no: la capacidad consulta su libro antes de aplicar nada y devolvía
  la tarifa anterior contestando 200. Republicar lo mismo sigue sin cambiar nada.

> Verificado contra `Api.Workflow`, `Api.Booking` y `Api.Pricing` levantados de verdad: las tres
> pasadas —`--verificar` en seco, publicar, y publicar otra vez— dejan **un** recurso por sujeto y
> **un** precio, y la segunda no toca nada. Lo que NO se pudo ejecutar acá es
> `importar-schema.sh`, que necesita el demonio de Docker.

---

## 6. El respaldo — que es el único que se puede comprobar de antemano

Todo lo demás de esta guía se nota cuando falla. Esto no: un respaldo roto **se ve exactamente
igual que uno bueno** hasta el día en que hace falta, y ese día ya no hay margen. Por eso la parte
importante de esta sección no es crear la copia; es el **ensayo**.

### Las tres piezas

| Fichero | Dónde corre | Qué hace |
|---|---|---|
| `tools/respaldo.sh` | **el servidor** (cron diario, 04:10 UTC) | para, copia los volúmenes en frío, arranca |
| `tools/enviar-respaldo.sh` | **el servidor** | cifra, sube al destino remoto, poda lo vencido |
| `tools/restaurar.sh` | **el servidor** | devuelve los volúmenes; exige `--si-estoy-seguro` |
| `tools/prueba-restauracion.sh` | **NO el servidor** | trae, descifra, restaura aparte y **lee los datos** |

`tools/bootstrap-servidor.sh` deja el cron puesto. El despliegue copia los tres primeros al
servidor — antes no lo hacía, y la máquina que había que proteger era justo la única que no tenía
con qué.

### 6.1 Crear la llave, una sola vez y **no en el servidor**

```bash
age-keygen -o identidad.txt      # en TU máquina
```

Imprime dos cosas: un fichero `identidad.txt` (la **privada**) y una línea `age1…` (la **pública**).

- La **pública** va en el `.env` del servidor. No es un secreto: con ella sólo se puede *escribir*
  un respaldo.
- La **privada** se guarda donde guardás lo que no se regenera, y **no se copia al servidor**.

> **Por qué asimétrica y no una contraseña.** Con la privada fuera de la máquina, el servidor puede
> escribir respaldos y **no puede leer los que ya mandó**. Con una contraseña simétrica, quien se
> lleva el servidor se lleva el histórico entero — que es bastante más de lo que hay vivo en la
> máquina en ese momento.
>
> **Y el precio, que hay que saberlo:** si se pierde la privada, los respaldos son ruido. Un
> respaldo ilegible y uno que no existe se ven **igual** desde el bucket. Lo único que distingue
> los dos casos es correr el ensayo.

### 6.2 El destino, en el `.env` del servidor

Un remoto de `rclone`, así que el proveedor es tuyo: S3, R2, B2, un SFTP a otra máquina o un disco
montado son el mismo comando. Los nombres exactos y el ejemplo completo están en `.env.example`;
lo mínimo es:

```
SYNERGOS_RESPALDO_DESTINO=respaldos:mi-bucket/prod
SYNERGOS_RESPALDO_LLAVE_PUBLICA=age1…
RCLONE_CONFIG_RESPALDOS_TYPE=s3
RCLONE_CONFIG_RESPALDOS_ACCESS_KEY_ID=…
RCLONE_CONFIG_RESPALDOS_SECRET_ACCESS_KEY=…
```

> **Con el destino vacío el respaldo sigue corriendo y DICE que lo que dejó no es un respaldo.** Es
> deliberado: un clon limpio y una máquina de desarrollo tienen que poder copiar sus volúmenes sin
> cuenta de nada, igual que el default `Stub` de todos los cableados del producto.
>
> **Lo que sí revienta es estar configurado a medias** — destino puesto y llave sin poner, por
> ejemplo—, y revienta **antes de tocar nada**. Es la forma del defecto #56 (el modo `Http` del CDN
> sin URL): arrancar verde, contestar que todo va bien y fallar el día que alguien lo necesita.
>
> La credencial del bucket debería poder **escribir** y no **leer**. Y borrar sólo si querés que la
> poda la haga el servidor; si no, dejale la caducidad al bucket.

### 6.3 Cuánto duran — es una decisión de privacidad, no de disco

Dentro van **direcciones de entrega y nombres de pacientes**.

| | por defecto | qué lo borra |
|---|---|---|
| Fuera del servidor | `SYNERGOS_RESPALDO_RETENCION_DIAS=30` | `enviar-respaldo.sh`, después de comprobar que la de hoy llegó |
| En el servidor | `SYNERGOS_RESPALDO_LOCAL_DIAS=7` | `respaldo.sh`, al final de cada corrida |
| Piso, en los dos | `SYNERGOS_RESPALDO_MINIMO=3` | nada: son las que sobreviven siempre |

- **No existe «para siempre»**: el `0` se rechaza. Guardar indefinidamente un archivo con los datos
  personales de todo el producto es una decisión que nadie tomó y que dentro de dos años es una
  filtración esperando a que alguien encuentre la credencial.
- **30 días fuera** es suficiente para notar al volver de vacaciones algo que empezó a corromperse
  hace tres semanas, y poco para que eso viva años en el disco de un tercero. Con un respaldo
  diario son **30 copias completas** allá; la cuenta hay que hacerla.
- **7 días en el servidor**, que cumple otra cosa: es la que se restaura rápido cuando alguien se
  lleva por delante algo esta mañana. Meses de datos personales en el mismo disco que se está
  protegiendo no añaden seguridad, sólo añaden dónde perderlos.
- **El piso de 3 no es paranoia.** La retención por edad a secas vacía el destino el día que el
  respaldo lleva más de 30 días sin correr — o sea que el cron que se rompió y nadie vio se lleva
  por delante la última copia buena, en silencio, justo el día que hace falta.

### 6.4 El ensayo — **esto es lo que cierra el asunto**

```bash
export SYNERGOS_RESPALDO_DESTINO=respaldos:mi-bucket/prod
export SYNERGOS_RESPALDO_IDENTIDAD=~/llaves/identidad.txt
export RCLONE_CONFIG_RESPALDOS_...       # de sólo lectura basta
./tools/prueba-restauracion.sh
```

Trae la copia **del remoto**, la descifra, la restaura sobre un proyecto de compose **aparte**,
levanta el producto en la versión que escribió esos datos y **le pide los datos de vuelta** por
HTTP. Al terminar borra los volúmenes del ensayo: dejarlos sería una segunda copia de datos
personales que nadie decidió.

Se niega a correr si el proyecto de ensayo es el de producción, y si la identidad está dentro de
`/opt/synergos`.

> **Por qué no basta con mirar que el fichero llegó.** Se levantó `Api.Audit` sobre un almacén
> ilegible para medirlo: **`/health` contesta 200 y la lectura contesta 500.** Un humo de salud da
> por buena esa restauración. Es el defecto #82 tal cual —la bitácora guardaba sus asientos y
> devolvía 500 en toda lectura tras un reinicio— y ningún `tar -t` ni ningún `sha256` lo habría
> visto: los bytes estaban perfectos.
>
> Y hay un escalón más: un almacén que se lee **a medias** contesta **200 con menos registros**,
> porque el conversor es tolerante. Por eso el ensayo no mira sólo el código de estado: exige que
> **vuelvan datos**. Un sistema recién instalado contesta 200 a las dieciocho colecciones.

### 6.5 Lo que el respaldo **no** lleva, dicho de frente

**El `.env` del servidor.** Ahí viven la llave compartida de las 22, la de firma de identidad, las
de la pasarela y la del correo. Meterlas en la copia convertiría el respaldo en el objeto más
valioso del producto y a la identidad de `age` en la llave de todo — es un problema de custodia de
secretos disfrazado de copia de datos, con 30 copias diarias de blanco.

Casi todo eso se regenera. **Uno no:** `Synergos:Academy:CertificateSigningSecret`. Sin él —o sin
el llavero `cms-dpkeys`, que **sí** va en la copia— los diplomas ya emitidos dejan de verificar, y
el propio código lo deja escrito en el log al pasar. Guardalo donde guardás la identidad de `age`.

---

## 7. La cuenta

| | |
|---|---|
| Servidor Hetzner **CX33** (Alemania) | €8,49/mes |
| Cloudflare (proxy, DDoS, SSL, y el CDN en Workers) | $0 |
| GitHub (Actions y GHCR, repo público) | $0 |
| Dominio `.com` en Cloudflare Registrar | ~$10,44/año |
| *(opcional)* backups | ~€1,70/mes |

> ### **~€8,5/mes + ~$10/año. Techo conocido de antemano.**
>
> *(Si se eligiera Ashburn en vez de Alemania: **$73,49/mes**. Ver la tabla del §1.)*
>
> Lo único que puede cobrar de más es el tráfico, y el CX33 incluye **20 TB/mes**. Con el proxy en
> naranja, además, el tráfico de un ataque ni llega al servidor: lo come Cloudflare, que no cobra
> por ancho de banda.
>
> **Eso es lo que compra un precio fijo.** El mes de un ataque cuesta lo mismo que el anterior —
> que era el miedo concreto detrás de no querer cobro por uso.
