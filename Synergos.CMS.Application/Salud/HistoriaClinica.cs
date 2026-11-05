using System.Globalization;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Salud;

/// <summary>Lo que el paciente manda en su pre-registro (003-salud-cita, I2 T058). Todo es opcional.</summary>
public sealed record CheckInDelPaciente(
    string? Documento = null,
    string? Telefono = null,
    string? Direccion = null,
    string? ContactoDeEmergencia = null,
    string? Aseguradora = null,
    string? NumeroDeAfiliacion = null,
    IReadOnlyList<string>? Medicamentos = null,
    string? MotivoDeLaVisita = null,
    IReadOnlyList<string>? Condiciones = null,
    IReadOnlyList<string>? Alergias = null,
    IReadOnlyList<VacunaDeclarada>? Vacunas = null);

/// <summary>Una vacuna como la declara el paciente: el nombre y, si la sabe, la fecha (<c>aaaa-mm-dd</c>).</summary>
public sealed record VacunaDeclarada(string? Nombre, string? Fecha = null);

/// <summary>Lo que pasó al validar un dato del resumen.</summary>
public enum ResultadoDeValidar
{
    Validado,
    YaEstabaValidado,
    NoExiste,
}

/// <summary>Un dato del resumen como se lee: sin las claves de quién lo registró ni de quién lo validó.</summary>
public sealed record DatoParaLeer(string Id, string Tipo, string Descripcion, string? Fecha, string Estado, string Origen);

/// <summary>El resumen de salud de una historia como lo leen el paciente y el médico (I2 T060).</summary>
/// <param name="Datos">Las condiciones, las alergias y las vacunas, con su estado.</param>
/// <param name="PreRegistro">Lo último que declaró el paciente, o <c>null</c>.</param>
/// <param name="HayResumen">Si la historia tiene resumen: <c>false</c> es «nadie lo ha llenado», no «sin alergias».</param>
public sealed record ResumenParaLeer(IReadOnlyList<DatoParaLeer> Datos, PreRegistro? PreRegistro, bool HayResumen);

/// <summary>
/// Las reglas puras de la historia clínica de I2 (003-salud-cita): el pre-registro del paciente, la validación del
/// médico y cómo se lee el resumen. Sin almacén ni acceso: quién puede hacer cada cosa lo decide la guardia
/// (<see cref="IPhiAccessGuard"/>) y el cambio lo guarda el editor de la historia, con su candado.
/// </summary>
public static class HistoriaClinica
{
    /// <summary>Lo más largo que puede ser un texto del pre-registro.</summary>
    public const int LargoMaximo = 200;

    /// <summary>Cuántos datos puede traer cada lista del pre-registro.</summary>
    public const int DatosMaximos = 30;

    public const string OrigenPaciente = "paciente";
    public const string OrigenMedico = "medico";

    /// <summary>
    /// Lo que no se puede guardar del pre-registro, o <c>null</c> si todo está bien. Nombra el campo: el paciente
    /// sabe qué corregir.
    /// </summary>
    public static string? ErrorDelCheckIn(CheckInDelPaciente checkIn, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(checkIn);
        foreach (var (campo, valor) in new (string, string?)[]
        {
            ("documento", checkIn.Documento),
            ("telefono", checkIn.Telefono),
            ("direccion", checkIn.Direccion),
            ("contactoDeEmergencia", checkIn.ContactoDeEmergencia),
            ("aseguradora", checkIn.Aseguradora),
            ("numeroDeAfiliacion", checkIn.NumeroDeAfiliacion),
            ("motivoDeLaVisita", checkIn.MotivoDeLaVisita),
        })
        {
            if (Normalizado(valor) is { Length: > LargoMaximo })
            {
                return $"{campo}: más de {LargoMaximo} caracteres.";
            }
        }

        foreach (var (campo, lista) in new (string, IReadOnlyList<string>?)[]
        {
            ("medicamentos", checkIn.Medicamentos),
            ("condiciones", checkIn.Condiciones),
            ("alergias", checkIn.Alergias),
        })
        {
            if (ErrorDeLaLista(campo, lista) is { } error)
            {
                return error;
            }
        }

        var vacunas = checkIn.Vacunas ?? Array.Empty<VacunaDeclarada>();
        if (vacunas.Count > DatosMaximos)
        {
            return $"vacunas: más de {DatosMaximos}.";
        }
        foreach (var vacuna in vacunas)
        {
            var nombre = Normalizado(vacuna?.Nombre);
            if (nombre is null)
            {
                return "vacunas: una vacuna sin nombre.";
            }
            if (nombre.Length > LargoMaximo)
            {
                return $"vacunas: más de {LargoMaximo} caracteres.";
            }
            if (Normalizado(vacuna!.Fecha) is { } fecha
                && (!DateOnly.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia) || dia > hoy))
            {
                return "vacunas: la fecha va como aaaa-mm-dd y no puede ser futura.";
            }
        }

        return null;
    }

    /// <summary>
    /// Aplica el pre-registro a la historia: el pre-registro se reemplaza entero, y lo clínico entra como DECLARADO.
    /// </summary>
    /// <remarks>
    /// <para><b>Lo validado no se toca.</b> Lo declarado por el paciente y todavía pendiente es su última palabra:
    /// cada pre-registro lo reemplaza, así puede corregirse antes de que el médico lo revise. Un dato pendiente que
    /// vuelve a declarar igual conserva su id, y uno que el médico ya validó no se duplica.</para>
    /// <para>Se llama después de <see cref="ErrorDelCheckIn"/>: no vuelve a validar.</para>
    /// </remarks>
    public static PatientRecord Declarar(
        PatientRecord historia,
        CheckInDelPaciente checkIn,
        Guid paciente,
        DateTime ahoraUtc,
        Func<string>? nuevoId = null)
    {
        ArgumentNullException.ThrowIfNull(historia);
        ArgumentNullException.ThrowIfNull(checkIn);
        nuevoId ??= static () => Guid.NewGuid().ToString("N");

        var antes = historia.Resumen?.Datos ?? Array.Empty<DatoDeSalud>();
        var quedan = antes.Where(d => !EsPendienteDelPaciente(d)).ToList();
        var pendientes = antes.Where(EsPendienteDelPaciente).ToList();

        foreach (var (tipo, descripcion, fecha) in DeclaradosDe(checkIn))
        {
            if (quedan.Any(d => EsElMismo(d, tipo, descripcion, fecha)))
            {
                continue;
            }
            var igual = pendientes.FirstOrDefault(d => EsElMismo(d, tipo, descripcion, fecha));
            quedan.Add(igual ?? new DatoDeSalud(
                nuevoId(), tipo, descripcion, fecha, EstadosDelDato.Declarado, OrigenPaciente, ahoraUtc, paciente));
        }

        var preRegistro = new PreRegistro(
            Normalizado(checkIn.Documento),
            Normalizado(checkIn.Telefono),
            Normalizado(checkIn.Direccion),
            Normalizado(checkIn.ContactoDeEmergencia),
            Normalizado(checkIn.Aseguradora),
            Normalizado(checkIn.NumeroDeAfiliacion),
            Limpia(checkIn.Medicamentos),
            Normalizado(checkIn.MotivoDeLaVisita),
            ahoraUtc);

        return historia with { Resumen = new ResumenDeSalud(quedan), PreRegistro = preRegistro };
    }

    /// <summary>
    /// El médico valida un dato del resumen. Idempotente: validar uno ya validado no lo cambia ni lo vuelve a
    /// fechar. Devuelve la historia con el cambio (la misma instancia si no hubo cambio).
    /// </summary>
    public static (PatientRecord Historia, ResultadoDeValidar Resultado) Validar(
        PatientRecord historia,
        string datoId,
        Guid medico,
        DateTime ahoraUtc)
    {
        ArgumentNullException.ThrowIfNull(historia);
        var datos = historia.Resumen?.Datos ?? Array.Empty<DatoDeSalud>();
        var indice = datos.ToList().FindIndex(d => string.Equals(d.Id, datoId, StringComparison.Ordinal));
        if (indice < 0)
        {
            return (historia, ResultadoDeValidar.NoExiste);
        }
        if (datos[indice].Estado == EstadosDelDato.Validado)
        {
            return (historia, ResultadoDeValidar.YaEstabaValidado);
        }

        var nuevos = datos.ToList();
        nuevos[indice] = datos[indice] with { Estado = EstadosDelDato.Validado, ValidadoUtc = ahoraUtc, ValidadoPor = medico };
        return (historia with { Resumen = new ResumenDeSalud(nuevos) }, ResultadoDeValidar.Validado);
    }

    /// <summary>
    /// El resumen como se lee: los datos por tipo (condiciones, alergias y vacunas), cada uno con su estado. Lo que
    /// no se ha validado se dice como pendiente; nunca se presenta como dato clínico.
    /// </summary>
    public static ResumenParaLeer ParaLeer(PatientRecord historia)
    {
        ArgumentNullException.ThrowIfNull(historia);
        var datos = historia.Resumen?.Datos ?? Array.Empty<DatoDeSalud>();
        var ordenados = TiposDeDatoDeSalud.Todos
            .SelectMany(tipo => datos.Where(d => d.Tipo == tipo))
            .Select(d => new DatoParaLeer(d.Id, d.Tipo, d.Descripcion, d.Fecha, d.Estado, d.Origen))
            .ToList();
        return new ResumenParaLeer(ordenados, historia.PreRegistro, historia.Resumen is not null);
    }

    private static bool EsPendienteDelPaciente(DatoDeSalud dato) =>
        dato.Estado == EstadosDelDato.Declarado && dato.Origen == OrigenPaciente;

    private static bool EsElMismo(DatoDeSalud dato, string tipo, string descripcion, string? fecha) =>
        dato.Tipo == tipo
        && string.Equals(dato.Descripcion, descripcion, StringComparison.OrdinalIgnoreCase)
        && string.Equals(dato.Fecha, fecha, StringComparison.Ordinal);

    private static IEnumerable<(string Tipo, string Descripcion, string? Fecha)> DeclaradosDe(CheckInDelPaciente checkIn)
    {
        foreach (var condicion in Limpia(checkIn.Condiciones))
        {
            yield return (TiposDeDatoDeSalud.Condicion, condicion, null);
        }
        foreach (var alergia in Limpia(checkIn.Alergias))
        {
            yield return (TiposDeDatoDeSalud.Alergia, alergia, null);
        }
        foreach (var vacuna in checkIn.Vacunas ?? Array.Empty<VacunaDeclarada>())
        {
            if (Normalizado(vacuna?.Nombre) is { } nombre)
            {
                yield return (TiposDeDatoDeSalud.Vacuna, nombre, Normalizado(vacuna!.Fecha));
            }
        }
    }

    private static string? ErrorDeLaLista(string campo, IReadOnlyList<string>? lista)
    {
        if (lista is null)
        {
            return null;
        }
        if (lista.Count > DatosMaximos)
        {
            return $"{campo}: más de {DatosMaximos}.";
        }
        return lista.Any(item => Normalizado(item) is { Length: > LargoMaximo })
            ? $"{campo}: más de {LargoMaximo} caracteres."
            : null;
    }

    /// <summary>Los textos de una lista, sin vacíos ni repetidos (sin distinguir mayúsculas).</summary>
    private static IReadOnlyList<string> Limpia(IReadOnlyList<string>? lista) =>
        (lista ?? Array.Empty<string>())
            .Select(Normalizado)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Sin espacios de más y sin caracteres de control; <c>null</c> si no queda nada.</summary>
    private static string? Normalizado(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }
        var partes = texto
            .Where(c => !char.IsControl(c) || char.IsWhiteSpace(c))
            .Aggregate(new System.Text.StringBuilder(), (sb, c) => sb.Append(char.IsWhiteSpace(c) ? ' ' : c))
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length == 0 ? null : string.Join(' ', partes);
    }
}
