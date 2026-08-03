using Synergos.Core;

namespace Synergos.Api.Cart.Domain;

/// <summary>Una línea de canasta: qué y cuánto.</summary>
public sealed record CartLine(Ref Subject, int Quantity);

/// <summary>
/// Una canasta efímera.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="Owner">De quién es — opaco. Puede ser un visitante sin identidad.</param>
/// <param name="Lines">Lo que lleva.</param>
/// <param name="CreatedAtUtc">Cuándo se abrió.</param>
/// <param name="ExpiresAtUtc">Cuándo deja de valer.</param>
/// <param name="CheckedOut">Si ya se convirtió en pedido.</param>
/// <remarks>
/// <para><b>Por qué la canasta es una capacidad aparte y no parte de Orders.</b> Su ciclo de
/// vida no se parece en nada: una canasta se abandona —la gran mayoría lo son—, vence sola, y
/// nadie la audita. Un pedido se conserva, se reclama y se factura. Meterlas juntas obligaría al
/// almacén de pedidos a cargar con millones de intenciones que nunca fueron nada.</para>
///
/// <para><b>No lleva precios.</b> Guarda qué y cuánto; el cuánto cuesta lo contesta
/// <c>Api.Pricing</c> en el momento de mirar. Congelar el precio acá lo haría envejecer dentro
/// de la canasta, y alguien pagaría un precio que ya no existe — en cualquiera de las dos
/// direcciones.</para>
///
/// <para><b>Cerrar la canasta la marca, no la borra.</b> "Nunca existió" y "se convirtió en el
/// pedido X" son cosas distintas cuando alguien reclama que compró y no le llegó.</para>
/// </remarks>
public sealed record Cart(
    string Id,
    Ref Owner,
    IReadOnlyList<CartLine> Lines,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool CheckedOut = false)
{
    /// <summary>Si todavía se puede tocar.</summary>
    public bool IsOpen(DateTimeOffset now) => !CheckedOut && now < ExpiresAtUtc;

    /// <summary>Cuántas unidades lleva en total.</summary>
    public int TotalUnits => Lines.Sum(l => l.Quantity);
}
