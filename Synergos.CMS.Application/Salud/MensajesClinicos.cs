using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Salud;

/// <summary>
/// La mensajería clínica (003-salud-cita, I7 P15) sobre la mensajería común (<see cref="IMessagingService"/>), que guarda
/// sus hilos cifrados (Q12): un hilo por paciente y profesional, entre el buzón del paciente y el del profesional.
/// </summary>
/// <remarks>
/// <para><b>Nadie se nombra por su correo</b>: el paciente es su historia (<c>salud.paciente:{clave}</c>) y el profesional,
/// su ficha del directorio (<c>salud.profesional:{id}</c>). El buzón del profesional lo atiende el consultorio, con la
/// guardia de la historia: el médico lee y contesta con el permiso del paciente.</para>
///
/// <para><b>«Esperando»</b> es el acuse que se ve en los dos lados: el último mensaje es del paciente y el consultorio
/// todavía no contestó.</para>
/// </remarks>
public static class MensajesClinicos
{
    public const string PrefijoDelContexto = "salud.consulta:";
    public const string PrefijoDelPaciente = "salud.paciente:";
    public const string PrefijoDelProfesional = "salud.profesional:";

    /// <summary>Lo más largo que puede ser un mensaje.</summary>
    public const int TextoMaximo = 2000;

    public static string Contexto(string profesionalId) => PrefijoDelContexto + profesionalId.Trim();

    public static string BuzonDelPaciente(Guid patientKey) => PrefijoDelPaciente + patientKey.ToString("N");

    public static string BuzonDelProfesional(string profesionalId) => PrefijoDelProfesional + profesionalId.Trim();

    public static bool EsClinico(string contexto) => contexto.StartsWith(PrefijoDelContexto, StringComparison.Ordinal);

    /// <summary>La historia del paciente del hilo, por su buzón; <c>null</c> si el hilo no tiene paciente.</summary>
    public static Guid? PacienteDe(IEnumerable<string> participantes)
        => participantes.Where(p => p.StartsWith(PrefijoDelPaciente, StringComparison.Ordinal))
            .Select(p => Guid.TryParseExact(p[PrefijoDelPaciente.Length..], "N", out var key) ? key : (Guid?)null)
            .FirstOrDefault(k => k is not null);

    /// <summary>El profesional del hilo, por su buzón; <c>null</c> si no lo tiene.</summary>
    public static string? ProfesionalDe(IEnumerable<string> participantes)
        => participantes.FirstOrDefault(p => p.StartsWith(PrefijoDelProfesional, StringComparison.Ordinal))?[PrefijoDelProfesional.Length..];

    /// <summary>Por qué el texto no es un mensaje, o <c>null</c> si lo es.</summary>
    public static string? Error(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? "El mensaje está vacío."
            : texto.Trim().Length > TextoMaximo ? $"El mensaje pasa de {TextoMaximo} caracteres."
            : null;

    /// <summary>Si el último mensaje del hilo es del paciente: el consultorio le debe una respuesta.</summary>
    public static bool Esperando(MessageThread hilo)
        => hilo.Messages.Count > 0 && hilo.Messages[^1].From.StartsWith(PrefijoDelPaciente, StringComparison.Ordinal);

    public static IReadOnlyList<MensajeClinico> Mensajes(MessageThread hilo)
        => hilo.Messages
            .Select(m => new MensajeClinico(m.MessageId, m.From.StartsWith(PrefijoDelPaciente, StringComparison.Ordinal), m.Body, m.SentAt))
            .ToList();

    public static HiloDelPaciente DelPaciente(MessageThread hilo, string profesional)
        => new(hilo.ThreadId, ProfesionalDe(hilo.Participants) ?? string.Empty, profesional, Esperando(hilo), hilo.LastMessageAt, Mensajes(hilo));

    public static HiloDelConsultorio DelConsultorio(MessageThread hilo, Guid patientKey, string paciente, string profesional)
        => new(hilo.ThreadId, ProfesionalDe(hilo.Participants) ?? string.Empty, profesional, patientKey, paciente,
            Esperando(hilo), hilo.LastMessageAt, Mensajes(hilo));
}

/// <summary>Un mensaje como lo lee la pantalla: si lo escribió el paciente, qué dice y cuándo.</summary>
public sealed record MensajeClinico(string Id, bool DelPaciente, string Texto, DateTimeOffset Fecha);

/// <summary>Un hilo como lo ve el paciente: con qué profesional, si espera respuesta y sus mensajes.</summary>
public sealed record HiloDelPaciente(
    string Id, string ProfesionalId, string Profesional, bool Esperando, DateTimeOffset Ultimo, IReadOnlyList<MensajeClinico> Mensajes);

/// <summary>Un hilo como lo ve el consultorio: además, de qué paciente es.</summary>
public sealed record HiloDelConsultorio(
    string Id, string ProfesionalId, string Profesional, Guid PacienteKey, string Paciente, bool Esperando, DateTimeOffset Ultimo,
    IReadOnlyList<MensajeClinico> Mensajes);

/// <summary>Lo que escribe el paciente a un profesional, o el consultorio en respuesta.</summary>
public sealed record MensajeEscrito(string? Profesional = null, string? Texto = null);

/// <summary>
/// La bandeja del consultorio (I7 P16): lo que espera al médico —un resultado por liberar, una renovación pedida, un
/// mensaje sin responder—, cada uno con su paciente. Sale de la bandeja al atenderlo.
/// </summary>
/// <param name="Tipo"><c>resultado</c>, <c>renovacion</c> o <c>mensaje</c>.</param>
/// <param name="Id">El resultado, la solicitud o el hilo.</param>
public sealed record ItemDeLaBandeja(
    string Tipo, string Id, Guid PacienteKey, string Paciente, string Resumen, DateTimeOffset Fecha, string? ProfesionalId = null);

/// <summary>La bandeja, de lo más reciente a lo más antiguo, y cuántos pacientes no se vieron por falta de su permiso.</summary>
public sealed record BandejaDelConsultorio(IReadOnlyList<ItemDeLaBandeja> Items, int SinPermiso);

/// <summary>Lo que suma la cuenta en una moneda: lo cobrado, lo devuelto y lo que queda.</summary>
public sealed record TotalDeLaCuenta(string Moneda, decimal Cobrado, decimal Devuelto, decimal Neto);

/// <summary>El estado de cuenta del paciente (I7 P17): las citas cobradas y lo que suman, con las cifras de Payments.</summary>
public sealed record CuentaParaLeer(IReadOnlyList<LineaDeLaCuenta> Lineas, IReadOnlyList<TotalDeLaCuenta> Totales)
{
    public static CuentaParaLeer De(EstadoDeCuenta cuenta)
    {
        ArgumentNullException.ThrowIfNull(cuenta);
        var lineas = cuenta.Lineas.OrderByDescending(l => l.Inicio).ToList();
        var totales = lineas.GroupBy(l => l.Moneda, StringComparer.Ordinal)
            .Select(g => new TotalDeLaCuenta(g.Key, g.Sum(l => l.Cobrado), g.Sum(l => l.Devuelto), g.Sum(l => l.Cobrado - l.Devuelto)))
            .OrderBy(t => t.Moneda, StringComparer.Ordinal)
            .ToList();
        return new CuentaParaLeer(lineas, totales);
    }
}
