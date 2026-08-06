namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Configuración del vertical Eventos — sección <c>Synergos:Events</c>.
/// </summary>
public sealed class EventsSettings
{
    /// <summary>
    /// Secreto con el que se firma el token del QR de las entradas (T9).
    /// </summary>
    /// <remarks>
    /// <para><b>Vacío NO significa "no firmar".</b> Si no se configura, el host genera
    /// una llave aleatoria la primera vez y la guarda cifrada en el almacén privado
    /// (ADR 0109), así que las entradas siguen firmadas y —esto es lo que importa— el QR
    /// sobrevive a un reinicio. Poblarlo es lo correcto en producción: permite rotar la
    /// llave y compartirla entre instancias.</para>
    /// <para>No se pone un default literal a propósito: un secreto escrito en el repo es
    /// un secreto conocido, y un token firmado con una llave pública es falsificable
    /// mientras aparenta lo contrario.</para>
    /// </remarks>
    public string TicketSigningSecret { get; init; } = string.Empty;
}
