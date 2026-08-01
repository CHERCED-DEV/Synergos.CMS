# ADR 0121 — La auditoría de acceso a PHI tiene su propia retención

- **Estado:** Aceptado
- **Fecha:** 2026-08-01
- **Corrige:** ADR 0098 (vertical Healthcare), ADR 0070 (retención de auditoría administrativa)
- **Complementa:** ADR 0120 (portal del paciente), ADR 0037 (rastro de auditoría)

## Contexto

El inventario funcional lo listaba como un caso de "documentación que se adelanta o se atrasa",
en la misma lista que un número de bloques desactualizado. Es bastante más que eso.

`HealthcareRetentionPolicy` decía, en su XML-doc:

> La auditoría de acceso PHI (`syn-audit`) NO se toca acá — retención **indefinida por
> obligación legal** (la gestiona otra policy).

La otra policy es `AuditRetentionPolicy`, y **borra esos archivos a los 90 días** —
`AdminSettings.AuditRetentionDays`, el mismo número que rige los logins de administración.

El camino completo: `DefaultPhiAccessGuard` audita **cada** acceso a PHI (concedido y negado,
fail-closed: si la auditoría falla, se deniega) escribiendo un `AuditEvent` con acción
`phi.access-granted` / `phi.access-denied`. `FileSystemAuditTrailWriter` lo persiste en
`App_Data/syn-audit/{fecha}.jsonl` — **el mismo archivo** que todo lo demás.
`AuditRetentionPolicy` borra archivos enteros por fecha. A los 90 días, el registro de quién
miró la historia clínica de quién desaparece.

Nada fallaba. El build estaba verde, los tests pasaban, la documentación prometía retención
indefinida, y el borrado era silencioso e irreversible.

## Decisión

**Dos familias de archivo, dos retenciones.**

1. `FileSystemAuditTrailWriter` enruta por el prefijo de la acción: los eventos `phi.*` van a
   `{fecha}.phi.jsonl`; el resto sigue en `{fecha}.jsonl`.
2. `AuditRetentionPolicy` **salta explícitamente** los `*.phi.jsonl`.
3. `HealthcareRetentionPolicy` pasa a purgarlos de verdad, según
   `HealthcareSettings.AccessAuditRetentionDays` — **default 0 = nunca**.
4. El XML-doc que mentía ahora describe lo que el código hace.

## Por qué así

### Por qué el discriminador es el prefijo `phi.` y no un campo nuevo

`AuditEvent` es un record compartido por todos los dominios (tienda, gobierno, eventos, PHI).
Añadirle una `Category` obligaría a tocar cada llamador y a decidir la categoría de eventos que
hoy nadie clasifica. El prefijo `phi.` **ya existe** y lo escribe el guard clínico: es el único
que lo produce, y es el único que necesita retención distinta. Cuando un segundo dominio pida
lo mismo, ahí habrá evidencia para generalizar; hoy sería inventar un eje de clasificación para
un solo caso.

### Por qué archivos separados y no filtrar líneas

La purga borra **archivos**, no líneas. Filtrar por evento obligaría a reescribir el archivo del
día dejando fuera los `phi.*` — es decir, **reescribir un log append-only**. Un rastro de
auditoría que se reescribe deja de ser un rastro de auditoría: el remedio sería peor que la
enfermedad. Separar el archivo conserva la inmutabilidad de los dos.

### Por qué el salto en `AuditRetentionPolicy` es explícito, aunque ya funcionara solo

`AuditRetentionPolicy` parsea el nombre del archivo como `yyyy-MM-dd`. Para
`2026-08-01.phi.jsonl`, `Path.GetFileNameWithoutExtension` da `2026-08-01.phi`, que no parsea, y
el archivo se salta. Es decir: la protección funcionaría **por accidente**.

Se añadió igual la línea explícita, porque una protección accidental es una trampa. El día que
alguien "arregle" ese parseo para tolerar sufijos —una tarde, con buena intención— empezaría a
borrar auditoría clínica y **nada fallaría**. Que es exactamente la forma del defecto que este
ADR corrige.

### Por qué el default es "nunca purgar"

Es lo contrario del criterio del resto del repo, donde toda retención tiene un número. Aquí:

- **Borrar es irreversible** y lo borrado es justo el registro que se pide cuando algo salió
  mal, a veces años después.
- **El coste de guardar es una línea JSON por acceso.** No es una base de datos: es un archivo
  de texto por día.
- **El número correcto no lo sabe el sistema.** Depende de la obligación legal del operador y
  de la jurisdicción. Elegir 2190 días "porque coincide con la historia clínica" habría sido
  inventar una política de cumplimiento; dejarlo en 90 —lo que hacía— era peor todavía.

Quien tenga un número, lo pone en `Synergos:Healthcare:AccessAuditRetentionDays`. El sistema no
lo elige por él.

### Por qué la purga clínica usa la fecha del NOMBRE y no la última escritura

`HealthcareRetentionPolicy` purga los registros PHI por `LastWriteTimeUtc`, que es correcto para
un expediente: se toca cuando se edita. Para el archivo de auditoría del día no sirve: **se
reescribe cada vez que alguien entra a un expediente**. Con la marca del sistema de archivos, la
retención se reiniciaría en cada acceso y el registro más consultado sería el último en
borrarse — exactamente al revés de lo que se quiere. La fecha sale del nombre, como en
`AuditRetentionPolicy`.

## Consecuencias

### Lo que se gana

- La auditoría de acceso a PHI deja de borrarse a los 90 días.
- La documentación y el código dicen lo mismo.
- El operador puede fijar su propia retención sin tocar la administrativa, y viceversa.

### Lo que se acepta

- **Los archivos ya escritos siguen mezclados.** Todo `{fecha}.jsonl` existente contiene
  eventos `phi.*` revueltos con los administrativos, y `AuditRetentionPolicy` los seguirá
  purgando a los 90 días. Separarlos requeriría reescribir logs append-only, que es
  precisamente lo que este ADR se niega a hacer. **La separación aplica de aquí en adelante**, y
  quien necesite conservar lo ya escrito debe copiar el directorio antes de la próxima barrida.
  Es la única acción manual que este cambio exige.
- **Dos archivos por día en vez de uno** cuando hay actividad clínica. El visor de
  administración no se entera: lista `*.jsonl` y los dos casan con ese patrón.
- `HealthcareRetentionPolicy` ahora barre dos directorios y su nombre se le queda corto. Se
  dejó como está: renombrarla mueve su registro en el composer y su nombre aparece en los logs
  de barrida, y el beneficio no justifica el ruido.
