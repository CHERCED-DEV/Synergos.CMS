# ADR 0110 — El QR de la entrada se FIRMA y la puerta lo VERIFICA (T9, Eventos)

- **Status:** Accepted
- **Date:** 2026-07-18
- **Deciders:** Arquitecto + agente, en modo autónomo (el arquitecto pidió olas completas sin consultar cada decisión). Investigación previa por agente explorador sobre el estado real de T9. Verificado en vivo contra el CMS corriendo, **reiniciándolo**, que es lo que destapó el bug del agente descrito abajo.
- **Relacionados:** ADR 0104 (`WebhookSigner`/`PaymentWebhookVerifier` — la convención HMAC del proyecto, que este ADR reusa **sin** colapsarla), ADR 0109 (`IPrivateFileStore` — se intentó reusar para la llave y **no encajó**; la razón está en Consequences), ADR 0105 (`IJsonEntityStore` — donde sí vive la llave), ADR 0103 (identidad server-trusted: el molde de `RequireMember` que Eventos no había adoptado), ADR 0002 (Application sin AspNetCore — por eso el firmante es BCL puro), ADR 0013 (cero I/O en boot: la llave se resuelve perezosamente), ADR 0075 (tests por seam).

---

## Context

El doc 25 describía T9 como *"QR/tokens firmados (HMAC/JWT) + wallet | string sin firma"*. **Se quedaba corto en lo que importa.** La investigación encontró tres cosas peores que "sin firma":

1. **El check-in nunca miraba el QR.** `MarkCheckedInAsync` comparaba el input contra el `ticketId`. Escanear el código contra el backend real devolvía `invalid`; **lo único que funcionaba era teclear el id**. El QR era decorativo.
2. **La UI imprime ese id en claro bajo el propio QR** (`caption`). Sumado a lo anterior: **una foto de la entrada ajena bastaba para entrar en su lugar**, sin decodificar nada.
3. **El token ya estaba roto y nadie lo notó.** El sufijo salía de `String.GetHashCode()`, que en .NET Core está **randomizado por proceso**: el mismo ticket producía un QR distinto tras cada reinicio. No se notó precisamente porque nada lo verificaba.

Y un cuarto hecho, ortogonal pero **acoplado**: los 9 endpoints de `EventosController` no tenían ninguna guarda. En particular `GET /tickets?holder=<email>` listaba las entradas de cualquiera **con su QR dentro**. Firmar el token sin cerrar eso no habría servido de nada: se pide la entrada ajena y se entra con ella.

## Decision

### 1. `ITicketSigner` — firmar el token, con la convención que el proyecto ya tiene

Seam nuevo en `Interfaces`, implementado en `Application` con `System.Security.Cryptography` (BCL puro: no viola ADR 0002). Formato:

```
SYN-TKT-{eventId}-{ticketId}-v{qrVersion}.{hmac-sha256-hex}
```

El payload se deja **legible a propósito** — un operador puede ver de qué evento y ticket habla un QR sin descifrar nada, y eso no debilita nada: el secreto no está ahí. Lo que no se puede es **fabricarlo**.

Se reusa la convención de `PaymentWebhookVerifier`: HMAC-SHA256, hex minúscula, y comparación con `CryptographicOperations.FixedTimeEquals` (nunca `==` de string).

**Por qué NO se colapsó con `WebhookSigner`**, aun siendo el segundo esquema de firma del proyecto: aquel firma `"{timestamp}.{body}"` con ventana de replay de ±5 minutos, porque un webhook que llega tarde es sospechoso. **Una entrada es lo contrario**: se compra semanas antes, se imprime, y debe seguir valiendo el día del evento. Meterle un timestamp la invalidaría sola. Comparten el primitivo del BCL y el formato; **no la política**, y forzar una abstracción común distorsionaría ambas.

### 2. La puerta VERIFICA — el cambio de comportamiento real de T9

`MarkCheckedInAsync` ahora **solo admite un token con firma válida**. Se rechaza:

- el `ticketId` suelto (lo que la UI imprime bajo el código);
- un token con payload manipulado;
- un token de una **`QrVersion` anterior** — al transferir la entrada el QR rota, y el del dueño anterior debe morir (anti-reventa). Esto el código ya lo prometía en un comentario y **nadie lo comprobaba**; ahora hay un test que lo ejerce.

Se conserva intacto lo que ya funcionaba: `CheckedIn` persistido y el tri-estado `valid` / `already-used` / `invalid`.

### 3. La llave: config, o generada UNA vez y persistida cifrada

`Synergos:Events:TicketSigningSecret` si está poblado (vía de producción: permite rotar y compartir entre instancias). Si no, se genera una llave aleatoria de 256 bits **una sola vez**, se cifra con `IDataProtector` y se guarda en `IJsonEntityStore` bajo la clave `ticket-signing-v1`.

**No se pone un default en el repo**: un secreto commiteado es un secreto conocido, y firmar con él daría tokens falsificables **con apariencia de seguros**. Y **no se genera en memoria**: eso repetiría exactamente el bug del `GetHashCode`.

### 4. Cerrar la fuga del token y las rutas que queman o roban entradas

Parte de T9, no un extra: sin esto la firma es decorativa.

- `GET /tickets` — **el `?holder=<email>` desaparece**. La bandeja es la del member de la sesión, con el correo server-trusted del gate.
- `POST /checkin` — exige sesión. Quemar una entrada es irreversible para su dueño; que no lo haga un anónimo de internet.
- `POST /ticket/{id}/transfer` — exige sesión **y ownership**: sin esto, conocer el id bastaba para quitarle la entrada a alguien (y de paso rotarle el QR, dejándolo fuera del evento). `StatusCode(403)`, no `Forbid()` (con auth de members `Forbid()` redirige al login).

El **rol de organizador** para el check-in queda para T2-Eventos: habría exigido sembrar un member group nuevo y el ataque práctico ya está cerrado (sin el token no se puede quemar nada, y el token ya no se puede pedir).

## Consequences

**Positivas:**

- **El QR pasa de decorativo a credencial.** Antes escanear devolvía `invalid` y el id impreso abría la puerta; ahora es al revés.
- **Una foto de la entrada ajena ya no sirve** — el `caption` con el id dejó de ser una llave.
- **El QR sobrevive un reinicio**, verificado en vivo: emitir → reiniciar el CMS → el mismo token, byte a byte. Es lo que el generador anterior no cumplía.
- **La reventa del QR viejo tras una transferencia queda cerrada** y, por fin, comprobada.
- **El token no se puede pedir**: cerrada la fuga de `?holder=`.

**Negativas o trade-offs:**

- **Cambio incompatible de comportamiento**: cualquier flujo que hiciera check-in con el `ticketId` deja de funcionar. Es el objetivo, pero **rompe la entrada manual del operador**, que ahora debe escanear o pegar el token completo. Un "override manual" auditado para el organizador es trabajo futuro, deliberadamente no improvisado aquí.
- **Sin rotación de llave**: cambiar `TicketSigningSecret` invalida **todas** las entradas emitidas. Hace falta un esquema de key-id en el token para rotar sin romper. **Criterio de reapertura:** cuando haya que rotar en caliente o correr más de una instancia con llaves distintas.
- **Si la llave guardada no se puede descifrar** (keyring de Data Protection rotado), se genera otra y las entradas vivas mueren. Se registra con `LogError` explicando la causa, para que no se descubra en la puerta del evento.
- **El resto de `EventosController` sigue sin auth** (`checkout`, `confirm`, `manage/{eventId}`, `event`): la consola del organizador sigue siendo anónima. Es T2-Eventos y está anotado, no resuelto.
- **Wallet nativo (Apple/Google, `.pkpass`) sigue sin existir.** En este proyecto "wallet" significa la vista "mis entradas"; el rótulo del doc 25 sugiere otra cosa.

**Notas de implementación:**

- **El bug que la verificación en vivo destapó, y los tests no.** La primera versión guardaba la llave en `IPrivateFileStore` (ADR 0109) — parecía el sitio natural: cifrado, fuera de `wwwroot`. Pero ese almacén **genera su propio id opaco y descarta el nombre del llamador**, que es justo lo que lo hace seguro para documentos subidos y lo que lo inhabilita para un singleton que hay que recuperar por nombre. La llave quedaba bajo un GUID, al reiniciar no se encontraba, se generaba otra: **el bug de T9 reintroducido por el arreglo de T9**. Los tests no lo veían porque ninguno reiniciaba el proceso. Se corrigió a `IJsonEntityStore` (que sí acepta la clave del llamador) y **se escribió el test que faltaba**: dos proveedores distintos sobre el mismo store devuelven la misma llave.
- Los tests que fallaron al cambiar el comportamiento **codificaban el contrato viejo** (check-in por id). Se actualizaron al nuevo y se **reforzaron**: el que decía "QR viejo invalidado" en un comentario ahora lo comprueba.
- El mock del cliente Angular se alineó al backend (solo QR): antes aceptaba id o QR, y un demo más permisivo que producción enseña lo contrario de lo que hace el servidor.
- Tests: 12 del firmante, 6 del proveedor de llave, más los de ticketing/management actualizados. Verificados por mutación (reabrir el agujero hace fallar los tests que lo cubren).

## Alternatives considered

- **JWT.** Rechazado: arrastra dependencia y ~3× el tamaño en el QR para un payload de tres campos, sin ganar nada — no hay terceros que validen ni necesidad de claims estándar. El encoder de QR del proyecto además topa en ~270 bytes.
- **Reusar `WebhookSigner` tal cual.** Rechazado por la ventana de replay: invalidaría la entrada por el simple paso del tiempo. Ver §1.
- **Firmar con `IDataProtector` en vez de HMAC.** Rechazado: viviría en Web y el generador del QR está en Application (ADR 0002); además produce cadenas mucho más largas y no es interoperable con un validador externo.
- **Guardar la llave en `IPrivateFileStore`.** Se intentó y falló por diseño (id opaco). Ver Consequences.
- **Aceptar también el `ticketId` como fallback manual.** Rechazado: sería dejar abierto exactamente el agujero que T9 cierra, y el patrón "la firma existe pero algo la puentea" es el que este proyecto ya se encontró en T6 y T2.
- **Meter el rol de organizador en el check-in ahora.** Diferido a T2-Eventos: exige sembrar un member group y el ataque práctico ya queda cerrado por la firma + el cierre de la fuga.

## References

- `Synergos.CMS.Interfaces/ITicketSigner.cs` — el seam y por qué firmar sin verificar no sirve.
- `Synergos.CMS.Application/Services/Impl/HmacTicketSigner.cs` — formato, parseo y por qué no se reusa `WebhookSigner`.
- `Synergos.CMS.Web/Services/TicketSigningKeyProvider.cs` — de dónde sale la llave y por qué no del almacén de ADR 0109.
- `Synergos.CMS.Application/Services/Impl/StubEventTicketingService.cs` — `MarkCheckedInAsync` (la puerta) y `BuildQr`.
- `Synergos.CMS.Web/Controllers/EventosController.cs` — `RequireMemberEmail` y las tres rutas cerradas.
- ADR 0104 (convención HMAC), ADR 0109 (almacén privado), ADR 0103 (identidad server-trusted).
