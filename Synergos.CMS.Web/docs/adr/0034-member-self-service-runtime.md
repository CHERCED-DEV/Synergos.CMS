# ADR 0034 — Member self-service runtime: register + login + profile (Ola 64)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 64
- **Extiende:** ADR (Ola 52.C — `IMemberAccessGate` + Members gating)
- **Related:** ADR 0009 (extension seams), ADR 0030 (Forms — referente
  patrón seam + controller)

## Context

Ola 52.C cerró Members gating (read-only): `IMemberAccessGate` +
`MemberGatingHandler` resuelven "este miembro puede ver esta página".
Ola 56.1 hizo `MembersSettings.LoginPath` configurable (default
`/login`). Pero el sitio fresh no tenía:

- Página pública para que el visitante se registre.
- Página de login funcional (el editor podía crear `pageBasic` en
  `/login` con un form a un endpoint inexistente).
- Página de perfil para ver/cambiar datos del miembro autenticado.
- Endpoint para cambiar contraseña.
- Endpoint para cerrar sesión.

Sin esto, el flujo Members era half-shipped: gating funcionaba pero no
había manera de que alguien creara una cuenta y se autenticara.

## Decision

Cerrar el ciclo Members agregando un seam write + controller MVC con
3 vistas Razor — sin schema editorial nuevo (controller-driven, no
DocType).

### Seam (Ola 64.1)

**`Synergos.CMS.Interfaces/IMemberAuthService.cs`**:

```csharp
Task<MemberAuthResult> RegisterAsync(MemberRegisterRequest, CancellationToken);
Task<MemberAuthResult> LoginAsync(string emailOrUsername, string password, bool isPersistent, CancellationToken);
Task LogoutAsync(CancellationToken);
Task<MemberAuthResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken);
```

Records:
- `MemberRegisterRequest(Email, Password, DisplayName, SignInImmediately = true)`
- `MemberAuthResult(Success, ErrorCode?, ErrorMessage?)` con factories
  `Ok()` / `Fail(code, message?)`.

`ErrorCode` slugs estables: `invalid-input`, `email-taken`,
`weak-password`, `invalid-credentials`, `locked-out`,
`current-password-wrong`, `not-authenticated`, `unknown`.

**`Synergos.CMS.Web/Services/DefaultMemberAuthService.cs`**:
implementa via `IMemberManager` + `IMemberSignInManager` de Umbraco.

- `RegisterAsync`: `FindByEmailAsync` para detectar duplicado;
  `MemberIdentityUser.CreateNew("Member" type)`; `CreateAsync`;
  `SignInAsync` opcional.
- `LoginAsync`: `PasswordSignInAsync` con `lockoutOnFailure: true`.
- `LogoutAsync`: `SignOutAsync()`.
- `ChangePasswordAsync`: `GetCurrentMemberAsync` + `ChangePasswordAsync`.
- `MapIdentityCode` traduce IdentityErrors de ASP.NET Identity a slugs
  estables (`DuplicateEmail` → `email-taken`,
  `PasswordRequiresDigit` → `weak-password`, etc.).

Wire en `SeamComposer`:
`services.AddTransient<IMemberAuthService, DefaultMemberAuthService>()`.
Transient porque IMemberManager/SignInManager son scoped per-request.

### Controller + Views (Ola 64.2)

**`AccountController`** (`[Route("account")]`, MVC clásico — no
ApiController):

| Verb | Path | Action |
|---|---|---|
| GET | `/account/login` | View(Login) con returnUrl + error |
| POST | `/account/login` | LoginAsync → PRG a returnUrl o `?error=` |
| GET | `/account/register` | View(Register) con error |
| POST | `/account/register` | RegisterAsync → PRG a /account/profile o `?error=` |
| POST | `/account/logout` | LogoutAsync → PRG a returnUrl |
| GET | `/account/profile` | View(Profile) con displayName + roles + msg |
| POST | `/account/profile/password` | ChangePasswordAsync → PRG a `/account/profile?msg=` |

**`SafeReturnUrl`** restringe a `Uri.TryCreate(UriKind.Relative)` —
defensa contra open redirect attacks.

Profile gate: `_gate.IsAuthenticated` false → redirect a
`/account/login?returnUrl=/account/profile`.

**Views** (`Views/Account/{Login,Register,Profile}.cshtml`):
- `Layout = null` — chrome HTML mínimo inline. Evita dependencia de
  `UmbracoContext.PublishedRequest.PublishedContent` (que no existe
  en routes controller-driven).
- `<meta name="robots" content="noindex" />` — pages auth no
  indexables.
- Forms POST nativos sin JS. `autocomplete` attrs correctos
  (`email`, `current-password`, `new-password`, `name`).
- Error feedback con `role="alert"` + `aria-live="polite"`.
- Dictionary keys con fallback inline (24 keys total: `Account.Login.*`,
  `Account.Register.*`, `Account.Profile.*`). Fallbacks en es-CO
  funcionan out-of-the-box; los XML uSync para internacionalizar
  vienen en micro-ola futura cuando se justifique.

## Consequences

**Positivas:**

- **Members ciclo completo end-to-end**: deploy fresh permite a un
  visitante anónimo registrarse → autenticarse → ver perfil →
  cambiar password → cerrar sesión, sin schema editorial ni JS.
- **Defensa estándar**: `lockoutOnFailure: true` en login, regex de
  password policy de Umbraco, `autocomplete` attrs (browser passwords
  managers funcionan), `noindex` en pages auth, `SafeReturnUrl`
  contra open redirect.
- **Seam intercambiable**: si el sitio adopta Synergos.API auth
  central (futuro), swap del binding sin tocar controller ni views.
  Si adopta Auth0/Cognito, igual — wrap del provider en una nueva
  impl de `IMemberAuthService`.
- **PRG consistente con resto del CMS**: Forms (Ola 60), Cart
  (Ola 57), Flow (Ola 41/58) — todos hacen POST → Redirect → GET
  con feedback via querystring. Account sigue el mismo patrón.
- **A11y baseline**: labels asociados via `for=`, `aria-required`,
  `aria-live` en feedback, `role="alert"` en errores.

**Negativas:**

- **Sin password reset por email**: requiere infra SMTP que aún no
  está en el CMS. Diferido a futura ola con
  `IPasswordResetEmailSender` seam + token store + endpoints
  `/account/forgot-password` + `/account/reset-password`. Mientras
  tanto, el operador resetea via backoffice de Umbraco.
- **Sin verificación de email post-registro**: `SignInImmediately:
  true` por default — la cuenta queda activa y firmada inmediatamente.
  Para sitios que requieren email-confirm antes de membership real,
  agregar flag en `MembersSettings.RequireEmailConfirmation` + paso
  de envío de token. Diferido.
- **Layout=null en views**: las pages auth no usan el `_Layout` con
  brand chrome. Decisión deliberada (controllers MVC no tienen
  PublishedRequest). El tradeoff es que el visitante "salta" del
  diseño del sitio a una página minimalista. Mejora futura: agregar
  `_AccountLayout.cshtml` que consume `IBrandingProvider` directo
  (sin pasar por PublishedContent) y emite header/footer brand-aware.
- **24 dictionary keys hardcoded inline (fallbacks)**: las traducciones
  funcionan vía fallback string del Razor. Si el operador quiere
  cambiar texto, debe editar el .cshtml. Crear los XML de Dictionary
  + workflow editor-friendly es trivial pero diferido.
- **Sin captcha en registro**: bots pueden crear cuentas en bulk.
  Para sitios públicos con valor alto, agregar HCaptcha/Turnstile.
  Diferido.
- **Sin profile editing más allá de password**: no hay edit del
  displayName/email/roles. Para casos editables, extender el seam con
  `UpdateProfileAsync(MemberProfileUpdate)`. Diferido.

**Neutras:**

- 0 schema editorial nuevo. 0 GUIDs.
- 1 seam (`IMemberAuthService`) + 1 default impl
  (`DefaultMemberAuthService`) + 1 controller (`AccountController`) +
  3 views (`Account/{Login,Register,Profile}.cshtml`).
- `IMemberAccessGate` (Ola 52.C) sigue siendo la única seam read-only
  del miembro actual; `IMemberAuthService` la complementa con writes.
  Cero overlap.

## Alternatives considered

- **Adoptar Umbraco Members AngularJS backoffice flow**. Descartado.
  El backoffice es para administradores, no para self-service público.
- **Razor Pages en lugar de MVC controller**. Descartado por
  consistencia — el resto del runtime usa controllers MVC con
  attribute routing.
- **Identity Server externo**. Diferido. Para sitios con SSO
  empresarial, swap del binding `IMemberAuthService`.
- **Magic-link login (passwordless)**. Diferido. Requiere infra
  SMTP igual que password reset. Mismo gating.
- **Profile editable de displayName/email**. Diferido — KISS
  para primer pase. Si se requiere, agregar `UpdateProfileAsync` al
  seam.
- **Crear DocTypes editoriales para Login/Register/Profile pages**.
  Descartado. La auth es funcional, no editorial. Si se quiere
  customizar copy, dictionary keys + override del view es suficiente.

## Implementation summary (Ola 64, 3 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-64.1)` | `2f83e50` | `IMemberAuthService` seam + `DefaultMemberAuthService` + wire en SeamComposer |
| `feat(ola-64.2)` | `6527014` | `AccountController` + 3 views Razor (Login/Register/Profile) con Layout=null |
| `docs(ola-64.3)` | (este) | ADR 0034 + index README |

## References

- Ola 52.C — `IMemberAccessGate` + `MemberGatingHandler` (read-only)
- Ola 56.1 — `MembersSettings.LoginPath` configurable
- ADR 0030 — Forms internal submission (referente PRG + seam +
  controller pattern)
- ADR 0009 — Extension seams
- Próxima ola natural: 65 — Email infrastructure
  (`IEmailSender` seam + SMTP adapter) → habilita password reset +
  email confirmation + form notifications
