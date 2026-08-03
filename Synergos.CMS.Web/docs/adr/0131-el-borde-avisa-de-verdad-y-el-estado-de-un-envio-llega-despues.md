# ADR 0131 — El borde avisa de verdad, y el estado de un envío llega después

- **Estado:** Aceptado
- **Fecha:** 2026-08-03
- **Cierra:** [HU #12](../../../../../issues/12) — paso 1 de la épica de Plataforma, **9 de 9 dominios**
- **Hermana de:** [HU #13](../../../../../issues/13) / el acuse de acceso, que se separó al refinar
- **Sigue el molde de:** ADR 0012 (el contrato del CDN se CONSUME, no se posee)

## Contexto

`Api.Notifications` estaba entera —plantillas, marcadores, tope de frecuencia, bitácora— y
terminaba entregándole el aviso a `LoggingNotificationSender`, que escribía una línea en el log
**y devolvía `true`**.

> **El sistema no le avisaba a nadie, y afirmaba lo contrario.** No hay confirmación de compra,
> ni recordatorio de cita, ni notificación de trámite. Es el único ítem que aparece en los nueve
> backlogs de prioridad 1 de la investigación de dominios.

Lo que bloqueaba no era escribir el adapter: era **la forma de la costura**.

```csharp
bool Send(Channel channel, string address, string subject, string body);   // no aguanta un proveedor real
```

| Problema | Por qué bloquea |
|---|---|
| **Síncrona** | hablar con un proveedor es una llamada de red |
| **Devuelve `bool`** | el proveedor devuelve un **id de mensaje**, y sin guardarlo **no se puede correlacionar el webhook de vuelta** |
| **`DeliveryStatus` solo `Sent│Failed`** | `Sent` quería decir «el transporte lo aceptó», que no es «llegó» |

> **Sin el id del proveedor, «entregado» y «rebotado» quedan fuera de alcance para siempre.** No
> es una mejora que se pueda añadir después sobre los envíos viejos: el evento ya pasó y no
> vuelve.

## Decisión

### 1. La costura cambia de forma, y el ciclo de vida con ella

```csharp
bool Supports(Channel channel);
Task<Result<string>> SendAsync(Channel, address, subject, body, idempotencyKey, ct);
```

`Queued → Accepted → Delivered → Bounced │ Complained`, con `Failed` para el rechazo de plano.

**`Accepted` no es «llegó»**: es «el proveedor se hace cargo de intentarlo». Las transiciones de
verdad llegan por webhook, minutos después.

### 2. El registro se reserva ANTES de tocar la red

Antes se enviaba primero y se registraba después. Si el proceso moría entre las dos cosas: el
correo había salido, no quedaba rastro, y **la llave de idempotencia no estaba anotada** — así
que el reintento mandaba un segundo correo idéntico.

Al revés, lo peor que queda es un `Queued` que se ve, se consulta y se reintenta.

> **Un `Queued` no cierra la llave, la sostiene.** Reintentar con la misma llave vuelve a
> intentar el envío *sobre el mismo registro*; en cuanto el proveedor acepta, la llave pasa a
> devolver lo que ya salió. Es la única forma de que «no mandar dos veces» y «un fallo
> transitorio se puede reintentar» sean verdad a la vez.

### 3. El estado avanza y **nunca retrocede**

Los eventos viajan por red y no vienen ordenados: `delivered` puede llegar antes que `accepted`.
Con «el último gana», ese par deja marcado como «quizá no llegó» un correo que sí llegó — y en
Gobierno ese dato es de lo que depende que un término haya empezado a correr.

Por eso el avance es una regla (`NotificationRules.Advance`) con rangos, y no una asignación:

| Rango | Estados |
|:--:|---|
| 0 | `Queued` |
| 1 | `Accepted` |
| 2 | `Delivered` · `Bounced` · `Failed` — excluyentes: el primero que llega es el que queda |
| 3 | `Complained` — lo único que ocurre legítimamente **después** de haber llegado |

### 4. El proveedor: **Resend**

3.000 correos/mes gratis, Pro desde USD 20, sin restricciones geográficas. SES es más barato a
volumen pero exige **salir de su sandbox** — un trámite con AWS en el camino crítico del ítem que
desbloquea a los nueve dominios.

> El cambio de forma de la costura es **idéntico con cualquier proveedor**. Elegir mal cuesta
> reescribir un fichero, que es exactamente para lo que la costura existe.

**Sin credenciales, `LoggingNotificationSender` rechaza** con `transport_not_configured` en vez
de devolver `true`. Un despliegue sin configurar no puede parecer uno que funciona.

### 5. El webhook, diseñado junto con PSE pero **sin promover**

`POST /v1/webhooks/resend` es el **único endpoint fuera de la llave compartida**, porque quien lo
llama es un tercero que no la tiene. Lo que lo protege es la firma. `UseSharedKeyAuth` acepta
ahora exenciones explícitas, una por una y comparando por segmentos.

Las cuatro cosas que resuelve —y que el receptor de PSE ([HU 6b](../../../../../issues/2)) va a
necesitar igual:

1. **verificación de firma** HMAC-SHA256 sobre `{id}.{instante}.{cuerpo}`, en tiempo constante
2. **antirrepetición** por ventana de 5 minutos sobre el instante firmado
3. **idempotencia por el id del EVENTO del proveedor** — no por uno nuestro: el que reintenta acá
   es él, y reentrega el mismo webhook durante días hasta ver un 2xx
4. **llegada fuera de orden** → la regla del punto 3

> **No se promueve a `Synergos.Shared` todavía.** Sería promover al **primer** consumidor, la
> misma regla que hizo esperar a `Bff.Core` hasta que existió `Bff.Tienda` y a
> `JsonCollectionStore` hasta tener seis (doc 10). Se promueve cuando la HU 6b lo pida.

## Consecuencias

**Lo que se gana**

- El borde **manda de verdad**, y el rastro dice la verdad sobre lo que pasó con cada aviso.
- Un proveedor caído produce `transport_unavailable` **transitorio**: `Rejection.IsTransient` es
  lo que ya decide en `Bff.Core` si algo se reintenta o se grita una vez.
- Un despliegue sin configurar **se nota**.

**Lo que cuesta**

- **`Delivery` cambió de forma.** Es un cambio de contrato, hecho antes de que existan datos en
  producción: después habría dejado de ser una decisión para pasar a ser una migración.
- `SendAsync` es asíncrono → el endpoint también. Los consumidores hablan HTTP, así que no se
  enteran.

**Lo que sigue afuera**

| | Por qué |
|---|---|
| **SMS y WhatsApp** | WhatsApp exige plantillas aprobadas por Meta: una cola ajena delante del desbloqueador 9/9. Cada uno sale como adapter propio detrás de esta misma costura |
| **Reintento automático de los `Queued`** | hoy reintenta quien llama. Un barrido periódico es la máquina de `Bff.Core`, y ponerlo acá duplicaría esa lógica en una capacidad |
| **Correlacionar un correo con la persona que lo abrió** | otro régimen de datos personales, y `Api.Messaging` ya tiene el acuse de acceso ([HU #13](../../../../../issues/13)) |

## Cómo se verificó

**47 tests nuevos** (1991 → 2038) y **siete mutaciones confirmadas en rojo**, más la verificación
con procesos reales que la HU marcaba como obligatoria: la API levantada contra un proveedor de
mentira, **matándolo a mitad de envío**.

La mutación que más enseñó fue una que **no** falló al primer intento: se quitó la verificación
de firma del endpoint y no cayó ni un test. Los de `WebhookVerifier` probaban el verificador;
ninguno probaba que alguien lo *llamara*. De ahí salió `WebhookHandler`, que existe para que esa
línea no dependa de que nadie la borre.

## Qué falta para que llegue a una casilla real

Es lo único de la HU que este repo no puede cerrar solo — hacen falta credenciales:

```
Notifications__Resend__ApiKey        re_…            (resend.com/api-keys)
Notifications__Resend__From          "Avisos <avisos@dominio-verificado.co>"
Notifications__Resend__WebhookSecret whsec_…         (al dar de alta el webhook)
```

El webhook se registra apuntando a `https://<host>/v1/webhooks/resend`. Sin `ApiKey` el servicio
arranca igual, con el transporte que rechaza y grita.
