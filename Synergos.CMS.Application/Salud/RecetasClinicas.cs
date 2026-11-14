using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Salud;

/// <summary>Lo que el médico manda al emitir una receta (I5 P13). <c>Dias</c> es su vigencia.</summary>
public sealed record RecetaEscrita(
    string? Medicamento = null, string? Dosis = null, string? Frecuencia = null, string? Indicaciones = null, int? Dias = null);

/// <summary>Lo que el paciente manda al pedir una renovación, y el médico al resolverla.</summary>
public sealed record PedidoDeRenovacion(string? Nota = null, int? Dias = null, string? Motivo = null);

/// <summary>Una receta como se lee: sin las claves de quién la emitió.</summary>
public sealed record RecetaParaLeer(
    Guid Id, string Medicamento, string Dosis, string Frecuencia, string? Indicaciones, DateTime DesdeUtc, DateTime HastaUtc,
    string Estado, bool Vigente, bool Firmada, Guid? RenuevaA);

public sealed record RenovacionParaLeer(
    Guid Id, Guid RecetaId, string? Nota, string Estado, DateTime PedidaUtc, DateTime? ResueltaUtc, Guid? NuevaRecetaId,
    string? Motivo);

/// <summary>
/// Las reglas puras de la receta y de su renovación (003-salud-cita, I5): qué se emite, su huella —lo que se firma—, si
/// está vigente, el PDF y la receta nueva que sale de aprobar una renovación. Quién puede cada cosa lo decide la guardia.
/// </summary>
public static class RecetasClinicas
{
    public const int LargoMaximo = 200;
    public const int LargoDeLasIndicaciones = 1000;
    public const int DiasMaximos = 365;
    public const int DiasPorDefecto = 30;

    public const string Activa = "active";
    public const string Pedida = "pedida";
    public const string Aprobada = "aprobada";
    public const string Rechazada = "rechazada";

    public static string? Error(RecetaEscrita receta)
    {
        ArgumentNullException.ThrowIfNull(receta);
        foreach (var (campo, texto) in new[] { ("medicamento", receta.Medicamento), ("dosis", receta.Dosis), ("frecuencia", receta.Frecuencia) })
        {
            var t = (texto ?? string.Empty).Trim();
            if (t.Length is 0 or > LargoMaximo)
            {
                return $"{campo}: falta o tiene más de {LargoMaximo} caracteres.";
            }
        }
        if ((receta.Indicaciones ?? string.Empty).Trim().Length > LargoDeLasIndicaciones)
        {
            return $"indicaciones: más de {LargoDeLasIndicaciones} caracteres.";
        }
        return receta.Dias is { } d && (d < 1 || d > DiasMaximos) ? $"dias: de 1 a {DiasMaximos}." : null;
    }

    public static PrescriptionRecord Emitir(RecetaEscrita receta, Guid patientKey, Guid medico, DateTime ahoraUtc, Guid? renuevaA = null)
    {
        ArgumentNullException.ThrowIfNull(receta);
        var indicaciones = (receta.Indicaciones ?? string.Empty).Trim();
        return new PrescriptionRecord(
            Guid.NewGuid(), patientKey, medico, receta.Medicamento!.Trim(), receta.Dosis!.Trim(), receta.Frecuencia!.Trim(),
            indicaciones.Length == 0 ? null : indicaciones, ahoraUtc, ahoraUtc.AddDays(receta.Dias ?? DiasPorDefecto), ahoraUtc,
            Activa, RenuevaA: renuevaA);
    }

    /// <summary>La receta nueva que sale de aprobar la renovación: lo mismo de la anterior, con su vigencia desde hoy.</summary>
    public static PrescriptionRecord Renovar(PrescriptionRecord anterior, int? dias, Guid medico, DateTime ahoraUtc)
    {
        ArgumentNullException.ThrowIfNull(anterior);
        var vigencia = dias ?? Math.Max(1, (int)Math.Round((anterior.ValidToUtc - anterior.ValidFromUtc).TotalDays));
        return Emitir(new RecetaEscrita(anterior.MedicationName, anterior.Dosage, anterior.Frequency, anterior.Instructions,
            Math.Clamp(vigencia, 1, DiasMaximos)), anterior.PatientKey, medico, ahoraUtc, anterior.PrescriptionId);
    }

    /// <summary>La huella de la receta: SHA-256 de su forma canónica (todo lo que afirma), en hexadecimal.</summary>
    public static string Huella(PrescriptionRecord receta)
    {
        ArgumentNullException.ThrowIfNull(receta);
        var canonica = JsonSerializer.Serialize(new[]
        {
            "salud.receta.v1", receta.PrescriptionId.ToString("N"), receta.PatientKey.ToString("N"), receta.IssuedByDoctorKey.ToString("N"),
            receta.MedicationName, receta.Dosage, receta.Frequency, receta.Instructions ?? string.Empty,
            receta.ValidFromUtc.ToString("O", CultureInfo.InvariantCulture), receta.ValidToUtc.ToString("O", CultureInfo.InvariantCulture),
            receta.RenuevaA?.ToString("N") ?? string.Empty,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonica))).ToLowerInvariant();
    }

    public static bool Vigente(PrescriptionRecord receta, DateTime ahoraUtc)
    {
        ArgumentNullException.ThrowIfNull(receta);
        return receta.Status == Activa && receta.Firma is not null && receta.ValidFromUtc <= ahoraUtc && ahoraUtc < receta.ValidToUtc;
    }

    public static RecetaParaLeer ParaLeer(PrescriptionRecord r, DateTime ahoraUtc)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new RecetaParaLeer(r.PrescriptionId, r.MedicationName, r.Dosage, r.Frequency, r.Instructions, r.ValidFromUtc,
            r.ValidToUtc, r.Status, Vigente(r, ahoraUtc), r.Firma is not null, r.RenuevaA);
    }

    public static RenovacionParaLeer ParaLeer(SolicitudDeRenovacion s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new RenovacionParaLeer(s.Id, s.RecetaId, s.Nota, s.Estado, s.PedidaUtc, s.ResueltaUtc, s.NuevaRecetaId, s.Motivo);
    }

    /// <summary>
    /// El PDF de la receta firmada: lo que dice, para quién y la firma —el sello y su llave— con que se comprueba. Se genera al
    /// pedirlo, de lo que guarda la historia: no hay un fichero aparte que pueda quedar distinto.
    /// </summary>
    public static byte[] Pdf(PrescriptionRecord r, string paciente, TimeZoneInfo zona)
    {
        ArgumentNullException.ThrowIfNull(r);
        string Fecha(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zona)
            .ToString("d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es-CO"));
        var lineas = new List<string>
        {
            "RECETA MÉDICA",
            string.Empty,
            $"Paciente: {paciente}",
            $"Emitida: {Fecha(r.IssuedAtUtc)}",
            $"Vigente del {Fecha(r.ValidFromUtc)} al {Fecha(r.ValidToUtc)}",
            string.Empty,
            $"Medicamento: {r.MedicationName}",
            $"Dosis: {r.Dosage}",
            $"Frecuencia: {r.Frequency}",
        };
        if (r.Instructions is { } indicaciones)
        {
            lineas.Add($"Indicaciones: {indicaciones}");
        }
        lineas.Add(string.Empty);
        if (r.Firma is { } firma)
        {
            lineas.Add("Firmada con la llave del consultorio.");
            lineas.Add($"Sello: {firma.Sello}");
            lineas.Add($"Llave: {firma.LlaveId}");
            lineas.Add($"Huella: {firma.Huella}");
        }
        else
        {
            lineas.Add("SIN FIRMA: esta receta no es válida.");
        }
        if (r.Status != Activa)
        {
            lineas.Add("ANULADA.");
        }
        return PdfDeTexto.Generar(lineas);
    }
}
