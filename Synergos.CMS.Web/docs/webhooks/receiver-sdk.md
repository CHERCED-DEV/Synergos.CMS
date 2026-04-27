# Synergos webhook receiver — verification guide

Documentación para integradores que implementan un endpoint receiver de
los outgoing webhooks de Synergos CMS (comments moderation, form
submissions, cart abandonment).

## Convención HTTP

Todo POST sale como `application/json; charset=utf-8` con UTF-8 BOM
en el body.

Headers que el receiver lee:

| Header | Cuándo aparece | Significado |
|---|---|---|
| `Authorization: Bearer {token}` | `WebhookBearerToken` configurado | Auth simple. Comparar literal contra valor esperado. |
| `X-Synergos-Timestamp` | `WebhookHmacSecret` configurado | ISO 8601 UTC del momento de firma. Usado para replay protection. |
| `X-Synergos-Signature` | `WebhookHmacSecret` configurado | `sha256={hex_lowercase}` HMAC sobre `"{timestamp}.{body}"`. |

Si ningún header de auth/firma está presente, el receiver debe
aceptar el payload solo si la URL del webhook fue distribuida por
canal seguro (la URL es el secret implícito).

## Verificación de firma (recomendado)

Algoritmo:

1. Leer header `X-Synergos-Timestamp`. Si no presente, abortar 400.
2. Leer header `X-Synergos-Signature`. Si no presente, abortar 400.
3. Leer body raw bytes (sin parsear JSON aún).
4. Construir input HMAC: `timestamp + "." + body_utf8`.
5. Computar `HMACSHA256(secret_compartido, input)` y formatear como
   `"sha256=" + hex_lowercase(hash)`.
6. Comparar **constant-time** contra el header recibido. Si difiere,
   abortar 401.
7. Verificar `|now - timestamp| < 5 min`. Si fuera de ventana,
   abortar 401 (replay).

### C# (System.Security.Cryptography)

```csharp
using System.Security.Cryptography;
using System.Text;

public static bool VerifySynergosWebhook(
    string secret,
    string timestampHeader,
    string signatureHeader,
    byte[] body,
    TimeSpan tolerance)
{
    if (string.IsNullOrWhiteSpace(timestampHeader)) return false;
    if (string.IsNullOrWhiteSpace(signatureHeader)) return false;
    if (!signatureHeader.StartsWith("sha256=")) return false;

    // 1. Replay window check.
    if (!DateTime.TryParse(timestampHeader, out var ts)) return false;
    var skew = (DateTime.UtcNow - ts.ToUniversalTime()).Duration();
    if (skew > tolerance) return false;

    // 2. Recompute HMAC.
    var key = Encoding.UTF8.GetBytes(secret);
    var inputPrefix = Encoding.UTF8.GetBytes(timestampHeader + ".");
    var input = new byte[inputPrefix.Length + body.Length];
    inputPrefix.CopyTo(input.AsSpan());
    body.CopyTo(input.AsSpan(inputPrefix.Length));

    var actual = HMACSHA256.HashData(key, input);
    var expected = Convert.FromHexString(signatureHeader.AsSpan(7));

    // 3. Constant-time compare.
    return CryptographicOperations.FixedTimeEquals(actual, expected);
}
```

### Node.js (built-in crypto)

```javascript
const crypto = require('crypto');

function verifySynergosWebhook(secret, timestampHeader, signatureHeader, bodyBuffer, toleranceMs = 5 * 60 * 1000) {
  if (!timestampHeader || !signatureHeader || !signatureHeader.startsWith('sha256=')) {
    return false;
  }
  const ts = Date.parse(timestampHeader);
  if (!Number.isFinite(ts)) return false;
  if (Math.abs(Date.now() - ts) > toleranceMs) return false;

  const input = Buffer.concat([
    Buffer.from(timestampHeader + '.', 'utf8'),
    bodyBuffer,
  ]);
  const expected = crypto.createHmac('sha256', secret).update(input).digest();
  const received = Buffer.from(signatureHeader.slice(7), 'hex');
  if (received.length !== expected.length) return false;
  return crypto.timingSafeEqual(received, expected);
}
```

### Python (hmac + secrets.compare_digest)

```python
import hashlib
import hmac
import secrets
from datetime import datetime, timezone, timedelta

def verify_synergos_webhook(secret: str, timestamp_header: str, signature_header: str,
                             body_bytes: bytes, tolerance: timedelta = timedelta(minutes=5)) -> bool:
    if not timestamp_header or not signature_header or not signature_header.startswith('sha256='):
        return False
    try:
        ts = datetime.fromisoformat(timestamp_header.replace('Z', '+00:00'))
    except ValueError:
        return False
    if abs(datetime.now(timezone.utc) - ts) > tolerance:
        return False

    input_bytes = (timestamp_header + '.').encode('utf-8') + body_bytes
    expected = hmac.new(secret.encode('utf-8'), input_bytes, hashlib.sha256).hexdigest()
    received = signature_header[len('sha256='):]
    return secrets.compare_digest(expected, received)
```

## Payloads por evento

### `comment.pending-moderation`

```json
{
  "event": "comment.pending-moderation",
  "siteName": "Brand A",
  "nodeId": 42,
  "commentId": "a1b2c3...",
  "authorName": "John Doe",
  "body": "Comment text...",
  "createdAtUtc": "2026-04-26T14:32:01.0000000Z"
}
```

### `form.submitted`

```json
{
  "event": "form.submitted",
  "siteName": "Brand A",
  "formKey": "contact-form",
  "fields": { "email": "x@y.z", "message": "..." },
  "clientIp": "203.0.113.7",
  "referrer": "/contact",
  "receivedAtUtc": "2026-04-26T14:32:01.0000000Z",
  "storageReference": "C:\\...\\contact-form\\20260426_143201_abc.json"
}
```

### `cart.abandoned`

```json
{
  "event": "cart.abandoned",
  "siteName": "Brand A",
  "cartId": "hash16chars",
  "itemCount": 3,
  "subtotal": 187500.0,
  "currency": "COP",
  "lastActivityUtc": "2026-04-26T12:32:01.0000000Z",
  "minutesSinceActivity": 120
}
```

## Idempotencia

Synergos NO garantiza exactly-once delivery. Polly retry puede
reenviar el mismo payload si el receiver retorna 5xx o timeout.

El receiver debe ser **idempotent** sobre el `(event, siteName, id)`
tuple (donde `id` = `commentId` / `storageReference` / `cartId`).
Estrategia común: deduplicate via tabla con UNIQUE constraint o
Redis SETNX.

## Fallos del lado receiver

Si tu endpoint retorna 5xx o tarda más de `AttemptTimeoutSeconds`
(default 10), Polly re-envía hasta `MaxRetryAttempts` (default 3)
con exponential backoff + jitter.

Si tu endpoint retorna 4xx, Polly NO retry (es un error semántico
del payload, no transitorio). El channel loguea Warning del CMS-side
y el evento se considera entregado-fallido.

Para garantía de entrega cross-restart del CMS, swap el channel
default por uno que enqueue a un broker (Kafka/RabbitMQ) que el
receiver consume independent del CMS uptime.

## Configuración multi-canal

Cada dominio (Comments / Forms / Cart) tiene 5 channels independientes
configurables:

| Setting | Channel emisor |
|---|---|
| `*Settings.NotifyEmailAddress` | Email via SMTP |
| `*Settings.WebhookUrl` | Webhook genérico (con HMAC opt-in) |
| `*Settings.SlackWebhookUrl` | Slack Block Kit |
| `*Settings.DiscordWebhookUrl` | Discord embeds |
| `*Settings.TeamsWebhookUrl` | Microsoft Teams Adaptive Cards |

Vacío = canal no-op. Múltiples activos disparan en paralelo (composite
itera).

## References

- Stripe webhook signing reference: <https://stripe.com/docs/webhooks/signatures>
- ADR 0050 — Slack channels + Webhook replay protection
- ADR 0057 — Adaptive Cards Teams + Polly per-channel config
