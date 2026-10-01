namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Dónde se guarda la analítica de búsqueda (ADR 0130) — sección <c>Synergos:SearchAnalytics</c>.
/// </summary>
/// <remarks>
/// <para><b>El modo se llama <c>Api</c>, como el de toda capacidad</b> (#177). Se llamaba
/// <c>Sessions</c> —el nombre del servicio, anterior al molde del doc 12— y el despliegue escribía
/// <c>Http</c>: ninguna de las dos palabras era la del otro, y el composer caía EN SILENCIO al
/// disco. En producción <c>Api.Sessions</c> —cuyo único consumidor es este— no recibía ni un
/// evento. Hoy un modo que no se reconoce no arranca: lo rechaza la pieza
/// <c>Interruptor</c> del composer nombrando los válidos, la misma que valida los otros catorce
/// (#182).</para>
///
/// <para><b>Nació sin POCO</b> —era el consumidor más viejo del árbol, escrito antes de que hubiera
/// molde— y la URL, la llave y un timeout fijo se leían a pelo del <c>IConfiguration</c>. Al
/// hablar el vocabulario del molde entra a sus reglas, y la primera es que la sección se enlaza
/// con sus cuatro campos.</para>
/// </remarks>
public sealed class SearchAnalyticsSettings
{
    /// <summary><c>FileSystem</c> (default, JSONL en <c>App_Data</c>) o <c>Api</c> (contra <c>Api.Sessions</c>).</summary>
    public string Mode { get; init; } = "FileSystem";

    /// <summary>Dónde vive la capacidad de sesión.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5200/";

    /// <summary>La llave compartida servicio↔servicio. Sin ella la ingesta responde 401.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Segundos de espera. Corto a propósito: el servicio es auxiliar.
    /// </summary>
    /// <remarks>
    /// Si tarda, el dashboard prefiere salir vacío antes que dejar la petición colgada, y la
    /// escritura no viaja en la petición del visitante (ADR 0130).
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 5;
}
