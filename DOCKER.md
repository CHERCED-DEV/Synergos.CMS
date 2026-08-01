# Correr Synergos.CMS en Docker (y consumirlo desde la tablet)

El objetivo de este setup es puntual: levantar el host Umbraco en un
contenedor en la PC y poder abrirlo desde otro dispositivo de la misma red
—una tablet, el teléfono— sin depender de `dotnet run` ni de las rutas
Windows cableadas en `appsettings.Development.json`.

No reemplaza el flujo de desarrollo normal. Para editar código seguís usando
`dotnet run` como siempre; esto es para *ver* el sitio desde otra pantalla.

## Arranque

```bash
docker compose up --build
```

El primer arranque compila la solución y hace el install desatendido de
Umbraco (~1-2 min con el caché frío). Los siguientes son de segundos.

| Comando | Qué hace |
|---|---|
| `docker compose up --build` | Compila y arranca |
| `docker compose logs -f cms` | Sigue el boot |
| `docker compose down` | Para; los volúmenes sobreviven |
| `docker compose down -v` | Para **y borra** DB, media y App_Data |

## Lo único que hay que editar

En `docker-compose.yml`, reemplazá `192.168.1.50` por la IP LAN real de la
PC (`ipconfig` → "Dirección IPv4"), en las dos variables:

```yaml
Umbraco__CMS__Global__UmbracoApplicationUrl: "http://TU-IP:8080/"
Synergos__Notifications__PublicBaseUrl: "http://TU-IP:8080"
```

Umbraco usa ese valor para construir URLs absolutas. Si queda en
`localhost`, la tablet resuelve *su propio* localhost y los links del
backoffice y de las notificaciones no llevan a ningún lado.

Después, desde la tablet:

- Sitio: `http://TU-IP:8080`
- Backoffice: `http://TU-IP:8080/umbraco` (`admin@synergos.local` /
  `Synergos2026!`, igual que en Development)

Windows Firewall va a pedir permiso la primera vez que algo entre al 8080;
hay que aceptarlo **para redes privadas**.

## Primer boot: la base arranca vacía

El contenedor instala Umbraco pero **no importa el schema**. La DB nace sin
DocTypes, así que el sitio responde 200 pero no hay contenido.

El schema sigue siendo el XML de `uSync/v9/` (ADR 0008), y el import lo
corre el arquitecto a mano desde el backoffice — igual que en local, el
agente no toca la DB. El directorio va montado como bind read-write, así
que el `ExportOnSave=All` de uSync escribe los cambios de vuelta en el árbol
versionado en lugar de dejarlos atrapados en el contenedor.

## Dónde vive el estado

| Volumen | Ruta en el contenedor | Qué guarda |
|---|---|---|
| `synergos-db` | `/app/umbraco/Data` | SQLite (`Umbraco.sqlite.db`) |
| `synergos-appdata` | `/app/App_Data` | Los stores FileSystem\* (orders, comments, audit, 2FA…) |
| `synergos-media` | `/app/wwwroot/media` | Media subida desde el backoffice |
| `synergos-logs` | `/app/umbraco/Logs` | Logs de Umbraco |
| `synergos-dpkeys` | `/root/.aspnet/DataProtection-Keys` | Claves de DataProtection |
| *(bind)* | `/app/uSync/v9` | Schema — apunta al repo, no a un volumen |

Las claves de DataProtection tienen volumen propio a propósito: sin él,
cada `up --build` desloguea el backoffice e invalida los secretos TOTP de
los members (ADR 0084).

## CDN local (opcional)

Mientras `HttpBundleRegistryClient` siga bloqueado (ADR 0012), los 71
`elementSyn*` emiten un placeholder. Si tenés bundles en `C:\LOCAL_CDN`,
descomentá el bind en `docker-compose.yml`:

```yaml
- C:\LOCAL_CDN:/cdn:ro
```

`appsettings.Docker.json` ya apunta `LocalCdn` y `BundleRegistry` a `/cdn`.

## En qué se diferencia de tu `dotnet run`

| | PC (`Development`) | Contenedor (`Docker`) |
|---|---|---|
| Protocolo | HTTPS con cert de `C:\LOCAL_CDN` | HTTP en 8080 |
| Host | `synergos.local:5001` | `0.0.0.0:8080` |
| ModelsBuilder | `SourceCodeAuto` | `InMemoryAuto` |
| Maildrop SMTP | Carpeta del Desktop | `/app/App_Data/maildrop` |
| CDN local | `C:\LOCAL_CDN` | `/cdn` (si se monta) |

Las dos que importan:

**HTTP, no HTTPS.** El endpoint HTTPS de Development apunta a un cert en
`C:\LOCAL_CDN` que no existe en Linux, y su ausencia mata el bind al
arrancar. Para LAN doméstica HTTP alcanza. Si necesitás HTTPS —por ejemplo
para probar algo que dependa de secure cookies— lo más limpio es un
Cloudflare Tunnel o `tailscale serve` por delante del contenedor, que dan
cert válido sin tener que hacérselo confiar a la tablet.

**`InMemoryAuto` en vez de `SourceCodeAuto`.** Las vistas están tipadas
contra `Synergos.CMS.Web.PublishedModels` (ej. `PostPage.cshtml`), y
`SourceCodeAuto` genera esos `.cs` para que los tome el *siguiente build* —
cosa que en una app ya publicada no vuelve a pasar. `InMemoryAuto` los
compila en runtime, que es lo que el contenedor necesita. Es además lo que
ya dice el comentario del `.csproj`.

## Si algo no anda

**`/_health` devuelve 503.** Esperado si no montaste el CDN: la probe
`bundle_registry` queda roja a propósito. El JSON lista qué probe falló. Por
eso el `HEALTHCHECK` del Dockerfile chequea *liveness* (que Kestrel
conteste), no el status code — si no, el contenedor viviría marcado
`unhealthy` sin que nada esté roto.

**La tablet no llega.** En orden: que la PC y la tablet estén en la misma
red (no una en Wi-Fi de invitados), que el firewall haya permitido el 8080,
y que `curl http://TU-IP:8080` funcione desde la propia PC.

**El backoffice carga pero se ve raro en la tablet.** El backoffice de
Umbraco 13 es AngularJS y no está pensado para touch. Para *ver* el sitio
va perfecto; para *editar* desde tablet vas a pelear con la UI.
