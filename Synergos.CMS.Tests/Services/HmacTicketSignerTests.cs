using System;
using System.Text;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="HmacTicketSigner"/> (seam <c>ITicketSigner</c>, T9 — la firma del QR
/// de las entradas): empty (token vacío/sin firma → null) / happy (round-trip: lo firmado
/// se verifica y devuelve lo que afirma) / filter (una firma de OTRA llave, un payload
/// manipulado o una versión de QR distinta NO pasan) / idempotent (firmar dos veces el
/// mismo token da el mismo string — el QR impreso sigue valiendo).
/// ADR 0075.
/// </summary>
public sealed class HmacTicketSignerTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("llave-de-prueba-para-firmar-tickets");
    private static readonly byte[] OtherKey = Encoding.UTF8.GetBytes("otra-llave-distinta");

    private static HmacTicketSigner Signer(byte[]? key = null) => new(key ?? Key);

    private static TicketToken Token(int version = 0) =>
        new("evt-festival-andino", "tkt_9f3a2b", version);

    [Fact] // Sin llave no se construye: firmaría tokens que cualquiera puede recalcular
    public void Ctor_EmptyKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => new HmacTicketSigner(Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() => new HmacTicketSigner(null!));
    }

    [Fact] // empty: nada, vacío o espacios → null (no lanza: en la puerta es un caso normal)
    public void Verify_NullOrBlank_ReturnsNull()
    {
        var signer = Signer();

        Assert.Null(signer.Verify(null));
        Assert.Null(signer.Verify(string.Empty));
        Assert.Null(signer.Verify("   "));
    }

    [Fact] // happy: lo firmado se verifica y devuelve exactamente lo que afirma
    public void Sign_ThenVerify_RoundTrips()
    {
        var signer = Signer();
        var token = Token(version: 2);

        var signed = signer.Sign(token);
        var verified = signer.Verify(signed);

        Assert.NotNull(verified);
        Assert.Equal(token.EventId, verified!.EventId);
        Assert.Equal(token.TicketId, verified.TicketId);
        Assert.Equal(token.QrVersion, verified.QrVersion);
    }

    [Fact] // El payload es legible a propósito (un operador lee de qué habla el QR)
    public void Sign_PayloadStaysReadable()
    {
        var signed = Signer().Sign(Token());

        Assert.StartsWith("SYN-TKT-evt-festival-andino-tkt_9f3a2b-v0.", signed, StringComparison.Ordinal);
    }

    [Fact] // EL CASO CENTRAL: el ticketId suelto NO abre la puerta (es lo que la UI imprime)
    public void Verify_BareTicketId_ReturnsNull()
    {
        var signer = Signer();

        Assert.Null(signer.Verify("tkt_9f3a2b"));
        // Ni el payload sin su firma.
        Assert.Null(signer.Verify("SYN-TKT-evt-festival-andino-tkt_9f3a2b-v0"));
    }

    [Fact] // filter: una firma hecha con OTRA llave no pasa
    public void Verify_SignedWithAnotherKey_ReturnsNull()
    {
        var forged = Signer(OtherKey).Sign(Token());

        Assert.Null(Signer().Verify(forged));
    }

    [Fact] // filter: manipular el payload invalida la firma (no se puede colar otro ticket)
    public void Verify_TamperedPayload_ReturnsNull()
    {
        var signer = Signer();
        var signed = signer.Sign(Token());

        // Cambiar el ticket, conservando la firma original.
        var tampered = signed.Replace("tkt_9f3a2b", "tkt_ROBADO", StringComparison.Ordinal);

        Assert.Null(signer.Verify(tampered));
    }

    [Fact] // filter: subir la versión a mano tampoco cuela (anti-reventa del QR rotado)
    public void Verify_TamperedVersion_ReturnsNull()
    {
        var signer = Signer();
        var signed = signer.Sign(Token(version: 0));

        var tampered = signed.Replace("-v0.", "-v1.", StringComparison.Ordinal);

        Assert.Null(signer.Verify(tampered));
    }

    [Fact] // filter: basura en la firma no revienta, solo no pasa
    public void Verify_GarbageSignature_ReturnsNull()
    {
        var signer = Signer();

        Assert.Null(signer.Verify("SYN-TKT-evt-x-tkt_1-v0.no-es-hex"));
        Assert.Null(signer.Verify("SYN-TKT-evt-x-tkt_1-v0."));
        Assert.Null(signer.Verify(".abcdef"));
    }

    [Fact] // idempotent: el mismo token firma igual — el QR impreso ayer vale hoy
    public void Sign_Twice_IsStable()
    {
        var signer = Signer();

        Assert.Equal(signer.Sign(Token(3)), signer.Sign(Token(3)));
    }

    [Fact] // Dos instancias con la MISMA llave coinciden: el QR sobrevive un reinicio.
           // Es exactamente lo que el GetHashCode() anterior NO cumplía (randomizado
           // por proceso), y nadie lo notó porque nada verificaba el QR.
    public void Sign_SurvivesProcessRestart_WhenKeyIsStable()
    {
        var before = new HmacTicketSigner(Key).Sign(Token(1));
        var after = new HmacTicketSigner(Key).Sign(Token(1));

        Assert.Equal(before, after);
        Assert.NotNull(new HmacTicketSigner(Key).Verify(before));
    }

    [Fact] // El eventId puede llevar guiones: el parseo no debe trocearlo mal
    public void Verify_EventIdWithDashes_ParsesCorrectly()
    {
        var signer = Signer();
        var token = new TicketToken("evt-rock-al-parque-2026", "tkt_abc", 5);

        var verified = signer.Verify(signer.Sign(token));

        Assert.NotNull(verified);
        Assert.Equal("evt-rock-al-parque-2026", verified!.EventId);
        Assert.Equal("tkt_abc", verified.TicketId);
        Assert.Equal(5, verified.QrVersion);
    }
}
