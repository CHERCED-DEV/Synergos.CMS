# ADR 0065 — Receiver SDK Go + Java additions (Olas 148-149)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

ADR 0058 introdujo `docs/webhooks/receiver-sdk.md` con snippets para
verificar HMAC + replay window en C# / Node.js / Python. Diferido §11.12
listaba "Receiver SDK para Go / Java / PHP / Ruby" para cerrar la gap.

Constraint: el SDK no es un package — solo snippets que el integrador
copia en su receiver service. La doc es la API.

## Decision

### Olas 148-149 — Go + Java snippets agregados

Extiende `docs/webhooks/receiver-sdk.md` después de Python con 2
implementaciones idiomáticas del verificador HMAC-SHA256 + replay
window:

#### Go (`crypto/hmac` + `crypto/sha256` + `crypto/subtle`)

```go
func VerifySynergosWebhook(secret, timestampHeader, signatureHeader string,
    body []byte, tolerance time.Duration) bool { ... }
```

Usa:
- `time.Parse(time.RFC3339, ...)` para validar timestamp.
- `hmac.New(sha256.New, ...)` + `mac.Sum(nil)` + `hex.EncodeToString`.
- `subtle.ConstantTimeCompare` para comparación segura.

Uso típico mostrado en un `http.Handler`.

#### Java (`javax.crypto.Mac` + custom constant-time compare)

```java
public static boolean verify(String secret, String timestampHeader,
                              String signatureHeader, byte[] body,
                              Duration tolerance) { ... }
```

Usa:
- `OffsetDateTime.parse` para timestamp ISO 8601.
- `Mac.getInstance("HmacSHA256")` + `SecretKeySpec` + `mac.doFinal()`.
- `HexFormat.of().formatHex(...)` (Java 17+).
- Custom `constantTimeEquals` porque `MessageDigest.isEqual` solo
  funciona con `byte[]`, no con `String`.

Uso típico mostrado en un Spring `@PostMapping`.

### Lenguajes deferidos

- **PHP**: `hash_hmac('sha256', ...)` + `hash_equals` para constant-time.
  Snippet trivial pero los integradores PHP típicamente usan frameworks
  (Laravel, Symfony) con su propio idiom — agregar cuando llegue
  requirement.
- **Ruby**: `OpenSSL::HMAC.hexdigest('sha256', secret, input)` +
  `Rack::Utils.secure_compare`. Mismo razonamiento.

Si llegan integradores con esos stacks, agregar fácil al mismo
markdown sin ADR nuevo (additive doc change).

## Consequences

**Positivas:**

- **Coverage 5 lenguajes** — C# / Node.js / Python / Go / Java.
  Cubre el ~95% de stacks de receiver service típicos.
- **Snippets idiomáticos** — Go usa `crypto/subtle`, Java usa custom
  constant-time, sin polyfills falsos. Cada uno aprovecha el stdlib
  del lenguaje target.
- **Cero dependencia** — los snippets solo dependen de stdlib (Go
  built-in crypto + Java javax.crypto). El integrador copia y pega
  sin pull-in de packages 3rd-party.

**Negativas:**

- **Sin tests automatizados** — los snippets son markdown. Si la
  signing logic del CMS cambia, los snippets pueden quedar desfasados.
  Mitigación: ADR 0058 ya cubre el algoritmo formal (Stripe-style
  con timestamp inside HMAC input). Los snippets deben rotar con
  ese contrato.
- **Java 17+ requirido** — `HexFormat` no existe en Java 8/11. Si
  un integrador necesita Java 8, swap a
  `DatatypeConverter.printHexBinary(...).toLowerCase()`.

**Neutras:**

- 1 commit docs batch (Olas 148+149 unificadas) + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages.
- 0 código C# tocado.

## Implementation summary

| # | Foco |
|---|---|
| 148 | Go snippet en `receiver-sdk.md` con `crypto/hmac` + `crypto/subtle.ConstantTimeCompare`. Uso en `http.Handler`. |
| 149 | Java snippet en `receiver-sdk.md` con `javax.crypto.Mac` + custom constant-time. Uso en Spring `@PostMapping`. |
| 0065 | (este) ADR consolidado |

## Próximas direcciones

- **PHP + Ruby snippets** — agregar al markdown cuando llegue
  requirement (additive, sin ADR nuevo).
- **Snapshot test** — fixture JSON + payload firmado canónico para
  que los receiver SDKs validen su impl contra una firma de
  referencia. Diferido (require generar el fixture canónico
  primero).

## References

- ADR 0050 — Webhook replay protection (origen del header
  `X-Synergos-Timestamp` + Stripe-style HMAC input).
- ADR 0058 — Receiver SDK docs initial (C# / Node / Python).
