namespace Synergos.CMS.Interfaces;

/// <summary>
/// Las respuestas de una radicación por la puerta, como las guarda el CMS (009-gobierno-tramite, FR-016): los datos
/// personales del expediente viven aquí y no en la saga ni en la capacidad de procesos.
/// </summary>
/// <param name="Radicacion">La saga de la radicación en el orquestador de Gobierno: une el expediente del CMS con el del
/// proceso, y lo nombran todos los actos posteriores.</param>
/// <param name="Tramite">El trámite (la clave del nodo), tal como lo lleva la saga.</param>
/// <param name="Ciudadano">El contacto de la sesión, con su <c>MemberKey</c>: el dueño.</param>
/// <param name="Respuestas">Las respuestas del formulario.</param>
/// <param name="Aceptacion">La aceptación de la notificación electrónica que lleva la saga; nula es «no consta».</param>
/// <param name="Tasa">La tasa que cotizó el servidor (la de la saga): cero si el trámite es gratuito.</param>
/// <param name="Moneda">La moneda de la tasa.</param>
public sealed record RespuestasDeLaPuerta(
    string Radicacion,
    string Tramite,
    GovCitizen Ciudadano,
    IReadOnlyDictionary<string, string> Respuestas,
    NotificationConsent? Aceptacion,
    decimal Tasa,
    string Moneda);

/// <summary>Un expediente nacido por la puerta: su radicado y si la radicación ya se vio completa.</summary>
/// <param name="Confirmado">Si el orquestador ya dijo que la radicación se completó. Mientras no, el expediente no se
/// muestra en ninguna bandeja: la tasa puede no cobrarse y el expediente no nacer (FR-018).</param>
public sealed record ExpedienteDeLaPuerta(string CaseId, string Radicado, string Radicacion, Guid? Dueno, bool Confirmado);

/// <summary>
/// El agregado del expediente visto desde la radicación por la puerta (009-gobierno-tramite T027): lo que guarda el
/// artefacto antes de cerrar y lo que la reconciliación confirma o descarta después.
/// </summary>
/// <remarks>
/// <para><b>El radicado se acuña al guardar</b>, con la misma secuencia de la ruta directa, y repetir el guardado lo
/// conserva: el ciudadano lo ve en su comprobante.</para>
///
/// <para><b>Un expediente por confirmar no existe para nadie más</b>: las bandejas, la cola y las decisiones no lo ven
/// hasta que la radicación se completa. Si termina deshecha, sus respuestas se borran (datos personales sin
/// expediente).</para>
/// </remarks>
public interface IExpedientesDeLaPuerta
{
    /// <summary>
    /// Guarda las respuestas de la radicación —o las reemplaza mientras no esté confirmada— y acuña el radicado la
    /// primera vez. Nulo si la radicación ya está confirmada: sus respuestas no cambian.
    /// </summary>
    /// <exception cref="ArgumentException">Si el trámite no existe, falta el nombre o el correo del ciudadano, o una
    /// pregunta obligatoria del formulario publicado está vacía (el mensaje la nombra).</exception>
    Task<ExpedienteDeLaPuerta?> GuardarAsync(RespuestasDeLaPuerta respuestas, CancellationToken cancellationToken = default);

    /// <summary>El expediente de una radicación, confirmado o no; nulo si no tiene.</summary>
    Task<ExpedienteDeLaPuerta?> DeLaRadicacionAsync(string radicacion, CancellationToken cancellationToken = default);

    /// <summary>Los expedientes nacidos por la puerta que todavía esperan a su radicación, los más recientes primero.</summary>
    Task<IReadOnlyList<ExpedienteDeLaPuerta>> PorConfirmarAsync(CancellationToken cancellationToken = default);

    /// <summary>La radicación se completó: el expediente pasa a existir. Repetirlo no hace nada.</summary>
    Task ConfirmarAsync(string radicacion, CancellationToken cancellationToken = default);

    /// <summary>
    /// La radicación terminó deshecha: borra el expediente y sus respuestas. Nunca borra uno confirmado; dice si
    /// borró.
    /// </summary>
    Task<bool> DescartarAsync(string radicacion, CancellationToken cancellationToken = default);
}

/// <summary>Por dónde nació un expediente (<c>Camino</c> del agregado): nulo es la ruta directa.</summary>
public static class CaminoDelExpediente
{
    /// <summary>Radicado por la ruta del CMS (<c>POST api/gov/application</c>).</summary>
    public const string Directa = "directa";

    /// <summary>Radicado por la puerta, con su saga en el orquestador de Gobierno.</summary>
    public const string Puerta = "puerta";
}
