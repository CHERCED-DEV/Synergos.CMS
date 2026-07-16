using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>El copy es-CO de un hecho transaccional. Un registro por <see cref="NotificationTypes"/>.</summary>
public sealed record TransactionalCopy(
    string Subject,
    string Heading,
    string Intro,
    string CodeLabel,
    string? ActionLabel);

/// <summary>
/// El copy de los emails transaccionales, data-driven: UN mapa <c>Type → copy</c> que
/// alimenta UNA sola plantilla (<c>Views/Emails/Transactional.cshtml</c>). Es lo que evita
/// una plantilla por dominio — la duplicación que la regla de oro del doc 25 prohíbe.
/// </summary>
/// <remarks>
/// Un <c>Type</c> sin copy NO revienta ni queda mudo: cae al <see cref="Fallback"/>
/// genérico y el canal loguea un warning. Así un vertical nuevo notifica desde el día 1 y
/// el copy fino llega después.
/// </remarks>
public static class TransactionalEmailCopy
{
    /// <summary>Copy genérico para un Type sin entrada — nunca un hueco silencioso.</summary>
    public static readonly TransactionalCopy Fallback = new(
        Subject: "Confirmación",
        Heading: "Tenemos una confirmación para ti",
        Intro: "Registramos la siguiente operación en tu cuenta. Guarda este código como comprobante.",
        CodeLabel: "Código",
        ActionLabel: "Ver detalle");

    private static readonly IReadOnlyDictionary<string, TransactionalCopy> _map =
        new Dictionary<string, TransactionalCopy>(StringComparer.OrdinalIgnoreCase)
        {
            [NotificationTypes.ShopOrderPaid] = new(
                Subject: "Tu compra está confirmada",
                Heading: "¡Gracias por tu compra!",
                Intro: "Recibimos tu pago y tu pedido quedó confirmado. Te avisaremos cuando lo despachemos.",
                CodeLabel: "Número de pedido",
                ActionLabel: "Ver mi pedido"),

            [NotificationTypes.TravelOrderConfirmed] = new(
                Subject: "Tu viaje está confirmado",
                Heading: "¡Tu viaje está listo!",
                Intro: "Confirmamos tu pago y tus reservas. Presenta este código al momento del check-in.",
                CodeLabel: "Código de reserva",
                ActionLabel: "Ver mi viaje"),

            [NotificationTypes.EventTicketsConfirmed] = new(
                Subject: "Tus entradas están listas",
                Heading: "¡Nos vemos en el evento!",
                Intro: "Tu compra quedó confirmada. Estas son tus entradas — llévalas contigo el día del evento.",
                CodeLabel: "Número de orden",
                ActionLabel: "Ver mis entradas"),

            [NotificationTypes.EnrollmentActive] = new(
                Subject: "Tu matrícula está activa",
                Heading: "¡Bienvenido al curso!",
                Intro: "Tu matrícula quedó activa. Ya puedes empezar cuando quieras — tu progreso se guarda solo.",
                CodeLabel: "Matrícula",
                ActionLabel: "Ir al curso"),

            [NotificationTypes.GovCaseFiled] = new(
                Subject: "Radicamos tu trámite",
                Heading: "Tu trámite quedó radicado",
                Intro: "Recibimos tu solicitud. Guarda el número de radicado: es tu comprobante y con él puedes consultar el estado.",
                CodeLabel: "Radicado",
                ActionLabel: "Consultar mi trámite"),

            [NotificationTypes.GovCaseDecided] = new(
                Subject: "Tu trámite tiene una decisión",
                Heading: "Actualizamos tu trámite",
                Intro: "Tu solicitud fue revisada y tiene una decisión. Consulta el detalle con tu número de radicado.",
                CodeLabel: "Radicado",
                ActionLabel: "Ver la decisión"),
        };

    /// <summary>Copy del tipo, o <see cref="Fallback"/> si no hay. <paramref name="isFallback"/> avisa al canal para loguear.</summary>
    public static TransactionalCopy For(string type, out bool isFallback)
    {
        if (!string.IsNullOrWhiteSpace(type) && _map.TryGetValue(type, out var copy))
        {
            isFallback = false;
            return copy;
        }
        isFallback = true;
        return Fallback;
    }
}
