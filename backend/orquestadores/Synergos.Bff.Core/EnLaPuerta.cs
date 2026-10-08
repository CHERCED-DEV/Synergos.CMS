using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;

namespace Synergos.Bff.Core;

/// <summary>
/// Metadato de un endpoint: la puerta del CMS lo expone como la operación <paramref name="Operacion"/>
/// del flujo <paramref name="Flujo"/> (ADR 0140 F3).
/// </summary>
/// <param name="Flujo">La clave del flujo, <c>dominio.flujo</c>: <c>eventos.compra</c>.</param>
/// <param name="Operacion">Su nombre en la puerta, del vocabulario fijo de saga (<see cref="EnLaPuertaExtensions.Operaciones"/>).</param>
/// <remarks>
/// <para><b>La lista de lo que el navegador puede pedir la escribe el orquestador, y está cerrada por
/// defecto.</b> El contrato publica la marca como <c>x-synergos-flujo</c>; el CMS incrusta el documento y
/// arma de ahí la tabla de su puerta. Lo que no se marca —reintentar, la vista de compensaciones, la
/// oferta— no existe para la puerta: da el mismo 404 que un flujo inventado, sin tener que listarlo en
/// ningún sitio. Una lista en la configuración del CMS sería una segunda copia del contrato sin nada
/// que la cruce.</para>
///
/// <para><b>Nombres de saga y no el <c>operationId</c></b>: el front sólo necesita la clave del flujo, y
/// los nombres coinciden con las fases de <c>flujos/*.json</c>. Atarlo a <c>BuyTickets</c> ataría el
/// front a cada orquestador.</para>
/// </remarks>
public sealed record OperacionEnLaPuerta(string Flujo, string Operacion);

/// <summary>Cómo se marca un endpoint para la puerta.</summary>
public static class EnLaPuertaExtensions
{
    /// <summary>El vocabulario fijo de saga con que la puerta nombra las operaciones.</summary>
    public static readonly IReadOnlyList<string> Operaciones = ["abrir", "cancelar", "cerrar", "consultar"];

    /// <summary>Una clave de flujo: <c>dominio.flujo</c>, en minúsculas. Es lo que va en la ruta de la puerta.</summary>
    public static readonly Regex ClaveDeFlujo = new(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Expone el endpoint en la puerta del CMS como <paramref name="operacion"/> de <paramref name="flujo"/>.
    /// </summary>
    /// <remarks>
    /// Una marca mal escrita revienta al mapear el endpoint, al arrancar el orquestador: en la puerta
    /// sería una operación que nadie puede pedir, y el UI ya rechaza el documento entero.
    /// </remarks>
    public static RouteHandlerBuilder EnLaPuerta(this RouteHandlerBuilder builder, string flujo, string operacion)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (flujo is null || !ClaveDeFlujo.IsMatch(flujo))
        {
            throw new ArgumentException($"«{flujo}» no es una clave de flujo (dominio.flujo, en minúsculas).", nameof(flujo));
        }
        if (!Operaciones.Contains(operacion, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"«{operacion}» no es del vocabulario de la puerta ({string.Join(", ", Operaciones)}).", nameof(operacion));
        }

        return builder.WithMetadata(new OperacionEnLaPuerta(flujo, operacion));
    }
}
