# ADR 0111 — Realtime por SSE (no SignalR) con autorización POR CANAL (T7)

- **Status:** Accepted
- **Date:** 2026-07-18
- **Deciders:** Arquitecto + agente, en modo autónomo (el arquitecto pidió olas completas sin consultar cada decisión). **La desviación del rótulo del doc 25 —"Realtime (SignalR)"— la decidió el agente** con los criterios del proyecto, igual que la desviación de Examine en ADR 0107; queda registrada aquí con su criterio de reapertura para que se pueda revisar por dato y no por opinión. Verificado en vivo contra el CMS corriendo.
- **Relacionados:** ADR 0107 (precedente de rótulo equivocado en el doc rector: "Examine" describía un motor de catálogo), ADR 0110 (T9 — el check-in que ahora publica en vivo), ADR 0106/0037 (side-effects post-commit best-effort: la regla que sigue el aviso), la ola T2-Eventos (el rol de organizador que reusa la política del canal), ADR 0002, ADR 0075. Regla de oro doc 25: ninguna capacidad transversal se implementa dos veces.

---

## Context

No había **nada**: cero SignalR, cero SSE, cero WebSocket en todo el repo. T7 partía de una pizarra en blanco, así que la primera decisión era el transporte.

El doc 25 rotula la capacidad como *"Realtime (SignalR) — DMs, feed, check-in"*. Al mirar **qué necesitan realmente esas tres superficies**, las tres son **server → client**:

- **Check-in**: alguien escanea en una puerta; las demás consolas deben enterarse.
- **Feed**: publicaron algo; los lectores abiertos deben verlo.
- **DMs**: mandar un mensaje ya es un `POST` que existe; lo que falta es avisar al destinatario.

En ningún caso hace falta que el cliente **invoque** métodos en el servidor sobre una conexión persistente. Eso cambia el cálculo: SignalR resuelve un problema (RPC bidireccional, fallback de transporte, grupos) que aquí no se tiene.

Y hay un coste concreto: el servidor de SignalR viene en el framework compartido (sin NuGet nuevo), pero **el cliente JS `@microsoft/signalr` es una dependencia npm que aterriza en cada bundle** — en una arquitectura donde los bundles se sirven por CDN con presupuestos de tamaño y un runtime compartido externalizado.

## Decision

### 1. SSE en vez de SignalR — con criterio de reapertura

Se implementa con **Server-Sent Events**: el servidor escribe `text/event-stream` y el navegador lo consume con `EventSource`, que es **nativo** y ya trae reconexión automática. **Cero dependencias nuevas**, ni NuGet ni npm.

**Criterio de reapertura** (para que se revise por dato, no por gusto): se adopta SignalR si aparece **(a)** necesidad real de RPC cliente→servidor sobre conexión persistente, **(b)** un entorno de despliegue donde SSE no atraviese (proxies que no soporten streaming), o **(c)** más de una instancia sirviendo, que exige backplane — aunque ese caso lo cubre el seam sin tocar los verticales (ver §2).

### 2. `IRealtimeNotifier` — un seam transversal, no un realtime por vertical

Los verticales **publican en un canal** y no saben del transporte. Es la regla de oro del doc 25 aplicada de entrada: esta capacidad no se implementará dos veces. Si mañana hay varias instancias, se cambia la implementación por una con backplane (Redis) **sin tocar a Eventos, Blogs ni Gobierno**.

El fan-out es **en proceso**: un deploy = un origen (no hay multi-tenant y hoy no hay escalado horizontal), así que un broker externo sería infraestructura sin problema que resolver.

### 3. Cada suscriptor con cola ACOTADA que descarta lo viejo

`BoundedChannel` con `DropOldest`, capacidad 32. Un cliente lento —una pestaña en segundo plano, una red mala— **no puede hacer crecer la memoria del servidor sin límite ni frenar a los demás**. Pierde los mensajes viejos, que es exactamente lo que un feed en vivo debe hacer: interesa lo último, no la historia. Publicar **nunca espera al lector** y **nunca lanza**.

### 4. Autorización POR CANAL, fail-closed — lo más importante de esta ola

Las tres olas anteriores (T6, T9, T2-Eventos) cerraron el acceso a expedientes, entradas y consolas. **Un flujo que empuja datos de negocio sin política sería la puerta trasera a todo eso**: da igual que `GET /manage` pida rol de organizador si `GET /stream?channel=eventos:checkin:X` sirve lo mismo a cualquiera.

Por eso el mapeo canal→permiso es **explícito** y el `default` **deniega**:

- `eventos:checkin:{eventId}` → mismo permiso que la consola que lo muestra (rol organizador).
- **Cualquier otro canal → 403**, incluso para un organizador de pleno derecho.

Añadir un canal **obliga** a declarar quién lo lee; olvidarlo lo deja cerrado, no abierto. Es el único default aceptable aquí, y está cubierto por un test que falla si se vuelve permisivo.

### 5. Primer consumidor: el check-in avisa en vivo

Tras validar una entrada (ADR 0110), se publica en el canal del evento: con varias puertas, cada operador ve entrar a la gente sin recargar. El aviso va **después** de que el hecho ocurrió y es **best-effort** (ADR 0037/0106): si nadie escucha o falla el envío, **la entrada sigue validada**. Un aviso caído no puede devolver un error sobre una operación que sí ocurrió.

## Consequences

**Positivas:**

- **Realtime sin deuda de dependencias**: nada nuevo que versionar, auditar ni meter en los bundles.
- **La capacidad queda transversal desde el día uno**: Blogs y Gobierno la consumen publicando en su canal, sin escribir transporte.
- **El realtime nace cerrado**, no abierto-y-luego-parcheado — que es el orden en el que este proyecto se encontró los otros huecos.
- **Reconexión gratis**: `EventSource` reintenta solo; no hay lógica de reconexión que mantener.
- El keep-alive periódico evita el bucle reconexión↔corte que provocan los proxies con conexiones "inactivas".

**Negativas o trade-offs:**

- **Sin RPC cliente→servidor.** Si hiciera falta, hay que volver sobre esto (criterio en §1).
- **Una conexión abierta por pestaña**, y los navegadores limitan conexiones por origen sobre HTTP/1.1 (~6). Con HTTP/2 deja de importar. **Criterio de reapertura:** si una vista necesitara varios streams simultáneos, conviene multiplexar en un solo canal antes que abrir varios.
- **Fan-out en proceso**: con dos instancias, un cliente conectado a la A no ve lo publicado en la B. Es aceptable hoy (un deploy = un origen) y el seam lo aísla, pero **es una limitación real, no una que se pueda olvidar**.
- **Los mensajes se pierden si nadie escucha**: no hay historial ni entrega garantizada. Correcto para un feed en vivo; **no** sirve como transporte de nada que deba llegar sí o sí — para eso está `ITransactionalNotifier` (ADR 0106).
- **`EventCheckInResult` cambia de forma** (aditivo, con defaults): sin el `eventId` no se puede elegir el canal.

**Notas de implementación:**

- Cuando el token del ticket **no verifica**, los campos nuevos van **vacíos**: no hay entrada de la que hablar, y rellenarlos con lo que el escáner afirmó sería dar por cierto justo lo que no se pudo comprobar.
- El endpoint escribe la respuesta a mano (`Response.StatusCode`) en vez de devolver `IActionResult`: es un stream, no una acción que devuelva un objeto — mismo patrón que el export en streaming de `AdminController`. De paso, esto lo hace testeable sin montar el pipeline de MVC.
- El payload se manda en **una línea**: un salto dentro de `data:` partiría el evento y el cliente recibiría JSON truncado.
- Tests: 8 del hub (incluidos *"un cliente lento no hace crecer la memoria"* y *"un canal no ve lo de otro evento"*) + 7 de autorización. Verificados por mutación: hacer permisivo el default de canales hace fallar dos.

## Alternatives considered

- **SignalR** (el rótulo del doc). Rechazado por ahora: resuelve RPC bidireccional y fallback, ninguno de los cuales necesitan las tres superficies, a cambio de un paquete npm en cada bundle CDN. Criterio de reapertura escrito en §1.
- **WebSocket a pelo.** Rechazado: es bidireccional (más de lo que hace falta), obliga a implementar a mano reconexión y heartbeat, y no gana nada sobre SSE para push unidireccional.
- **Polling.** Rechazado: para un check-in en puerta, el intervalo que lo haría sentir "vivo" (1-2 s) multiplica las peticiones a la consola —la ruta más cara— sin ahorrar complejidad real.
- **Un realtime por vertical.** Rechazado de plano: es exactamente lo que la regla de oro prohíbe, y lo que el proyecto ya pagó con las 5 copias del matching (ADR 0107) y los 4 stores duplicados (ADR 0105).
- **Autorizar el stream solo con "estar logueado".** Rechazado: no cierra nada. El feed de check-in dice quién entra a un evento; pedirle lo mismo que a la consola que lo muestra es lo único coherente.
- **Canales abiertos por defecto con lista de bloqueo.** Rechazado: invierte el fallo. Olvidar declarar un canal debe dejarlo cerrado.

## References

- `Synergos.CMS.Interfaces/IRealtimeNotifier.cs` — el seam y por qué la autorización no vive ahí.
- `Synergos.CMS.Web/Services/SseRealtimeHub.cs` — fan-out en proceso y la cola acotada.
- `Synergos.CMS.Web/Controllers/RealtimeController.cs` — `DeniedStatusFor`: el mapa canal→permiso fail-closed.
- `Synergos.CMS.Web/Controllers/EventosController.cs` — `BestEffortPublishAsync`: el primer consumidor.
- ADR 0110 (el check-in que publica), ADR 0106 (best-effort post-commit), ADR 0107 (precedente de desviación del rótulo).
