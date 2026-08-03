# Versionar el contenido editorial — el primer export

> Procedimiento para el **arquitecto**. El agente no puede hacerlo: el contenido vive en la
> SQLite de tu máquina y el contenedor no la tiene. Contexto y razones en el
> [ADR 0129](../adr/0129-el-contenido-editorial-tambien-se-versiona-y-la-media-va-con-el.md).

## Qué cambió en el repo

| | antes | ahora |
|---|---|---|
| `ContentHandler` / `MediaHandler` | apagados (default de uSync) | encendidos en `appsettings.json` |
| `uSync/v9/Content/` y `Media/` | en `.gitignore` | versionados |
| `wwwroot/media/` | en `.gitignore` (dos reglas) | versionado |
| Contenido del seeder | lo bloqueaba el `.gitignore` | lo rechaza el check `seeded-content` del audit |
| Gate de reconstrucción | solo esquema | esquema **+ contenido + nodos de media** |

**No tienes que exportar a mano.** El `ExportOnSave` por defecto de uSync ya escribe el XML
cuando guardas; lo que faltaba era que los handlers estuvieran encendidos. A partir del
próximo `git pull`, cada vez que guardes una página aparecerá su `.config` en el working tree.

## El primer export

La primera vez sí conviene forzarlo, porque el contenido que ya existe en tu DB nunca se
exportó.

1. **Respaldo primero.** El export escribe en el working tree, no en la DB — pero es la primera
   vez y el respaldo es barato:
   `C:\Users\HITMA\Desktop\synergos-backups\`.
2. Arranca el CMS y entra al backoffice.
3. En la sección de uSync, lanza un **Export** completo. Debe aparecer
   `Synergos.CMS.Web/uSync/v9/Content/` y, si usas la biblioteca de media, `Media/`.
4. **Revisa el diff antes de commitear.** Es el paso que sustituye a la vieja regla de
   `.gitignore`:
   ```
   git status --short Synergos.CMS.Web/uSync/v9/Content Synergos.CMS.Web/uSync/v9/Media
   node tools/usync-audit.mjs
   ```
   Si el audit falla con `seeded-content`, es contenido de `DevTestContentSeeder`: bórralo del
   working tree y vuelve a mirar. No es trabajo tuyo, se regenera.
5. **Mira si hay datos personales.** Un teléfono o un correo escritos en una página quedan en
   el repo para siempre. Si aparecen, decide antes de commitear — es la contrapartida real de
   versionar contenido.
6. Commitea contenido y media **en un commit aparte** del código. El diff es grande y mezclarlo
   con un cambio de lógica hace ilegibles los dos.
7. Verifica que la promesa se cumple:
   ```
   node tools/usync-rebuild-check.mjs
   ```
   Si pasa, el repo reproduce el entorno **completo** — esquema y contenido.

## Lo que sigue sin versionarse, y por qué

- **Members.** uSync 13 free no trae `MemberHandler`. Un entorno reconstruido no trae cuentas;
  hay que volver a crearlas. No hay salida dentro de la edición actual.
- **Datos de runtime** — comentarios, órdenes, formularios, audit trail (`App_Data/`). Son
  operacionales, no trabajo autorado: quieren **respaldo**, no control de versiones. Meterlos
  en git haría un repo que crece sin parar y con datos de personas dentro.
- **Dominios** (hostname → siteRoot). `DomainHandler` existe, pero `synergos.local` no es el
  hostname de producción y versionarlo metería tu configuración local en el repo. Si algún día
  conviene lo contrario, es una línea en `appsettings.json`.
- **`umbraco/mediacache/`.** Derivable: Umbraco lo regenera del original.

## Si algún día trabajan dos personas a la vez

Dos ediciones de la misma página en dos bases distintas dan un **conflicto de git en el XML**.
Es resoluble y, sobre todo, es *visible* — en el modelo anterior el segundo en importar
simplemente perdía el trabajo sin enterarse. La convención sana, mientras sean pocos: que una
sola persona autora un árbol de contenido a la vez, igual que con el esquema.
