# ADR 0120 — El portal del paciente no estaba bloqueado por permiso, sino por direccionamiento

- **Estado:** Aceptado
- **Fecha:** 2026-08-01
- **Complementa:** ADR 0098 (vertical Healthcare, PHI cifrada y auditada), ADR 0037 (rastro de
  auditoría), ADR 0115 (el guard de auto-acceso)

## Contexto

El inventario funcional cerraba la ficha de Salud con *"**No hay portal del paciente**"* y
remitía al patrón §3.2: `HealthcareApiController` construía el `AccessCheckRequest` de forma
posicional, así que `TargetOwnerMemberKey` quedaba `null` siempre y la rama de auto-acceso de
`DefaultPhiAccessGuard` estaba muerta. Ningún paciente podía leer su propio expediente.

Eso ya se arregló: el guard resuelve el dueño por sí mismo y el auto-acceso funciona —
acotado a `read`, porque un paciente tiene derecho a **ver** lo suyo y el criterio clínico lo
firma quien lo emite.

Y el portal seguía sin existir.

La razón es más simple y bastante más difícil de ver que un guard mal llamado: **todos los
endpoints se direccionan por `patientKey`**, que es una clave clínica **deliberadamente
distinta** del `MemberKey` —así lo exige el RTBF: borrar un Member no puede dejar PHI
huérfana— y que **nadie le dice al paciente**. `GET /patients` exige rol de staff.
`GET /patients/{patientKey}` funciona… si adivinás un GUID.

El permiso estaba concedido y era inalcanzable.

## Decisión

Un endpoint, `GET /api/healthcare/me`, y el método de repositorio que lo hace posible:

```csharp
Task<Guid?> FindKeyByMemberAsync(Guid memberKey, CancellationToken cancellationToken);
```

Con eso el resto del portal ya funciona sin tocar nada más: conocida la clave, el auto-acceso
del guard cubre `patients/{key}`, `patients/{key}/prescriptions`, `patients/{key}/consent` y
`appointments?patientKey=`, porque la rama de auto-acceso es agnóstica del tipo de recurso.

### El orden de las tres operaciones es la decisión

```
1. ¿Hay sesión?          → si no, 401 SIN tocar el repositorio
2. Resolver la clave     → SOLO desde la sesión, nunca de un parámetro
3. Pasar por el guard    → aunque el expediente sea suyo
```

**(1) El corte del anónimo va antes de todo.** Es la propiedad que
`GetPatient_Anonymous_Returns401` ya fija para el resto del controller: no se consulta PHI
antes de autorizar. Aquí importa el doble, porque el paso siguiente lee del almacén clínico.

**(2) La clave sale de la sesión y de ningún otro sitio.** No hay parámetro donde escribir un
miembro ajeno, así que el endpoint no necesita defenderse de un IDOR: no existe la entrada.

**(3) Se pasa por el guard igual.** Es lo que parece redundante y no lo es: el guard no solo
decide, **audita**. Saltárselo porque "es su propio expediente" dejaría sin rastro
precisamente el acceso que más lo necesita, y bloquearía cualquier regla futura —una
suspensión, un menor de edad, un expediente en litigio— antes de poder escribirla.

### `FindKeyByMemberAsync` devuelve SOLO la clave

Nunca el expediente. Es lo que hace segura la llamada del paso 2, que ocurre **antes** de
autorizar: el llamador la usa para poder *nombrar* el recurso sobre el que va a pedir permiso,
y cualquier dato clínico sigue pasando por el guard. Una versión que devolviera el
`PatientRecord` convertiría este método en una puerta trasera al lado del guard, con el
agravante de que se ve inocente.

### Es un barrido, no un índice

La implementación recorre el espacio vigente comparando `MemberKey`. Un índice
`memberKey → patientKey` sería más rápido y sería **un segundo lugar donde vive el vínculo**;
de los dos, el archivo del paciente es el que manda. Un índice desincronizado le mostraría a
alguien el expediente de otro — la peor falla posible de este vertical, y silenciosa. El
barrido lee de la única fuente y no puede mentir. Si el volumen algún día lo exige, el índice
se añade **junto con su invalidador**, que es la regla que este repo ya aplica a las cachés.

## Por qué así

### Por qué no se amplió `PatientQuery` con un filtro de miembro

Era la alternativa obvia: `PatientQuery` ya tiene `DoctorKey`, y añadir `MemberKey` habría
sido aditivo. Se descartó porque `ListAsync` devuelve `PatientSummary`, que existe para
listados de staff y **no lleva el vínculo con el miembro**. Para que sirviera habría que
exponer el `MemberKey` en el resumen — es decir, filtrar el vínculo paciente↔miembro a todo
listado de staff — o devolver el registro completo, que es la puerta trasera que el punto
anterior descarta. Un método estrecho que devuelve un `Guid?` no tiene ninguno de los dos
problemas.

### Por qué 404 y no 200 con cuerpo vacío

Un miembro sin expediente recibe 404. No filtra nada: solo se puede preguntar por uno mismo,
así que no hay oráculo de existencia que explotar. Y un `200` con `null` obligaría a cada
cliente a distinguir "no tengo expediente" de "no tengo permiso" a partir del cuerpo, que es
exactamente la ambigüedad que los códigos de estado existen para evitar.

### Por qué dos expedientes vigentes no son un error fatal

Un miembro tiene como mucho uno. Si hay dos, es dato corrupto y no un caso de negocio — pero
la respuesta correcta no es tirarle un 500 al paciente: se sirve el más reciente. Negarle el
portal a alguien por una inconsistencia del almacén es peor que mostrarle su expediente más
nuevo, y la inconsistencia se arregla donde vive, no en la puerta.

## Consecuencias

### Lo que se gana

- El portal del paciente **existe**: un miembro autenticado llega a su expediente, sus citas,
  sus recetas y sus consentimientos, todo auditado.
- La última fila de "Todavía no" del inventario funcional se cierra sin tocar el guard, sin
  tocar el cifrado y sin añadir una sola ruta insegura.

### Lo que se acepta

- **El barrido es O(n) sobre los expedientes vigentes.** Es una lectura por sesión de portal,
  no por request, y el almacén es de un consultorio, no de un país. Cuando deje de serlo, ahí
  está la nota del índice.
- **Un miembro sigue sin poder ESCRIBIR nada suyo**, ni siquiera corregir su nombre. Es
  deliberado (el auto-acceso es solo `read`) y significa que "actualizar mis datos" necesita
  su propio flujo — probablemente una solicitud que un humano aprueba, no un `PUT`.
- **`IPatientRepository` gana un método**, así que cualquier implementación futura tiene que
  poder resolver el vínculo. Es el precio de que el vínculo sea una capacidad del repositorio
  y no un índice suelto.
