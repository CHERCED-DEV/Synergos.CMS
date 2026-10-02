namespace Synergos.CMS.Interfaces;

/// <summary>
/// La configuración de negocio de Eventos que rige la petición en curso (ADR 0137): la de su
/// sección <c>Synergos:Features:Eventos</c>, con el override del siteRoot de la petición fusionado
/// encima.
/// </summary>
/// <remarks>
/// <para><b>Una fuente, dos lectores.</b> La lee el resolver del elemento para lo que se MUESTRA
/// (la comisión del carrito) y la leen los dos motores de compra para lo que se COBRA. Medido antes
/// de existir (#194): la comisión vivía sólo en el bundle —<c>DEFAULT_FEE_PERCENT = 12</c>— y ningún
/// camino del servidor la cobraba, así que quien compraba veía un total y se le cobraba otro.</para>
/// <para><b>El sitio es el del hostname</b>, como lo resuelve el router de Umbraco
/// (<c>SitioDeLaPeticion</c>), y no el de la página: la API del checkout no tiene página, y lo que se
/// muestra y lo que se cobra tienen que salir de la MISMA regla o vuelven a separarse. Sin dominio que
/// case rigen los valores base.</para>
/// <para><b>La moneda no está acá</b>: es un dato del precio y la decide el catálogo, que la manda
/// con cada importe. Una moneda de configuración sería una segunda fuente para el mismo dato.</para>
/// </remarks>
public interface INegocioDeEventos
{
    /// <summary>La configuración que rige la petición en curso.</summary>
    NegocioDeEventos Actual();
}

/// <summary>Los valores de negocio de Eventos ya fusionados para un sitio.</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
/// <param name="FeePercent">Comisión de servicio que paga quien compra, sobre el subtotal de las
/// entradas, de 0 a 100.</param>
/// <param name="PlatformFeePercent">Comisión de la plataforma que se descuenta de lo que se le
/// liquida al organizador, de 0 a 100.</param>
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
