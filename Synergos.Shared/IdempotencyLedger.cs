using Microsoft.AspNetCore.Http;
using Synergos.Core;

namespace Synergos.Shared;

/// <summary>
/// Recuerda qué produjo cada llave de idempotencia.
/// </summary>
/// <remarks>
/// <para>Es lo que hace que reintentar una mutación no la ejecute dos veces. Cuando una API no
/// contesta, el llamador <b>no sabe</b> si se ejecutó; sin esto, reintentar duplica y no
/// reintentar pierde.</para>
///
/// <para><b>La consulta está separada de la escritura, y no es un detalle.</b> Cuando estaban
/// fundidas, el servicio tenía que validar ANTES de consultar la llave — y entonces un reintento
/// se topaba con el estado que él mismo había creado (el cupo ya tomado, el pedido ya cerrado) y
/// salía rechazado por <c>Conflict</c>: justo lo que la llave existía para evitar. Lo destapó un
/// run con el proceso vivo de <c>Api.Booking</c>, no un test. La regla que salió de ahí está en
/// el molde: <b>la llave se consulta antes que cualquier regla que dependa del estado</b>.</para>
///
/// <para>La llave se guarda por <b>ámbito</b> y no global: dos operaciones distintas de la misma
/// capacidad pueden legítimamente traer la misma llave del mismo cliente, y colisionarlas haría
/// que la segunda devolviera el resultado de la primera.</para>
/// </remarks>
public interface IIdempotencyLedger
{
    /// <summary>Qué produjo esta llave, o <c>null</c> si no se ha usado.</summary>
    string? Find(string scope, IdempotencyKey key);

    /// <summary>Asocia la llave a lo que produjo.</summary>
    void Remember(string scope, IdempotencyKey key, string resultId);
}

/// <summary>Lo que se guarda de una llave usada.</summary>
public sealed record IdempotencyEntry(string Id, string ResultId);

/// <summary><see cref="IIdempotencyLedger"/> sobre un fichero JSON.</summary>
public class FileIdempotencyLedger : IIdempotencyLedger
{
    private readonly JsonCollectionStore<IdempotencyEntry> _store;
    private readonly object _gate = new();

    public FileIdempotencyLedger(string root)
        => _store = new JsonCollectionStore<IdempotencyEntry>(root, "idempotency", e => e.Id);

    public string? Find(string scope, IdempotencyKey key)
    {
        lock (_gate) { return _store.Find(Clave(scope, key))?.ResultId; }
    }

    public void Remember(string scope, IdempotencyKey key, string resultId)
    {
        lock (_gate) { _store.Put(new IdempotencyEntry(Clave(scope, key), resultId)); }
    }

    private static string Clave(string scope, IdempotencyKey key) => $"{scope}|{key.Value}";
}

/// <summary>
/// Lee la cabecera <c>Idempotency-Key</c> del borde.
/// </summary>
/// <remarks>
/// Vive acá y no en cada capacidad porque el mensaje de rechazo tiene que ser el mismo en las
/// veinte: un cliente que aprendió a manejar el 400 de una API no debería tener que aprenderlo
/// otra vez para la siguiente.
/// </remarks>
public static class IdempotencyHeader
{
    public const string Name = "Idempotency-Key";

    /// <summary>Exige la cabecera. Devuelve el rechazo listo si falta o no sirve.</summary>
    /// <param name="http">La petición.</param>
    /// <param name="codePrefix">Prefijo de códigos de la capacidad — <c>booking</c>, <c>audit</c>.</param>
    /// <param name="key">La llave leída, si la hay.</param>
    /// <param name="missing">El rechazo ya formado, si falta.</param>
    public static bool TryRead(HttpRequest http, string codePrefix, out IdempotencyKey key, out IResult? missing)
    {
        var raw = http.Headers[Name].ToString();
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > IdempotencyKey.MaxLength)
        {
            key = default;
            missing = Rejection.Invalid($"{codePrefix}.idempotency_key_required",
                $"Toda mutación exige la cabecera {Name} (hasta {IdempotencyKey.MaxLength} caracteres). " +
                "Sin ella, un reintento tras un timeout duplicaría la operación.").ToProblem();
            return false;
        }
        key = IdempotencyKey.Of(raw);
        missing = null;
        return true;
    }
}
