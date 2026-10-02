using Synergos.Core;

namespace Synergos.Bff.Eventos.Domain;

/// <summary>
/// La comisión de servicio de una compra de entradas: un porcentaje del subtotal (ADR 0137, #194).
/// </summary>
/// <remarks>
/// <para><b>La regla es la misma en los tres sitios que la calculan</b> —el carrito del navegador,
/// el motor en proceso del CMS y este orquestador— porque lo que se muestra tiene que ser lo que se
/// cobra: a dos decimales, con el redondeo de la casa (<see cref="Money.Round"/>, al par). El
/// porcentaje admite dos decimales como mucho, y con eso las tres cuentas son exactas.</para>
/// </remarks>
public static class ComisionDeServicio
{
    /// <summary>La comisión que corresponde a <paramref name="subtotal"/>.</summary>
    public static Money Sobre(Money subtotal, decimal porcentaje)
        => porcentaje <= 0m || subtotal.Amount <= 0m
            ? Money.Zero(subtotal.Currency)
            : (subtotal with { Amount = subtotal.Amount * porcentaje / 100m }).Round(2);

    /// <summary>Por qué <paramref name="porcentaje"/> no sirve, o <c>null</c> si sirve.</summary>
    public static Rejection? Revisar(decimal porcentaje)
        => porcentaje is < 0m or > 100m || decimal.Round(porcentaje, 2) != porcentaje
            ? Rejection.Invalid("eventos.bad_service_fee",
                $"La comisión de servicio va de 0 a 100 con dos decimales como mucho, y llegó {porcentaje}.")
            : null;
}
