using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Salud;

/// <summary>Lo que el médico manda al escribir una nota. Sin <c>Id</c>, es una nota nueva.</summary>
public sealed record NotaEscrita(
    Guid? Id = null, string? ReservaId = null, string? Subjetivo = null, string? Objetivo = null, string? Analisis = null, string? Plan = null);

/// <summary>
/// Las reglas puras de la nota de la consulta (003-salud-cita, I3 P6): qué se puede guardar, y su huella, que es lo que se
/// firma. Sin almacén ni acceso: quién escribe lo decide la guardia y el sello lo pone <c>Api.Signing</c>.
/// </summary>
public static class NotaClinica
{
    /// <summary>Lo más largo que puede ser cada parte de la nota.</summary>
    public const int LargoMaximo = 4000;

    /// <summary>Lo que impide guardar la nota, o <c>null</c>.</summary>
    public static string? Error(NotaEscrita nota)
    {
        ArgumentNullException.ThrowIfNull(nota);
        var partes = new[] { ("subjetivo", nota.Subjetivo), ("objetivo", nota.Objetivo), ("analisis", nota.Analisis), ("plan", nota.Plan) };
        foreach (var (campo, texto) in partes)
        {
            if ((texto ?? string.Empty).Trim().Length > LargoMaximo)
            {
                return $"{campo}: más de {LargoMaximo} caracteres.";
            }
        }
        return partes.All(p => string.IsNullOrWhiteSpace(p.Item2)) ? "la nota está vacía." : null;
    }

    /// <summary>La nota nueva, o la misma con el texto cambiado. Una firmada no se toca: devuelve <c>null</c>.</summary>
    public static NotaDeLaConsulta? Escribir(NotaDeLaConsulta? actual, NotaEscrita escrita, Guid patientKey, Guid autor, DateTime ahoraUtc)
    {
        ArgumentNullException.ThrowIfNull(escrita);
        if (actual?.Firma is not null)
        {
            return null;
        }
        var texto = (string? t) => (t ?? string.Empty).Trim();
        return actual is null
            ? new NotaDeLaConsulta(
                Guid.NewGuid(), patientKey, Limpio(escrita.ReservaId), texto(escrita.Subjetivo), texto(escrita.Objetivo),
                texto(escrita.Analisis), texto(escrita.Plan), autor, ahoraUtc, ahoraUtc)
            : actual with
            {
                Subjetivo = texto(escrita.Subjetivo),
                Objetivo = texto(escrita.Objetivo),
                Analisis = texto(escrita.Analisis),
                Plan = texto(escrita.Plan),
                ActualizadaUtc = ahoraUtc,
            };
    }

    /// <summary>
    /// La huella de la nota: SHA-256 de su forma canónica, en hexadecimal. Entra todo lo que la nota afirma —quién, de
    /// quién, de qué cita y las cuatro partes—; un arreglo JSON y no un texto pegado, para que ninguna parte pueda
    /// correrse a la siguiente sin cambiar la huella.
    /// </summary>
    public static string Huella(NotaDeLaConsulta nota)
    {
        ArgumentNullException.ThrowIfNull(nota);
        var canonica = JsonSerializer.Serialize(new[]
        {
            "salud.nota.v1", nota.Id.ToString("N"), nota.PatientKey.ToString("N"), nota.ReservaId ?? string.Empty,
            nota.AutorKey.ToString("N"), nota.Subjetivo, nota.Objetivo, nota.Analisis, nota.Plan,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonica))).ToLowerInvariant();
    }

    /// <summary>La nota como se lee: sin las claves de quién la escribió ni de quién la firmó.</summary>
    public static NotaParaLeer ParaLeer(NotaDeLaConsulta nota)
    {
        ArgumentNullException.ThrowIfNull(nota);
        return new NotaParaLeer(
            nota.Id, nota.ReservaId, nota.Subjetivo, nota.Objetivo, nota.Analisis, nota.Plan, nota.CreadaUtc, nota.ActualizadaUtc,
            nota.Firma is not null, nota.Firma?.FirmadaUtc, nota.Firma?.LlaveId);
    }

    private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}

/// <summary>Una nota como la leen el médico y el paciente.</summary>
public sealed record NotaParaLeer(
    Guid Id, string? ReservaId, string Subjetivo, string Objetivo, string Analisis, string Plan, DateTime CreadaUtc,
    DateTime ActualizadaUtc, bool Firmada, DateTime? FirmadaUtc, string? LlaveId);

/// <summary>Un paso de la llegada que marca el personal (I3 P10).</summary>
public sealed record PasoDeLlegada(string? Paso = null);

/// <summary>Los pasos de la llegada a la consulta, los del proceso <c>salud.llegada.v1</c>.</summary>
public static class LlegadaALaConsulta
{
    public static IReadOnlyList<string> Pasos { get; } = ["llegar", "pasar", "atender", "no-vino"];
}
