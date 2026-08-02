# ADR 0128 — La base de datos es derivable: un gate reconstruye el entorno desde el XML

- **Estado:** Aceptado
- **Fecha:** 2026-08-02
- **Complementa:** ADR 0008 (schema via uSync), ADR 0013 (seeders prohibidos), ADR 0115 (gates
  que fallan cerrados)

## Contexto

El arquitecto lo formuló como un miedo, y el miedo era legítimo:

> *"tenemos que actualizarlo, commitearlo, persistirlo y darle una estrategia de release para
> nunca cargarnos la base de datos y perder todo el trabajo… acoplar múltiples grupos de
> trabajo… que haya una trazabilidad… donde no se dañe la base de datos ni nada de lo que
> estamos creando."*

La primera respuesta instintiva —versionar la SQLite— se descartó por escrito en la misma
conversación: binaria (el repo crece sin poda), sin diff (un PR no muestra qué cambió), sin
merge (dos personas tocando contenido = una pierde el trabajo **en silencio**, exactamente el
accidente que se quería evitar), y con hashes de contraseñas y PHI adentro.

Mientras tanto, el import real era un acto único sin compuerta: 836 cambios de un golpe,
verificados solo después de aplicados. Y en la primera reconstrucción completa que se hizo en
un contenedor limpio aparecieron dos datos: que **funciona** (880 ítems, 0 errores, 74s), y que
tiene un efecto colateral —el import **reescribe plantillas Razor en disco** (BOM incluido),
ensuciando el working tree sin aviso.

## Decisión

**La protección no es respaldar la base: es poder regenerarla.** Se declara como propiedad del
repo y se vigila con un gate:

> Una DB **vacía** + el XML de `uSync/v9/` reproducen el entorno completo, siempre.

Mientras ese gate esté verde, la DB es **derivable**: perder trabajo exigiría perder el repo.

### El gate: `tools/usync-rebuild-check.mjs` + `usync-rebuild.yml`

En cada PR que toque `uSync/`, CI compila la aplicación real, la arranca contra una SQLite
temporal, deja que uSync importe todo el árbol en el arranque, y evalúa tres cosas en orden de
severidad:

1. **El import termina** (resumen presente; un proceso que muere antes es fallo).
2. **Cero líneas `[ERR]`** — un import que "termina" con errores adentro es el peor resultado:
   se ve verde y dejó huecos.
3. **`processed >= archivos .config trackeados`.** No es heurística: la medición de referencia
   es exacta (880 archivos = 880 ítems). Si uSync se salta una carpeta entera —el modo de falla
   que de verdad importa— este número se desploma.

El camino negativo está probado, no supuesto: un XML corrupto dispara **las dos** alambradas
(línea ERR **y** 879 < 880) y el gate sale con código 1.

### El entorno del gate es deliberadamente distinto al del arquitecto

| Ajuste | Valor en el gate | Por qué |
|---|---|---|
| ConnectionStrings | SQLite en temp | El gate **jamás** toca una DB real. Correrlo localmente es seguro por construcción, no por disciplina |
| `ExportOnSave` | `None` | El gate lee el repo, nunca lo reescribe. Un gate que muta lo que verifica no es un gate |
| `FailOnMissingParent` | `true` (solo aquí) | Estricto en CI sin cambiar el flujo manual: un padre ausente revienta fuerte en vez de crear huérfanos callados |
| `DevSeed` | apagado | La reconstrucción demuestra que el **repo** reproduce el entorno; un seeder maquillaría el resultado |
| Temp storage | `EnvironmentTemp` + TMPDIR propio | Dos Umbraco en la misma máquina no se pelean por los índices Examine |
| Puerto | `:0` | El gate no hace ni un request HTTP; nunca colisiona con un CMS corriendo |

### Dos matices del veredicto que costaron una iteración cada uno

- **Los ERR posteriores al apagado no cuentan.** El SIGTERM del gate cancela los hosted
  services de Umbraco y eso emite `OperationCanceledException` como `[ERR]` — *a veces*, según
  el timing. Contarlos haría fallar una reconstrucción perfecta al azar, y un gate
  intermitente es peor que no tener gate. Tras el veredicto, el gate deja de escuchar (el log
  completo los conserva).
- **Los archivos que el import ensucia se AVISAN, nunca se restauran.** El import reescribe
  `.cshtml` trackeados (BOM). Restaurar automáticamente pisaría ediciones locales sin
  commitear; el gate lista los archivos y deja la decisión a la persona.

## Por qué así

### Por qué no versionar la SQLite

Está en el contexto. La versión corta: git no sabe mergear binarios, así que el mecanismo
pensado para no perder trabajo sería el que lo pierde. Y una DB con hashes de contraseñas y
auditoría PHI no entra a un repo.

### Por qué el gate arranca la aplicación real en vez de validar más XML

`usync-audit.mjs` ya valida la forma (GUIDs, refs, iconos, mojibake). Lo que ningún análisis
estático puede decir es si **Umbraco acepta** el árbol: orden de dependencias, compositions,
serialización de cada editor. La única prueba de eso es el import mismo — así que el gate lo
corre, contra una DB que no le importa a nadie.

### Por qué esto habilita múltiples equipos (y qué queda para después)

El mecanismo de coordinación entre equipos **es git, no uSync**: uSync serializa, y el
conflicto lo resuelve la revisión del diff — dos equipos creando ítems nuevos nunca chocan
(GUIDs), y dos editando el mismo ítem lo atrapa el PR. Este gate es la pieza que faltaba para
que esa coordinación sea segura: ningún merge llega a `main` sin haber demostrado que el árbol
completo sigue aplicando desde cero.

Capas siguientes, decididas pero **no construidas aquí**: handler sets por vertical (importar
"solo Eventos", radio de daño acotado), `CODEOWNERS` sobre las carpetas de uSync, y un recibo
de import versionado (set, conteos, hash, fecha) en vez de commitear `uSync.History`.

**Descartado con intención:** `IsRootSite` / `LockRoot` (base compartida con sitios hijos).
Choca de frente con el principio 8 —un deploy = un origen— y meterlo por comodidad de equipos
sería reintroducir la topología multi-tenant que este producto decidió no tener.

## Consecuencias

### Lo que se gana

- **El import manual deja de ser un acto de fe.** Cada PR ya demostró que el árbol aplica
  limpio sobre una DB vacía; el arquitecto importa sabiendo el resultado.
- **La DB deja de ser preciosa.** El backup pasa de "única línea de defensa" a comodidad.
- La medición queda fijada: 880 ítems / 836 cambios / ~75-80s. Una regresión de completitud es
  un número que baja, no una sensación.
- El efecto colateral del import (reescritura de Views con BOM) quedó **documentado y
  vigilado**: el gate lo lista en cada corrida.

### Lo que se acepta

- **El gate cuesta ~3-5 minutos de CI** (build + boot + import) y solo corre cuando cambia
  `uSync/` — un cambio puramente C# no lo paga.
- **El contenido sigue fuera de la propiedad.** `uSync/v9/Content/` y `Media/` están
  gitignored por decisión previa del arquitecto (memoria `feedback_schema_ownership_agent`);
  mientras eso siga así, "el entorno completo" significa *schema + dictionary + templates*, no
  los nodos editoriales. Revertir esa exclusión es una decisión pendiente y separada.
- **`processed >= trackeados` es un piso, no una igualdad.** En la máquina del arquitecto hay
  Content/ local que infla `processed`; el invariante se definió contra archivos *trackeados*
  precisamente para que el gate dé lo mismo en cualquier máquina.
- La reescritura de Views por el import **no se corrige aquí**, solo se detecta. Sacar los
  Templates del import (ya viven en git como `.cshtml`) es candidato natural a siguiente paso.
