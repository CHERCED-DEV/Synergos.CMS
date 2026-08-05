using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Los hechos de UNA entrada, sin nada del camino por el que se compró.
/// </summary>
/// <param name="EventId">De qué evento es.</param>
/// <param name="SeatRef">El identificador de <b>lo que se apartó</b> para esta entrada. Hoy el
/// de la reserva del motor propio; mañana el del apartado de aforo del orquestador.</param>
/// <param name="HolderName">Nombre del portador ACTUAL (cambia al transferir).</param>
/// <param name="HolderEmail">Email del portador actual.</param>
/// <param name="Tier">La localidad.</param>
/// <param name="Seat">La butaca, o <c>null</c> en cupo general.</param>
/// <param name="QrVersion">Sube en cada transferencia: el QR del dueño anterior deja de valer.</param>
/// <param name="CheckedIn">Si ya pasó por la puerta.</param>
/// <remarks>
/// <para><b>Lo que este record NO tiene es lo que lo hace útil:</b> ni orden, ni sesión de pago,
/// ni reserva, ni saga. Son los hechos que el portador puede leer en su entrada y que la puerta
/// necesita para dejarlo pasar — nada del camino por el que la compró.</para>
///
/// <para>Esa ausencia es todo el punto de la extracción: <c>ToTicket</c> proyectaba desde una
/// <c>PersistedEventUnit</c>, o sea desde el estado que el propio motor de checkout había creado.
/// Un camino de compra que no cree esa unidad —porque el cobro y el aforo se fueron a un
/// orquestador— no podía emitir nada, y la salida obvia era copiar el formato del QR a otro
/// fichero. Un QR con dos definiciones es un QR que un día se firma de dos maneras.</para>
/// </remarks>
public sealed record EventTicketFacts(
    string EventId,
    string SeatRef,
    string HolderName,
    string HolderEmail,
    string Tier,
    string? Seat,
    int QrVersion = 0,
    bool CheckedIn = false);

/// <summary>
/// El ÚNICO sitio donde se decide cómo se llama una entrada y qué lleva dentro su QR.
/// </summary>
/// <remarks>
/// <para><b>Es una clase concreta y no un seam, a propósito</b> (<c>CLAUDE.md</c> §6). No hay dos
/// implementaciones ni las va a haber: hay dos <i>llamadores</i> —el motor propio y, cuando se
/// cablee, el que compra contra el orquestador— y lo que comparten es precisamente que la
/// emisión sea una sola. La costura que sí hacía falta ya existe y es <see cref="ITicketSigner"/>:
/// lo que cambia entre entornos es la llave, no el formato.</para>
///
/// <para><b>Sin firmante no se emite QR</b> (fail-closed, T9/ADR 0110): se devuelve vacío en vez
/// de un token falsificable. La UI no pinta código y la puerta no valida nada. Una entrada sin
/// código es mejor que una que finge tenerlo.</para>
///
/// <para><b>No tiene estado.</b> Dos instancias sobre el mismo firmante son indistinguibles, así
/// que quien la construya no necesita coordinarse con nadie — solo resolver el mismo
/// <see cref="ITicketSigner"/>.</para>
/// </remarks>
public sealed class EventTicketIssuer
{
    private readonly ITicketSigner? _signer;

    /// <param name="signer">Firmante del QR. <c>null</c> ≡ fail-closed: se emiten entradas sin
    /// código en vez de códigos sin firma.</param>
    public EventTicketIssuer(ITicketSigner? signer) => _signer = signer;

    /// <summary>Si hay firmante y por tanto las entradas salen con QR.</summary>
    public bool CanSign => _signer is not null;

    /// <summary>
    /// El id de la entrada que corresponde a lo apartado en <paramref name="seatRef"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Determinista</b>: el mismo apartado da siempre la misma entrada, así que
    /// re-confirmar una compra no emite entradas nuevas — es de dónde sale la idempotencia del
    /// artefacto, y por eso hace falta suelto y no solo dentro de <see cref="Issue"/>: quien
    /// busca una entrada por su id necesita derivarlo sin construirla entera.</para>
    ///
    /// <para><b>Estático porque el id no tiene nada que ver con la llave</b>: derivarlo no firma
    /// nada, y quien solo necesita buscar una entrada por su id no debería tener que conseguir un
    /// firmante para hacerlo.</para>
    ///
    /// <para>El recorte de <c>resv_</c> es cosmético y se conserva tal cual para no renombrar las
    /// entradas ya emitidas. Un identificador con otra forma sale íntegro detrás de
    /// <c>tkt_</c>, que es correcto: el id es opaco y solo tiene que ser estable y único.</para>
    /// </remarks>
    public static string TicketIdOf(string seatRef)
    {
        if (string.IsNullOrWhiteSpace(seatRef))
        {
            throw new ArgumentException("Una entrada necesita saber qué se apartó para ella.", nameof(seatRef));
        }

        var id = "tkt_" + seatRef.Trim().Replace("resv_", string.Empty, StringComparison.Ordinal);

        // El id NO puede llevar guiones, y no es capricho de estilo: el payload del token es
        // `SYN-TKT-{evento}-{entrada}-v{n}`, y al deshacerlo se corta por el ÚLTIMO guion para
        // que el identificador del evento sí pueda llevarlos. Un guion en el id de la entrada
        // mueve ese corte, y el token verifica bien pero devuelve OTRA entrada.
        //
        // Lo que sale de ahí es lo peor que puede pasar en esta parte del producto: se emite un
        // QR con firma válida y la puerta dice `invalid`, sin un error en ningún log. Se prefiere
        // reventar en la primera compra —donde hay un stack— a fallar en la entrada del recinto.
        if (id.Contains('-', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"El identificador de lo apartado ('{seatRef}') produce un id de entrada con guiones, "
                + "y el token del QR no puede deshacerlos: verificaría bien y devolvería otra entrada.",
                nameof(seatRef));
        }

        return id;
    }

    /// <summary>
    /// Emite la entrada: su id, su QR firmado y su estado.
    /// </summary>
    /// <remarks>
    /// Emitir dos veces los mismos hechos devuelve exactamente la misma entrada — mismo id y
    /// mismo QR. No hay nada que «gastar» acá: la emisión es una proyección, y lo que de verdad
    /// no se puede repetir (cobrar, consumir aforo) vive antes y en otro sitio.
    /// </remarks>
    public EventTicket Issue(EventTicketFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var ticketId = TicketIdOf(facts.SeatRef);
        return new EventTicket(
            Id: ticketId,
            Qr: Qr(facts.EventId, ticketId, facts.QrVersion),
            EventId: facts.EventId,
            AttendeeName: facts.HolderName,
            Tier: facts.Tier,
            Seat: facts.Seat,
            HolderEmail: facts.HolderEmail,
            Status: StatusOf(facts.CheckedIn, facts.QrVersion));
    }

    /// <summary>
    /// El estado que ve el asistente: <c>used</c> manda sobre todo; luego
    /// <c>transferred</c> (el QR rotó al menos una vez); si no, <c>valid</c>.
    /// </summary>
    public static string StatusOf(bool checkedIn, int qrVersion)
    {
        if (checkedIn)
        {
            return "used";
        }
        return qrVersion > 0 ? "transferred" : "valid";
    }

    /// <summary>
    /// El payload del QR: rotativo por versión (SafeTix-like) y FIRMADO.
    /// </summary>
    /// <remarks>
    /// Antes el sufijo salía de <c>String.GetHashCode()</c> del email: no criptográfico y, en
    /// .NET Core, RANDOMIZADO POR PROCESO — el mismo ticket daba un QR distinto tras cada
    /// reinicio. Nadie lo notó porque nada verificaba el QR (T9/ADR 0110).
    /// </remarks>
    private string Qr(string eventId, string ticketId, int qrVersion)
        => _signer is null ? string.Empty : _signer.Sign(new TicketToken(eventId, ticketId, qrVersion));
}
