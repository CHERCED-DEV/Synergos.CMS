using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Cuándo se puede vender una entrada: el evento no ha empezado y la localidad está dentro de
/// su ventana de venta (#195).
/// </summary>
/// <remarks>
/// <para><b>Está afuera de los motores de compra porque los dos tienen que aplicarla igual</b>,
/// el que compra en proceso y el que compra contra <c>Bff.Eventos</c>. Ninguna capacidad del
/// orquestador conoce el calendario —<c>Api.Pricing</c> fija precios sin vigencia y
/// <c>Api.Inventory</c> cuenta existencias sin ventana— así que la regla la aplica quien sí lo
/// conoce, que es el catálogo de este lado. Copiada en cada motor, el día que uno la cambie el
/// otro sigue vendiendo lo que la ficha dice que ya no se vende: el mismo defecto de #194, lo que
/// se muestra y lo que se cobra separados.</para>
///
/// <para><b>Se mira ANTES de apartar nada.</b> Un rechazo acá no deja cupo retenido ni sesión de
/// pago abierta, porque no llegó a haber ninguno.</para>
///
/// <para><b>Solo compara instantes.</b> Qué día del calendario es «el 14 de agosto» lo decide
/// quien publica el catálogo, en la zona del sitio (ver <see cref="EventTier"/>): esta regla no
/// sabe de zonas horarias y por eso no se puede equivocar de zona.</para>
///
/// <para>Lanza <see cref="ArgumentException"/>, lo mismo que el aforo o el máximo por orden: es lo
/// que el controlador traduce a <c>400 { error }</c> con el motivo a la vista, así que el
/// comprador recibe el rechazo en la forma que su pantalla ya sabe pintar.</para>
/// </remarks>
public static class CalendarioDeVenta
{
    /// <summary>
    /// Por qué no se venden entradas de este evento ahora, o <c>null</c> si se venden.
    /// </summary>
    /// <remarks>
    /// <b>«Ya empezó» es el mismo límite que pinta la ficha</b> (<c>BuildStatus</c>: inicio
    /// &lt;= ahora es <c>past</c>). Si los dos límites difirieran en un tick, habría un instante en
    /// que la ficha dice «ya comenzó» y el checkout cobra.
    /// </remarks>
    public static string? PorQueNoSeVende(EventSummary evento, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(evento);
        return evento.StartUtc <= ahora
            ? $"El evento '{evento.Title}' ya comenzó: ya no se venden entradas."
            : null;
    }

    /// <summary>
    /// Por qué no se vende esta localidad ahora, o <c>null</c> si se vende.
    /// </summary>
    /// <remarks>
    /// El motivo lleva el texto de la ventana que pinta la ficha, si lo hay: es lo que el
    /// comprador ya leyó, y repetirlo explica el rechazo con sus propias palabras.
    /// </remarks>
    public static string? PorQueNoSeVende(EventTier localidad, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(localidad);

        if (localidad.SaleOpensUtc is { } abre && ahora < abre)
        {
            return $"La venta de la localidad '{localidad.Name}' todavía no abre{ComoSeLee(localidad)}.";
        }

        if (localidad.SaleClosesUtc is { } cierra && ahora >= cierra)
        {
            return $"La venta de la localidad '{localidad.Name}' ya cerró{ComoSeLee(localidad)}.";
        }

        return null;
    }

    /// <summary>Lanza si el evento ya empezó.</summary>
    /// <exception cref="ArgumentException">Con el motivo, listo para el comprador.</exception>
    public static void Exigir(EventSummary evento, DateTimeOffset ahora)
    {
        if (PorQueNoSeVende(evento, ahora) is { } motivo)
        {
            throw new ArgumentException(motivo);
        }
    }

    /// <summary>Lanza si la localidad está fuera de su ventana de venta.</summary>
    /// <exception cref="ArgumentException">Con el motivo, listo para el comprador.</exception>
    public static void Exigir(EventTier localidad, DateTimeOffset ahora)
    {
        if (PorQueNoSeVende(localidad, ahora) is { } motivo)
        {
            throw new ArgumentException(motivo);
        }
    }

    private static string ComoSeLee(EventTier localidad)
        => string.IsNullOrWhiteSpace(localidad.SaleWindow) ? string.Empty : $" ({localidad.SaleWindow.Trim()})";
}
