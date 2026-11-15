using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Salud.Domain;

/// <summary>
/// <c>salud.momento-del-recordatorio</c> (003-salud-cita, I8 P18): lee la franja de la cita y escribe cuándo sale su
/// recordatorio —<see cref="HorasAntes"/> horas antes del inicio—, o nulo si ese momento ya pasó.
/// </summary>
/// <remarks>
/// <para><b>Nulo y no ahora</b>: una cita agendada para dentro de menos de un día no recibe un «mañana tienes cita» que
/// saldría en el acto; el paso que programa se salta con <c>omitir_si_falta</c>. El correo de confirmación ya dijo la hora.</para>
///
/// <para>El disparo lo hace <c>Api.Notifications</c> («no antes de»), con el paso común <c>notifications.programar</c>; aquí
/// sólo se decide el momento, con el reloj del orquestador.</para>
/// </remarks>
public sealed class PasoMomentoDelRecordatorio(TimeProvider reloj, int horasAntes) : IPaso
{
    /// <summary>Cuántas horas antes de la cita sale el recordatorio, si la configuración no dice otra cosa.</summary>
    public const int HorasPorDefecto = 24;

    public int HorasAntes { get; } = horasAntes > 0 ? horasAntes : HorasPorDefecto;

    public string Tipo => "salud.momento-del-recordatorio";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(DateTimeOffset)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var momento = Momento(entrada.Lee<TimeWindow>(0).Start, reloj.GetUtcNow(), HorasAntes);
        return Task.FromResult(SalidaDePaso.Sigue(momento is { } m ? (object)m : null));
    }

    /// <summary>Cuándo sale el recordatorio de una cita que empieza en <paramref name="inicio"/>; nulo si ya pasó ese momento.</summary>
    public static DateTimeOffset? Momento(DateTimeOffset inicio, DateTimeOffset ahora, int horasAntes)
    {
        var momento = inicio.AddHours(-horasAntes);
        return momento > ahora ? momento : null;
    }
}

/// <summary>
/// <c>salud.recordar</c> (I8 P18): programa el recordatorio con el paso común <c>notifications.programar</c> y escribe su
/// entrega, para que la cancelación la retire; si no se puede programar, sigue sin recordatorio.
/// </summary>
/// <remarks>
/// <para><b>Por qué no el paso común con <c>al_fallar: seguir</c></b>: la definición no deja que un paso que puede fallar
/// escriba, y la cancelación necesita la entrega para retirarla. Y sin <c>al_fallar</c>, un recordatorio caído al final de
/// «cerrar» desharía una cita ya cobrada y confirmada: devolvería el copago por un correo. Este paso hace lo mismo que el
/// común y, si Notifications dice que no, lo deja en el registro y escribe nada: la cita queda confirmada sin recordatorio, y
/// la cancelación no tiene qué retirar.</para>
///
/// <para>Lee, escribe y declara lo mismo que <c>notifications.programar</c> (la plantilla, los valores fijos y los
/// declarados): la plantilla se cruza igual con <c>tools/provisionar.plantillas.json</c>.</para>
/// </remarks>
public sealed class PasoRecordarLaCita(PasoProgramarAvisoDeSalud comun, ILogger log) : IPaso
{
    public string Tipo => "salud.recordar";
    public IReadOnlyList<Type> TiposDeEscritura => comun.TiposDeEscritura;
    public int Lecturas => comun.Lecturas;
    public int LecturasOpcionales => comun.LecturasOpcionales;
    public int Escrituras => comun.Escrituras;
    public int EscriturasOpcionales => comun.Escrituras;
    public LlaveRequerida Llave => comun.Llave;
    public bool UsaPlantilla => true;
    public IReadOnlyCollection<string>? ValoresFijos => comun.ValoresFijos;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var programado = await comun.EjecutarAsync(entrada, ct);
        if (programado.Rechazo is { } rechazo)
        {
            log.LogWarning("El recordatorio de la cita {Saga} no se programó ({Codigo}): la cita sigue confirmada, sin recordatorio.",
                entrada.SagaId, rechazo.Code);
            return SalidaDePaso.Sigue((object?)null);
        }
        return programado;
    }
}

/// <summary>El paso común que programa un aviso, con lo que lee y declara a la vista de <see cref="PasoRecordarLaCita"/>.</summary>
public sealed class PasoProgramarAvisoDeSalud(Synergos.Bff.Pasos.IAvisosProgramados avisos, TimeZoneInfo zona)
{
    private readonly Synergos.Bff.Pasos.PasoProgramarAviso _paso = new(avisos, zona);

    public IReadOnlyList<Type> TiposDeEscritura => _paso.TiposDeEscritura;
    public int Lecturas => _paso.Lecturas;
    public int LecturasOpcionales => _paso.LecturasOpcionales;
    public int Escrituras => _paso.Escrituras;
    public LlaveRequerida Llave => _paso.Llave;
    public IReadOnlyCollection<string>? ValoresFijos => _paso.ValoresFijos;

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct) => _paso.EjecutarAsync(entrada, ct);
}
