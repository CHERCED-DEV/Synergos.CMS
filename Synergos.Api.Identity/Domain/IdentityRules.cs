using System.Security.Cryptography;
using System.Text;
using Synergos.Core;

namespace Synergos.Api.Identity.Domain;

/// <summary>
/// Lo que la identidad rechaza <b>sola</b>, y cómo deriva credenciales.
/// </summary>
public static class IdentityRules
{
    public const string CodePrefix = "identity";

    /// <summary>Algoritmo de derivación. Viaja con cada credencial — ver <see cref="DerivedSecret"/>.</summary>
    public const string Algorithm = "PBKDF2-SHA256";

    /// <summary>Iteraciones actuales.</summary>
    public const int Iterations = 210_000;

    /// <summary>Largo mínimo de una credencial.</summary>
    public const int MinSecretLength = 12;

    /// <summary>Fallos consecutivos antes de bloquear.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>Cuánto dura el bloqueo.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Si la credencial propuesta sirve. <b>No traerla es válido</b> — ver
    /// <see cref="Principal.Secret"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Un principal SIN credencial es legítimo, y hacía falta</b> (HU #14). Esta
    /// capacidad contesta «¿quién es y qué puede hacer?»; verificar una contraseña es <i>una</i>
    /// forma de establecerlo, no la única. Quien ya entró por otro sitio —una sesión del CMS,
    /// mañana una federación— necesita existir acá para poder ser sujeto de un token y llevar
    /// roles, y no necesita una contraseña.</para>
    ///
    /// <para><b>Exigirla era peor que no exigirla:</b> obligaba a quien registra a inventarse un
    /// secreto por persona y a guardarlo, o sea a fabricar credenciales que nadie usa y que hay
    /// que custodiar igual. Una credencial que no se usa es sólo superficie de ataque.</para>
    ///
    /// <para>Lo que NO cambia: si se manda una, tiene que ser buena. Media credencial es peor
    /// que ninguna, porque parece protección.</para>
    /// </remarks>
    public static Rejection? CheckSecret(string? secret)
        => string.IsNullOrWhiteSpace(secret) || secret!.Length >= MinSecretLength
            ? null
            : Rejection.Invalid($"{CodePrefix}.weak_secret",
                $"La credencial tiene que tener al menos {MinSecretLength} caracteres.");

    /// <summary>Deriva una credencial nueva con sal fresca.</summary>
    public static DerivedSecret Derive(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return new DerivedSecret(Algorithm, Convert.ToBase64String(salt), Iterations, Hash(secret, salt, Iterations));
    }

    /// <summary>
    /// Verifica una credencial contra su derivación.
    /// </summary>
    /// <remarks>
    /// Comparación de tiempo constante: una comparación normal filtra cuántos bytes coincidían
    /// por tiempo de respuesta, que es como se adivina un hash sin conocerlo.
    /// </remarks>
    public static bool Verify(string? secret, DerivedSecret? stored)
    {
        if (stored is null || string.IsNullOrEmpty(secret)) return false;
        if (!string.Equals(stored.Algorithm, Algorithm, StringComparison.Ordinal)) return false;

        var candidato = Hash(secret, Convert.FromBase64String(stored.Salt), stored.Iterations);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidato), Encoding.UTF8.GetBytes(stored.Hash));
    }

    /// <summary>El resultado de intentar autenticar, ya decidido.</summary>
    /// <param name="Rejection">El rechazo, o <c>null</c> si acertó.</param>
    /// <param name="FailedAttempts">Cómo queda el contador.</param>
    /// <param name="LockedUntilUtc">Hasta cuándo queda bloqueado, si aplica.</param>
    public sealed record AuthOutcome(Rejection? Rejection, int FailedAttempts, DateTimeOffset? LockedUntilUtc);

    /// <summary>
    /// Decide un intento de autenticación, incluido el bloqueo por fuerza bruta.
    /// </summary>
    /// <remarks>
    /// <para><b>El mismo rechazo para credencial mala y principal inexistente.</b> Distinguirlos
    /// convierte el endpoint en un oráculo que dice qué identidades existen — y eso es la mitad
    /// del trabajo de quien va a atacarlas. Es incómodo para depurar y es lo correcto.</para>
    ///
    /// <para><b>El bloqueo SÍ se distingue</b>, y es deliberado: a quien de verdad es el dueño
    /// hay que decirle por qué no entra, o llamará a soporte convencido de que su contraseña se
    /// borró. Quien ataca ya sabe que falló cinco veces.</para>
    /// </remarks>
    public static AuthOutcome Authenticate(Principal? principal, string? secret, DateTimeOffset now)
    {
        if (principal is null)
        {
            return new AuthOutcome(BadCredentials(), 0, null);
        }

        // Un principal sin credencial no se autentica por credencial NUNCA, y se dice con su
        // propio código en vez de con «credenciales inválidas»: lo segundo invita a reintentar
        // algo que no puede funcionar.
        //
        // Y NO cuenta como intento fallido, que es la mitad que importa: si contara, cualquiera
        // podría bloquear a una persona que ni siquiera entra por acá mandando cinco peticiones
        // con cualquier contraseña. Sería una negación de servicio contra alguien por una puerta
        // que no usa.
        if (principal.Secret is null)
        {
            return new AuthOutcome(
                Rejection.Conflict($"{CodePrefix}.no_credential",
                    "Este principal no tiene credencial: su identidad se establece en otro sitio."),
                principal.FailedAttempts, principal.LockedUntilUtc);
        }

        if (principal.IsLocked(now))
        {
            return new AuthOutcome(
                Rejection.Forbidden($"{CodePrefix}.locked",
                    $"Demasiados intentos fallidos. Vuelve a intentar después de {principal.LockedUntilUtc:O}."),
                principal.FailedAttempts, principal.LockedUntilUtc);
        }

        if (Verify(secret, principal.Secret))
        {
            // El acierto RESETEA el contador. Sin esto, cinco fallos repartidos a lo largo de
            // meses acabarían bloqueando a alguien que entra todos los días.
            return new AuthOutcome(null, 0, null);
        }

        var fallos = principal.FailedAttempts + 1;
        return fallos >= MaxFailedAttempts
            ? new AuthOutcome(
                Rejection.Forbidden($"{CodePrefix}.locked",
                    $"Demasiados intentos fallidos. Bloqueado por {LockoutDuration}."),
                fallos, now + LockoutDuration)
            : new AuthOutcome(BadCredentials(), fallos, null);
    }

    private static Rejection BadCredentials()
        => Rejection.Forbidden($"{CodePrefix}.bad_credentials", "Credenciales inválidas.");

    private static string Hash(string secret, byte[] salt, int iterations)
        => Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, 32));
}
