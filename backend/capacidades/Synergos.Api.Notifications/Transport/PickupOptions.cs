namespace Synergos.Api.Notifications.Transport;

/// <summary>
/// La carpeta de recogida: el transporte de DESARROLLO que deja cada correo como un <c>.eml</c>.
/// </summary>
/// <remarks>
/// <para><b>Existe para poder VERIFICAR un aviso sin proveedor</b> (ADR 0140 F3): la regla del repo es
/// que un correo se comprueba abriendo el <c>.eml</c>, nunca mirando el log, y sin esto
/// <c>Api.Notifications</c> sólo tenía Resend o el transporte que rechaza.</para>
///
/// <para><b>Sólo en Development, y el arranque lo exige.</b> Una carpeta llena de correos que nadie
/// recibe es, en producción, el defecto que <c>LoggingNotificationSender</c> dejó de cometer: decir
/// «aceptado» sin entregar. Y con Resend configurado a la vez, cuál de los dos manda no lo decidiría
/// nadie, así que tampoco arranca.</para>
/// </remarks>
public sealed class PickupOptions
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Notifications:Pickup";

    /// <summary>Dónde se escriben los <c>.eml</c>. Vacío ≡ sin recogida.</summary>
    public string? Directory { get; set; }

    /// <summary>El remitente que lleva el <c>.eml</c>.</summary>
    public string From { get; set; } = "Synergos (desarrollo) <avisos@synergos.localhost>";

    /// <summary>Si hay carpeta.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Directory);
}
