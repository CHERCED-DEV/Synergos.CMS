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

            // Quien participa sale como lo nombra el vertical: un miembro de Blogs, por su alias y nunca por el correo
            // con que lo guarda la mensajería (012-blogs-social, I4).
            var otro = resumen.Participants.FirstOrDefault(p => !string.Equals(p, yo, StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;
            var hilo = await _mensajeria.GetThreadAsync(resumen.ThreadId, cancellationToken);
            var mensajes = new List<MensajeDelHilo>();
            foreach (var m in hilo?.Messages ?? [])
            {
                mensajes.Add(new MensajeDelHilo(
                    Id: m.MessageId,
                    De: await contexto.ParticipanteAsync(m.From, cancellationToken),
                    Mio: string.Equals(m.From, yo, StringComparison.OrdinalIgnoreCase),
                    Texto: m.Body,
                    Fecha: m.SentAt));
            }

            hilos.Add(new HiloDeLaBandeja(
                Id: resumen.ThreadId,
                Contexto: resumen.ContextRef,
                Asunto: await contexto.AsuntoAsync(resumen.ContextRef, otro, cancellationToken),
                Contraparte: otro.Length == 0 ? string.Empty : await contexto.ParticipanteAsync(otro, cancellationToken),
                Ultimo: resumen.LastMessagePreview,
                Fecha: resumen.LastMessageAt,
                Cantidad: resumen.MessageCount,
                Mensajes: mensajes));
        }

        return Ok(new BandejaDelVertical(hilos));
    }
}
