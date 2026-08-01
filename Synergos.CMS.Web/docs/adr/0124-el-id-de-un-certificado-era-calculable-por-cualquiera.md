# ADR 0124 — El id de un certificado era calculable por cualquiera

- **Estado:** Aceptado
- **Fecha:** 2026-08-01
- **Corrige:** ADR 0075 (Ola 5 Educación — el seam `ICertificateService` y su promesa de verificación pública)
- **Complementa:** ADR 0105 (`IJsonEntityStore`), ADR 0002 (Application sin Umbraco), ADR 0108 (identidad server-trusted en los controllers)
- **Precedente que se reusa (en forma, no en código):** T9 — `ITicketSigner` / `TicketSigningKeyProvider`, el QR de las entradas

## Contexto

El inventario funcional lo listaba como un enlace muerto: *"`ICertificateService.VerifyAsync`
— sin controller que lo exponga"*. Sonaba a trabajo de media hora: añadir el endpoint que
faltaba.

Lo que había detrás:

- `ICertificateService.VerifyAsync` existe, está registrado en DI y su XML-doc dice, textual,
  que es *"la cara pública de la credencial (QR → esta verificación)"*.
- `Certificate.VerifyUrl` se emite como `/academy/verify/{certId}` — en **dos** sitios que
  arman la misma cadena por separado (`StubCertificateService` y `StubEnrollmentService`).
- Ningún controller servía esa ruta. Un empleador que escaneara el QR de un diploma recibía
  un 404 del CMS.

Y debajo de eso, la razón por la que ese endpoint **no se podía añadir tal cual**. El id de
la credencial era:

```csharp
var certId = "cert-" + StableHash($"{courseId.Trim()}|{student.Trim()}");
// StableHash = FNV-1a 32-bit, enmascarado a 31 bits
```

Tres propiedades, cada una suficiente por sí sola:

1. **FNV-1a no es criptográfico** y se enmascara a 31 bits: ~2.100 millones de valores. Un
   espacio enumerable con un script y una tarde.
2. **No entra ningún secreto.** La entrada es `"{courseId}|{student}"`. El `courseId` es
   público —sale del catálogo, `GET /api/academy/courses`— y el `student` es el correo de la
   persona. Quien supiera las dos cosas calculaba el id del certificado ajeno en cinco líneas
   de código, sin tocar el servidor.
3. **`Certificate` lleva `StudentName`.** La verificación devuelve el nombre de una persona.

Juntas: exponer `VerifyAsync` sobre ese id no habría entregado una credencial verificable.
Habría entregado un **padrón consultable de quién estudió qué**, alimentable por fuerza bruta
o por adivinanza dirigida ("¿este empleado hizo el curso de compliance?"). El agujero llevaba
ahí desde la Ola 5; lo único que lo mantenía cerrado era que nadie había construido la puerta
que la propia documentación prometía.

Un cuarto defecto, más aburrido y con la misma forma: el índice de certificados emitidos
vivía en un `ConcurrentDictionary` del proceso. Un reinicio del CMS lo vaciaba. Es decir que
incluso con el endpoint puesto, un QR impreso en un diploma **dejaba de verificar en el primer
despliegue** — aunque el alumno siguiera al 100% y `GetAsync` volviera a emitir el mismo id.

Nada fallaba. El build estaba verde, los tests pasaban, y la documentación describía una
capacidad segura que el código no tenía.

## Decisión

**El id de un certificado se firma con HMAC antes de que exista una puerta pública, y el
padrón de emitidos se vuelve durable.**

1. **`ICertificateIdSigner`** (Interfaces) — deriva y comprueba el id.
   `HmacCertificateIdSigner` (Application, BCL puro) produce
   `cert-{32 hex}` = los primeros 128 bits de `HMAC-SHA256(llave, "certid.v1|curso|alumno")`,
   con curso y alumno normalizados (trim + minúscula invariante).
2. **La llave sale de `CertificateSigningKeyProvider`** (Web): el secreto configurado en
   `Synergos:Academy:CertificateSigningSecret` o, si no hay, una llave aleatoria de 256 bits
   generada una vez y guardada **cifrada** (`IDataProtector`) en `IJsonEntityStore`.
3. **El índice de emitidos es durable** — `IJsonEntityStore`, familia `certificates`
   (ADR 0105). Guarda el **sujeto** (curso + alumno) además del id.
4. **`GET /academy/verify/{certificateId}`** (y su gemelo `GET /api/academy/verify/{id}`) en
   `AcademyController`: anónimo, sin caché, con **una sola respuesta** para todo lo que no sea
   una credencial válida.
5. La verificación **recalcula** el id desde el sujeto guardado y lo compara en tiempo
   constante. El índice no es la autoridad; la llave sí.

## Por qué así

### Por qué no se reusó `ITicketSigner`, que resuelve el mismo problema

Es la pregunta correcta: el repo ya cerró esta grieta una vez, para el QR de las entradas
(T9), y la regla del proyecto es no implementar dos veces una capacidad transversal.

Se reusa **la forma** —HMAC-SHA256 del BCL, hex minúscula, `FixedTimeEquals`, llave persistida
cifrada, firmante que se niega a construirse sin llave— y **no el seam**, por dos razones que
no son de estilo:

- **El token de una entrada lleva su payload legible a propósito**
  (`SYN-TKT-{evento}-{ticket}-v{n}.{firma}`), y ahí eso es correcto: un operador de puerta
  quiere leer de qué evento habla un QR. Aquí el payload es `(curso, ALUMNO)`. Meter el
  identificador del titular dentro del id que se imprime en el diploma y viaja en cada
  verificación pública es exactamente lo que este ADR existe para evitar. El id de un
  certificado tiene que ser **opaco**; el de una entrada, no.
- **La llave del ticket sale de `Synergos:Events:TicketSigningSecret`.** Rotar el secreto de
  Eventos —algo que se hace tras un incidente de puerta, o por temporada— invalidaría de paso
  **todos los diplomas emitidos**. Una entrada vale una noche; un diploma se presenta años
  después. Atar los dos ciclos de vida sería un fallo esperando fecha.

Como efecto lateral hay ~40 líneas casi gemelas entre `CertificateSigningKeyProvider` y
`TicketSigningKeyProvider`. Se dejan a propósito: la regla del repo es extraer al **tercer**
caso, no al segundo (ADR 0105 se ganó extrayendo a la cuarta repetición), y generalizarlo hoy
obligaría a re-registrar el firmante de tickets en `SeamComposer`, mezclando un refactor con
esta corrección. Cuando aparezca el tercer firmante, la evidencia está anotada en el propio
XML-doc del proveedor.

### Por qué el id es opaco y no un token auto-verificable

Un token que lleva su payload se verifica solo, sin consultar nada. Suena mejor. Pero aquí el
payload sería el correo del alumno, y la única alternativa —meter un hash del alumno dentro
del token— da un id el doble de largo para verificar exactamente lo mismo que ya verifica el
recálculo contra el índice.

El id opaco es un **capability token**: tenerlo es la autorización, igual que el
`trip_{Guid:N}` de Viajes, que `TravelControllerAuthTests` fija explícitamente como correcto.
128 bits no se adivinan ni se enumeran, y el valor no dice nada de nadie.

### Por qué la verificación recalcula el id en vez de creerle al índice

Porque si no, el fichero JSON sería un sello que se cree a sí mismo. Quien consiguiera escribir
en `App_Data/syn-certificates/` podría dejar caer un registro con el id que quisiera y el
nombre de quien quisiera, y la verificación pública lo confirmaría. Recalculando el id desde el
sujeto que el propio registro afirma, un registro fabricado no sobrevive: el id no cuadra con
la llave. Es un test (`Un_registro_FABRICADO_en_el_store_no_verifica`), no una intención.

### Por qué NO fail-closed cuando no hay secreto configurado

Eventos tomó la misma decisión y conviene decir por qué no es una excepción cómoda.

Fail-closed protege contra un secreto **adivinable** — el caso clásico es el default literal
commiteado en el repo, que es un secreto conocido y por eso peor que ninguno. Aquí no hay
ninguno que proteger: si no se configura nada, se generan 256 bits aleatorios y se guardan
cifrados. La propiedad que importa —*el id no se puede calcular sin la llave*— se cumple
íntegra. Negarse a emitir certificados en una instalación limpia no compraría seguridad:
apagaría la capacidad y, peor, empujaría a alguien a poner un secreto de ejemplo en
`appsettings.json` para desbloquearla.

Lo que un secreto configurado sí compra —y por eso es lo correcto en producción— es **poder
rotarlo** y **compartirlo entre instancias**. Nada de eso es secreto; es operación.

Donde sí es fail-closed, y estricto, es en el firmante: `HmacCertificateIdSigner` rechaza una
llave vacía, y **`StubCertificateService` no tiene ningún constructor sin firmante**. No existe
forma de cablear el seam de manera que emita ids derivables. La puerta "solo para tests" es
justo por donde estas cosas llegan a producción.

### Por qué "no encontrado" y "firma inválida" son la misma respuesta

Un verificador que puede distinguir *"ese id no existe"* de *"ese id existe pero ya no vale"*
tiene un oráculo sobre terceros. Por eso hay **una sola** respuesta de fallo, y es una constante
compartida en el controller (`NotAValidCredential`) en lugar de dos objetos que podrían
divergir con el tiempo — el día que alguien añada un `reason` "para depurar", que es
exactamente la forma del defecto que este ADR corrige.

Debajo, el seam ya colapsa los cuatro casos (id malformado, id desconocido, registro fabricado,
alumno que ya no está al 100%) en un único `null`: no hay dos ramas que puedan separarse.

Conviene ser claro sobre qué sostiene esto: **no es el 404 uniforme**. Con el id anterior, este
endpoint habría sido enumerable por muy uniforme que fuera su error. Lo que lo cierra es que el
id sea infalsificable; la uniformidad solo evita regalar información de más.

### Qué ve —y qué no— quien verifica

Ve lo que *verificar* significa: **qué curso, quién lo completó y cuándo**, más el título del
curso. Mostrar el nombre del titular no es una fuga, es el punto: un empleador con el diploma
en la mano necesita comparar el nombre impreso con el que responde el emisor. Una verificación
que no dijera de quién es no verificaría nada.

No ve el **identificador de la cuenta del alumno**. Esto importa porque
`ResolveStudentName` cae al propio `student` (el correo) cuando no hay matrícula con nombre:
para esos alumnos, publicar `StudentName` tal cual sería publicar su correo en un endpoint
anónimo. El controller publica el nombre **solo si es un nombre**; si lo único disponible es el
identificador, responde `studentName: null` y la credencial sigue siendo válida. Es honesto —el
sistema no atestigua un nombre que no tiene— y no filtra.

Tampoco ve el progreso, la matrícula, la orden, el pago, ni nada que permita ir del certificado
a los **otros** cursos del titular. `PublicCertificateDto` es un record aparte de
`CertificateDto` a propósito, y no una proyección "casi igual": así, añadir mañana un campo al
certificado privado no lo publica de paso.

### Por qué no se usa `ListAsync`

`IJsonEntityStore.ListAsync` devuelve los **documentos**, no las claves. Pasarle uno a
`ReadAsync` como si fuera clave devuelve `null` para siempre y en silencio. Aquí no hace falta:
el id **es** la clave, y ninguna operación de este seam necesita recorrer el padrón de emitidos
— lo cual, además, es lo correcto: un seam que no puede listar credenciales es un seam que no
puede filtrarlas por accidente.

### Por qué no se añadió rate limiting al endpoint

Se consideró (existe `InMemoryFormRateLimiter`). Con 128 bits de id no hay nada que limitar:
enumerar es inviable por varios órdenes de magnitud, y un límite por IP daría una falsa
sensación de que **eso** es lo que protege la credencial. Lo que la protege es la llave. Si
algún día se añade, será por coste de infraestructura, no por seguridad.

## Consecuencias

### Lo que se gana

- La verificación pública que la documentación prometía desde la Ola 5 **existe**, y es
  anónima: `GET /academy/verify/{id}` y `GET /api/academy/verify/{id}`.
- El id de una credencial ya no se puede calcular sin la llave del servidor.
- Un QR impreso sigue verificando tras un reinicio del CMS.
- El almacén dejó de ser la autoridad: fabricar un fichero de certificado no sirve de nada.
- La emisión es idempotente **de verdad**: re-emitir devuelve el registro guardado, fecha
  incluida. Antes el id era estable pero `IssuedAt` se recalculaba en cada llamada, así que la
  fecha que devolvía la API podía no ser la impresa en el diploma.

### Lo que se acepta

- **TODOS los certificados emitidos bajo el esquema anterior dejan de ser válidos, y no hay
  migración posible.** No es una pérdida de datos: el índice viejo vivía en memoria y ya se
  perdía en cada reinicio, así que en la práctica no hay nada que migrar. Pero cualquier id
  `cert-xxxxxxxx` (8 caracteres) que un alumno tenga impreso o guardado **no verificará** — ni
  siquiera pasa el filtro de forma. **Lo que el operador debe hacer:** re-emitir. Los alumnos
  que sigan al 100% obtienen su credencial nueva llamando a `GET /api/academy/certificate`, y
  el id nuevo es igual de estable. Los ids viejos no deben "tolerarse por compatibilidad":
  tolerarlos sería conservar exactamente el agujero que este ADR cierra.
- **Rotar `Synergos:Academy:CertificateSigningSecret` invalida todos los certificados
  emitidos**, por construcción. Es el precio de que el id sea una función de la llave. Si hace
  falta rotar sin invalidar, el cambio es introducir un id versionado con verificación contra
  N llaves — trabajo real, y hoy no hay nadie pidiéndolo.
- **`IEnrollmentService.GetCertificateAsync` sigue derivando su propio id sin firma** (el
  segundo sitio que armaba `/academy/verify/{...}`). No se tocó: no lo llama ningún controller,
  solo tests. `StubCertificateService` ahora lo consume para saber si el alumno terminó y con
  qué nombre, y **descarta su id** — desde este ADR el id de una credencial tiene exactamente
  una fuente, el firmante. El riesgo residual es acotado y del lado seguro: si alguien expusiera
  ese método, su id no pasaría la verificación (falla cerrado, no abierto). Unificarlo es una
  limpieza pendiente, no un agujero.
- **Dos proveedores de llave casi idénticos** (tickets y certificados). Deuda anotada, con el
  criterio para saldarla escrito en el código.
- El endpoint responde JSON, no una página. Un QR escaneado desde el móvil enseña JSON crudo.
  Servir una vista de verificación es trabajo de UI y va aparte; la superficie de datos ya está
  y es lo que hacía falta para que el enlace deje de estar muerto.
