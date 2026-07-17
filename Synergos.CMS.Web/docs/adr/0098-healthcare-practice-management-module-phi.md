# ADR 0098 — Módulo Healthcare (practice-management: historia, agenda, recetas) + frontera PHI

- **Status:** Proposed (proyección — el caso duro: PHI + cumplimiento)
- **Date:** 2026-06-25
- **Deciders:** Arquitecto + agente, fase SynergosLabs. Diseño verificado por workflow multi-agente con lente PHI/regulatorio contra código vivo.
- **Depende de:** ADR 0096 (module-mount). Frontera regulatoria análoga a ADR 0095.

## Context

El arquitecto quiere un módulo de negocio "healthcare" con **histórico de pacientes,
agenda de consultas y recetas médicas**, como app Angular completa. Es un consumidor
del patrón module-mount (ADR 0096), pero el **caso duro**: maneja **PHI** (datos de
salud — sensibles), con implicaciones legales que ningún otro módulo tiene.

**Postura regulatoria (no negociable):** el sistema es **RECORD-KEEPER /
practice-management**. **Registra** historia, agenda, y almacena/genera PDFs de
recetas que el **profesional licenciado** crea. **NO diagnostica, NO aconseja, NO
toma decisiones clínicas.** Es la misma frontera de gobernanza que la capa IA ("la
IA propone, el arquitecto publica"): aquí, "el sistema registra, el profesional
decide".

**Hechos verificados contra código vivo:**

1. El SQLite de Umbraco guarda contenido **sin cifrar** → PHI ahí sería inaceptable.
   Pacientes/citas/recetas son datos **operacionales**, no contenido editorial.
2. El bridge `window.synergos` (`HostBridgeMember`) **incluye `Email`** — un
   identificador HIPAA Safe-Harbor que se filtraría client-side en toda página
   healthcare.
3. El precedente de cifrado (`FileSystemMemberTwoFactorStore`) usa
   `IDataProtectionProvider` pero con `File.WriteAllText` — **no atómico**.
4. `FileSystemGdprRtbfCoordinator` hoy **hard-deletes** el Member tras anonimizar
   comments/forms → dejaría PHI huérfana si el `patientKey` cuelga del `MemberKey`.
5. `IMemberAccessGate` solo expone datos del **miembro actual** — no puede responder
   "este doctor puede ver a este paciente".
6. `FeatureFlagsSettings.RequireTwoFactorForRoles` está **deferred** — el 2FA
   obligatorio para staff es trabajo nuevo, no reuso.

## Decision

App Angular completa (`<synergos-healthcare>`, Tier=experience) sobre module-mount;
datos PHI por `HealthcareApiController` detrás de seams; **store dedicado cifrado**,
**autorización centralizada fail-closed**, y **frontera regulatoria explícita**.

### 1. Seams nuevos (Interfaces, puros; 4 tests canónicos cada uno)

**Dominio (3):**
- **`IPatientRepository`** — historia clínica. Records inmutables **versionados**
  (append, no overwrite). `GetAsync` / `UpsertAsync` / `ListAsync(PatientQuery)`.
  `PatientSummary` (listados) sin PHI sensible.
- **`IAppointmentScheduler`** — agenda. `BookAsync` (anti-overbooking) /
  `ListAsync` / `CancelAsync` / `AvailableSlotsAsync`. Store en UTC; TZ de display
  de `cfgHealthcareSettings`.
- **`IPrescriptionService`** — recetas **append-only** (espejo de audit). `IssueAsync`
  (solo rol `doctor`) / `ListForPatientAsync` / `GetAsync` / `RenderPdfAsync`.
  RECORD-KEEPER: genera el PDF del registro que creó el profesional; **NO valida
  clínica ni interacciones**.

**Compliance (2):**
- **`IConsentLedger`** — `GrantAsync` / `RevokeAsync` / `HasActiveConsentAsync`
  (paciente→doctor). Cada cambio audita.
- **`IPhiAccessGuard`** (corrección central) — **un único punto de decisión**,
  invocado **primero en CADA endpoint**. Combina: (a) `HasAnyRole`, (b) ownership
  `patientKey == CurrentMemberKey` (self-service), (c) `IConsentLedger` (doctor→
  paciente), (d) **escribe el audit PHI ANTES de devolver allow/deny**. Regla
  **fail-closed**: si el audit falla, se **deniega** (no-audit ⇒ no-access). Evita
  drift de autorización entre endpoints.

### 2. Persistencia PHI — store dedicado CIFRADO + ATÓMICO (corrige #1, #3)

- **NO** en SQLite de Umbraco (#1). Store dedicado bajo `App_Data/syn-healthcare/`.
- Cifrado at-rest: `IDataProtectionProvider.CreateProtector("Synergos.Healthcare.PHI.v1")`
  (master key `App_Data/Keys/`, ya operativa por 2FA).
- **Escritura atómica real** (temp + `File.Move`), append-only con flush para
  ledgers — **NO** propagar el bug `WriteAllText`/`AppendAllText` del precedente (#3).
- **Disclaimer explícito (no negociable):** esto es **"at-rest baseline, NO
  HIPAA-grade"**. HIPAA-grade real (AES-256 dedicado + rotación/HSM/FIPS, audit
  WORM/hash-chain, BAA con hosting) queda como **ADR futuro listado**. No presentar
  el filesystem cifrado como "compliant".
- **Diferido**: si llega multi-instancia o >100K registros → SQLite **separado**
  column-encrypted (no el de Umbraco, no EF) detrás del **mismo seam** (invisible al
  módulo).

### 3. Minimización: bridge SIN Email en páginas PHI (corrige #2)

`window.synergos` filtra `Email` (#2). Entregable nuevo: variante
`HostBridgeMemberMinimal` (key + display + roles, **sin Email**) — o flag de omisión
— para las páginas-módulo PHI. **Test**: el JSON de `window.synergos` en una página
healthcare no contiene ningún identificador Safe-Harbor. Logs: cero PHI (la auditoría
va por `IAuditTrailWriter`, no por logs).

### 4. Auditoría + retención (HIPAA vs default)

- **Todo acceso PHI auditado** (`IAuditTrailWriter`, slugs nuevos:
  `patient.record.viewed/updated`, `appointment.booked/cancelled`,
  `prescription.issued/viewed/exported`, `consent.granted/revoked`,
  `phi.access-denied` — incluye intentos denegados), escrito por `IPhiAccessGuard`.
- **Audit PHI = retención indefinida**: NO se incluye en la policy de purga de audit
  (HIPAA). **Records clínicos = 6 años**: `HealthcareRetentionPolicy` (RetainDays
  default 2190), idempotente.

### 5. RTBF healthcare-aware (corrige #4)

GDPR Art.17 borra el Member; HIPAA exige retener records 6 años. Resolución: extender
`IGdprRtbfCoordinator` + nuevo `IHealthcareDataAnonymizer` que, bajo retención legal,
**de-identifica** (18 identificadores Safe-Harbor) en vez de hard-delete, preservando
el prescription ledger y el audit (exención análoga a la GDPR 17(3) ya implementada).
**Orden correcto**: de-identificar PHI **antes** del delete del Member, y **desacoplar
`patientKey` de `MemberKey`** para no dejar PHI huérfana (#4).

### 6. Roles + 2FA obligatorio staff (corrige #6)

Roles `doctor` / `nurse` / `reception` / `patient` como **Member Roles** (datos, no
schema). **2FA obligatorio para staff clínico** (doctor/nurse/reception) — gate de
enrollment en el login flow. **Es entregable nuevo** (`RequireTwoFactorForRoles` está
deferred, #6), no reuso. Paciente: 2FA opt-in. Scoping fino por rol = server-side en
el guard; la UI gate (`window.synergos.member.roles`) es cosmética.

### 7. Frontera regulatoria (la línea legal mínima — no diferible)

- `clinicalDisclaimerText` de `cfgHealthcareSettings` se renderiza **siempre**.
- **Cero endpoint** con verbos clínicos (suggest/diagnose/recommend/interactions).
  **Test de arquitectura** que falle si aparece un endpoint con verbo clínico.
- Las recetas las **crea** el profesional (rol `doctor`); el sistema solo las
  registra y genera el PDF. `IPrescriptionService` no valida clínica.

### 8. Anti-overbooking sin romper el grafo (principio 1)

Cálculo de solapamiento de intervalos = lógica **pura en Application** (recibe slots
ocupados + request → conflict/ok, testeable sin IO). El **lock atómico + persistencia**
= Web (`LockingAppointmentScheduler` con `IDataProtectionProvider`/FS). Application
nunca toca filesystem ni DataProtection.

### 9. Render Angular + degradación

App standalone en Shadow DOM; rutas `#/patients`, `#/agenda`, `#/prescriptions`,
`#/me` (portal paciente self-service); router hash/`<base href>` (ADR 0096); lazy
sub-rutas; tema por tokens; CustomEvents (`syn:healthcare:appointment-booked`,
`...:prescription-issued`, `...:error`); error boundary. **CSR-only**; el `<noscript>`
del module-mount apunta a un **form intake** ("pedir cita") vía `IFormSubmissionHandler`
(honeypot/rate-limit/audit ya endurecidos).

### 10. `siteRootKey` — single-origin (Decisión D1 de ADR 0096)

Fase 1 = **un deploy = una práctica clínica**, sin partición por `siteRootKey`
(coherente con todos los stores vivos). `HealthcareDataIntegrityProbe`
(`ISchemaHealthProbe`) **falla si detecta datos de >1 siteRoot**. Multi-clínica
multi-siteRoot = el ADR transversal diferido de 0096 (nunca `ITenantContext`).

## Phases

| Fase | Entregable | Verificable |
|---|---|---|
| **0** | Este ADR + frontera regulatoria + índice §11.2. | ADR mergeado. |
| **1** | (Hereda Fase 1 de ADR 0096: mount + `<noscript>` + POC tokens.) `<noscript>`→form intake. | Mount genérico vivo. |
| **2** | 5 seams (Interfaces) + overbooking puro (Application) + `DataProtected*`/`Locking*`/`AppendOnly*`/`FileSystemConsentLedger`/`DefaultPhiAccessGuard` (Web, **write atómico**) + `HealthcareApiController` guard-first. **4 tests por seam + tests de ENDPOINT** (anónimo→401, rol-malo→403, doctor-sin-consent→403, patient→patientKey-ajeno→403). | Build 0 CS; Application sin refs AspNetCore/Umbraco; endpoints gated; guard fail-closed. |
| **3** | `cfgHealthcareSettings` (uSync quad-check) + nodo Settings (vía IContentService) + `compMemberGating` + disclaimer renderizado + roles creados + **2FA obligatorio staff** + **bridge minimal sin Email**. | Anónimo bloqueado SSR; doctor entra con 2FA; `window.synergos` sin Email; disclaimer visible; tokens atraviesan Shadow DOM. |
| **4** | App Angular real: patients/agenda/prescriptions/me + PDF receta + lazy + tema + CustomEvents + error boundary. | `synergos-smoke-test`: hidrata, CRUD contra API, PDF descarga, role-gates UI coherentes con server. |
| **5 (compliance hardening)** | `HealthcareRetentionPolicy` (6 años) + audit PHI exento de purga + RTBF healthcare-aware (`IHealthcareDataAnonymizer`, de-identifica antes del delete) + export paciente (portabilidad) + `HealthcareDataIntegrityProbe` + recordatorios (IEmailService) + **test de arquitectura anti-verbos-clínicos**. | Retención idempotente; RTBF preserva ledger+audit; probe verde; cross-siteRoot probe falla ante fuga. |
| **6 (diferido)** | Swap de persistencia a SQLite separado column-encrypted (mismo seam). | El módulo no cambia (solo conoce el API). |

> **El disclaimer regulatorio, el guard centralizado fail-closed, el bridge sin
> Email y el write atómico NO son diferibles** — son la frontera legal/seguridad
> mínima. El resto del hardening (Fase 5) puede ser una ola separada (Decisión D6).

## Consequences

**Positivas:** practice-management real reutilizando members/2FA/audit/RTBF/email
vivos; PHI aislada y cifrada fuera del árbol CMS; autorización en un solo punto
auditado; frontera regulatoria explícita que mantiene el producto del lado correcto
de la ley.

**Costos/riesgos:** PHI eleva el listón (cifrado, atomicidad, minimización del bridge,
RTBF de-identificación, retención dual) — varios son entregables nuevos, no reuso;
el cifrado baseline **no es HIPAA-grade** (disclaimer); 2FA obligatorio staff es
trabajo nuevo; tentación de `ITenantContext` para multi-clínica → prohibido.

## Decisiones abiertas

- **D2 — persistencia PHI**: ¿aceptar "at-rest baseline" con disclaimer (Fase 1) o
  exigir HIPAA-grade antes de tocar PHI real? Recomendado: baseline + disclaimer +
  ADR HIPAA-grade futuro. *Define si 0098 es "demo-ready" o "production-ready".*
- **D6 — alcance**: ¿MVP record-keeper (Fases 1-4) y diferir el hardening (Fase 5),
  o todo de una? Recomendado: MVP + hardening como ola separada — pero disclaimer +
  guard fail-closed + bridge sin Email + write atómico van en el MVP.

## Addendum (2026-06-25) — Healthcare es un VERTICAL (siteRoot), no un bloque

Aclaración del arquitecto: Healthcare es un **dominio/vertical completo** con
su **propio siteRoot**, no un `elementSynModuleMount` sobre una página ajena.

- **Vive como siteRoot propio** bajo el `platformRoot` (la entrada de Synergos),
  hermano de Entidad / Blogs / Ecommerce. "Un motor, mil productos": Healthcare
  es uno de los productos. (El DocType `siteRoot` ya existe; se crea el nodo
  Healthcare + sus contenidos vía `IContentService`, "componer nunca hardcodear".)
- **Identidad propia** por el sistema por-siteRoot (**ADR 0094**): su brand →
  mapeo a tokens canónicos, su nav, sus páginas, su tema — sin `if(brand.Key==)`.
  Definir el tema Healthcare al scaffoldear el siteRoot. (Nota: la identidad de
  Synergos **core** todavía está en refinamiento — es trabajo aparte y previo.)
- **Estructura del siteRoot Healthcare:** páginas públicas (landing del
  consultorio + "pedir cita" vía form intake `IFormSubmissionHandler` — abiertas)
  **+** páginas de la app clínica (gated por `compMemberGating` a roles
  doctor/nurse/reception/patient) que montan la app vía `elementSynModuleMount`
  (ADR 0096). Portal del paciente (`#/me`) bajo gating de rol `patient`.
- **Angular pesado = tier `experience`** (>50KB, budget <200KB el entry): app
  standalone con lazy sub-rutas (code-splitting), que **externaliza Angular vía
  el import map compartido** (ADR 0099 — ya emitido en el `<head>`) y se publica
  al CDN como cualquier bundle (lado UI, su CLI). El CMS la monta igual.
- **PHI con multi-vertical (refina §10):** el deploy tiene varios verticals;
  **solo el siteRoot Healthcare** toca el store PHI (vía `HealthcareApiController`
  gated). NO es multi-tenant — es multi-siteRoot nativo por hostname. La decisión
  D1 (single-origin, sin `siteRootKey`) se mantiene: **una práctica clínica por
  deploy**; el `HealthcareDataIntegrityProbe` falla si detecta PHI fuera del
  vertical Healthcare.
- **Desacople CMS↔UI (ADR 0083):** el CMS define el siteRoot + schema + API +
  identidad; el UI construye+publica la app `experience` al CDN. Se encuentran en
  el CDN + los contratos, nunca en código.
- **Fase nueva 0.5 — scaffold del siteRoot Healthcare** (nodo siteRoot +
  identidad/brand + nav + páginas públicas + gating) **antes** de la Fase 4 (app
  Angular real).

## Relación con otros ADRs

Depende de 0096 + 0099 (distribución) + 0094 (identidad por-siteRoot) + 0083
(desacople CMS↔UI). Extiende 0034/0035 (members), 0067 (audit), 0070/0088 (retención),
0076/0081/0084 (2FA), 0078 (GDPR/WCAG/backup), 0030 (forms intake). Frontera
regulatoria análoga a 0095 ("registra/propone, no decide/diagnostica"); puede anidar
`<synergos-chatbot>` (0095) record-keeper-aware (ej. "¿hay slot el martes?") sin
cruzar a consejo clínico.
