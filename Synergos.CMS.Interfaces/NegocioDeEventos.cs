namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio de Eventos ya fusionados para un sitio (ADR 0137).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
/// <param name="FeePercent">Comisión de servicio que paga quien compra, sobre el subtotal de las
/// entradas, de 0 a 100.</param>
/// <param name="PlatformFeePercent">Comisión de la plataforma que se descuenta de lo que se le
/// liquida al organizador, de 0 a 100.</param>
/// <remarks>
/// <b>La moneda no está acá</b>: es un dato del precio y la decide el catálogo, que la manda con cada
/// importe. Una moneda de configuración sería una segunda fuente para el mismo dato.
/// </remarks>
public sealed record NegocioDeEventos(string ApiBase, decimal FeePercent, decimal PlatformFeePercent)
{
    /// <summary>
    /// La comisión de servicio que corresponde a <paramref name="subtotal"/>: a dos decimales, con
    /// el redondeo de la casa (al par, el de <c>Synergos.Core.Money</c>). Es la misma regla del
    /// carrito y del orquestador; lo que se muestra es lo que se cobra.
    /// </summary>
    public decimal ComisionSobre(decimal subtotal)
        => FeePercent <= 0m || subtotal <= 0m
            ? 0m
            : Math.Round(subtotal * FeePercent / 100m, 2, MidpointRounding.ToEven);
}
