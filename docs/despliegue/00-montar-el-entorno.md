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

## 1. El servidor — Hetzner, ~€6,80/mes

### Por qué precio fijo y no cobro por uso

Es la decisión que protege del miedo real:

> En AWS, Azure o Vercel, **un ataque genera factura**. En un VPS de precio fijo, un ataque pone
> el sitio lento o caído — y **nunca** genera factura. Pagás lo mismo el mes del ataque que el
> anterior.

### Qué crear

1. Cuenta en [console.hetzner.cloud](https://console.hetzner.cloud) (pide verificación; puede
   tardar unas horas la primera vez)
2. **New Project** → `synergos`
3. **Add Server**:

   | | |
   |---|---|
   | **Location** | **Ashburn, VA** — ~80 ms a Colombia. Alemania son ~200 ms |
   | **Image** | Ubuntu 24.04 |
   | **Type** | **CX32** — 4 vCPU · 8 GB · 80 GB |
   | **SSH Key** | pegar la pública. **Sin contraseña**, ver §1.1 |
   | **Backups** | ver §1.2 |

> **Por qué CX32 y no el CX22 de €3,79.** Son 23 procesos .NET: Umbraco solo pide 400-600 MB y
> 22 APIs a ~80-100 MB son otros ~2 GB. En 4 GB entra con swap; en 8 GB entra tranquilo.
> Ahorrarse €3 para después depurar por qué el servidor se traba es mal negocio.

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

## 3. Los bundles del CDN — Cloudflare Pages ([#20](../../../../issues/20))

Proyecto **Pages** (no Workers) conectado a **`Synergos.UI`** (no al CMS). El comando es el build
de Angular; el directorio de salida, el `dist` que produzca.

Y un `_headers` en la salida, que es lo que hace que Pages sea mejor que GitHub Pages acá:

```
/bundles/*
  Cache-Control: public, max-age=31536000, immutable
  Access-Control-Allow-Origin: *

/element-registry.json
  Cache-Control: public, max-age=60
```

> **Caché eterna en los bundles, corta en el registry.** Al revés es el error fácil de cometer y
> difícil de diagnosticar: publicás una versión nueva y **nadie la ve durante un año**.

---

## 4. Los secretos en GitHub

**Settings → Secrets and variables → Actions → New repository secret.**

| Nombre | Qué es |
|---|---|
| `DEPLOY_HOST` | la IP del servidor |
| `DEPLOY_USER` | el usuario SSH |
| `DEPLOY_SSH_KEY` | la llave **privada** del §1.1 |
| `SYNERGOS_API_KEY` | la llave compartida entre servicios. Inventala larga: `openssl rand -hex 32` |
| `CART_SECRET` | firma de la cookie del carrito (ADR 0028) |
| `RESEND_API_KEY` | ← HU [#12](../../../../issues/12), para que el correo salga de verdad |
| `RESEND_FROM` | `Avisos <avisos@tu-dominio.co>` |
| `RESEND_WEBHOOK_SECRET` | `whsec_…`, al dar de alta el webhook |

> **Ninguno de estos va al repo. Nunca.** Y si alguno se pega por error en un commit, en un
> issue o en un chat: **se rota**, no se borra el mensaje. Un secreto que se vio una vez está
> quemado.

Los tres de Resend se sacan en [resend.com](https://resend.com) después de verificar el dominio;
el webhook apunta a `https://<tu-dominio>/v1/webhooks/resend`.

---

## 5. Qué pasa después

Con eso, un `git push` a `master`:

```
gates (2044 tests + 7 workflows) → 23 imágenes a GHCR → el servidor las baja
  → parada antes de arranque → humo contra la URL pública → verde
```

Y si el humo falla: vuelve a la imagen anterior y **el action se pone rojo**. Un deploy verde con
el sitio caído es peor que uno rojo, porque nadie lo mira.

### El primer arranque tarda, y es normal

Umbraco se instala desatendido e importa 880 ítems de uSync — unos 74 s medidos en CI (ADR 0128).
**Es lo que hace que no haya que correr el import a mano.** Durante ese rato el proxy espera; no
da 502.

---

## 6. La cuenta

| | |
|---|---|
| Servidor Hetzner CX32 | €6,80/mes |
| Cloudflare (proxy, DDoS, SSL, Pages) | $0 |
| GitHub (Actions y GHCR, repo público) | $0 |
| Dominio `.com` | ~$10,44/año |
| *(opcional)* backups | ~€1,40/mes |

> ### **~€7/mes + ~$10/año. Techo conocido de antemano.**
>
> Lo único que puede cobrar de más: Hetzner en EE.UU. incluye 1 TB/mes y cobra **€1 por TB**
> extra. Un ataque de 10 TB costaría €10 — y con el proxy en naranja, ese tráfico ni llega al
> servidor.
