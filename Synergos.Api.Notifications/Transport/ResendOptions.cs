namespace Synergos.Api.Notifications.Transport;

/// <summary>
/// Lo que hace falta para hablar con Resend.
/// </summary>
/// <remarks>
/// <para><b>Nada de esto tiene valor por defecto que sirva</b>, y es a propósito: un default que
/// «casi funciona» produce un despliegue que cree que manda correos. Sin <see cref="ApiKey"/> o
/// sin <see cref="From"/>, el transporte rechaza con <c>transport_not_configured</c> y lo dice en
/// el log — que es lo único honesto que puede hacer.</para>
///
/// <para><b>El proveedor se eligió por una razón de calendario, no de ingeniería</b> (HU #12):
/// SES es más barato a volumen pero exige salir de su sandbox, un trámite con AWS en el camino
/// crítico del ítem que desbloquea a los nueve dominios. Cambiar de proveedor cuesta reescribir
/// este fichero — para eso existe la costura.</para>
/// </remarks>
public sealed class ResendOptions
{
    /// <summary>La clave de API (<c>re_…</c>).</summary>
    public string? ApiKey { get; set; }

    /// <summary>El remitente, en un dominio verificado: <c>Avisos &lt;avisos@midominio.co&gt;</c>.</summary>
    public string? From { get; set; }

    /// <summary>El secreto del webhook (<c>whsec_…</c>). Sin él, no se aceptan eventos de vuelta.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>La base de la API. Se sobrescribe en los tests para apuntar a un proveedor de mentira.</summary>
    public string BaseUrl { get; set; } = "https://api.resend.com";

    /// <summary>Cuánto se espera al proveedor antes de darlo por no disponible.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Si hay lo mínimo para poder mandar.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(From);
}
