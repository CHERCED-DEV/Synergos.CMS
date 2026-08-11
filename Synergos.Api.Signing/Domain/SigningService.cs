using Synergos.Api.Signing.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Signing.Domain;

/// <summary>Compone las reglas de <see cref="SigningRules"/> con el almacén de llaves.</summary>
public sealed class SigningService
{
    private readonly ISigningKeyStore _keys;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public SigningService(ISigningKeyStore keys, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _keys = keys;
        _idempotency = idempotency;
        _clock = clock;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Result<SigningKey> CreateKey(string? purpose, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("key", key) is { } yaEra)
            {
                return _keys.Find(yaEra) is { } previa
                    ? Result.Ok(previa)
                    : Rejection.Conflict($"{SigningRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero no está.");
            }

            if (string.IsNullOrWhiteSpace(purpose))
            {
                return Rejection.Invalid($"{SigningRules.CodePrefix}.purpose_required",
                    "Una llave necesita propósito: con una sola llave para todo, quien firma una entrada firma un certificado.");
            }

            var id = Guid.NewGuid().ToString("n");
            var nueva = new SigningKey(id, purpose!.Trim(), SigningRules.NewSecret(), Now);
            _keys.Put(nueva);
            _idempotency.Remember("key", key, id);
            return Result.Ok(nueva);
        }
    }

    /// <summary>Retira una llave: deja de emitir, sigue verificando.</summary>
    public Result<SigningKey> RetireKey(string id)
    {
        lock (_gate)
        {
            var key = _keys.Find(id);
            if (key is null)
            {
                return Rejection.NotFound($"{SigningRules.CodePrefix}.key_not_found", $"No existe la llave {id}.");
            }
            if (!key.CanSign) return Result.Ok(key);   // idempotente

            var retirada = key with { RetiredAtUtc = Now };
            _keys.Put(retirada);
            return Result.Ok(retirada);
        }
    }

    public Result<(string Token, DateTimeOffset ExpiresAt)> Sign(string? purpose, string? payload, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(purpose)) return Rejection.Invalid($"{SigningRules.CodePrefix}.purpose_required", "Hace falta el propósito.");
        if (string.IsNullOrWhiteSpace(payload)) return Rejection.Invalid($"{SigningRules.CodePrefix}.empty_payload", "No se firma un contenido vacío.");

        var motivo = SigningRules.CheckLifetime(lifetime);
        if (motivo is not null) return Result.Rejected<(string, DateTimeOffset)>(motivo);

        var key = _keys.FindActive(purpose!);
        if (key is null)
        {
            return Rejection.NotFound($"{SigningRules.CodePrefix}.no_active_key",
                $"No hay llave vigente para '{purpose}'. Hay que crear una antes de firmar.");
        }

        var expira = Now + lifetime;
        return Result.Ok((SigningRules.Sign(key, payload!, expira), expira));
    }

    /// <summary>
    /// Sella un contenido con la llave vigente del propósito y devuelve el identificador opaco
    /// y la llave que lo produjo.
    /// </summary>
    /// <remarks>
    /// <para><b>No lleva llave de idempotencia, y no por olvido.</b> Sellar es una función pura:
    /// no escribe nada y repetirla con el mismo insumo da el mismo resultado. Exigir una llave
    /// obligaría a quien re-emite un certificado a inventarse una para una operación que no
    /// puede duplicar nada. Es el mismo criterio que el ajuste absoluto de <c>Api.Inventory</c>
    /// (defecto #30): la llave la piden los relativos, no los idempotentes por construcción.</para>
    ///
    /// <para><b>Devuelve la llave que selló</b> porque el emisor puede querer anotarla. Sin eso,
    /// «qué llave firmó cuál» solo se sabría probándolas todas — que es justo lo que hoy no sabe
    /// hacer el firmante local que esto viene a reemplazar.</para>
    /// </remarks>
    public Result<(string Seal, string KeyId)> Seal(string? purpose, string? payload)
    {
        if (string.IsNullOrWhiteSpace(purpose)) return Rejection.Invalid($"{SigningRules.CodePrefix}.purpose_required", "Hace falta el propósito.");
        if (string.IsNullOrWhiteSpace(payload)) return Rejection.Invalid($"{SigningRules.CodePrefix}.empty_payload", "No se sella un contenido vacío.");

        var key = _keys.FindActive(purpose!);
        if (key is null)
        {
            return Rejection.NotFound($"{SigningRules.CodePrefix}.no_active_key",
                $"No hay llave vigente para '{purpose}'. Hay que crear una antes de sellar.");
        }

        return Result.Ok((SigningRules.Seal(key, payload!), key.Id));
    }

    /// <summary>
    /// Comprueba que un sello es el que le corresponde a ese contenido, y devuelve la llave con
    /// la que cuadró.
    /// </summary>
    /// <remarks>
    /// <para><b>Se prueban TODAS las llaves del propósito, retiradas incluidas</b>, y por eso
    /// rotar no obliga a migrar nada. Un sello depende de la llave que lo produjo, así que si
    /// solo se probara la vigente, crear una llave nueva invalidaría de golpe todo lo ya
    /// emitido — con el identificador ya impreso en manos de su titular. Retirar significa
    /// «deja de emitir, sigue verificando», igual que para las firmas.</para>
    ///
    /// <para><b>Que no haya ninguna llave se dice, y que ninguna cuadre no.</b> Distinguirlos
    /// parece la misma indiscreción que <see cref="SigningRules.Verify"/> evita a propósito, y
    /// no lo es: allá el identificador de llave lo trae el token —o sea el atacante—, y
    /// confirmar que existe adelanta trabajo. Acá el propósito es una etiqueta que declara quien
    /// llama, y no saber distinguir «no hay llaves configuradas» de «este sello no cuadra»
    /// convertiría un despliegue a medio configurar en un rechazo que parece un ataque.</para>
    /// </remarks>
    public Result<string> VerifySeal(string? purpose, string? payload, string? seal)
    {
        if (string.IsNullOrWhiteSpace(purpose)) return Rejection.Invalid($"{SigningRules.CodePrefix}.purpose_required", "Hace falta el propósito.");
        if (string.IsNullOrWhiteSpace(payload)) return Rejection.Invalid($"{SigningRules.CodePrefix}.empty_payload", "No se verifica un contenido vacío.");
        if (string.IsNullOrWhiteSpace(seal)) return Rejection.Invalid($"{SigningRules.CodePrefix}.seal_required", "Hace falta el sello a comprobar.");

        var llaves = _keys.ForPurpose(purpose!)
            .OrderByDescending(k => k.CreatedAtUtc)
            .ThenBy(k => k.Id, StringComparer.Ordinal)
            .ToList();

        if (llaves.Count == 0)
        {
            return Rejection.NotFound($"{SigningRules.CodePrefix}.no_keys_for_purpose",
                $"No hay ninguna llave para '{purpose}': nada emitido bajo ese propósito se puede comprobar.");
        }

        foreach (var llave in llaves)
        {
            if (SigningRules.SealMatches(llave, payload!, seal)) return Result.Ok(llave.Id);
        }

        return Rejection.Forbidden($"{SigningRules.CodePrefix}.seal_mismatch",
            "El sello no es el que le corresponde a ese contenido.");
    }

    /// <summary>Verifica un token y devuelve su contenido.</summary>
    public Result<string> Verify(string? token)
    {
        if (!SigningRules.TryRead(token, out var keyId, out _, out var payload))
        {
            return Rejection.Invalid($"{SigningRules.CodePrefix}.malformed_token", "El token no tiene la forma esperada.");
        }

        // Se busca la llave por el identificador que trae el token — incluidas las RETIRADAS.
        // Si las retiradas no verificaran, rotar una llave invalidaría de golpe todo lo que ya
        // circula.
        var motivo = SigningRules.Verify(_keys.Find(keyId), token!, Now);
        return motivo is not null ? Result.Rejected<string>(motivo) : Result.Ok(payload);
    }

    public Result<Page<SigningKey>> ListForPurpose(string? purpose, int offset, int limit)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            return Rejection.Invalid($"{SigningRules.CodePrefix}.purpose_required", "Hace falta filtrar por propósito.");
        }

        var todas = _keys.ForPurpose(purpose!)
            .OrderByDescending(k => k.CreatedAtUtc)
            .ThenBy(k => k.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<SigningKey>(todas.Skip(offset).Take(limit).ToList(), todas.Count, offset));
    }
}
