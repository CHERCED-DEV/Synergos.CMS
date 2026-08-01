# Hosting y deployment — investigación y topología objetivo

- **Status:** Research. **No es una decisión tomada** — ningún ADR respalda
  todavía lo que sigue. Este documento existe para que la decisión se tome
  con números en la mano en vez de por intuición.
- **Fecha del relevamiento:** 2026-08-01. Los precios y los free tiers
  cambian rápido; ver §7 antes de confiar en cualquier cifra.
- **Relacionado:** [`DOCKER.md`](../../../DOCKER.md) (cómo correr la imagen),
  ADR 0003 (SQLite), ADR 0008 (uSync SSoT), ADR 0012 (CDN consumido).

## 1. Qué se quería responder

Tener tres entornos —dev, staging, producción— con releases en vivo, para
poder evaluar cómo se comporta Synergos en una web real. Preferentemente
gratis, dado que todo el stack es open source.

## 2. Baseline medido

Números tomados sobre la imagen de `Dockerfile`, no estimados:

| Métrica | Valor | Cómo se midió |
|---|---|---|
| Tamaño de imagen | 835 MB | `docker images` |
| Boot en frío hasta HTTP 200 | 8 s | Contenedor nuevo, DB vacía |
| Memoria en reposo | 208 MiB | `docker stats`, post-boot, DB vacía |

⚠️ Los 208 MiB son un **piso**, no el régimen: se midieron con la base
vacía, sin los 243 DocTypes importados y sin NuCache poblado. La
estimación de trabajo es **400–600 MB por instancia** una vez cargado el
schema. Es una extrapolación y **conviene re-medirla** después del primer
uSync Import — si sale mucho más alta, cambia el sizing de §5.

## 3. Los constraints que eliminan casi todo

Tres hechos encadenados, en este orden:

1. **Umbraco 13 sólo soporta SQL Server o SQLite.** No Postgres. Descarta
   todos los "free Postgres" del mercado (Render, Koyeb, Neon, Supabase):
   el motor que regalan no le sirve a este CMS.
2. **SQLite es un archivo → exige disco persistente.** El free tier de
   Render lo dice explícito: filesystem efímero, *no admite* disco
   persistente, y el servicio se apaga a los 15 min de inactividad.
3. **Aun con una DB gestionada, sigue haciendo falta disco.** En este repo
   `App_Data/` guarda orders, comments, audit, form submissions y los
   secretos TOTP (ADR 0084), más `wwwroot/media` y las claves de
   DataProtection. Nada de eso vive en la base.

De ahí sale la conclusión estructural: **lo que se necesita no es un PaaS,
es una máquina con disco.**

El mercado además se achicó en 2026: Fly.io eliminó el free tier para
cuentas nuevas, Koyeb cerró el suyo tras la adquisición por Mistral, y
Oracle recortó su Always Free de 4 OCPU/24 GB a 2 OCPU/12 GB el 15 de junio
de 2026 sin anuncio público.

Azure merece una nota porque parece encajar y no encaja: su **SQL Database
free tier es real y vitalicio** (100.000 vCore-segundos/mes, 32 GB, hasta
10 DBs), pero App Service **F1 da 60 CPU-minutos por día** y no soporta
contenedores Linux en el tier gratis. La mitad DB es gratis; la mitad
cómputo, no.

## 4. Opción A — gratis

Un VM **Oracle Always Free ARM** (2 OCPU / 12 GB / 200 GB tras el recorte),
corriendo los tres contenedores detrás de Caddy.

Caveats que hay que aceptar conscientemente:

- Oracle recortó el tier a la mitad sin avisar. Es información sobre cuánto
  vale la promesa de "gratis para siempre".
- Capacidad ARM agotada en regiones populares; Frankfurt y Singapur
  aprovisionan en minutos, US East puede tardar días.
- Reclamo de instancias inactivas.
- **Es ARM64**: el `Dockerfile` actual compila amd64. Habría que pasarlo a
  build multi-arch — trabajo real, no un flag.
- Latencia desde Colombia a Frankfurt ≈ 120–150 ms.

Como respaldo existe la **e2-micro always-free de GCP** (1 vCPU / 1 GB /
30 GB, en us-west1/us-central1/us-east1). No aguanta los tres entornos,
pero sirve de segunda caja.

## 5. Opción B — pago barato (recomendada)

**La cuenta no es por entorno: es una sola caja.** Con la estimación de
§2, tres entornos son ~1,8 GB + OS + Caddy ≈ **2,3 GB**. Una caja de 4 GB
sobra.

Y el boot de 8 s es la palanca de costo: **dev y staging no necesitan estar
prendidos**. Se levantan con `docker compose up` cuando se usan. El único
always-on real es producción, así que hasta 2 GB alcanzaría.

| Proveedor | Plan | Specs | Precio |
|---|---|---|---|
| RackNerd (anual) | Promo | 2 vCPU / 3,5 GB / 65 GB | < $2,50/mes |
| RackNerd (anual) | Promo | 3 vCPU / 4 GB / 60 GB | $59,99/año ≈ $5/mes |
| Hetzner | CX23 (x86) | 2 vCPU / 4 GB / 40 GB | ≈ €5–7/mes ⚠️ |
| Hetzner | CAX11 (ARM) | 2 vCPU / 4 GB / 40 GB | ≈ €6/mes ⚠️ |
| Netcup | Entry | 2 vCPU / 2 GB / 64 GB | €3,35/mes |

⚠️ Hetzner subió precios en junio de 2026 y las fuentes se contradicen
entre sí (unas citan $4,59, otras €6,49–6,99 para el plan de entrada).
**Confirmar en la consola** antes de decidir.

**Recomendación: RackNerd anual, datacenter Miami.** Es el más barato y la
latencia desde Colombia a Miami (~50–70 ms) es la mejor del grupo. Hetzner
sólo tiene Ashburn en EE.UU., y sus regiones americanas traen 1 TB de
tráfico contra 20 TB en las europeas; Netcup es sólo Europa.

Contras honestos de RackNerd: prepago anual (compromiso de 12 meses) y
precios promocionales que no siempre renuevan igual.

**Lo que el pago ahorra en trabajo, no sólo en dolores de cabeza:**
RackNerd y Hetzner CX son **x86**, así que el `Dockerfile` ya construido y
probado sirve tal cual — desaparece el trabajo de build multi-arch que
exigía la opción gratis, además de la ruleta de capacidad y el recorte
unilateral.

## 6. Lo que es gratis en cualquier escenario

El VPS es el único renglón de la factura:

| Pieza | Dónde | Límites |
|---|---|---|
| UI (Synergos.UI) | Cloudflare Pages | Ancho de banda y requests ilimitados; 25 MiB/archivo, 20k archivos |
| Bundles CDN | jsDelivr desde tags de GitHub | Sin límite de ancho de banda; 20 MB/archivo |
| CI/CD | GitHub Actions | Los 4 workflows actuales ya corren ahí |
| Backups | `rsync` de volúmenes a Cloudflare R2 | 10 GB gratis — no hace falta el add-on del proveedor |

### El registry del CDN puede ser estático

`docs/umbraco/cdn-contract.md` pide cinco cosas al "CDN team" (endpoint,
schema, semántica de error, versionado, entornos dev/staging). Las cinco se
satisfacen con **archivos JSON estáticos** servidos desde Cloudflare Pages:

```
/registry/v1/bundles/{elementKey}.json
```

con exactamente el shape propuesto en §2 de ese documento. La semántica de
error de §3 sale gratis: un host estático devuelve 404 para una key
inexistente, que es justo lo que el contrato mapea a `null`.

Eso desbloquearía `HttpBundleRegistryClient` sin servidor y sin costo. **No
viola ADR 0012** —la seam sigue intacta, el CMS sigue consumiendo— pero
implica que el operador se pone el sombrero del CDN team. **Es una decisión
que requiere ADR**, no una consecuencia técnica automática.

## 7. Problemas abiertos — resolver ANTES de montar infra

Esto es lo que realmente bloquea tener tres entornos útiles. Ningún
proveedor lo resuelve:

1. **Promoción de schema.** Vive en XML (`uSync/v9`), pero
   `ImportAtStartup=None` por ADR 0008 y el import lo corre el arquitecto a
   mano desde el backoffice. Con tres entornos son tres imports manuales
   por release. Automatizarlo implica flipear ese flag en dev/staging —
   **decisión de ADR**.
2. **Promoción de contenido.** `uSync/v9/Content/` y `Media/` están
   gitignoreados. **Hoy no existe ningún camino** para promover contenido
   de dev a staging a prod. Es el hueco más grande del plan.
3. **Verificar que el import funciona de verdad.** Está asumido, no
   comprobado en un entorno limpio. Un contenedor recién levantado es
   justamente el banco de pruebas para confirmarlo.
4. **Secretos.** `appsettings.Docker.json` trae credenciales de instalación
   desatendida pensadas para LAN. Para un host público hay que moverlas a
   variables de entorno o a un secret store, y rotar la password del admin.
5. **Re-medir memoria** con el schema cargado (§2).

## 8. Estado del trabajo

| Ítem | Estado |
|---|---|
| Imagen Docker + compose, probados | Hecho — ver `DOCKER.md` |
| Build multi-arch ARM64 | Sólo si se elige la opción gratis |
| Workflow de deploy (GHCR + SSH) | Pendiente |
| `Caddyfile` con tres subdominios | Pendiente |
| Registry estático del CDN | Pendiente + ADR |
| Promoción de schema/contenido | Pendiente — bloqueante real |

## 9. Fuentes

Relevadas el 2026-08-01:

- [Umbraco 13 requirements](https://docs.umbraco.com/umbraco-cms/13.latest/fundamentals/setup/requirements)
- [Render — Deploy for Free](https://render.com/docs/free) · [Persistent Disks](https://render.com/docs/disks)
- [Oracle recorta el Free Tier Ampere A1 (InfoQ)](https://www.infoq.com/news/2026/07/oracle-cloud-free-tier-limits/) · [Always Free Resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm)
- [Fly.io tras la muerte del free tier](https://expresstech.io/7-fly-io-alternatives-in-2026-real-pricing-after-the-free-tier-died/)
- [Azure SQL Database free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql) · [App Service hosting plans](https://docs.azure.cn/en-us/app-service/overview-hosting-plans)
- [Google Cloud Compute free tier](https://cloud.google.com/free/docs/compute-getting-started)
- [Cloudflare Pages pricing](https://developers.cloudflare.com/pages/functions/pricing/) · [jsDelivr](https://github.com/jsdelivr/jsdelivr)
- [Hetzner cost-optimized](https://www.hetzner.com/cloud/cost-optimized/) (precios cargados por JS, no extraíbles) · [análisis de precios 2026](https://agentdeals.dev/hetzner-pricing-2026)
- [RackNerd 2026 specials](https://racknerd.club/en/specials-2026)
