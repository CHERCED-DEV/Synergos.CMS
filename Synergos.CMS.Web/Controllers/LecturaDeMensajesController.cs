using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// La bandeja de quien lee en un vertical, con forma neutra (014-despiece-tienda, I4): sólo los hilos del vertical,
/// cada uno con su asunto y sus mensajes. La tienda la leía de <c>GET api/shop/messages</c>, que sigue como interina y
/// traía todos los hilos del correo, sin asunto.
/// </summary>
[ApiController]
[Route("api/lecturas/{vertical}")]
public sealed class LecturaDeMensajesController : ControllerBase
{
    private readonly IEnumerable<IContextoDeMensajes> _contextos;
    private readonly IMessagingService _mensajeria;
    private readonly IMemberAccessGate _sesion;

    /// <summary>Con los contextos que declaran los verticales, la mensajería común y la sesión de quien lee.</summary>
    public LecturaDeMensajesController(
        IEnumerable<IContextoDeMensajes> contextos,
        IMessagingService mensajeria,
        IMemberAccessGate sesion)
    {
        _contextos = contextos ?? throw new ArgumentNullException(nameof(contextos));
        _mensajeria = mensajeria ?? throw new ArgumentNullException(nameof(mensajeria));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    /// <summary>Los hilos del vertical en los que participa quien lee, del más reciente al más antiguo.</summary>
    [HttpGet("mensajes")]
    public async Task<IActionResult> Bandeja(string vertical, CancellationToken cancellationToken)
    {
        if (_contextos.DelVertical(vertical) is not { } contexto)
        {
            return this.SinLectura();
        }

        if (!_sesion.IsAuthenticated || string.IsNullOrWhiteSpace(_sesion.CurrentMemberEmail))
        {
            return this.SinSesion();
        }

        var yo = _sesion.CurrentMemberEmail.Trim();
        var hilos = new List<HiloDeLaBandeja>();
        foreach (var resumen in await _mensajeria.GetInboxAsync(yo, cancellationToken))
        {
            if (!contexto.EsDelVertical(resumen.ContextRef))
            {
                continue;
            }

            var hilo = await _mensajeria.GetThreadAsync(resumen.ThreadId, cancellationToken);
            hilos.Add(new HiloDeLaBandeja(
                Id: resumen.ThreadId,
                Contexto: resumen.ContextRef,
                Asunto: await contexto.AsuntoAsync(resumen.ContextRef, cancellationToken),
                Contraparte: resumen.Participants.FirstOrDefault(p => !string.Equals(p, yo, StringComparison.OrdinalIgnoreCase))
                    ?? string.Empty,
                Ultimo: resumen.LastMessagePreview,
                Fecha: resumen.LastMessageAt,
                Cantidad: resumen.MessageCount,
                Mensajes: hilo?.Messages
                    .Select(m => new MensajeDelHilo(
                        Id: m.MessageId,
                        De: m.From,
                        Mio: string.Equals(m.From, yo, StringComparison.OrdinalIgnoreCase),
                        Texto: m.Body,
                        Fecha: m.SentAt))
                    .ToList() ?? []));
        }

        return Ok(new BandejaDelVertical(hilos));
    }
}
