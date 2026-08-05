using System;
using System.Text;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="EventTicketIssuer"/> — el único sitio donde se decide cómo se llama una
/// entrada y qué lleva dentro su QR.
/// </summary>
/// <remarks>
/// <para>Los cuatro canónicos (ADR 0075) leídos sobre lo que acá significan: <b>empty</b> = sin
/// firmante no sale QR (fail-closed); <b>happy</b> = el QR sale firmado y el firmante lo
/// reconoce; <b>filter</b> = el estado se deriva de los hechos, no se pasa; <b>idempotent</b> =
/// emitir dos veces los mismos hechos da exactamente la misma entrada.</para>
///
/// <para><b>Y el que justifica la extracción</b>: los mismos hechos, con un identificador de
/// apartado que NO viene de una reserva de este motor, producen una entrada verificable con el
/// mismo firmante. Es lo que hace que un camino de compra que aparte el aforo en otro sitio
/// pueda emitir sin copiar el formato.</para>
/// </remarks>
public class EventTicketIssuerTests
{
    private static readonly ITicketSigner Signer =
        new HmacTicketSigner(Encoding.UTF8.GetBytes("llave-de-tests-emisor"));

    private static EventTicketFacts Hechos(
        string seatRef = "resv_9f3a2b",
        int qrVersion = 0,
        bool checkedIn = false,
        string? seat = null)
        => new(
            EventId: "evt-festival-andino",
            SeatRef: seatRef,
            HolderName: "Ana Portadora",
            HolderEmail: "ana@synergos.co",
            Tier: "GEN",
            Seat: seat,
            QrVersion: qrVersion,
            CheckedIn: checkedIn);

    [Fact] // empty: sin firmante la entrada sale, pero SIN código. Nunca un token sin firma.
    public void Sin_firmante_no_hay_QR_pero_si_entrada()
    {
        var ticket = new EventTicketIssuer(null).Issue(Hechos());

        Assert.Equal(string.Empty, ticket.Qr);
        Assert.Equal("tkt_9f3a2b", ticket.Id);
        Assert.False(new EventTicketIssuer(null).CanSign);
    }

    [Fact] // happy: el QR va firmado y el firmante lo reconoce con lo que la entrada afirma.
    public void El_QR_lo_verifica_el_MISMO_firmante()
    {
        var ticket = new EventTicketIssuer(Signer).Issue(Hechos(seat: "F12"));

        var token = Signer.Verify(ticket.Qr);
        Assert.NotNull(token);
        Assert.Equal("evt-festival-andino", token!.EventId);
        Assert.Equal(ticket.Id, token.TicketId);
        Assert.Equal(0, token.QrVersion);

        Assert.Equal("F12", ticket.Seat);
        Assert.Equal("GEN", ticket.Tier);
        Assert.Equal("Ana Portadora", ticket.AttendeeName);
        Assert.Equal("ana@synergos.co", ticket.HolderEmail);
    }

    [Fact] // filter: el estado se DERIVA de los hechos; usado manda sobre transferido.
    public void El_estado_sale_de_los_hechos_y_usado_manda()
    {
        var emisor = new EventTicketIssuer(Signer);

        Assert.Equal("valid", emisor.Issue(Hechos()).Status);
        Assert.Equal("transferred", emisor.Issue(Hechos(qrVersion: 3)).Status);
        Assert.Equal("used", emisor.Issue(Hechos(checkedIn: true)).Status);
        Assert.Equal("used", emisor.Issue(Hechos(qrVersion: 3, checkedIn: true)).Status);
    }

    [Fact] // idempotent: emitir no gasta nada. Los mismos hechos, la misma entrada.
    public void Emitir_dos_veces_da_la_MISMA_entrada()
    {
        var primera = new EventTicketIssuer(Signer).Issue(Hechos());
        var segunda = new EventTicketIssuer(Signer).Issue(Hechos());

        Assert.Equal(primera.Id, segunda.Id);
        Assert.Equal(primera.Qr, segunda.Qr);
        Assert.Equal(primera, segunda);
    }

    /// <summary>
    /// Rotar la versión mata el QR viejo <b>sin</b> cambiar el id.
    /// </summary>
    /// <remarks>
    /// Es el anti-reventa completo en una frase: quien guardó una captura de la entrada antes de
    /// transferirla tiene un código que ya no verifica contra la versión vigente, pero la entrada
    /// sigue siendo la misma para quien la busque por su id.
    /// </remarks>
    [Fact]
    public void Rotar_la_version_cambia_el_QR_y_NO_el_id()
    {
        var emisor = new EventTicketIssuer(Signer);
        var antes = emisor.Issue(Hechos(qrVersion: 0));
        var despues = emisor.Issue(Hechos(qrVersion: 1));

        Assert.Equal(antes.Id, despues.Id);
        Assert.NotEqual(antes.Qr, despues.Qr);
        Assert.Equal(1, Signer.Verify(despues.Qr)!.QrVersion);
    }

    /// <summary>
    /// <b>Lo que la extracción compra.</b> Un apartado que no salió de una reserva de este motor
    /// —el de un orquestador, por ejemplo— emite con el mismo firmante y el mismo formato.
    /// </summary>
    /// <remarks>
    /// Antes esto era imposible: la emisión proyectaba desde la unidad persistida que el propio
    /// checkout había creado, así que un camino de compra que no la creara no tenía de dónde
    /// emitir, y la salida obvia era copiar el formato del QR a otro fichero.
    /// </remarks>
    [Fact]
    public void Un_apartado_que_NO_es_una_reserva_emite_igual()
    {
        var emisor = new EventTicketIssuer(Signer);
        var deOtroCamino = emisor.Issue(Hechos(seatRef: "hold_7c1e"));

        Assert.Equal("tkt_hold_7c1e", deOtroCamino.Id);
        var token = Signer.Verify(deOtroCamino.Qr);
        Assert.NotNull(token);
        Assert.Equal(deOtroCamino.Id, token!.TicketId);
    }

    [Fact] // el recorte de resv_ es cosmético y se conserva: no se renombra lo ya emitido.
    public void El_id_deriva_de_lo_apartado_y_es_estable()
    {
        Assert.Equal("tkt_9f3a2b", EventTicketIssuer.TicketIdOf("resv_9f3a2b"));
        Assert.Equal("tkt_9f3a2b", EventTicketIssuer.TicketIdOf("  resv_9f3a2b  "));
    }

    [Fact] // una entrada sin nada apartado detrás no es una entrada.
    public void Sin_apartado_no_hay_entrada()
    {
        Assert.Throws<ArgumentException>(() => EventTicketIssuer.TicketIdOf(" "));
        Assert.Throws<ArgumentNullException>(() => new EventTicketIssuer(Signer).Issue(null!));
    }

    /// <summary>
    /// Un id con guiones se RECHAZA al emitir, y es lo contrario de una manía de estilo.
    /// </summary>
    /// <remarks>
    /// El payload del token es <c>SYN-TKT-{evento}-{entrada}-v{n}</c> y al deshacerlo se corta
    /// por el último guion —para que el identificador del evento sí pueda llevarlos—. Un guion
    /// en el id de la entrada mueve ese corte: el token verifica bien y devuelve OTRA entrada.
    /// Lo que se ve desde afuera es un QR con firma válida al que la puerta le dice
    /// <c>invalid</c>, sin un error en ningún log. Reventar en la compra es mucho más barato.
    /// </remarks>
    [Fact]
    public void Un_id_con_guiones_no_se_emite_porque_el_QR_no_lo_deshace()
    {
        Assert.Throws<ArgumentException>(() => EventTicketIssuer.TicketIdOf("saga-1-00"));

        // Y la razón, demostrada: con el id troceado, lo que vuelve NO es la entrada emitida.
        var token = Signer.Verify(Signer.Sign(new TicketToken("evt-1", "tkt_saga-1-00", 0)));
        Assert.NotNull(token);
        Assert.NotEqual("tkt_saga-1-00", token!.TicketId);
    }
}
