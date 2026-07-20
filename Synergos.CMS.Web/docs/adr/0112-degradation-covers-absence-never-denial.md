# ADR 0112 — La degradación a mock cubre la AUSENCIA, nunca la NEGACIÓN (y jamás devuelve un registro distinto al pedido)

- **Status:** Accepted
- **Date:** 2026-07-20
- **Deciders:** Arquitecto (carta de autonomía) + agente. Originado al cerrar el barrido IDOR del backend: al hacer que las rutas respondieran 401/403, la UI empezó a tragárselos. Ejecutado como ola autónoma con auditoría adversarial (un agente cierra, otro intenta refutar el cierre ejecutando el código, no leyéndolo).
- **Relacionados:** ADR 0107 (una promesa que nadie cumple se BORRA — es el principio que decide este caso), ADR 0108 (las superficies de demo no se gatean: se hacen indesplegables; su lista de "trabajo abierto" es lo que este barrido cierra), ADR 0103 (identidad server-trusted, el molde de `RequireMember`), ADR 0075 (tests por seam).

---

## Context

Los clientes Angular de los verticales nacieron **antes que su backend**. Para poder
construir la UI entera sin esperar, cada método del cliente lleva un `catch` que
degrada a datos sembrados y enciende una bandera `degraded` que pinta un banner
"datos de ejemplo". Fue una buena decisión: permitió tener el journey completo.

El barrido IDOR (T2, cinco verticales + Educación y Viajes) cambió el mundo bajo esos
`catch`. Las rutas que antes servían a cualquiera ahora responden **401** (anónimo) o
**403** (sin el rol / no es suyo). Y un `catch (error)` no distingue: se tragaba el
403 igual que un timeout.

El resultado era **peor que el bug original**. Antes de T2, pedir el expediente de
otro ciudadano devolvía el expediente de otro ciudadano: mal, pero honesto sobre lo
que estaba pasando. Después de T2 y antes de este ADR, el server negaba
correctamente, la UI se comía la negación, y pintaba **datos fabricados con cara de
propios**. El gate funcionaba y era invisible; la pantalla mentía.

Y había un defecto peor escondido dentro: el patrón

```ts
const seed = this.mockStore().get(id) ?? [...this.mockStore().values()][0];
```

Ese `?? [0]` no es degradación: es **fabricación**. Devuelve el PRIMER registro
sembrado rotulado con el id que pediste. Apareció **cinco veces** (gov ×3, blogs ×2,
realty ×1), y la peor no la encontró el que escribió el arreglo sino el auditor
adversarial, ejecutando:

- `gov.mockDecide` — si el POST de la decisión cae con 500, el funcionario pulsa
  Aprobar, la UI anuncia "El caso pasó a: Aprobada", **la decisión no se registra en
  ninguna parte**, y en pantalla queda el expediente de OTRO ciudadano —nombre,
  respuestas, documentos— como el que acaba de aprobar. Fingir una escritura que no
  ocurrió es peor que no escribir.
- `blogs.buildMockThread` — deep-link a un hilo, 404, y se abre **otra conversación**
  con mensajes que el usuario nunca tuvo, indistinguible de una suya.

## Decision

### La regla, en una línea

> **Se degrada por AUSENCIA (el backend todavía no está), nunca por NEGACIÓN (el
> backend dijo que no). Y nunca, bajo ninguna circunstancia, se devuelve un registro
> DISTINTO al que se pidió.**

Operativamente, tres reglas:

**1. El 401 y el 403 viajan tipados hasta la UI y se vuelven ESTADO, no caída.**
Cada app declara `<App>UnauthorizedError` / `<App>ForbiddenError` y guards que
discriminan por **`error.name`, nunca `instanceof`** — `instanceof` no cruza bundles
(la clase se duplica y el chequeo falla en silencio, en producción, sin que ningún
test de unidad lo vea). La UI los traduce a una señal (`citizenAccess`,
`agentAccess`, `sessionAccess`, `threadAccess`) con tres valores: `ok` / `anon` /
`forbidden`.

**2. El 401 y el 403 se pintan DISTINTO, porque son cosas distintas.** El 401 ofrece
iniciar sesión. El 403 **no ofrece login**: volver a entrar no cambia de quién es el
expediente ni te da el rol de agente. Ofrecer login ante un 403 es mandar al usuario
a hacer lo único que con seguridad no arregla nada.

**3. Al entrar en estado denegado se VACÍA lo que ya no se puede mostrar.** Y no solo
las señales de la UI: también **las cachés en memoria del cliente**. En realty había
`#savedSearches` y `#agentLeads` sobreviviendo a la pérdida de sesión, y un fallo de
red posterior —que sí degrada— las repintaba plegadas al mock. Los leads llevan
teléfono.

### El `?? [primero]` se borra en todas partes, sea o no de seguridad

Aunque la ficha pública de un post no tenga dueño y degradarla no le atribuya nada a
nadie, devolver **otro** post bajo el id pedido sigue siendo mentir sobre qué es esto
que estás viendo. Se distingue de degradar: degradar dice "esto es un ejemplo";
fabricar dice "esto es lo que pediste" y no lo es. Donde no hay semilla con ese id,
se propaga el error.

### Lo que se degrada, y por qué eso está bien

El catálogo, la vitrina, la ficha de producto, el buscador. **No tienen dueño**: un
ejemplo de un curso no es el curso de nadie. Además el banner "datos de ejemplo"
sigue vivo ahí, y esa es exactamente la promesa que sí se cumple.

## Consequences

**Positivas:**

- El gate deja de ser invisible. Antes, cerrar una ruta en el backend no cambiaba
  nada en pantalla — lo que hacía imposible *demostrar* el trabajo de seguridad, y
  fácil creer que no se había hecho.
- La UI ya no puede fabricar un registro bajo un id ajeno. Cinco sitios cerrados.
- El molde es replicable: `gov` es la referencia y las otras cuatro la calcan.

**Negativas o trade-offs, escritos y no escondidos:**

- **Las rutas del DUEÑO siguen degradando ante errores NO-auth (500, red).** Un 500
  en `gov.applications` todavía devuelve la carpeta sembrada, y `createApplication`
  todavía fabrica un radicado local — un número que no existe en ninguna agencia, que
  es justo lo que su propio comentario dice que hay que evitar. **Se firma como
  desviación acotada**, no se deja implícita: el banner "datos de ejemplo" SÍ se pinta
  en esos casos, así que la pantalla avisa, y acotarlo del todo es rehacer el modo
  demo de cinco apps. **Criterio de reapertura:** cuando el backend de un vertical sea
  el camino normal y no el opcional, ese vertical deja de degradar lo del dueño y
  muestra un error de verdad.
- Un usuario anónimo ve más paneles de "inicie sesión" y menos pantallas llenas. Es
  el precio de no mentir, y es el comportamiento correcto.

**Notas de implementación:**

- **La auditoría adversarial pagó su coste.** Los dos hallazgos bloqueantes los
  encontró el verificador, no el implementador, y los dos los confirmó **ejecutando
  el código con `fetch` stubbeado**, no leyéndolo. El informe del implementador decía
  "barrido este patrón en `application(id)` y `case(id)`" — eran tres.
- **`tsc --noEmit` no abre los templates `.html`.** Un cambio estructural de
  `@if/@else` compila "verde" y revienta en AOT. Verificar con
  `nx build <app> --configuration=production`.
- **Trampa de signals:** `set('')` + `set(msg)` seguidos no re-anuncian nada en una
  región `aria-live` — ambas escrituras colapsan en un render y el `''` nunca llega al
  DOM.
- **Trampa del eco de `hashchange`:** limpiar un aviso en `applyRoute` no funciona; el
  eco asíncrono del `navigate()` que dispara el propio rechazo re-entra y lo borra
  justo después de ponerlo. El aviso solo se limpia en entradas con intención.

## Alternatives considered

- **Quitar el modo mock del todo.** Rechazada: el backend de varios verticales aún no
  cubre todo el journey, y sin degradación la demo se cae a trozos. La degradación no
  es el defecto; tragarse la negación sí.
- **Un interceptor global que traduzca 401/403 una sola vez.** Atractivo y quizá
  correcto a futuro, pero hoy cada app es un bundle independiente con su propio
  cliente y no hay una capa compartida donde vivir (los guards ni siquiera pueden usar
  `instanceof` entre bundles). Se rechaza como *premature*: primero cinco
  implementaciones que funcionan, después la abstracción, si el patrón se sostiene.
- **Tratar el 403 como el 401 (mandar a login siempre).** Rechazada: manda al usuario
  a hacer lo único que no arregla su problema. En realty el auditor lo señaló como
  hallazgo menor precisamente porque el patrón correcto ya existía tres pantallas más
  allá.

## References

- Referencia canónica: `modules/gov/src/gov/gov-api.client.ts` (clases de error +
  guards por `name`) y `modules/gov/src/gov/gov.ts` (`citizenAccess` / `officerAccess`).
- Los cinco fabricadores: gov `application(id)` · gov `case(id)` · gov `mockDecide` ·
  blogs `buildMockThread` · blogs `post()`. Realty `mockDetail` queda anotado.
- Backend que provocó el cambio: barrido IDOR en `GovController`, `BlogsController`,
  `RealtyController`, `AcademyController`, `TravelController`, `EventosController`.
- Commits: CMS `7dac42d`, `1d4282e` · UI `2692549` (gov), `933632d` (realty).
