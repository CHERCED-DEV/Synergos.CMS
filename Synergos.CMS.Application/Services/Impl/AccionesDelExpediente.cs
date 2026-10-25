namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Qué puede hacer quien mira un expediente, según el proceso que lo lleva (009, D-3).
/// </summary>
/// <remarks>
/// <para><b>Puro, y por eso hay UNO.</b> El motor en proceso lo alimenta con su tabla y el cliente de
/// <c>Api.Workflow</c> con la definición de la instancia; los dos contestan con la misma regla —las
/// transiciones del proceso que salen del estado actual y que ese rol puede dar— y la consola pinta
/// sólo eso. Antes el elemento ofrecía la misma lista fija a todo expediente abierto, y con la
/// capacidad conectada tres de esas ofertas acababan en un rechazo y una en nada (SC-002).</para>
///
/// <para><b>Una transición sin rol no se ofrece a nadie.</b> El proceso exige un rol en cada una
/// (FR-006, y el gate lo vigila en la v2); una que no lo tenga es un defecto de la definición, y
/// ofrecerla a cualquiera pondría un botón de funcionario en el portal del ciudadano.</para>
///
/// <para><b>Lo cerrado no ofrece nada</b>, aunque una definición tuviera una transición que saliera
/// de un final: un final es un final.</para>
/// </remarks>
public static class AccionesDelExpediente
{
    /// <summary>Una transición del proceso, con los roles que pueden darla. Los estados, en slug público.</summary>
    public sealed record Paso(string Accion, string Desde, string Hacia, IReadOnlyList<string> Roles);

    /// <summary>
    /// Las acciones que <paramref name="rol"/> puede dar desde <paramref name="estado"/>, en el orden del proceso.
    /// </summary>
    /// <param name="proceso">Las transiciones del proceso que lleva el expediente.</param>
    /// <param name="finales">Los estados finales de ese proceso.</param>
    /// <param name="estado">Dónde está el expediente (slug público).</param>
    /// <param name="rol">La cara de quien mira: <c>funcionario</c> o <c>ciudadano</c>.</param>
    public static IReadOnlyList<string> Calcular(
        IEnumerable<Paso> proceso,
        IEnumerable<string> finales,
        string estado,
        string rol)
    {
        ArgumentNullException.ThrowIfNull(proceso);
        ArgumentNullException.ThrowIfNull(finales);

        if (string.IsNullOrWhiteSpace(estado) || string.IsNullOrWhiteSpace(rol)
            || finales.Contains(estado.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        return proceso
            .Where(p => string.Equals(p.Desde, estado.Trim(), StringComparison.OrdinalIgnoreCase)
                        && p.Roles.Contains(rol.Trim(), StringComparer.OrdinalIgnoreCase))
            .Select(p => p.Accion)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
