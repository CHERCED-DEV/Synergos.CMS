# ADR 0085 — Cap-240: deferred cleanup — IEmailTemplateRenderer + GDPR RTBF + recovery emails + UI contract tests (Olas 231-240)

- **Status:** Accepted
- **Date:** 2026-04-28
- **Deciders:** Arquitecto + agente.

## Context

Cap-230 cerró 2FA Phase 2.C + tokens sync + Member CRUD tests pero
dejó 4 deferred items concretos en §11.20 + ADR 0084 "Próximas
direcciones":

1. `SendPasswordResetAsync` sin tests — necesitaba refactor a
   `IEmailTemplateRenderer` interface seam.
2. GDPR RTBF coordinator — el doc `gdpr-rtbf.md` ya tenía el flow
   automatizado especificado pero sin implementar.
3. Recovery emails post-alert — el `WebhookTelemetryAlertHostedService`
   solo notificaba el firing, nunca el resolution.
4. Contract tests UI — Vitest harness validando shape canónica.

Cap-240 los cierra todos en 4 batches.

## Decision

### Batch A — Olas 231-232 — IEmailTemplateRenderer + tests

**Refactor `UmbracoMemberRosterWriter`** para inyectar
`IEmailTemplateRenderer` (interface, ya existente) en vez de
`RazorEmailTemplateRenderer` (clase concreta). Sin cambios de
comportamiento — la concrete class ya implementaba la interfaz. Solo
honor a SOLID/DIP.

**5 tests nuevos** para `SendPasswordResetAsync` via NSubstitute:
- HappyPath: render + send con URL bien formada
  (`https://host/account/reset-password?email=...&token=...`).
- MemberNotFound → false sin tocar auth ni email.
- MemberHasNoEmail → false sin tocar auth.
- AuthReturnsNoToken (EmailExists=false) → false y NO envío
  (defensa contra enumeration leak).
- FallbackSiteNameWhenBrandDisplayBlank → DisplayName "  " falls
  back a "Synergos".

Stub helper `StubHttpContext` usa `DefaultHttpContext` con
scheme+host controlados — evita Substituting de IRequest (props
read-only en NSubstitute).

Total: 16/16 passing en `UmbracoMemberRosterWriterTests`.

### Batch B — Olas 233-235 — GDPR RTBF coordinator

**Seam**: `IGdprRtbfCoordinator.ProcessRequestAsync(memberKey,
actorEmail, ct) → RtbfResult`.

`RtbfResult` agrega counts (`MemberDeleted`, `OriginalEmail`,
`CommentsAnonymized`, `FormSubmissionsAnonymized`,
`AuditPreserved`, `FailureReason`) — útil para reportar al
requester.

**Implementación** `FileSystemGdprRtbfCoordinator`:

1. Resuelve Member via `IMemberService` → captura email original.
2. Itera `App_Data/syn-comments/*.json`: comments con
   `MemberKey == memberKey` → `AuthorName="[deleted]",
   MemberKey=null`. Idempotent en replay.
3. Itera `App_Data/syn-form-submissions/**/*.json`: cualquier field
   cuyo valor matchea (case-insensitive) el email del Member →
   `[deleted]@gdpr.local`. JSON parsing via `JsonNode` (free-form
   schema).
4. Llama `IMemberRosterWriter.DeleteAsync` para el record del
   Member.
5. Audit terminal `gdpr.rtbf-processed` con outcome `success` |
   `partial` (delete-failed) | `failure` (member-not-found).

**Decisión**: anonimización ANTES del delete. Si el delete falla
(race con backoffice concurrente), los stores ya quedan limpios y
el operator puede reintentar el delete.

**Audit preservation**: GDPR Art. 17(3) exime el processing
requerido para "compliance with a legal obligation". Audit events
que mencionan al Member NO se borran — `AuditPreserved` siempre
true, hecho explícito en el reporting.

**Admin endpoint**: `POST /admin/members/{key}/gdpr-erase` con
self-erase guard (mismo pattern que `DeleteMember`) + cascada de
2FA `DisableAsync` para no dejar el secret huérfano.

**5 tests** con temp ContentRoot + `IMemberService` stubbed
(NSubstitute):
- MemberNotFound → failure + audit.
- HappyPath → 3 comments + 1 form anonymized, ajenos intactos,
  audit success con counts en Detail.
- DeleteFails → outcome=partial, comments/forms ya anonimizados.
- NoStoresExist → success aunque no haya files.
- IdempotentReplay → segundo run sobre mismos comments retorna 0.

### Batch C — Olas 236-237 — Recovery emails post-alert

**Refactor**: Extrae la lógica del scan del
`WebhookTelemetryAlertHostedService` a un nuevo
`WebhookTelemetryAlertScanner` (instance class testable, no
BackgroundService). El hosted service queda thin loop.

**Recovery logic**: cuando un canal previamente alerting vuelve a
healthy (failRate < threshold con sample suficiente), el scanner
emite un email "recovered" al mismo destinatario y limpia el state
interno.

`WebhookTelemetryAlertSettings.RecoveryEmailEnabled` (default true)
permite silenciar el recovery email. Si false, el state se limpia
igualmente — próxima alerting fires como first-time.

`ChannelAlertState` per-canal con `FirstFiredUtc`, `LastFiredUtc`,
`LastFailRate` permite que el recovery email reporte:
- Duración del incidente.
- Fail rate al disparar vs actual.
- Total calls / Success / Failure.

**TimeProvider inyectable** (default `System`) para tests
deterministas con `FakeTimeProvider`.

**10 tests** cubriendo: enabled flag, MinimumSampleSize,
threshold, alert firing, cooldown respect, post-cooldown re-fire,
recovery firing, no-recovery-if-never-alerted, recovery-disabled,
recovery-requires-min-sample.

### Batch D — Ola 238 — UI contract tests skeleton

`Synergos.CMS.Web/docs/contracts/tests/` standalone Vitest harness:

- `package.json` con vitest 2.x + happy-dom + TypeScript 5.6 (deps
  declaradas, no installed — el resto de Synergos.CMS no consume
  npm).
- `vitest.config.ts` con happy-dom env y filter `*.contract.test.ts`.
- `dom-events.contract.test.ts` cubriendo:
  - Naming convention `syn:{component}:{event}`.
  - Shape de `syn:component:ready` (`{ tag, version }`).
  - Shape de `syn:component:error` (`{ tag, message, error? }`).
  - Tag prefix `synergos-*` per ADR 0083.
  - Outcome tri-state.
  - `syn:form-stepper:submitted` con outcome.
  - Bubbling default (bubbles+composed).
- `tsconfig.json` con strict mode + DOM types.
- `README.md` documentando setup, scope inicial, y pending
  (host-bridge, i18n-bridge, css-tokens — futuros).

**Decisión**: opt-in, no integrado al CI .NET. El contract owner /
UI team lo corre manualmente al bumpear contract version o agregar
evento nuevo. Si los specs eventually mudan al repo Synergos.UI con
tests propios, este harness queda obsoleto (DRY).

### Olas 239-240 — Cierre

Este ADR + actualización current-state §11.21 + memory.

## Consequences

**Positivas:**

- **Test coverage del writer completo**: 16/16 tests cubriendo todos
  los métodos de `IMemberRosterWriter` incluyendo el complejo
  `SendPasswordResetAsync`. Lift del último uncovered del cap-230.
- **GDPR compliant automatable**: el flow manual del doc ahora es
  un seam ejecutable desde el admin dashboard. Reduce error humano
  + tiempo de processing del request.
- **Loop alerting cerrado**: operador recibe explicit "this canal
  recovered" sin tener que volver al dashboard. Reduce alert fatigue.
- **Contract tests foundation**: skeleton listo para que el equipo
  expanda con host-bridge + i18n-bridge + css-tokens cuando bump.
- **WebhookTelemetryAlertScanner ahora testable**: 10 tests cubren
  comportamiento que antes era validación-via-deployment-y-hope.
- **§11.20 deferred items todos cerrados** (4/4): cap-240 deja la
  lista limpia para cap futuro.

**Negativas:**

- **GDPR coordinator filesystem-locked**: `FileSystemGdprRtbfCoordinator`
  asume stores filesystem. Si llega adapter DB-backed para comments
  o forms, hay que implementar coordinator paralelo o extraer
  IFileSystem abstraction. YAGNI mientras solo hay un store por
  surface.
- **Recovery email duplicación de código**: `SendAlertAsync` y
  `SendRecoveryAsync` comparten ~80% del HTML body. Extraer a
  helper sería mejora marginal — preferí keep simple.
- **UI contract tests no-CI**: el harness no se ejecuta en CI hasta
  que el equipo decide. Riesgo de drift si nadie corre el comando.
  Mitigación: README explícito + linkeado desde ADR 0083.
- **NSubstitute "Returns inside Returns" trampa**: descubrí en
  Batch B que llamar `_x.GetByKey().Returns(StubMember(...))` con
  StubMember setting up sub-returns internamente confunde a
  NSubstitute. Fix: build el member ANTES de Returns. Memoria nueva
  para no repetir.

**Neutras:**

- 4 commits feat/test/test/test + 1 commit ADR docs.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.
- 0 schema rompedor.
- Tests project: 161 → 181 (+20: 5 SendPasswordReset + 5 GDPR + 10
  scanner).

## Implementation summary

| # | Foco | Commit |
|---|---|---|
| 231-232 | `IEmailTemplateRenderer` interface + SendPasswordReset tests | `51a230b` |
| 233-235 | GDPR RTBF coordinator + admin endpoint + tests | `647cb14` |
| 236-237 | Recovery emails post-alert resolved + tests | `1541d19` |
| 238 | UI contract tests skeleton | `9ee5409` |
| 239-240 | (este) ADR + current-state §11.21 + memory |

## Próximas direcciones

- **GDPR coordinator UI**: el endpoint existe pero sin botón en
  `/admin/members` view. Agregar dialog confirm dedicado (separado
  del DeleteMember actual).
- **Contract tests CI integration**: si el equipo decide, agregar
  GitHub Action que corra el Vitest harness en PRs que toquen
  `docs/contracts/`.
- **Multi-instance encryption-at-rest**: configurar
  `AddDataProtection().PersistKeysToX` cuando llegue scale-out
  requirement (sigue deferred desde cap-230).
- **Performance benchmarks**: BenchmarkDotNet harness + targets
  (sigue deferred desde cap-230).
- **Recovery email Slack/Discord channel**: `IEmailService` directo
  ya cubre email; si algún operador prefiere notification por chat,
  implementar composite notifier paralelo a Comments / Forms / Cart
  (tradeoff: cuidar Rule of Three).

## References

- ADR 0067 — IAuditTrailWriter (Outcome enum tri-state alineado).
- ADR 0068 — Member roster writer.
- ADR 0076 / 0081 / 0082 — 2FA Phase 1 / 2.A / 2.B.
- ADR 0080 — Webhook telemetry alerts (base de Batch C).
- ADR 0083 — CMS↔UI alignment via contracts (base de Batch D).
- ADR 0084 — Cap-230 cierre (deferred items origen de cap-240).
- `docs/hardening/gdpr-rtbf.md` — flow doc base de Batch B.
- [GDPR Article 17](https://gdpr-info.eu/art-17-gdpr/) — Right to erasure.
