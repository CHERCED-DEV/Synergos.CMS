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

### Go (crypto/hmac + crypto/sha256)

```go
package synergos

import (
	"crypto/hmac"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/hex"
	"strings"
	"time"
)

// VerifySynergosWebhook returns true if the signature header matches and
// the timestamp is within tolerance of now (default 5 min).
func VerifySynergosWebhook(secret, timestampHeader, signatureHeader string,
	body []byte, tolerance time.Duration) bool {
	if timestampHeader == "" || signatureHeader == "" {
		return false
	}
	if !strings.HasPrefix(signatureHeader, "sha256=") {
		return false
	}

	ts, err := time.Parse(time.RFC3339, timestampHeader)
	if err != nil {
		return false
	}
	delta := time.Since(ts)
	if delta < 0 {
		delta = -delta
	}
	if delta > tolerance {
		return false
	}

	mac := hmac.New(sha256.New, []byte(secret))
	mac.Write([]byte(timestampHeader + "."))
	mac.Write(body)
	expected := hex.EncodeToString(mac.Sum(nil))
	received := signatureHeader[len("sha256="):]

	return subtle.ConstantTimeCompare([]byte(expected), []byte(received)) == 1
}
```

Uso típico en un `http.Handler`:

```go
body, _ := io.ReadAll(r.Body)
ok := VerifySynergosWebhook(
    os.Getenv("SYNERGOS_WEBHOOK_SECRET"),
    r.Header.Get("X-Synergos-Timestamp"),
    r.Header.Get("X-Synergos-Signature"),
    body,
    5*time.Minute)
if !ok {
    http.Error(w, "invalid signature", http.StatusUnauthorized)
    return
}
```

### Java (javax.crypto.Mac)

```java
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.format.DateTimeParseException;
import java.util.HexFormat;

public final class SynergosWebhookVerifier {

    public static boolean verify(String secret, String timestampHeader,
                                 String signatureHeader, byte[] body,
                                 Duration tolerance) {
        if (timestampHeader == null || signatureHeader == null) return false;
        if (!signatureHeader.startsWith("sha256=")) return false;

        Instant ts;
        try {
            ts = OffsetDateTime.parse(timestampHeader).toInstant();
        } catch (DateTimeParseException e) {
            return false;
        }
        Duration delta = Duration.between(ts, Instant.now()).abs();
        if (delta.compareTo(tolerance) > 0) return false;

        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(secret.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
            mac.update((timestampHeader + ".").getBytes(StandardCharsets.UTF_8));
            mac.update(body);
            String expected = HexFormat.of().formatHex(mac.doFinal());
            String received = signatureHeader.substring("sha256=".length());
            return constantTimeEquals(expected, received);
        } catch (Exception e) {
            return false;
        }
    }

    private static boolean constantTimeEquals(String a, String b) {
        if (a.length() != b.length()) return false;
        int diff = 0;
        for (int i = 0; i < a.length(); i++) {
            diff |= a.charAt(i) ^ b.charAt(i);
        }
        return diff == 0;
    }
}
```

Uso típico en un Spring controller:

```java
@PostMapping(value = "/synergos/comment-moderation", consumes = "application/json")
public ResponseEntity<Void> receive(
        @RequestHeader(value = "X-Synergos-Timestamp", required = false) String ts,
        @RequestHeader(value = "X-Synergos-Signature", required = false) String sig,
        @RequestBody byte[] body) {
    boolean ok = SynergosWebhookVerifier.verify(
        System.getenv("SYNERGOS_WEBHOOK_SECRET"),
        ts, sig, body, Duration.ofMinutes(5));
    if (!ok) {
        return ResponseEntity.status(401).build();
    }
    // process payload …
    return ResponseEntity.ok().build();
}
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
