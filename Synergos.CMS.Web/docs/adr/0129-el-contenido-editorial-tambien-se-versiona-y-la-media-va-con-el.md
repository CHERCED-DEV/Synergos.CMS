# ADR 0129 — El contenido editorial también se versiona, y la media va con él

- **Estado:** Aceptado
- **Fecha:** 2026-08-03
- **Extiende:** ADR 0128 (la DB es derivable)
- **Revierte parcialmente:** la regla de `.gitignore` sobre `uSync/v9/Content` y `Media`
  (memoria `feedback_schema_ownership_agent`)

## Contexto

El ADR 0128 declaró que la base de datos es derivable y le puso un gate. Al hacer el arqueo del
proyecto apareció que esa afirmación era **media verdad**, y la mitad que faltaba era justo la
que le preocupaba al arquitecto:

| | ¿versionado? | ¿lo cubre un gate? |
|---|---|---|
| Esquema (DocTypes, DataTypes, Dictionary, Templates) | sí, 885 XML | sí, ADR 0128 |
| **Contenido editorial** (páginas, propiedades autoradas) | **no** | no |
| **Nodos de Media** | **no** | no |
| **Binarios de media** (`wwwroot/media`) | **no**¹ | no |
| Datos de runtime (comentarios, órdenes, formularios, audit) | no, y está bien | — |

¹ 30 archivos figuraban trackeados, pero de antes de que se añadiera la regla. Git no
desindexa lo ya trackeado, así que **el hueco era invisible**: parecía versionado y ninguna
subida nueva se sumaba.

O sea: las páginas que el arquitecto autora vivían **solo en la SQLite**, que nunca se
commitea. La frase *"nunca cargarnos la base de datos y perder todo el trabajo"* seguía sin
cumplirse para la mitad del trabajo.

`uSync/v9/Content/` y `Media/` estaban en `.gitignore` por una decisión deliberada, con dos
razones escritas allí mismo:

1. **el agente no es dueño del contenido**, y
2. **el contenido del seeder no debe commitearse** — se regenera con `DevTestContentSeeder`.

Las dos eran buenas razones. Pero solo la segunda seguía en pie como *problema*: la primera
habla de **quién autora**, no de **qué se versiona**. Que el agente no cree contenido no
implica que el contenido del arquitecto no se guarde.

## Decisión

**Se versiona el contenido editorial y los binarios de media que lo respaldan.** En concreto:

1. `ContentHandler` y `MediaHandler` se habilitan en `appsettings.json`. Vienen apagados de
   fábrica en uSync 13; el `ExportOnSave` por defecto **ya exporta al guardar** —se verificó
   arrancando sin fijar la variable—, así que encender los handlers basta y el comportamiento
   del esquema no cambia.
2. `uSync/v9/Content/` y `Media/` salen de `.gitignore`.
3. **`wwwroot/media/` sale de `.gitignore`** — en los DOS ficheros que lo bloqueaban (el de la
   raíz y el que genera Umbraco dentro del proyecto web). Versionar el nodo de Media sin su
   binario restauraría el contenido **con las imágenes rotas**, que es peor que no restaurarlo:
   parece que funcionó. `umbraco/mediacache/` sí sigue ignorado, porque eso sí es derivable.
4. **La razón #2 pasa de `.gitignore` a un check del audit.** La regla de ignore resolvía el
   problema a martillazos: bloqueaba *todo* el contenido para no dejar pasar el del seeder. El
   check nuevo (`seeded-content`, el noveno de `usync-audit.mjs`) es el bisturí — deja pasar lo
   editorial y rechaza lo sembrado, por nombre de nodo.
5. **El gate de reconstrucción importa contenido.** Sin esto seguiría diciendo "derivable"
   saltándose la mitad, que es exactamente el fallo que este ADR corrige.

## Lo que se verificó, y cómo

No se dio nada por supuesto. En un sandbox con la carpeta de uSync apuntada a un temporal —el
repo intacto en todo momento—:

1. **Los handlers existen y funcionan.** `ContentHandler` y `MediaHandler` están en uSync
   13.3.2. **No hay `MemberHandler`**: los members no se pueden versionar en la edición libre.
2. **El export al guardar produce XML legible.** Al sembrar contenido apareció
   `Content/synergos-platform.config` con claves, ruta, tipo, estado de publicación por cultura,
   plantilla y propiedades. Es revisable en un diff, que es la mitad del punto.
3. **El round-trip cierra.** DB **nueva**, mismo XML: el import procesó 886 ítems y los dos
   nodos de contenido aparecieron en una base que nunca los tuvo — comprobado consultando la
   SQLite por el GUID exacto, no por lo que dijera el log.

## Consecuencias

**A favor.** El trabajo editorial deja de vivir en un único fichero binario sin diff ni merge.
Un PR muestra qué página cambió y en qué propiedad. Y la promesa del ADR 0128 pasa a ser
entera: repo limpio + import = entorno completo, contenido incluido.

**En contra, y hay que decirlo.**

- **El repo crece con los binarios de media.** Hoy son 5.5 MB. Es el precio de que las imágenes
  vuelvan; un LFS o un almacenamiento externo es la salida si algún día pesa.
- **Dos personas editando la misma página en dos DB distintas producen un conflicto de XML.**
  Es un conflicto *visible* y resoluble — al revés que el modelo anterior, donde el segundo
  simplemente perdía el trabajo sin enterarse.
- **El contenido puede llevar datos personales.** Si un editor escribe un teléfono en una
  página, ese teléfono queda en el repo. Es la contrapartida de versionar contenido y hay que
  tenerlo presente antes de abrir el repo a más gente.
- **Los members siguen fuera.** uSync 13 free no los exporta. Un entorno reconstruido no trae
  cuentas: hay que volver a crearlas. Queda anotado, no resuelto.
- **Los dominios (hostname → siteRoot) siguen fuera** a propósito. `DomainHandler` existe, pero
  los hostnames son específicos del entorno —`synergos.local` no es el de producción— y
  versionarlos metería configuración local en el repo. Es una decisión reversible si algún día
  conviene lo contrario.

## Lo que falta, y es del arquitecto

**El primer export no lo puede hacer el agente**: el contenido vive en la DB de la máquina del
arquitecto, y este contenedor no la tiene. El procedimiento está en
[`docs/product/05-versionar-contenido.md`](../product/05-versionar-contenido.md).
