using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Lo que el catálogo decide de un alquiler —cuántos días son, cuánto vale y si se puede— escrito
/// UNA vez para los dos motores.
/// </summary>
/// <remarks>
/// <para><b>Vivía dentro del motor en proceso, y el cliente del orquestador no lo tenía</b>
/// (#204): con <c>Synergos:Alquiler:Mode=Bff</c> el CMS le pedía a <c>Synergos.Bff.Alquiler</c>
/// una cotización que el orquestador nunca supo dar —el catálogo, con sus tarifas, es el eje 1 y
/// vive acá—, y los límites de duración de cada equipo no se aplicaban. Una regla que sólo cumple
/// uno de los dos caminos es una regla que depende de un interruptor.</para>
///
/// <para><b>Lo que NO está acá</b>: cuántas unidades quedan libres. Eso lo sabe quien guarda los
/// alquileres —el almacén local o <c>Api.Booking</c>—, y repetirlo de este lado sería una segunda
/// verdad sobre el mismo cupo.</para>
/// </remarks>
public static class ReglasDeAlquiler
{
    /// <summary>Cuántos días cubre un pedido: del 1 al 2 es un día.</summary>
    /// <param name="request">El pedido.</param>
    /// <returns>Los días; cero o negativo si la devolución no es posterior al retiro.</returns>
    public static int Dias(RentalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.End.DayNumber - request.Start.DayNumber;
    }

    /// <summary>
    /// El valor del día que aplica para una duración: el tramo más alto que la cubre, o la
    /// tarifa base.
    /// </summary>
    /// <remarks>
    /// Los tramos llegan ya ordenados y sin repetidos de <c>EquipmentContentRules</c>, así que
    /// acá no hay desempate que tomar — que es justo lo que el #131 pide evitar.
    /// </remarks>
    /// <param name="equipo">El equipo, con sus tramos.</param>
    /// <param name="dias">La duración del alquiler.</param>
    /// <returns>El valor del día.</returns>
    public static decimal ValorDelDia(RentalEquipment equipo, int dias)
    {
        ArgumentNullException.ThrowIfNull(equipo);

        var tramo = equipo.Rates
            .Where(r => r.MinDays <= dias)
            .OrderByDescending(r => r.MinDays)
            .FirstOrDefault();

        return tramo?.PerDay ?? equipo.DailyRate;
    }

    /// <summary>Cuánto cuesta y cuánto se retiene, en la moneda del catálogo.</summary>
    /// <param name="equipo">El equipo.</param>
    /// <param name="cantidad">Cuántas unidades.</param>
    /// <param name="dias">Cuántos días.</param>
    /// <returns>La cotización.</returns>
    public static RentalQuote Cotizar(RentalEquipment equipo, int cantidad, int dias)
    {
        ArgumentNullException.ThrowIfNull(equipo);

        var porDia = ValorDelDia(equipo, dias);
        return new RentalQuote(
            EquipmentId: equipo.Id,
            Quantity: cantidad,
            Days: dias,
            PerDay: porDia,
            RentalTotal: porDia * dias * cantidad,
            Deposit: equipo.Deposit * cantidad,
            Currency: equipo.Currency);
    }

    /// <summary>
    /// El primer «no» del catálogo para este pedido, o <c>null</c> si el catálogo lo admite.
    /// </summary>
    /// <remarks>
    /// <c>window_too_long</c> y <c>window_out_of_bounds</c> van separados a propósito: el remedio
    /// del primero no es elegir otras fechas, es que este despliegue no puede retener una
    /// garantía tanto tiempo (spec del piloto 2, «los rechazos»).
    /// </remarks>
    /// <param name="equipo">El equipo pedido.</param>
    /// <param name="request">El pedido.</param>
    /// <param name="maxRentalDays">El tope del despliegue; cero o menos es sin tope.</param>
    /// <returns>El código —sin prefijo— y su motivo, o <c>null</c>.</returns>
    public static (string Codigo, string Motivo)? Revisar(RentalEquipment equipo, RentalRequest request, int maxRentalDays)
    {
        ArgumentNullException.ThrowIfNull(equipo);
        ArgumentNullException.ThrowIfNull(request);

        var dias = Dias(request);
        if (dias <= 0)
        {
            return ("bad_window", "La devolución tiene que ser posterior al retiro: del 1 al 2 es un día.");
        }

        if (maxRentalDays > 0 && dias > maxRentalDays)
        {
            return ("window_too_long",
                $"Este despliegue no alquila más de {maxRentalDays} días: una autorización "
                + "de garantía no dura más, y al devolver no quedaría nada que liberar.");
        }

        if (dias < equipo.MinDays || dias > equipo.MaxDays)
        {
            return ("window_out_of_bounds",
                $"'{equipo.Name}' se alquila entre {equipo.MinDays} y {equipo.MaxDays} días.");
        }

        if (request.Quantity < 1)
        {
            return ("bad_quantity", "Hay que alquilar al menos una unidad.");
        }

        return null;
    }
}
