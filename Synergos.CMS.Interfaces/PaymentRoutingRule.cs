namespace Synergos.CMS.Interfaces;

/// <summary>
/// Una regla de enrutamiento: si la petición encaja con los criterios llenos,
/// cobra <see cref="ProviderKey"/>.
/// </summary>
/// <remarks>
/// <para>Los criterios vacíos son comodines. Una regla sin ningún criterio
/// encaja con todo y sirve de default explícito — aunque el default real es
/// <c>PaymentRoutingSettings.DefaultProviderKey</c>, que no depende del orden
/// de la lista.</para>
/// <para>Se evalúan EN ORDEN y gana la primera que encaja: es más fácil de
/// razonar para un operador que un sistema de puntajes, y hace que mover una
/// regla en la lista sea un cambio visible en vez de un efecto lateral.</para>
/// </remarks>
/// <param name="ProviderKey">A quién enrutar. Debe existir entre los
///   proveedores registrados; si no, el router lo ignora y avisa.</param>
/// <param name="Vertical">Negocio que cobra (<c>"shop"</c>, <c>"events"</c>…).
///   Null o vacío = cualquiera.</param>
/// <param name="CountryCode">ISO 3166-1 alfa-2. Null = cualquiera.</param>
/// <param name="Currency">ISO 4217. Null = cualquiera.</param>
/// <param name="Method">Riel (<c>"pse"</c>, <c>"nequi"</c>, <c>"card"</c>…).
///   Null = cualquiera.</param>
public sealed record PaymentRoutingRule(
    string ProviderKey,
    string? Vertical = null,
    string? CountryCode = null,
    string? Currency = null,
    string? Method = null)
{
    /// <summary>
    /// ¿Encaja esta petición? Un criterio vacío no discrimina; uno lleno exige
    /// igualdad exacta, sin distinguir mayúsculas.
    /// </summary>
    public bool Matches(PaymentSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Fits(Vertical, request.Vertical)
            && Fits(CountryCode, request.CountryCode)
            && Fits(Currency, request.Currency)
            && Fits(Method, request.PreferredMethod);
    }

    private static bool Fits(string? criterion, string? value)
        => string.IsNullOrWhiteSpace(criterion)
        || string.Equals(criterion, value, StringComparison.OrdinalIgnoreCase);
}
