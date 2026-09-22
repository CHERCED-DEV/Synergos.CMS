using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El registro de las visitas agendadas: quién agendó qué inmueble, cuándo, y en qué quedó.
/// Es el EJE 3 de Realty, y hasta el #158 no existía.
/// </summary>
/// <remarks>
/// <para><b>Lo que faltaba no era durabilidad: era el artefacto.</b> El hallazgo #158 midió que
/// <c>StubVisitSchedulingService</c> guarda en <c>realty-visits</c> y
/// <c>HttpVisitSchedulingService</c> no guarda nada, y concluyó que con
/// <c>Synergos:Realty:Mode=Api</c> «quien reservó no ve su visita». Al ir a arreglarlo apareció
/// que era peor y más simple a la vez: <b>tampoco la veía en el otro modo</b>. Lo que hay en
/// <c>realty-visits</c> está indexado por <c>{listado}/{slot}</c> y sirve para una cosa —marcar
/// el slot como no disponible— así que es ESTADO DE DISPONIBILIDAD, no el artefacto. Medido:
/// fuera de sus propios tests, nadie lo lee.</para>
///
/// <para>Y con eso el defecto se lee al revés de como venía escrito: <b>perder ese almacén en el
/// modo cableado es CORRECTO</b> —la disponibilidad la sabe <c>Api.Booking</c>, que es la dueña
/// del cuándo (§0.B.12)—. Lo que no puede perderse es la constancia de que alguien agendó, y ésa
/// no existía en ninguno de los dos caminos.</para>
///
/// <para><b>Por eso el registro va FUERA de los dos, y no dentro del cliente <c>Http*</c></b>,
/// que era la otra salida que el hallazgo dejaba abierta. Es la invariante del doc 12 §3.2, y ya
/// se pagó una vez: la cara de organizador de Eventos colgaba del motor de compra concreto, así
/// que cambiar por dónde se compra habría dejado la puerta leyendo un almacén vacío
/// (<see cref="EventTicketLedger"/>). Con el registro dentro de una de las dos implementaciones,
/// las visitas agendadas en un modo serían invisibles en el otro — y el interruptor es de
/// despliegue, así que el día que se encienda la gente pierde lo suyo sin que nada falle.</para>
///
/// <para><b>Familia propia (<c>realty-visit-records</c>) y no la de la disponibilidad</b>, aunque
/// el nombre invite: son dos formas de documento con dos claves distintas, y el propio
/// <c>StubVisitSchedulingService</c> ya tiene escrito por qué eso no se mezcla —«una
/// deserialización que "casi" encaja no falla: devuelve otra cosa en silencio»—.</para>
///
/// <para><b>Lo que este registro NO hace, dicho para no mentir sobre su alcance:</b> no sustituye
/// al motor. No aparta, no confirma y no sabe si un slot sigue libre; eso lo decide quien esté
/// detrás del seam. Acá sólo entra lo que YA se agendó.</para>
/// </remarks>
public sealed class RealtyVisitLedger
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // acentos es-CO legibles en disco
    };

    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-realty-visit-records/).</summary>
    public const string ResourceType = "realty-visit-records";

    private readonly IJsonEntityStore _store;
    private readonly Func<DateTimeOffset> _now;

    /// <param name="store">Dónde viven las visitas. Null ≡ en memoria del proceso.</param>
    /// <param name="now">Reloj inyectable para determinismo en tests.</param>
    public RealtyVisitLedger(IJsonEntityStore? store = null, Func<DateTimeOffset>? now = null)
    {
        _store = store ?? new InMemoryJsonEntityStore();
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Anota la visita. Repetir la misma <paramref name="visitId"/> reescribe el documento con lo
    /// último, que es lo correcto: el seam es idempotente por slot, así que un reintento trae los
    /// mismos datos y no un segundo registro.
    /// </summary>
    /// <param name="visitId">El id que devolvió el seam. Es la clave del documento.</param>
    /// <param name="listingId">El inmueble.</param>
    /// <param name="slotId">El slot de la agenda del agente.</param>
    /// <param name="startUtc">Cuándo es la visita.</param>
    /// <param name="contact">Quién agendó.</param>
    /// <param name="status">El estado que devolvió el seam.</param>
    /// <param name="mode">La modalidad ya normalizada (<see cref="VisitModes"/>), o <c>null</c>
    ///   para «no consta» — que es lo que dicen, con razón, las visitas anteriores al #160.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    public async Task<PersistedVisit?> RecordAsync(
        string? visitId,
        string? listingId,
        string? slotId,
        DateTimeOffset? startUtc,
        VisitContact? contact,
        string? status,
        string? mode = null,
        CancellationToken cancellationToken = default)
    {
        // Sin id no hay documento que escribir, y fabricar uno sería inventar el acuse — el
        // defecto que este repo ya pagó cuatro veces (`feedback_a_two_step_write_must_remember_
        // which_step_landed`). Se devuelve null y quien llama decide; hoy nadie aborta por esto,
        // porque la visita YA quedó apartada y perder su constancia es menos malo que tirar el
        // apartado.
        if (string.IsNullOrWhiteSpace(visitId)) { return null; }

        var registro = new PersistedVisit(
            VisitId: visitId.Trim(),
            ListingId: (listingId ?? string.Empty).Trim(),
            SlotId: (slotId ?? string.Empty).Trim(),
            StartUtc: startUtc,
            // Lo que no se reconoce NO se guarda: el borde ya rechazó eso con un 400, así que
            // llegar acá con algo raro significa que un llamador nuevo se saltó la puerta, y
            // anotarlo dejaría en el registro un valor que ninguna pantalla sabe pintar.
            Mode: VisitModes.TryNormalize(mode, out var modalidad) ? modalidad : null,
            VisitorName: contact?.Name?.Trim() ?? string.Empty,
            VisitorEmail: NormalizarCorreo(contact?.Email),
            VisitorPhone: string.IsNullOrWhiteSpace(contact?.Phone) ? null : contact.Phone.Trim(),
            Status: string.IsNullOrWhiteSpace(status) ? "Unknown" : status.Trim(),
            BookedAtUtc: _now());

        await _store.WriteAsync(ResourceType, registro.VisitId, JsonSerializer.Serialize(registro, Json), cancellationToken)
            .ConfigureAwait(false);
        return registro;
    }

    /// <summary>La visita, o <c>null</c> si no existe o el documento está corrupto.</summary>
    public async Task<PersistedVisit?> GetAsync(string? visitId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(visitId)) { return null; }
        var json = await _store.ReadAsync(ResourceType, visitId.Trim(), cancellationToken).ConfigureAwait(false);
        return Leer(json);
    }

    /// <summary>
    /// Las visitas de una persona, de la más próxima a la más lejana.
    /// </summary>
    /// <remarks>
    /// <para><b>Se indexa por CORREO y no por miembro</b>, porque agendar una visita no exige
    /// sesión: <see cref="VisitContact"/> pide nombre y correo y nada más. Quien agendó como
    /// invitado y después se registra con el mismo correo recupera lo suyo, que es el
    /// comportamiento que alguien espera; quien usó otro correo, no — y eso es la verdad sobre
    /// ese dato, no un hueco que haya que rellenar.</para>
    ///
    /// <para><b>El correo lo pone la SESIÓN y nunca el llamador.</b> Aceptarlo por parámetro
    /// desde el borde sería el IDOR que Eventos ya cerró —«<c>?holder=&lt;email&gt;</c> listaba
    /// las entradas de cualquiera»—; acá listaría a qué hora va a estar una persona en una
    /// dirección concreta, que es peor.</para>
    ///
    /// <para><b>Vacío es «todavía ninguna» y es honesto</b>, porque existe un camino de escritura
    /// que la llena: agendar. Es el corte de
    /// <c>feedback_an_empty_list_is_honest_when_something_could_have_filled_it</c>.</para>
    /// </remarks>
    public async Task<IReadOnlyList<PersistedVisit>> ForVisitorAsync(string? email, CancellationToken cancellationToken = default)
    {
        var buscado = NormalizarCorreo(email);
        if (buscado.Length == 0) { return Array.Empty<PersistedVisit>(); }

        // `ListAsync` devuelve los DOCUMENTOS, no las claves — las dos implementaciones del seam
        // coinciden y el nombre no lo dice. Leerlo como una lista de ids y pedir cada uno da una
        // bandeja vacía, sin error: el `ReadAsync` de un id que es en realidad un JSON no
        // encuentra nada. Costó siete tests en rojo.
        var visitas = new List<PersistedVisit>();
        foreach (var json in await _store.ListAsync(ResourceType, cancellationToken).ConfigureAwait(false))
        {
            var v = Leer(json);
            if (v is not null && string.Equals(v.VisitorEmail, buscado, StringComparison.Ordinal))
            {
                visitas.Add(v);
            }
        }

        // Orden explícito: un directorio no tiene orden, y sin decirlo dos consultas idénticas
        // pueden paginar distinto (la lección del #112 sobre `JsonCollectionStore`).
        return visitas
            .OrderBy(v => v.StartUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(v => v.VisitId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Un documento ilegible se trata como ausente en vez de reventar: una visita corrupta no
    /// puede tumbar la bandeja entera de alguien que tiene otras cinco bien.
    /// </summary>
    private static PersistedVisit? Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try
        {
            var v = JsonSerializer.Deserialize<PersistedVisit>(json, Json);
            return v is null || string.IsNullOrWhiteSpace(v.VisitId) ? null : v;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// El correo, en minúsculas y sin espacios. El índice es una comparación exacta, así que sin
    /// normalizar al ESCRIBIR y al LEER con la misma regla, «Ana@x.com» no encuentra lo que
    /// «ana@x.com» guardó — y eso no falla: devuelve una bandeja vacía.
    /// </summary>
    private static string NormalizarCorreo(string? email)
        => string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
}

/// <summary>
/// Una visita agendada, tal como se guarda. Es el artefacto del eje 3 de Realty (#158).
/// </summary>
/// <param name="VisitId">El id que devolvió el seam de agendamiento. Clave del documento.</param>
/// <param name="ListingId">El inmueble que se visita.</param>
/// <param name="SlotId">El slot de la agenda del agente.</param>
/// <param name="StartUtc">Cuándo es la visita. <c>null</c> si el camino que la agendó no lo
///   supo — pasa con el cliente cableado cuando la capacidad no devuelve la hora, y es «no
///   consta» y no una fecha inventada.</param>
/// <param name="Mode">Presencial o videollamada (<see cref="VisitModes"/>). <c>null</c> es «no
///   consta», y es lo que dicen las visitas anteriores al #160 — cuando el borde enlazaba la
///   modalidad y nadie la leía. Rellenarlas con <c>in-person</c> afirmaría que esa gente pidió
///   que la recibieran en el inmueble, que es justo lo que no sabemos.</param>
/// <param name="VisitorName">El nombre que dio quien agendó.</param>
/// <param name="VisitorEmail">Su correo, normalizado en minúsculas: es el índice.</param>
/// <param name="VisitorPhone">Su teléfono, si lo dio. <c>null</c> es «no lo dio».</param>
/// <param name="Status">El estado que devolvió el seam al agendar.</param>
/// <param name="BookedAtUtc">Cuándo se agendó.</param>
public sealed record PersistedVisit(
    string VisitId,
    string ListingId,
    string SlotId,
    DateTimeOffset? StartUtc,
    string? Mode,
    string VisitorName,
    string VisitorEmail,
    string? VisitorPhone,
    string Status,
    DateTimeOffset BookedAtUtc);
