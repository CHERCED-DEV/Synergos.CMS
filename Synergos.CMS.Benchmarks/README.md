# Synergos.CMS.Benchmarks

BenchmarkDotNet 0.15.8 harness para los hot paths del CMS.
Cap-260 Batch D (Olas 259-260, ADR 0087).

## Run

```bash
# Todos los benchmarks
dotnet run -c Release --project Synergos.CMS.Benchmarks

# Subset por glob filter
dotnet run -c Release --project Synergos.CMS.Benchmarks -- --filter '*WebhookSigner*'
dotnet run -c Release --project Synergos.CMS.Benchmarks -- --filter '*BridgeContext*'

# Listing de benchmarks disponibles
dotnet run -c Release --project Synergos.CMS.Benchmarks -- --list flat
```

**Importante:** BenchmarkDotNet exige `Release` build — Debug
introduce JIT cost variability inaceptable.

## Benchmarks actuales

### WebhookSignerBenchmarks
HMAC-SHA256 signing del body para outbound webhooks (3 family
notifiers + 1 alert webhook = 4 sites en producción).

| Bench | Body size | Why |
|---|---|---|
| Sign_Small_200B | ~200 B | Alert payload típico |
| Sign_Medium_2KB | ~2 KB | Comment con body largo |
| Sign_Large_20KB | ~20 KB | Form submission con muchos fields |
| Sign_NullSecret_NoOp | — | Path no-secret (early return) |

Baseline = small. Detecta regresiones si Algorithm cambia (ej. SHA512
vs SHA256 con más rounds).

### BridgeContextSerializerBenchmarks
Serialización de `HostBridgeContext` que el partial
`_SynergosBridge.cshtml` y el endpoint `/synergos-bridge.js`
ejecutan per-request.

| Bench | Scenario | Why |
|---|---|---|
| Serialize_SmallAnon_FewKeys | anónimo, 1 key | Páginas públicas mínimas |
| Serialize_TypicalAuthMember_50Keys | member auth + 50 keys | Caso típico cap-220+ |
| Serialize_FullCatalog_369Keys | full Dictionary 369 keys | Worst-case si I18nKeyPrefixes incluye todo |
| SerializeUtf8_FullCatalog_369Keys | bytes directo | Path del endpoint CSP-strict |

Baseline = SmallAnon. Detecta regresiones si la shape crece o el
serializer change.

## Próximos targets candidatos (deferred)

- `RecoveryCodesHelper` (PBKDF2 100k SHA256 — intentionally slow,
  pero queremos baseline para detectar accidental change a 1k iters).
- `FileSystemAuditTrailWriter.WriteAsync` — JSONL append throughput.
- `FileSystemMemberTwoFactorStore.Save` — encrypt + write throughput
  (requiere ephemeral data protection setup).
- `IBundleRegistryClient.ResolveAsync` — lookup latency cuando llegue
  el HttpBundleRegistryClient real.

## Output

BenchmarkDotNet escribe resultados a `BenchmarkDotNet.Artifacts/`
(gitignored a nivel repo). Para CI integration / históricos, pipe a
JSON con `--exporters json` y archivar el output.
