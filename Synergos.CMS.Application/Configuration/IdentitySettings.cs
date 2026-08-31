namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Contra qué se consigue una identidad verificable (HU #14) — sección <c>Synergos:Identity</c>.
/// </summary>
/// <remarks>
/// <para><b>El default es no emitir, y no es una transición.</b> Un clon limpio arranca sin
/// <c>Api.Identity</c> y opera igual que antes: dice quién actúa y la capacidad le cree. Poner
/// <see cref="Mode"/> en <c>Api</c> sin la capacidad arriba degrada —no hay token, se sigue
/// declarando— pero no tumba nada, que es la propiedad que esta seam tiene que conservar: un
/// trámite no se cae porque la identidad esté caída.</para>
///
/// <para><b>Encenderlo NO basta para cerrar el agujero de #48.</b> Este lado empieza a
/// <i>presentar</i> identidad; que la capacidad deje de creerle a lo declarado se enciende del
/// otro lado, con <c>Workflow:Roles:RequireVerifiedRoles</c>. Son dos interruptores a propósito:
/// encender el segundo antes que el primero deja a ventanilla sin poder decidir.</para>
/// </remarks>
public sealed class IdentitySettings
{
    /// <summary><c>Stub</c> (default, no emite) o <c>Api</c> (contra <c>Synergos.Api.Identity</c>).</summary>
    public string Mode { get; init; } = "Stub";

    /// <summary>Dónde vive la capacidad.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5220/";

    /// <summary>La llave compartida servicio↔servicio. Sin ella todo responde 401.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Segundos de espera. Corto a propósito, al revés que reservar.
    /// </summary>
    /// <remarks>
    /// Acá un timeout NO deja nada a medias —no se movió plata ni cupo— y lo que cuesta esperar
    /// es que quien está en ventanilla mire una pantalla quieta. Sin token se sigue adelante
    /// declarando, así que rendirse pronto es la respuesta correcta.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// Con cuánta antelación se renueva un token, en segundos.
    /// </summary>
    /// <remarks>
    /// El token vive 15 minutos. Reusarlo hasta el último segundo garantiza que alguna petición
    /// salga con uno recién vencido: entre que se lee de la caché y llega al otro lado hay una
    /// red de por medio. Este margen es esa red.
    /// </remarks>
    public int RenewSkewSeconds { get; init; } = 60;
}
