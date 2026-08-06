# ADR 0035 — Email transactional runtime: IEmailService over Umbraco IEmailSender (Ola 65)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 65
- **Habilita:** ADR 0034 (password reset deferred), ADR 0030 (Forms
  notifications deferred), ADR (futuro confirmation post-registro)
- **Related:** ADR 0009 (extension seams)

## Context

Tras Olas 60-64 (Forms internal + Search + SEO + Member self-service),
varios módulos tenían un TODO consistente: "necesita SMTP". Sin un
seam de email transaccional:

- **Password reset** (ADR 0034 deferred): `IPasswordResetEmailSender`
  no existía. Operador tenía que reset desde backoffice manualmente.
- **Form notifications** (ADR 0030 mencionado como "diferido"): el
  operador no recibía email cuando llegaba una submission al
  `FileSystemFormSubmissionHandler`.
- **Email confirmation post-registro** (ADR 0034 deferred):
  `MembersSettings.RequireEmailConfirmation` no se podía implementar
  sin envío del token.

Decisión clave: Umbraco 13 ya tiene `Umbraco.Cms.Core.Mail.IEmailSender`
y gestiona SMTP config (sección `Umbraco:CMS:Global:Smtp`) y pickup
directory (modo dev). Reinventar SMTP/MailKit es trabajo perdido.

Pero dejar a los consumidores acoplarse directamente a
`Umbraco.Cms.Core.Mail.IEmailSender` viola ADR 0002 (Application no
referencia Umbraco) y ADR 0009 (seams obligatorios) — y bloquea swap
futuro a SendGrid/Mailgun/etc.

## Decision

Crear un seam thin `IEmailService` en Synergos.CMS.Interfaces con DTOs
estables, e implementar el default en Web/Services como adapter sobre
Umbraco's `IEmailSender`. Cero NuGet nuevo.

### Seam (Ola 65.1)

**`Synergos.CMS.Interfaces/IEmailService.cs`**:

```csharp
Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
```

Record:
- `EmailMessage(To, Subject, BodyHtml, BodyText? = null, From? = null,
  ReplyTo? = null)`

Sin tipos Umbraco-specific en el seam — los consumidores construyen
`EmailMessage` con strings.

**`Synergos.CMS.Application/Configuration/EmailSettings.cs`** POCO:

- `FromAddress` (default `"noreply@synergos.local"`)
- `FromName` (default `"Synergos"`)
- `WarnOnMissingSmtp` (default `true`) — toggle del Warning log cuando
  el envío falla por config ausente.

**Importante**: la config SMTP transport-specific (host, port,
credentials, EnableSsl) **NO vive en EmailSettings**. Vive en
`Umbraco:CMS:Global:Smtp` que Umbraco usa internamente. EmailSettings
solo gobierna identidad default + behavior de logging.

### Default impl (Ola 65.2)

**`DefaultEmailService`** en `Web/Services/`:

- Inyecta `Umbraco.Cms.Core.Mail.IEmailSender` +
  `IOptions<EmailSettings>` + `ILogger`.
- Construye `fromHeader = "{FromName} <{FromAddress}>"` cuando el
  caller no override `From` explícito.
- Convierte el `EmailMessage` del seam a
  `Umbraco.Cms.Core.Models.Email.EmailMessage` (`isBodyHtml=true`).
- Llama `_umbracoEmailSender.SendAsync(umbracoMessage, "synergos")`.
- Catchea excepciones y logea Warning si
  `WarnOnMissingSmtp=true` (mensaje hint indica revisar
  `Umbraco:CMS:Global:Smtp` o pickup directory).

Wire en `SeamComposer`:
`services.AddSingleton<IEmailService, DefaultEmailService>()`. Singleton
OK — solo depende de servicios singleton.

## Consequences

**Positivas:**

- **Zero NuGet nuevo**: aprovecha la infra que Umbraco ya tiene. El
  operador configura SMTP en `appsettings.json` con la sección estándar
  Umbraco — lo que cualquier doc Umbraco le explica.
- **Pickup directory para dev**: si configurado vía
  `Umbraco:CMS:Global:Smtp:DeliveryMethod=SpecifiedPickupDirectory`,
  los emails se escriben como `.eml` a disco. Self-contained dev
  experience sin SMTP server local.
- **Seam swap-able**: si producción quiere SendGrid API directo
  (REST, no SMTP), se cambia el binding en SeamComposer por una nueva
  impl `SendGridEmailService` que use el SDK. Cero cambios en
  consumidores.
- **DTO estable**: los consumidores nunca tocan tipos Umbraco. Si
  Umbraco cambia su API entre majors, solo el adapter rompe.
- **Habilita 3+ módulos diferidos**: password reset (ADR 0034), form
  notifications (ADR 0030), email confirmation post-registro
  (ADR 0034). Cada uno wirea el `IEmailService` cuando se prioritice.

**Negativas:**

- **Sin retry/backoff**: si el envío falla por error transitorio
  (SMTP timeout, rate limit del provider), el adapter loguea Warning
  pero no reintenta. El caller puede no saber que el envío falló.
  Para sitios con SLA de delivery, agregar adapter alterno con
  `IRetryPolicy` (Polly). Diferido.
- **Sin queue async**: el envío es inline al request del caller. Para
  forms con muchas submissions o registration burst, el SMTP puede
  ser bottleneck. Agregar `IBackgroundEmailQueue` con
  `IHostedService` si justifica. Diferido.
- **Sin templating**: los consumidores construyen el HTML inline.
  Para emails con plantillas (header, footer, branding), agregar un
  seam `IEmailTemplateRenderer` que combine Razor template +
  `EmailMessage`. Diferido — KISS para primer pase.
- **Logging de Warning sin distinción de causa**: el catch genérico
  no distingue "SMTP not configured" de "SMTP timeout" de
  "credentials wrong". Se pierde granularidad. Mejorable con catch
  específicos de SmtpException — pero Umbraco's IEmailSender envuelve
  excepciones y la API es opaca. Aceptable.
- **`isBodyHtml=true` siempre**: el seam recibe `BodyHtml` mandatory
  + `BodyText` opcional. El adapter solo manda HTML. Para clientes
  text-only (rare hoy), la decisión es perder fidelidad. Si justifica,
  generar text alternativo desde HTML strip-tags antes de enviar.

**Neutras:**

- 1 seam (`IEmailService`) + 1 record (`EmailMessage`) +
  1 default impl (`DefaultEmailService`) + 1 POCO (`EmailSettings`).
  Cero schema editorial, cero GUIDs.
- `messageType="synergos"` en SendAsync — Umbraco usa esto para
  notification handlers internos. Identifica los emails que origina
  Synergos vs los que Umbraco envía (ej. password reset del
  backoffice).

## Alternatives considered

- **Acoplar consumidores directamente a
  `Umbraco.Cms.Core.Mail.IEmailSender`**. Viola ADR 0002 + ADR 0009.
  Bloquea swap a SendGrid API directo (sin SMTP).
- **Adoptar MailKit + escribir SmtpEmailService directo**. Descartado.
  Reinventa lo que Umbraco ya hace + agrega NuGet con su CVE history.
  Si justifica para perf, swap directo del binding.
- **Adoptar SendGrid SDK + skip Umbraco IEmailSender**. Premature.
  Sin requerimiento de provider específico, KISS dice usar lo que
  está. Adapter pattern facilita el swap futuro.
- **Templating Razor desde el seam**. Diferido. Mezcla
  responsabilidades — el seam es transport, no presentation.
  `IEmailTemplateRenderer` separado puede venir cuando se necesiten
  emails con plantilla.

## Implementation summary (Ola 65, 3 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-65.1)` | `258da7d` | `IEmailService` seam + `EmailMessage` record + `EmailSettings` POCO |
| `feat(ola-65.2)` | `c4482f5` | `DefaultEmailService` adapter sobre `Umbraco.Cms.Core.Mail.IEmailSender` + wire OptionsComposer + SeamComposer |
| `docs(ola-65.3)` | (este) | ADR 0035 + index README |

## References

- ADR 0009 — Extension seams (`IEmailService` sigue el patrón)
- ADR 0002 — Multi-project architecture (Application no referencia
  Umbraco — adapter en Web)
- ADR 0030 — Forms internal submission (notifications consumer
  candidato)
- ADR 0034 — Member self-service (password reset + email confirmation
  consumers candidatos)
- Umbraco SMTP config: `Umbraco:CMS:Global:Smtp` en appsettings
