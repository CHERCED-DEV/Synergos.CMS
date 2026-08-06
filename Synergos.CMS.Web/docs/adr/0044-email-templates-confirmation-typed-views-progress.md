# ADR 0044 — Email templates Razor + email confirmation post-registro + typed views progress (Olas 75.3-75.4 + 82 + 83)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 81 — *"continuamos sin parar"*.
- **Consolida:** 4 sub-olas en un único ADR.

## Context

Tras Ola 81 (cart abandonment) quedaban 3 deferred items concretos:

1. **`IEmailTemplateRenderer`** (deferred ADR 0035) — emails con
   string concat inline carecen de branding consistente y mezclan
   layout con lógica.
2. **Email confirmation post-registro** (deferred ADR 0034) —
   `MembersSettings.RequireEmailConfirmation` no implementada.
3. **Typed views progress** (Ola 75.x continuation) — refactor
   gradual a `@inherits UmbracoViewPage<T>` pendiente para varias
   views.

## Decision

Ejecutar las 3 olas en secuencia.

### Ola 75.3 — typed views blog/shop categories (1 commit `43b0521`)

- `PostCategoryPage.cshtml` → `@inherits UmbracoViewPage<PublishedModels.PostCategoryPage>`
  con `Model.CategoryName / Description / Name / Url()` typed.
- `ProductCategoryPage.cshtml` → idéntico patrón.
- **`ProductPage.cshtml` skip** — el legacy view itera `productImages`
  como `IEnumerable<IPublishedContent>` pero el typed model lo expone
  como `MediaWithCrops` singular (DataType `Multiple=false`). Refactor
  requiere coordinación con schema. Diferido.

### Ola 75.4 — typed views Flow (1 commit `a94fd40`)

- `FlowDefinition.cshtml` → typed `Model.FlowKey / FlowTitle /
  FlowDescription`.
- `FlowStep.cshtml` → typed `Model.StepLabel / IsTerminal / Id` +
  `Model.Parent as FlowDefinition` cast (elimina string-matching del
  ContentType.Alias).

### Ola 82 — `IEmailTemplateRenderer` (1 commit `9ec2911`)

**Seam** (`Synergos.CMS.Interfaces/IEmailTemplateRenderer.cs`):
```csharp
Task<string> RenderAsync<TModel>(string viewName, TModel model, CancellationToken ct);
```

**Default impl** `RazorEmailTemplateRenderer`:
- Singleton sobre `IRazorViewEngine` + `ITempDataProvider` + `IServiceProvider`.
- Construye `HttpContext` sintético — funciona desde
  `IHostedService` sin request activo.
- Convención: `viewName "X"` → resuelve `Views/Emails/X.cshtml`.
- Lanza si view no existe (fail-fast — mejor que email silently roto).

**Email layout + 2 templates iniciales**:
- `Views/Emails/_Layout.cshtml`: chrome inline-CSS email-safe (table
  600px max-width, header con SiteName, footer copyright). Variables
  ViewBag: `Title`, `SiteName`, `PreheaderText`.
- `Views/Emails/PasswordReset.cshtml`: saludo + CTA negro + URL
  fallback legible.
- `Views/Emails/FormNotification.cshtml`: header metadata + tabla de
  campos (HtmlEncode automático via `@value`).

**View models** (`Synergos.CMS.Web/Services/EmailModels.cs`):
- `PasswordResetEmailModel(DisplayName, ResetUrl, SiteName)`
- `FormNotificationEmailModel(FormKey, Fields, ClientIp, Referrer,
  ReceivedAtUtc, SiteName)`
- `EmailConfirmationEmailModel(DisplayName, ConfirmUrl, SiteName)`
  (para Ola 83)

**Adoption**:
- `AccountController.ForgotPasswordPost`: reemplaza string concat por
  `_emailRenderer.RenderAsync("PasswordReset", model)`.
- `FormSubmissionsController.SendNotificationAsync`: reemplaza string
  concat por `_emailRenderer.RenderAsync("FormNotification", model)`.

Wire: `SeamComposer.AddSingleton<IEmailTemplateRenderer, RazorEmailTemplateRenderer>()`.

### Ola 83 — Email confirmation post-registro (1 commit `c680bf5`)

**Schema** (`MembersSettings.RequireEmailConfirmation`, default false
— opt-in): si true, registro NO firma sesión inmediata.

**`IMemberAuthService` extends**:
- `RequestEmailConfirmationAsync(email, ct) → EmailConfirmationRequestResult`
  (`MemberExists`, `AlreadyConfirmed`, `Token?`, `DisplayName?`).
  Idempotente para emails ya confirmados.
- `ConfirmEmailAsync(email, token, ct) → MemberAuthResult`. Idempotente
  si ya confirmado.

`DefaultMemberAuthService` impl via `IMemberManager.GenerateEmailConfirmationTokenAsync`
+ `ConfirmEmailAsync` de Umbraco.

**`AccountController` extends**:
- `RegisterPost`: lee `RequireEmailConfirmation`. Si true,
  `SignInImmediately=false` + `SendEmailConfirmationLinkAsync` +
  redirect `/account/registered`.
- NUEVO GET `/account/registered` → view "Revisa tu email".
- NUEVO GET `/account/confirm-email?email=X&token=Y` → success
  redirect `/account/login?msg=email-confirmed`; failure render con
  mensaje contextual.
- `SendEmailConfirmationLinkAsync` helper: try-catch defense, no
  rompe pipeline si SMTP falla.

**Views** (`Account/Registered.cshtml` + `Account/ConfirmEmail.cshtml`):
Layout=null, dictionary keys con fallback inline.

**Email template** (`Views/Emails/EmailConfirmation.cshtml`):
CTA verde para distinguir visualmente del password reset (CTA negro).

**Analytics events**: `account.email-confirmed` +
`account.email-confirm-failed`.

## Consequences

**Positivas:**

- **Emails branded consistentes**: PasswordReset + FormNotification +
  EmailConfirmation usan `_Layout.cshtml` shared con SiteName/footer/
  preheader. Designer puede iterar el styling sin tocar C#.
- **HtmlEncode automático**: Razor `@value` aplica encoding sin que el
  caller tenga que recordar. Cero riesgo de XSS-via-email.
- **Email confirmation flow seguro**: token Umbraco-generado
  (security stamp), TTL gobernado por policy de Umbraco, idempotente
  para reconfirmaciones, anti-enumeration en RequestPasswordReset
  preservado.
- **Typed views progress**: 7 de 17+ views ya en typed (PlatformRoot,
  SearchPage, PostPage, PostCategoryPage, ProductCategoryPage,
  FlowDefinition, FlowStep). Pattern establecido y replicable.
- **Cero NuGet packages nuevos** — todo con APIs existentes de
  ASP.NET Core + Umbraco.

**Negativas:**

- **`SiteName` hardcoded como "Synergos"**: en consumers de
  `_emailRenderer.RenderAsync`, el SiteName se pasa fijo. Future
  refinement: resolver del siteRoot via `IBrandingProvider` para
  emails brand-aware en deploys multi-brand.
- **Email confirmation no resend**: si el visitante perdió el email
  o el token expiró, no hay UX explícita de "reenvíame". Workaround:
  vuelve a registrar (devuelve "email-taken" pero no re-genera
  token). Mejorable con endpoint `/account/resend-confirmation` —
  diferido.
- **`ProductPage` aún sin typed**: el legacy iterates productImages
  como collection pero el typed model es singular. Refactor requiere
  decisión arquitectónica (migrar gallery a Block Grid sections vs
  agregar prop `productGallery` con MediaPicker3 Multiple=true).
  Diferido.
- **Email templates inline-CSS solo**: para client compatibility (Outlook
  desktop, Gmail web, etc.) inline-CSS es necesario. Mantener templates
  compactos agrega fricción al designer. Diferido — `IEmailTemplateRenderer`
  permite swap a Mjml/Foundation for Emails compiler en futura ola.

**Neutras:**

- 5 commits + ADR consolidado.
- 0 schema rompedor.
- 0 GUIDs nuevos.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 75.3 | `43b0521` | PostCategoryPage + ProductCategoryPage typed |
| 75.4 | `a94fd40` | FlowDefinition + FlowStep typed |
| 82 | `9ec2911` | IEmailTemplateRenderer + RazorEmailTemplateRenderer + 3 records EmailModels + _Layout + PasswordReset + FormNotification templates + adopt en 2 controllers |
| 83 | `c680bf5` | MembersSettings.RequireEmailConfirmation + IMemberAuthService extends + AccountController.Registered + ConfirmEmail endpoints + 3 views (Registered/ConfirmEmail/EmailConfirmation email) |
| 0044 | (este) | ADR consolidado |

## Próximas direcciones

- **Ola 75.5+**: typed views remaining — _Layout (master master con
  fallback brand), PageBase/Basic/Landing/Bare via PageBaseResponse
  intermedio (refactor más invasivo), Account/Login/Register/Profile
  (Layout=null sin Model), Error.cshtml (typed model ya existe pero
  ViewModel intermedio del controller).
- **Ola 78** (deferred): backoffice section custom AngularJS para
  gestionar transversales — listado flat con quick actions
  publicar/despublicar/schedule.
- **Ola 84**: cart abandonment Redis adapter para multi-instancia.
- **Ola 85**: resend email confirmation endpoint
  `/account/resend-confirmation`.
- **Ola 86**: brand-aware email SiteName via `IBrandingProvider`
  resolution en `IEmailTemplateRenderer` consumers.

## References

- ADR 0030 — Forms internal submission (form notifications now
  templated)
- ADR 0034 — Member self-service (password reset templated, email
  confirmation closed)
- ADR 0035 — Email transactional runtime (templating layer added)
- ADR 0040 — Gran Consolidación (typed views ModelsBuilder setup)
- ADR 0042 — Error pages + Typed views first batch
- ADR 0043 — Email consumers wired
