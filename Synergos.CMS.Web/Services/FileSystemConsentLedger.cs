using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IConsentLedger"/> (ADR 0098). Persiste los consentimientos
/// sobre <see cref="IPhiStore"/> (resourceType <c>"consents"</c>), cifrados y
/// atómicos. Append-only: revocar marca el registro, no lo borra.
/// </summary>
public sealed class FileSystemConsentLedger : IConsentLedger
{
    private const string ResourceType = "consents";

    private readonly IPhiStore _store;
    private readonly ILogger<FileSystemConsentLedger> _logger;

    public FileSystemConsentLedger(IPhiStore store, ILogger<FileSystemConsentLedger> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<Guid> GrantAsync(ConsentGrant grant, CancellationToken cancellationToken)
    {
        var consentId = Guid.NewGuid();
        var record = new ConsentRecord(
            consentId, grant.PatientKey, grant.DoctorKey,
            grant.GrantedAtUtc, grant.ExpiresAtUtc, grant.Purpose,
            IsRevoked: false, RevokedAtUtc: null);
        await _store.WriteAsync(ResourceType, consentId, JsonSerializer.Serialize(record), cancellationToken)
            .ConfigureAwait(false);
        return consentId;
    }

    public async Task RevokeAsync(Guid consentId, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(ResourceType, consentId, cancellationToken).ConfigureAwait(false);
        if (json is null) return;
        var record = Deserialize(json);
        if (record is null || record.IsRevoked) return;

        var revoked = record with { IsRevoked = true, RevokedAtUtc = DateTime.UtcNow };
        await _store.WriteAsync(ResourceType, consentId, JsonSerializer.Serialize(revoked), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> HasActiveConsentAsync(Guid patientKey, Guid doctorKey, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        foreach (var record in await ReadAll(cancellationToken).ConfigureAwait(false))
        {
            if (record.PatientKey == patientKey
                && record.DoctorKey == doctorKey
                && !record.IsRevoked
                && record.GrantedAtUtc <= now
                && record.ExpiresAtUtc >= now)
            {
                return true;
            }
        }
        return false;
    }

    public async Task<IReadOnlyList<ConsentRecord>> GetConsentHistoryAsync(Guid patientKey, CancellationToken cancellationToken)
    {
        var all = await ReadAll(cancellationToken).ConfigureAwait(false);
        return all
            .Where(r => r.PatientKey == patientKey)
            .OrderByDescending(r => r.GrantedAtUtc)
            .ThenByDescending(r => r.ConsentId)   // desempate determinista ante GrantedAtUtc iguales
            .ToList();
    }

    private async Task<List<ConsentRecord>> ReadAll(CancellationToken cancellationToken)
    {
        var raw = await _store.ListAsync(ResourceType, cancellationToken).ConfigureAwait(false);
        var result = new List<ConsentRecord>(raw.Count);
        foreach (var json in raw)
        {
            var record = Deserialize(json);
            if (record is not null) result.Add(record);
        }
        return result;
    }

    private ConsentRecord? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<ConsentRecord>(json); }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "FileSystemConsentLedger: registro de consentimiento corrupto — ignorado");
            return null;
        }
    }
}
