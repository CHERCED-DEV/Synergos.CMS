namespace Synergos.Core;

/// <summary>
/// Un intervalo de tiempo, medio abierto: <c>[Start, End)</c>.
/// </summary>
/// <param name="Start">Inicio, inclusive.</param>
/// <param name="End">Fin, exclusivo.</param>
/// <remarks>
/// <para><b>Medio abierto, y no cerrado.</b> Es la decisión que evita el error clásico de las
/// agendas: con intervalos cerrados, una cita de 10:00–10:30 y otra de 10:30–11:00 "se solapan"
/// en el instante 10:30, y la agenda rechaza reservas perfectamente válidas. Con
/// <c>[Start, End)</c> encajan exactamente, que es lo que un humano espera al mirar el
/// calendario.</para>
///
/// <para><b>Por qué un tipo y no dos <c>DateTimeOffset</c>.</b> Dos parámetros del mismo tipo se
/// pasan al revés, el compilador no dice nada, y el síntoma es "no hay disponibilidad" — que se
/// investiga como un problema de datos durante horas. Aquí construirla al revés falla en el
/// sitio donde se creó.</para>
///
/// <para><b><see cref="DateTimeOffset"/> y no <see cref="DateTime"/>:</b> una agenda cruza husos
/// y horarios de verano. Un <c>DateTime</c> sin offset es una hora sin lugar, y a la hora de
/// comparar dos reservas de dos países no significa nada.</para>
/// </remarks>
public readonly record struct TimeWindow(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>Construye validando que la ventana avance.</summary>
    /// <exception cref="ArgumentException">Si <paramref name="end"/> no es posterior a <paramref name="start"/>.</exception>
    public static TimeWindow Of(DateTimeOffset start, DateTimeOffset end)
        => end > start
            ? new TimeWindow(start, end)
            : throw new ArgumentException(
                $"Una ventana necesita avanzar: {start:O} → {end:O}. Una ventana vacía o invertida " +
                "no es 'nada reservado', es un error de quien la construyó.", nameof(end));

    /// <summary>Ventana que arranca en <paramref name="start"/> y dura <paramref name="duration"/>.</summary>
    public static TimeWindow Starting(DateTimeOffset start, TimeSpan duration) => Of(start, start + duration);

    public TimeSpan Duration => End - Start;

    /// <summary>Si el instante cae dentro. El fin queda FUERA, por ser exclusivo.</summary>
    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;

    /// <summary>
    /// Si las dos ventanas comparten algún instante.
    /// </summary>
    /// <remarks>
    /// Es la pregunta que hace una agenda mil veces al día. Dos ventanas contiguas
    /// —10:00–10:30 y 10:30–11:00— <b>no</b> se solapan.
    /// </remarks>
    public bool Overlaps(TimeWindow other) => Start < other.End && other.Start < End;

    /// <summary>Si esta ventana contiene enteramente a la otra.</summary>
    public bool Encloses(TimeWindow other) => other.Start >= Start && other.End <= End;

    /// <summary>Si la ventana terminó respecto del reloj dado.</summary>
    /// <remarks>
    /// El reloj se pasa, no se lee de <c>DateTimeOffset.UtcNow</c> aquí dentro: un tipo que
    /// mira el reloj por su cuenta no se puede probar sin esperar, y la mitad de los errores de
    /// agenda son de borde temporal — justo los que hay que poder reproducir.
    /// </remarks>
    public bool HasEnded(DateTimeOffset now) => now >= End;

    public override string ToString() => $"[{Start:O} → {End:O})";
}
