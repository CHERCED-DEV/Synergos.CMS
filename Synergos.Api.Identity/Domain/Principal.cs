using Synergos.Core;

namespace Synergos.Api.Identity.Domain;

/// <summary>
/// Quien puede actuar en el sistema: una persona, un servicio o un trabajo programado.
/// </summary>
/// <param name="Id">Identificador dentro de esta capacidad.</param>
/// <param name="Subject">A qué apunta en el vocabulario de quien lo registró. Opaco.</param>
/// <param name="Secret">La credencial, derivada. Nunca la credencial en claro.</param>
/// <param name="Roles">Roles vigentes.</param>
/// <param name="FailedAttempts">Intentos fallidos consecutivos desde el último acierto.</param>
/// <param name="LockedUntilUtc">Hasta cuándo está bloqueado, si lo está.</param>
/// <remarks>
/// <para><b>"Principal" y no "usuario" a propósito.</b> Un trabajo de retención que borra
/// ficheros también actúa, y la bitácora necesita poder nombrarlo. Llamar a esto "usuario"
/// habría empujado a modelar los procesos como si fueran personas, o —peor— a dejarlos sin
/// identidad y perder quién hizo qué.</para>
///
/// <para><b>No guarda nombre ni correo.</b> Esta capacidad contesta "¿quién es y qué puede
/// hacer?", no "¿cómo se llama?". Los datos personales viven donde el dominio decida, con su
/// régimen de consentimiento y de retención — y así el derecho al olvido no obliga a borrar la
/// identidad que la bitácora todavía necesita para ser legible.</para>
/// </remarks>
public sealed record Principal(
    string Id,
    Ref Subject,
    DerivedSecret? Secret,
    IReadOnlyList<string> Roles,
    int FailedAttempts = 0,
    DateTimeOffset? LockedUntilUtc = null)
{
    /// <summary>Si está bloqueado en el instante dado.</summary>
    public bool IsLocked(DateTimeOffset now) => LockedUntilUtc is { } hasta && now < hasta;

    /// <summary>Su vista como <see cref="Actor"/>, que es como viaja al resto del sistema.</summary>
    public Actor AsActor() => Actor.Of(Subject, Roles.ToArray());
}

/// <summary>Una credencial derivada: algoritmo, sal, iteraciones y el resultado.</summary>
/// <remarks>
/// Se guardan los parámetros junto al hash y no en una constante del código: el día que haya que
/// subir las iteraciones, las credenciales viejas tienen que seguir verificándose con las suyas
/// mientras se re-derivan de a poco. Con el parámetro clavado en el código, subirlo invalida
/// todas las contraseñas a la vez.
/// </remarks>
public sealed record DerivedSecret(string Algorithm, string Salt, int Iterations, string Hash);
