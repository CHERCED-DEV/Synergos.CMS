using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Reservas;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al publicar una ficha con la composición «reservable», publica su oferta en <c>Bff.Reservas</c>
/// (011-booking-reserva): un servicio nuevo queda reservable sin tocar código ni configuración.
/// </summary>
/// <remarks>
/// <para><b>Lo que la ficha dice, y lo que no puede decir</b>: marcada reservable y completa, se publica con la
/// moneda del sitio (<c>Synergos:Cart:Currency</c>) y su zona (<c>Synergos:Listados:ZonaHoraria</c>, la misma con
/// la que el elemento arma las franjas). Marcada e incompleta, NO se llama al orquestador para publicar: el
/// editor ve en el backoffice qué le falta, y la oferta que tuviera se retira para no seguir reservando con
/// datos viejos. Sin marcar, se retira (si nunca se ofreció, no hay nada que hacer).</para>
///
/// <para><b>Lo que el orquestador dice llega al editor</b>: un horario, cupos, plazo o duración distintos REVISAN
/// el recurso en la agenda (NB-7), que no cancela ninguna reserva; si alguna confirmada ya no cabe, el editor lo
/// lee en el backoffice. Otra zona horaria, que la agenda no cambia, se rechaza sin publicar nada.</para>
///
/// <para><b>Nunca tumba el publicar</b>: el contenido ya se guardó cuando esto corre.</para>
/// </remarks>
public sealed class OfertaDeReservaAlPublicar : INotificationAsyncHandler<ContentPublishedNotification>
{
    /// <summary>El rechazo del orquestador cuando la agenda no puede cambiar el recurso ya registrado (su zona).</summary>
    internal const string NoActualizable = "reservas.oferta_no_actualizable";

    private const string Categoria = "Reserva";

    private readonly PublicadorDeOfertasDeReservas _ofertas;
    private readonly IOptions<CartSettings> _carrito;
    private readonly IOptions<ListadosSettings> _listados;
    private readonly ILogger<OfertaDeReservaAlPublicar> _log;

    public OfertaDeReservaAlPublicar(
        PublicadorDeOfertasDeReservas ofertas,
        IOptions<CartSettings> carrito,
        IOptions<ListadosSettings> listados,
        ILogger<OfertaDeReservaAlPublicar> log)
    {
        _ofertas = ofertas;
        _carrito = carrito;
        _listados = listados;
        _log = log;
    }

    public async Task HandleAsync(ContentPublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        foreach (var ficha in notification.PublishedEntities.Where(FichaReservable.LaLleva))
        {
            try
            {
                await OfrecerAsync(ficha, notification.Messages, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo publicar la oferta del servicio {Ficha}. Se repara republicando.", ficha.Key);
            }
        }
    }

    private async Task OfrecerAsync(IContent ficha, EventMessages mensajes, CancellationToken ct)
    {
        var lectura = FichaReservable.Leer(ficha);
        if (lectura is null)
        {
            await _ofertas.RetirarAsync(ficha.Key.ToString("D"), ct).ConfigureAwait(false);
            return;
        }

        if (!lectura.Completa)
        {
            mensajes.Add(new EventMessage(Categoria,
                $"«{lectura.Nombre}» está marcada reservable y le falta: {string.Join(", ", lectura.Faltan)}. No se ofrece para reservar hasta completarla.",
                EventMessageType.Warning));
            await _ofertas.RetirarAsync(lectura.Servicio, ct).ConfigureAwait(false);
            return;
        }

        var resultado = await _ofertas.PublicarAsync(lectura, _carrito.Value.Currency, _listados.Value.ZonaHoraria, ct).ConfigureAwait(false);
        if (resultado.Estado == EstadoDeLaOferta.Hecha && resultado.ReservasFuera > 0)
        {
            mensajes.Add(new EventMessage(Categoria,
                $"«{lectura.Nombre}» quedó publicada con los datos nuevos, y {resultado.ReservasFuera} reserva(s) ya confirmada(s) no caben en el horario o los cupos nuevos. No se cancelaron: revísalas con quien reservó.",
                EventMessageType.Warning));
            return;
        }
        if (resultado.Estado != EstadoDeLaOferta.Fallida) return;

        mensajes.Add(string.Equals(resultado.Codigo, NoActualizable, StringComparison.Ordinal)
            ? new EventMessage(Categoria,
                $"«{lectura.Nombre}» ya está en la agenda en otra zona horaria, y la agenda no cambia la zona de un servicio registrado: no se publicó nada. Las reservas siguen con los datos anteriores.",
                EventMessageType.Error)
            : new EventMessage(Categoria,
                $"No se pudo publicar la oferta de «{lectura.Nombre}»{(resultado.Codigo is null ? string.Empty : $" ({resultado.Codigo})")}. Se repara volviendo a publicar.",
                EventMessageType.Warning));
    }
}
