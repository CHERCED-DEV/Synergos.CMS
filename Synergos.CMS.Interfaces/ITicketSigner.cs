namespace Synergos.CMS.Interfaces;

/// <summary>
/// Lo que un ticket afirma cuando se escanea: de qué evento es, cuál es, y en qué
/// versión de QR va (<paramref name="QrVersion"/> sube en cada transferencia, así que
/// el QR del dueño anterior deja de valer — es el anti-reventa).
/// </summary>
public sealed record TicketToken(string EventId, string TicketId, int QrVersion);

/// <summary>
/// Firma y verifica el token que va dentro del QR de una entrada (T9).
/// </summary>
/// <remarks>
/// <para><b>El problema que resuelve.</b> El token era
/// <c>SYN-TKT-{evento}-{ticket}-v{n}-{hash8}</c>, donde el sufijo salía de
/// <c>String.GetHashCode()</c>: no es criptográfico y en .NET Core está <b>randomizado
/// por proceso</b>, así que el mismo ticket producía un QR distinto tras cada reinicio.
/// Cualquiera que supiera el id del evento y del ticket podía fabricar el resto.</para>
/// <para><b>Un token firmado no sirve de nada si nadie lo verifica.</b> El check-in
/// comparaba el input contra el <c>ticketId</c> y jamás miraba el QR — escanear devolvía
/// <c>invalid</c> y lo único que funcionaba era teclear el id, que además la UI imprime
/// bajo el código. Por eso T9 no es "firmar": es <b>firmar y que la puerta verifique</b>.</para>
/// <para>Vive en <c>Interfaces</c> y se implementa en <c>Application</c> con
/// <c>System.Security.Cryptography</c> (BCL puro, no viola ADR 0002). Reusa la convención
/// del proyecto para HMAC (<c>WebhookSigner</c>/<c>PaymentWebhookVerifier</c>):
/// HMAC-SHA256, hex minúscula, comparación en tiempo constante.</para>
/// </remarks>
public interface ITicketSigner
{
    /// <summary>Devuelve el token firmado que se codifica en el QR.</summary>
    string Sign(TicketToken token);

    /// <summary>
    /// Verifica la firma y devuelve lo que el token afirma, o <c>null</c> si viene
    /// malformado, sin firma o con una firma que no cuadra.
    /// </summary>
    /// <remarks>
    /// Devolver <c>null</c> en vez de lanzar es deliberado: en la puerta, un token
    /// inválido es un caso <b>esperado</b> (una captura de pantalla ajena, un QR de otro
    /// evento), no una excepción.
    /// </remarks>
    TicketToken? Verify(string? rawToken);
}
