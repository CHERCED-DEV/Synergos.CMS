namespace Synergos.CMS.Interfaces;

/// <summary>
/// Un slot de visita disponible de un listado (la agenda del agente). Es la
/// unidad reservable del flujo "agendar visita": el comprador elige uno y se
/// aparta vía el motor de reservas. <see cref="StartUtc"/> es el instante de
/// inicio; <see cref="Available"/> indica si sigue libre (false = ya apartado/
/// confirmado por otra visita).
/// </summary>
public sealed record VisitSlot(
    string Id,
    DateTimeOffset StartUtc,
    bool Available = true);

/// <summary>
/// Datos de contacto del interesado que agenda la visita (caso degenerado del
/// "huésped" del motor: nombre + email + teléfono, sin pax/ocupación).
/// </summary>
public sealed record VisitContact(
    string Name,
    string Email,
    string? Phone = null);

/// <summary>
/// Resultado de agendar una visita: el id de la cita + su estado. El estado
/// refleja el ciclo del recurso reservable (held mientras se confirma →
/// <c>Confirmed</c> tras apartar+confirmar, SIN pago — la visita es gratis).
/// </summary>
public sealed record VisitResult(string VisitId, string Status);

/// <summary>
/// El vocabulario de la MODALIDAD de una visita: presencial o videollamada (#160).
/// </summary>
/// <remarks>
/// <para><b>Vive acá y no en el borde porque son DOS consumidores</b> —el controller, que
/// rechaza lo que no reconoce, y el registro del artefacto, que lo guarda— y una segunda copia
/// haría que el mismo valor significara cosas distintas según quién lo lea
/// (<c>feedback_the_same_algorithm_is_not_the_same_thing</c>).</para>
///
/// <para><b>Son los literales que ya habla el otro árbol</b> (<c>VisitMode</c> del módulo
/// Angular), no una traducción: el eslabón entre los dos es el texto que viaja por el cable, y
/// renombrarlo acá dejaría al consumidor leyendo un valor que su normalizador no conoce.</para>
///
/// <para><b>Y «no consta» NO es un valor de este vocabulario: es su ausencia</b>, o sea
/// <c>null</c>. Un <c>InPerson</c> por defecto le diría a alguien que se desplace cuando pidió
/// videollamada — <c>feedback_an_omitted_key_can_be_an_assertion</c> sobre un dato que mueve a
/// una persona por la ciudad.</para>
/// </remarks>
public static class VisitModes
{
    /// <summary>El agente recibe en el inmueble.</summary>
    public const string InPerson = "in-person";

    /// <summary>La visita es por videollamada.</summary>
    public const string Video = "video";

    /// <summary>
    /// Normaliza lo que llegó. Devuelve <c>false</c> SÓLO cuando vino algo que no se reconoce:
    /// ausente o vacío es <c>true</c> con <paramref name="mode"/> en <c>null</c>, que es «no
    /// consta» y un caso legítimo (un consumidor anterior a #160 no manda nada).
    /// </summary>
    /// <remarks>
    /// La distinción es la de <c>feedback_a_failed_tryparse_is_not_a_value</c>: un valor que no
    /// se entiende NO se guarda como «no consta», porque quien llamó sí lo declaró y anotar su
    /// ausencia sería afirmar algo falso sobre lo que dijo. Se rechaza, y quien llama corrige.
    /// </remarks>
    /// <param name="raw">Lo que mandó el llamador.</param>
    /// <param name="mode">La modalidad canónica, o <c>null</c> si no vino ninguna.</param>
    /// <returns><c>true</c> si se pudo interpretar (ausencia incluida).</returns>
    public static bool TryNormalize(string? raw, out string? mode)
    {
        mode = null;
        if (string.IsNullOrWhiteSpace(raw)) { return true; }

        var limpio = raw.Trim();
        if (string.Equals(limpio, InPerson, StringComparison.OrdinalIgnoreCase)) { mode = InPerson; return true; }
        if (string.Equals(limpio, Video, StringComparison.OrdinalIgnoreCase)) { mode = Video; return true; }
        return false;
    }
}

/// <summary>
/// Servicio de agendamiento de visitas del vertical Propiedades. Es el sub-flujo
/// transaccional de la PDP inmobiliaria (panel sticky del agente, doc
/// propiedades-app-spec §2): <see cref="GetSlotsAsync"/> lista la agenda del
/// agente para un listado; <see cref="BookAsync"/> aparta un slot y confirma la
/// visita.
/// </summary>
/// <remarks>
/// REUSA EL MOTOR (no reinventa): la visita es un RECURSO RESERVABLE POLIMÓRFICO
/// (igual que habitación≈asiento≈médico). <see cref="BookAsync"/> llama
/// <see cref="IReservationService.HoldItemAsync"/> para apartar el slot (con
/// hold-timeout) y luego <see cref="IReservationService.ConfirmAsync"/> para
/// confirmar — pero <strong>SIN paso de pago</strong> (la visita es gratis): se
/// confirma con un PaymentSessionId neutro <c>visit-free</c>, validando la
/// generalidad del flujo <c>seleccionar→[pagar]→confirmar</c> con el paso de pago
/// desactivado (spec §1). El default <c>StubVisitSchedulingService</c>
/// (Application, lógica pura) siembra la agenda en memoria y marca el slot
/// apartado como no disponible; <see cref="BookAsync"/> es idempotente por slot
/// (re-agendar el mismo slot ya confirmado devuelve la misma visita). ADR 0002 +
/// ADR 0075.
/// </remarks>
public interface IVisitSchedulingService
{
    /// <summary>
    /// Devuelve los slots de visita de un listado (la agenda del agente).
    /// Vacío si el listado no existe o no tiene agenda. Los ya apartados vienen
    /// con <see cref="VisitSlot.Available"/> = false.
    /// </summary>
    Task<IReadOnlyList<VisitSlot>> GetSlotsAsync(string listingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aparta el slot indicado vía el motor de reservas (HoldItem → Confirm, sin
    /// pago) y devuelve la visita confirmada. Lanza <see cref="ArgumentException"/>
    /// si el listado/slot no existe; <see cref="InvalidOperationException"/> si el
    /// slot ya no está disponible. Idempotente por slot.
    /// </summary>
    /// <remarks>
    /// <para><b><paramref name="mode"/> no cambia lo que este método HACE, y aun así entra por
    /// acá</b> (#160). El motor aparta el mismo slot sea presencial o por video; quien necesita
    /// la modalidad es el ARTEFACTO —la constancia que después lee quien agendó y la agenda del
    /// agente— y el artefacto lo escriben las dos implementaciones de este seam, por la
    /// invariante del doc 12 §3.2. O sea que todo lo que el registro guarda tiene que poder
    /// llegar hasta acá: pedírselo al controller en una segunda escritura dejaría el documento
    /// existiendo un instante sin modalidad y perdiéndola en silencio si ese paso falla
    /// (<c>feedback_a_two_step_write_must_remember_which_step_landed</c>).</para>
    ///
    /// <para><b>Quien valida el vocabulario es el BORDE</b>, con <see cref="VisitModes"/>: acá
    /// llega ya normalizado o <c>null</c>. Dos porteros para el mismo valor serían dos reglas
    /// que se desincronizan.</para>
    /// </remarks>
    /// <param name="listingId">El inmueble.</param>
    /// <param name="slot">El slot de la agenda del agente.</param>
    /// <param name="contact">Quién agenda.</param>
    /// <param name="mode">La modalidad, ya normalizada (<see cref="VisitModes"/>), o <c>null</c>
    ///   para «no consta». Nunca un default: ver <see cref="VisitModes"/>.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<VisitResult> BookAsync(
        string listingId,
        string slot,
        VisitContact contact,
        string? mode = null,
        CancellationToken cancellationToken = default);
}
