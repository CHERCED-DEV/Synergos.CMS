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

## 3. Los bundles del CDN — ✅ **ya está hecho** ([#20](../../../../issues/20))

**https://synergos-ui.synergos-labs.workers.dev** — 139 elementos, desplegándose solo en cada push
a `master` de `Synergos.UI`. No hay nada que montar acá.

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

Umbraco se instala desatendido e importa 880 ítems de uSync — unos 74 s medidos en CI (ADR 0128).
**Es lo que hace que no haya que correr el import a mano.** Durante ese rato el proxy espera; no
da 502.

---

## 6. La cuenta

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
