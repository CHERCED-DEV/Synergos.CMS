using Synergos.Core;

namespace Synergos.Api.Notifications.Domain;

/// <summary>Por dónde sale un aviso.</summary>
public enum Channel
{
    Email,
    Sms,
    Push,
}

/// <summary>
/// Una plantilla de aviso.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="Key">Nombre estable con el que la pide un dominio — <c>cita.recordatorio</c>.</param>
/// <param name="Channel">Por dónde sale.</param>
/// <param name="Subject">Asunto, con marcadores <c>{nombre}</c>.</param>
/// <param name="Body">Cuerpo, con marcadores <c>{nombre}</c>.</param>
/// <remarks>
/// <b>La plantilla vive acá y el texto lo escribe el dominio.</b> Es la línea que mantiene esta
/// capacidad agnóstica: Notifications sabe rellenar marcadores y entregar, no sabe qué es una
/// cita. Si el texto viviera acá cableado por caso de uso, la primera plantilla clínica la
/// habría atado a Salud.
/// </remarks>
public sealed record Template(string Id, string Key, Channel Channel, string Subject, string Body);

/// <summary>En qué quedó un envío.</summary>
public enum DeliveryStatus
{
    /// <summary>Aceptado y entregado al transporte.</summary>
    Sent,

    /// <summary>El transporte lo rechazó. No se reintenta desde acá.</summary>
    Failed,
}

/// <summary>Un envío, con su rastro.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="To">A quién — opaco.</param>
/// <param name="Address">La dirección concreta en ese canal.</param>
/// <param name="Channel">Por dónde salió.</param>
/// <param name="TemplateKey">Con qué plantilla.</param>
/// <param name="Subject">Asunto ya rellenado.</param>
/// <param name="Body">Cuerpo ya rellenado.</param>
/// <param name="Status">Cómo terminó.</param>
/// <param name="AtUtc">Cuándo.</param>
public sealed record Delivery(
    string Id,
    Ref To,
    string Address,
    Channel Channel,
    string TemplateKey,
    string Subject,
    string Body,
    DeliveryStatus Status,
    DateTimeOffset AtUtc);

/// <summary>
/// Por dónde sale de verdad un aviso.
/// </summary>
/// <remarks>
/// Es la costura hacia el mundo: SMTP, un proveedor de SMS, una pasarela de push. El servicio no
/// sabe cuál — y por eso esta API se puede probar y desplegar sin ninguno.
/// </remarks>
public interface INotificationSender
{
    /// <summary>Entrega, o dice que no pudo. <b>No lanza.</b></summary>
    bool Send(Channel channel, string address, string subject, string body);
}
