# ADR 0068 — Member roster writer (lock/unlock) + audit trail integration (Olas 155-156)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

ADR 0063 introdujo `IMemberRosterReader` read-only para `/admin/members`.
La diferida lista del cap-150 §11.12 mencionaba "Members admin CRUD"
con la nota "merece ADR con threat model". El cap-160 ahora cierra
el subset reversible: **lock / unlock**.

Trade-off: lock/unlock son acciones reversibles e idempotentes — el
threat model es simple (solo admin/moderator/editor pueden ejecutarlas
y todas se registran en el audit trail). Los CRUD destructivos
(delete, password reset, role-toggle) merecen ADRs separados con
threat model más fuerte.

## Decision

### Ola 155 — IMemberRosterWriter seam (split de Reader por ISP)

```csharp
public interface IMemberRosterWriter
{
    Task<bool> LockAsync(Guid memberKey, CancellationToken cancellationToken);
    Task<bool> UnlockAsync(Guid memberKey, CancellationToken cancellationToken);
}
```

Split del `IMemberRosterReader` por ISP — el reader se consume desde
toda parte del dashboard que solo necesite listar; el writer solo
desde el AdminController que tiene actions POST. Implementaciones
swappable independientes.

Devolución `Task<bool>`:
- `true` — Member existe y la operación tuvo efecto (o ya estaba en
  el estado deseado — idempotent no-op).
- `false` — Member no existe.

### Ola 156 — UmbracoMemberRosterWriter impl

```csharp
public Task<bool> LockAsync(Guid memberKey, CancellationToken ct)
{
    var member = _memberService.GetByKey(memberKey);
    if (member is null) return Task.FromResult(false);
    if (!member.IsLockedOut)
    {
        member.IsLockedOut = true;
        _memberService.Save(member);
        _logger.LogInformation("Admin locked member key={Key} email={Email}",
            memberKey, member.Email);
    }
    return Task.FromResult(true);
}
```

`UnlockAsync` espejo + `member.FailedPasswordAttempts = 0` para que
un intento subsecuente no vuelva a triggerear el lockout
inmediatamente.

Wireado transient en `SeamComposer` (depende de scoped
`IMemberService`).

### AdminController POST actions

```csharp
[HttpPost("members/{memberKey:guid}/lock")]
public async Task<IActionResult> LockMember(Guid memberKey, CancellationToken ct)
{
    if (!_gate.HasAnyRole(ModeratorRolesCsv)) return Forbid();
    var ok = await _memberRosterWriter.LockAsync(memberKey, ct);
    await EmitAuditAsync("member.lock", $"memberKey={memberKey:N}",
        ok ? "success" : "failure", cancellationToken: ct);
    return RedirectToAction(nameof(Members));
}
```

`UnlockMember` espejo. Ambas emiten `EmitAuditAsync` (ADR 0067) con
action `member.lock` / `member.unlock` y outcome reflectando éxito
del Member-found check.

### Members.cshtml view

Columna "Acciones" agregada con un botón condicional por Member:
- **Locked** → 🔓 Desbloquear (form POST a `/unlock`)
- **No locked** → 🔒 Bloquear (form POST a `/lock`)

## Consequences

**Positivas:**

- **Operacional**: moderator puede desbloquear a un Member que
  triggereó el lockout por intentos fallidos sin abrir backoffice
  Umbraco.
- **Auditable**: cada lock/unlock deja trazo en
  `App_Data/syn-audit/{yyyy-MM-dd}.jsonl`. Si un moderator desbloquea
  a un Member malicioso, se ve quién lo hizo y cuándo.
- **ISP clean**: reader/writer split permite implementaciones swappable
  independientes — e.g., un test reader stub no requiere implementar
  writer.
- **Idempotent**: backoffice muestra el estado correcto al volver al
  listing tras la acción. No hay race entre dos moderators clickeando
  "lock" simultáneo.

**Negativas:**

- **Sin "lock con razón"**: actualmente lock no acepta una razón
  (e.g., "spam from this account") que se persistiría en audit detail.
  Mitigación futura: form con `<input name="reason">` que pase el
  string al `EmitAuditAsync.detail`.
- **Reset de attempts on unlock**: discutible si debería preservar el
  contador para que un nuevo fail re-triggeree lockout rápido. Decisión:
  reset porque normalmente unlock = "este fue un false positive,
  empieza fresco".
- **CRUD destructivo no incluido**: delete / password reset /
  role-toggle siguen siendo backoffice-only. Si llegan requirements,
  ADRs separados con threat model.

**Neutras:**

- 1 commit feat batch (Olas 155+156 unificadas) + 1 docs ADR.
- 0 GUIDs nuevos (la columna "Acciones" no requiere nuevo Dictionary
  key — los botones usan strings hardcoded por ahora).
- 0 NuGet packages nuevos.

## Implementation summary

| # | Foco |
|---|---|
| 155 | `IMemberRosterWriter` seam con LockAsync + UnlockAsync. |
| 156 | `UmbracoMemberRosterWriter` impl + AdminController actions LockMember/UnlockMember + audit emit `member.lock`/`member.unlock` + Members.cshtml view extendida con columna "Acciones". |
| 0068 | (este) ADR consolidado |

## Próximas direcciones

- **Lock con razón** — text input persistido en audit detail.
- **Members CRUD destructivo** — delete / password reset / role-toggle
  con threat model + ADR por feature.
- **Bulk lock** — checkbox + bulk action para múltiples Members.
- **i18n button labels** — extraer "🔒 Bloquear" / "🔓 Desbloquear" a
  Dictionary.

## References

- ADR 0063 — Member roster admin read-only (origen del seam).
- ADR 0067 — `IAuditTrailWriter` (donde se emiten los eventos
  `member.lock` / `member.unlock`).
- ADR 0034 — Member self-service runtime (origen de Members runtime
  donde se setea `IsLockedOut` automáticamente).
