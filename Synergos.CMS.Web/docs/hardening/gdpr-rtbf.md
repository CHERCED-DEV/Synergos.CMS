# GDPR — right-to-be-forgotten flow

- **Status:** Initial doc (Ola 187).
- **Standard:** [GDPR Article 17 — Right to erasure](https://gdpr-info.eu/art-17-gdpr/).
- **Scope:** Members + their persisted artifacts (comments, form
  submissions, audit references, 2FA secrets, search analytics).

## Personal data inventory

Personal data persisted per Member:

| Surface | Linked by | Erasure path |
|---|---|---|
| Member record | `IMember` GUID + email + name | `IMemberRosterWriter.DeleteAsync` (Olas 181-184) |
| 2FA secret + recovery codes | `App_Data/syn-2fa/{memberKey}.json` | `IMemberTwoFactorService.DisableAsync` (auto-cascade del delete via AdminController.DeleteMember) |
| Comments authored | `App_Data/syn-comments/{nodeId}.json` Member.Id field | Anonymize (replace `authorName`/`email` con `[deleted]`) |
| Form submissions con email | `App_Data/syn-forms/{formKey}/{storageId}.json` | Anonymize (replace email field con `[deleted]`) |
| Audit events done by member | `App_Data/syn-audit/{yyyy-MM-dd}.jsonl` ActorEmail field | **Preserved** — audit es legal requirement immutable. Anonymized via separate deletion request after retention. |
| Login analytics | `App_Data/syn-search-analytics/*.jsonl` (si captura email) | Anonymize / drop |
| Cart history | `App_Data/syn-carts/*.json` if persisted by user | Hard delete |

## Architectural fit

GDPR RTBF requires **3 capabilities** the CMS must provide:

1. **Identify** — Locate all personal data linked to a Member key.
2. **Erase or anonymize** — Hard-delete where possible, replace with
   `[deleted]` placeholders where the record must persist for
   integrity (FK refs, audit immutability).
3. **Audit the erasure** — Log "Member X requested erasure on
   {date}, processed by admin Y on {date}" for compliance evidence.

Current state:

| Capability | Status | Notes |
|---|---|---|
| Identify | Partial | `IMemberRosterReader.GetRosterPage` exposes member by key/email. Cross-table query (comments/forms by author) not yet automated. |
| Erase | Partial | `IMemberRosterWriter.DeleteAsync` handles Member record. 2FA cascades. Comments/forms anonymization is **manual** (deferred). |
| Audit erasure | Yes | `member.delete` event in `App_Data/syn-audit/` (ADR 0067/0068). |

## Manual procedure (current)

Until automated, RTBF requests are processed manually by admin:

1. **Receive request** — email or web form.
2. **Identify Member** — query `/admin/members?role=...` o backoffice.
3. **Hard-delete Member** — `/admin/members/{key}/delete` via dashboard.
   - Cascades: 2FA secret + recovery codes via `member.2fa-reset`
     auto-trigger (Olas 178-180).
   - Audit logged: `member.delete` event.
4. **Anonymize comments by author** — manual filesystem edit:
   ```bash
   # Find comments by member email or member ID
   grep -l '"authorEmail":"alice@example.com"' App_Data/syn-comments/*.json

   # For each match: edit JSON, replace authorName + authorEmail
   # con "[deleted]" + "[deleted]@gdpr.local"
   ```
5. **Anonymize form submissions** — manual filesystem edit:
   ```bash
   grep -l '"email":"alice@example.com"' App_Data/syn-forms/**/*.json
   ```
6. **Search analytics** — drop user-identifiable rows. Currently
   the analytics store does not capture email (only query strings),
   so usually nothing to do.
7. **Log the erasure** explicitly for audit:
   ```bash
   echo '{"Id":"...","OccurredAtUtc":"...","ActorEmail":"admin@x","Action":"gdpr.rtbf-processed","Resource":"memberKey=...","Outcome":"success","Detail":"originalEmail=alice@example.com,anonymizedComments=N,anonymizedForms=M"}' >> App_Data/syn-audit/$(date +%Y-%m-%d).jsonl
   ```
8. **Notify the requester** — email confirming completion.

## Automated procedure (proposed)

Future ola: `IGdprRtbfCoordinator` seam con
`Task<RtbfResult> ProcessRequestAsync(memberKey, ct)` que:

1. Reads Member from `IMemberService`.
2. Calls `IMemberRosterWriter.DeleteAsync`.
3. Iterates `App_Data/syn-comments/*.json` y reemplaza fields del
   Member match.
4. Iterates `App_Data/syn-forms/**/*.json` y reemplaza email field.
5. Audits `gdpr.rtbf-processed` con counts.
6. Returns aggregate `(MemberDeleted, CommentsAnonymized,
   FormsAnonymized, AuditPreserved)` para reporting al requester.

Admin endpoint nuevo: `POST /admin/members/{key}/gdpr-erase` con
form requiriendo confirmation token (mismo pattern del Members.delete
dialog).

Deferred — el flow es complejo y merece ADR aparte con threat model
(falsa request, partial failure handling, retention overrides).

## Audit trail special case

Audit events son **legalmente requeridos como inmutables** para forensic
review. GDPR Article 17(3) exime el processing necesario para
"compliance with a legal obligation". Por lo tanto:

- Audit events que mention al Member **NO se borran**.
- Si llega un request específico de auditar logs, se procesa tras el
  retention period (current `AuditRetentionDays` default 90 — ADR 0070).
- Si la jurisdicción requiere shorter retention, ajustar setting al
  mínimo legal.

## Data minimization

Cada surface debe minimizar lo que captura:

- **Comments**: solo `authorName` + `authorEmail` cuando el commentor
  optó por leave un comment público. Sin IP / UA persistido.
- **Form submissions**: capturan lo que el form schema declara. Forms
  con campos sensibles (DNI, healthcare data) requieren explicit
  consent + encryption-at-rest (ADR futuro).
- **Audit**: solo `ActorEmail` del moderator (admin staff), no del
  Member operado. Resource fields tienen `memberKey:N` GUID (no PII).
- **Search analytics**: `query` text + timestamp + result count. No
  IP / Member identification. ✅ Compliant.

## Cookies

Cookie consent block (`cookieConsent` SynHost) está implementado pero
no formalmente vinculado al GDPR consent record. Para sites con tráfico
EU significativo, considerar:

- Persistir el consent decision con un signed cookie (1 year TTL).
- Exponer "Manage cookies" link del footer abriendo el banner de
  vuelta para re-consent.
- Auditar cambios de consent ("user-X re-consented at Y").

Diferido — depende de si el site target tiene tráfico EU regulado.

## References

- ADR 0034 — Member self-service runtime.
- ADR 0067 — Audit trail seam.
- ADR 0068 — Member roster writer (delete action).
- [GDPR Article 17](https://gdpr-info.eu/art-17-gdpr/) — Right to erasure.
- [ICO guidance on RTBF](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/individual-rights/the-right-to-erasure/).
