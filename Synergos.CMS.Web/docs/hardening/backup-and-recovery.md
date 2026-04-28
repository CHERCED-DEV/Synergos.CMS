# Backup & disaster recovery — Synergos CMS

- **Status:** Initial doc (Ola 186).
- **Scope:** SQLite (dev) + SQL Server (prod) + uSync schema + App_Data
  artifacts (audit, comments, form submissions, 2FA secrets, member
  data).

## Persistence inventory

Every persistent surface that requires backup:

| Surface | Where | Critical? | Backup recipe |
|---|---|---|---|
| Umbraco DB | SQLite `umbraco/Data/Umbraco.sqlite.db` (dev) or SQL Server `dbo.umbraco*` (prod) | **Critical** — schema + content + members | Full + tx-log per RPO target |
| uSync schema | Repo `Synergos.CMS.Web/uSync/v9/*` | Critical — source-of-truth schema (ADR 0008) | Git history (already covered) |
| Comments | `App_Data/syn-comments/{nodeId}.json` | High — user-generated | rsync / S3 sync |
| Form submissions | `App_Data/syn-forms/{formKey}/{storageId}.json` | High — leads / contact data | rsync / S3 sync |
| Audit trail | `App_Data/syn-audit/{yyyy-MM-dd}.jsonl` | High — forensic | rsync / S3 sync (immutable) |
| 2FA secrets | `App_Data/syn-2fa/{memberKey}.json` | **Critical** — security credentials | rsync con encryption-at-rest target |
| Search analytics | `App_Data/syn-search-analytics/*.jsonl` | Medium — telemetry | rsync |
| Custom uploads | `wwwroot/media/*` | High — user media | rsync / S3 |
| App_Plugins | `App_Plugins/LayoutComposer/*` | Low — code in repo | Git history |
| `appsettings.*.json` | Repo + secrets manager | **Critical** — connection strings + secrets | Secrets manager + git for non-secret keys |

## RPO / RTO targets (proposed)

- **RPO** (Recovery Point Objective): 15 minutes — DB backup + App_Data
  rsync every 15 min via cron / Azure Backup / equivalent.
- **RTO** (Recovery Time Objective): 60 minutes — restore DB + sync
  App_Data + boot the host (warming del published cache toma ~30s,
  ADR 0059 / 0062).

These are negotiable per deployment. For a small site, daily backup
+ 4h RTO is acceptable.

## Backup recipes

### SQL Server (prod)

```sql
-- Full backup (daily)
BACKUP DATABASE [SynergosCms]
TO DISK = N'D:\backups\SynergosCms-FULL-{yyyymmdd}.bak'
WITH NAME = N'Synergos.CMS-FULL', STATS = 10, INIT;

-- Differential (every 4h)
BACKUP DATABASE [SynergosCms]
TO DISK = N'D:\backups\SynergosCms-DIFF-{yyyymmdd-hhmm}.bak'
WITH DIFFERENTIAL, NAME = N'Synergos.CMS-DIFF', STATS = 10;

-- Transaction log (every 15min during business hours)
BACKUP LOG [SynergosCms]
TO DISK = N'D:\backups\SynergosCms-LOG-{yyyymmdd-hhmm}.trn'
WITH NAME = N'Synergos.CMS-LOG', STATS = 10;
```

### SQLite (dev)

Per project memory `feedback_backups_external_to_repo` — backups en
`C:\Users\HITMA\Desktop\synergos-backups\` con timestamp:

```powershell
$ts = Get-Date -Format 'yyyyMMdd-HHmm'
$src = 'C:\Users\HITMA\Desktop\synergos\Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db'
$dst = "C:\Users\HITMA\Desktop\synergos-backups\Umbraco.sqlite-$ts.db"
Copy-Item -Path $src -Destination $dst
```

### App_Data (prod + dev)

```bash
# rsync to S3 every 15 minutes via cron
rsync -avz --delete \
  /var/www/synergos/App_Data/ \
  s3://synergos-backups/App_Data/{yyyy-mm-dd-hhmm}/
```

For Windows hosts, equivalent:

```powershell
$dst = "\\backupserver\synergos\App_Data-$(Get-Date -Format 'yyyyMMdd-HHmm')"
Copy-Item -Recurse -Path 'C:\inetpub\synergos\App_Data\*' -Destination $dst
```

## Restore procedure (full disaster)

1. **Stop the running CMS instance** if any (avoid mid-write
   corruption).
2. **Provision a fresh host** (clone VM image, run
   `dotnet publish` artifacts).
3. **Restore the DB**:
   - SQL Server: `RESTORE DATABASE [SynergosCms] FROM DISK = ...`
     latest full + diffs + tx logs to point-in-time.
   - SQLite: copy the backup `.db` file back to
     `Synergos.CMS.Web/umbraco/Data/Umbraco.sqlite.db`.
4. **Restore App_Data**: copy the latest sync of `App_Data/`.
5. **Re-import secrets** to `appsettings.Production.json` (DB
   connection string + 2FA encryption key + webhook HMAC secrets +
   SMTP password). NEVER from a leaked backup of plaintext config.
6. **uSync schema verification**: el repo es source-of-truth. Si la
   DB restaurada está desincronizada del schema esperado:
   ```
   Login backoffice → uSync section → Import (no destructive).
   ```
   Esto normaliza el schema sin tocar Content.
7. **Boot the CMS** y verifica `/healthz/ready` retorna 200.
8. **Smoke test** golden paths:
   - Public homepage loads.
   - Member login.
   - Comment moderation (admin).
   - Form submission.
9. **Verify audit trail integrity**: `App_Data/syn-audit/*.jsonl` no
   tiene gaps en fechas críticas.

## Recovery testing

- **Quarterly**: full restore drill from last backup to a staging host.
- **Monthly**: validate that backups are readable (rsync diff against
  source).
- **On schema change**: post-uSync Import test that all DocTypes
  funcionan en backoffice + frontend.

## Encryption-at-rest

**2FA secrets** son el target #1 para encrypt-at-rest. Currently
`App_Data/syn-2fa/*.json` plain text — backup leak = compromise.

Plan futuro:
- Wrap secrets en ASP.NET Core `IDataProtectionProvider`.
- Master key persisted en KMS (Azure Key Vault / AWS KMS).
- File contents binary-encrypted; deserialize requires master key.

Diferido — backup-with-encryption es la mitigación operacional
mientras la encryption-at-rest no esté shipped.

## Shared state — Multi-instance

Si el deploy escala a > 1 instancia:

- DB ya está shared (single SQL Server).
- App_Data/ no shared by default — cada instancia escribe a su
  filesystem local.
- Audit trail fragmenta en N instancias; coalesce via DB-backed
  adapter futuro (deferred §11.15).
- 2FA secrets fragmentan — Member que enroll en pod A no puede
  loguear en pod B. Mitigación: Sticky sessions O DB-backed 2FA store.

Para deploys con > 1 instancia, **considerar DB-backed adapters
para todo App_Data antes de scale-out**. Sticky sessions es un
workaround corto-plazo aceptable.
