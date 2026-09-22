namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Cómo habla el CMS con el eje 2 de Alquiler, y el único límite que el despliegue impone.
/// </summary>
/// <remarks>
/// <para>Sección <c>Synergos:Alquiler</c>. El sello del contrato NO vive acá: tiene su propio
/// POCO anidado bajo <c>Synergos:Alquiler:Agreement</c>, que es lo que el #154 dejó escrito
/// después de que <c>Synergos:Eventos</c> y <c>Synergos:Events</c> quedaran a UNA letra y el
/// binder descartara una de las dos en silencio.</para>
///
/// <para><b><see cref="Mode"/> por defecto es <c>Stub</c></b>, como los otros cinco
/// interruptores: el repo se levanta entero sin ningún servicio.</para>
/// </remarks>
public sealed class AlquilerSettings
{
    /// <summary><c>Stub</c> (motor en proceso) o <c>Bff</c> (contra el orquestador).</summary>
    public string Mode { get; init; } = "Stub";

    /// <summary>Dónde vive <c>Synergos.Bff.Alquiler</c>.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5305/";

    /// <summary>La llave compartida servicio↔servicio. Vacía significa sin cablear.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>El <c>Kind</c> con que viaja quien alquila. No es su correo (#47).</summary>
    public string RenterKind { get; init; } = "alquiler.arrendatario";

    /// <summary>Cuánto se espera al orquestador.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// El alquiler más largo que este despliegue admite, en días.
    /// </summary>
    /// <remarks>
    /// <b>No es prudencia: es la vida de una autorización de pago.</b> La garantía se RETIENE
    /// autorizando —nunca capturando— y una autorización no dura para siempre. Un alquiler más
    /// largo que eso llegaría al día de la devolución con la retención ya vencida: sin nada que
    /// anular y sin nada que capturar, o sea con la garantía perdida y sin que nada falle.
    /// <para>Treinta días es el suelo conservador de lo que las pasarelas sostienen; el
    /// despliegue lo sube si su proveedor aguanta más. Cero o menos se trata como «sin tope»
    /// y lo rechaza el composer al arrancar, porque un tope ausente acá es una garantía perdida
    /// allá.</para>
    /// </remarks>
    public int MaxRentalDays { get; init; } = 30;
}
