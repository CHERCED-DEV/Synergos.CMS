namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Contra qué se aparta la visita al inmueble (HU #33a) — sección <c>Synergos:Realty</c>.
/// </summary>
/// <remarks>
/// <para><b>El default es el stub</b>, igual que en Tienda y Salud y por lo mismo: un clon limpio
/// arranca con el portal inmobiliario funcionando sin levantar ninguna capacidad.</para>
///
/// <para><b>Y acá el modo se llama <c>Api</c>, no <c>Bff</c></b>, que no es cosmética: agendar una
/// visita toca <b>una sola</b> capacidad. Una visita no se cobra —el motor viejo la confirmaba con
/// una sesión de pago de mentira, <c>visit-free</c>—, así que no hay nada que ordenar ni nada que
/// deshacer si un segundo paso falla. Meter un orquestador en medio sería una saga de un paso: la
/// máquina de compensar sin nada que compensar.</para>
/// </remarks>
public sealed class RealtySettings
{
    /// <summary>
    /// <c>Stub</c> (default, el motor en proceso) o <c>Api</c> (directo contra <c>Api.Booking</c>).
    /// </summary>
    public string Mode { get; init; } = "Stub";

    /// <summary>Dónde vive la capacidad.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5202/";

    /// <summary>La llave compartida servicio↔servicio.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Con qué <c>Kind</c> se nombra al inmueble cuando se busca su recurso en la capacidad.
    /// </summary>
    /// <remarks>
    /// El identificador del recurso NO se adivina: se resuelve preguntándole a <c>Api.Booking</c>
    /// por el sujeto (<c>GET /v1/resources?subjectKind=&amp;subjectId=</c>). Lo genera ella, así
    /// que ninguna convención del CMS podría acertarlo — es la misma lección que costó una vuelta
    /// en la HU #25.
    /// </remarks>
    public string ListingKind { get; init; } = "realty.listado";

    /// <summary>Con qué <c>Kind</c> se nombra a quien pide la visita.</summary>
    public string VisitorKind { get; init; } = "realty.interesado";

    /// <summary>Segundos de espera. Apartar una visita no es una consulta auxiliar.</summary>
    public int TimeoutSeconds { get; init; } = 15;
}
