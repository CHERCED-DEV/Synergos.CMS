using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Salud;

/// <summary>Lo que el médico manda al ordenar (I4 P11): el tipo, los exámenes, uno por renglón, y la indicación.</summary>
public sealed record OrdenEscrita(string? Tipo = null, IReadOnlyList<string>? Examenes = null, string? Indicacion = null);

/// <summary>El PDF del informe del laboratorio, en base64.</summary>
public sealed record InformeAdjunto(string? Nombre = null, string? Tipo = null, string? ContenidoBase64 = null);

/// <summary>Lo que el consultorio carga de un examen (I4 P12), como lo trae el informe del laboratorio.</summary>
public sealed record ResultadoCargado(
    string? ExamenId = null, string? Valor = null, string? Unidad = null, string? Rango = null, string? Marca = null,
    string? Nota = null, InformeAdjunto? Informe = null);

/// <summary>Un resultado como se lee: sin las claves de quién lo cargó ni de quién lo liberó.</summary>
public sealed record ResultadoParaLeer(
    Guid Id, string Valor, string? Unidad, string? Rango, string Marca, string? Nota, bool TieneInforme, bool Liberado,
    DateTime CargadoUtc, DateTime? LiberadoUtc);

public sealed record ExamenParaLeer(string Id, string Nombre, ResultadoParaLeer? Resultado);

public sealed record OrdenParaLeer(
    Guid Id, string Tipo, string? Indicacion, DateTime CreadaUtc, string Estado, IReadOnlyList<ExamenParaLeer> Examenes);

/// <summary>
/// Las reglas puras de la orden médica y sus resultados (003-salud-cita, I4): qué se puede guardar, qué ve cada quien y
/// cuándo se cumple la orden. Quién puede cada cosa lo decide la guardia; dónde se guarda, el almacén PHI.
/// </summary>
public static class OrdenesClinicas
{
    public static IReadOnlyList<string> Tipos { get; } = ["laboratorio", "imagenes", "remision"];
    public static IReadOnlyList<string> Marcas { get; } = ["normal", "alto", "bajo", "critico"];

    public const int ExamenesMaximos = 20;
    public const int LargoDelExamen = 200;
    public const int LargoDeLaIndicacion = 1000;
    public const int LargoDelValor = 100;

    /// <summary>Lo más grande que puede ser el PDF del informe.</summary>
    public const int BytesDelInforme = 5 * 1024 * 1024;

    public const string Abierta = "abierta";
    public const string Cumplida = "cumplida";

    public static string? ErrorDeLaOrden(OrdenEscrita orden)
    {
        ArgumentNullException.ThrowIfNull(orden);
        if (!Tipos.Contains(orden.Tipo ?? string.Empty))
        {
            return "tipo: laboratorio, imagenes o remision.";
        }
        var examenes = Limpios(orden.Examenes);
        if (examenes.Count is 0 or > ExamenesMaximos)
        {
            return $"examenes: de 1 a {ExamenesMaximos}.";
        }
        if (examenes.Any(e => e.Length > LargoDelExamen))
        {
            return $"examenes: más de {LargoDelExamen} caracteres.";
        }
        return (orden.Indicacion ?? string.Empty).Trim().Length > LargoDeLaIndicacion
            ? $"indicacion: más de {LargoDeLaIndicacion} caracteres."
            : null;
    }

    public static OrdenMedica Ordenar(OrdenEscrita orden, Guid patientKey, Guid medico, DateTime ahoraUtc)
    {
        ArgumentNullException.ThrowIfNull(orden);
        var examenes = Limpios(orden.Examenes).Select((nombre, i) => new ExamenOrdenado($"e{i + 1}", nombre)).ToList();
        var indicacion = (orden.Indicacion ?? string.Empty).Trim();
        return new OrdenMedica(Guid.NewGuid(), patientKey, orden.Tipo!, examenes, indicacion.Length == 0 ? null : indicacion,
            medico, ahoraUtc, Abierta);
    }

    /// <summary>Lo que impide cargar el resultado, o <c>null</c>. El informe, si viene, tiene que ser un PDF que quepa.</summary>
    public static string? ErrorDelResultado(OrdenMedica orden, ResultadoCargado resultado)
    {
        ArgumentNullException.ThrowIfNull(orden);
        ArgumentNullException.ThrowIfNull(resultado);
        if (orden.Examenes.All(e => e.Id != resultado.ExamenId))
        {
            return "examenId: no es un examen de esta orden.";
        }
        var valor = (resultado.Valor ?? string.Empty).Trim();
        if (valor.Length is 0 or > LargoDelValor)
        {
            return $"valor: falta o tiene más de {LargoDelValor} caracteres.";
        }
        if (!Marcas.Contains(resultado.Marca ?? string.Empty))
        {
            return "marca: normal, alto, bajo o critico.";
        }
        if (resultado.Informe is { } informe && BytesDe(informe) is null)
        {
            return $"informe: un PDF de hasta {BytesDelInforme / (1024 * 1024)} MB.";
        }
        return null;
    }

    /// <summary>Los bytes del informe, o <c>null</c> si no es un PDF en base64 que quepa.</summary>
    public static byte[]? BytesDe(InformeAdjunto informe)
    {
        ArgumentNullException.ThrowIfNull(informe);
        if (!string.Equals(informe.Tipo, "application/pdf", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(informe.ContenidoBase64) || informe.ContenidoBase64.Length > BytesDelInforme / 3 * 4 + 4)
        {
            return null;
        }
        try
        {
            var bytes = Convert.FromBase64String(informe.ContenidoBase64);
            // Un PDF empieza por «%PDF»: el tipo que declara el navegador no basta.
            return bytes.Length is > 4 and <= BytesDelInforme && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F'
                ? bytes
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Cumplida cuando cada examen tiene su resultado liberado.</summary>
    public static OrdenMedica ConSuEstado(OrdenMedica orden, IEnumerable<ResultadoDeExamen> resultados)
    {
        ArgumentNullException.ThrowIfNull(orden);
        var liberados = resultados.Where(r => r.OrdenId == orden.Id && r.Liberado).Select(r => r.ExamenId).ToHashSet();
        var estado = orden.Examenes.All(e => liberados.Contains(e.Id)) ? Cumplida : Abierta;
        return estado == orden.Estado ? orden : orden with { Estado = estado };
    }

    /// <summary>
    /// La orden como se lee, con el último resultado de cada examen. Para el paciente, <paramref name="verSinLiberar"/>
    /// es <c>false</c>: un resultado que el médico no ha revisado no se le muestra.
    /// </summary>
    public static OrdenParaLeer ParaLeer(OrdenMedica orden, IEnumerable<ResultadoDeExamen> resultados, bool verSinLiberar)
    {
        ArgumentNullException.ThrowIfNull(orden);
        var deLaOrden = resultados.Where(r => r.OrdenId == orden.Id && (verSinLiberar || r.Liberado)).ToList();
        return new OrdenParaLeer(orden.Id, orden.Tipo, orden.Indicacion, orden.CreadaUtc, orden.Estado,
            orden.Examenes.Select(e => new ExamenParaLeer(e.Id, e.Nombre,
                deLaOrden.Where(r => r.ExamenId == e.Id).OrderByDescending(r => r.CargadoUtc).Select(Leer).FirstOrDefault())).ToList());
    }

    private static ResultadoParaLeer Leer(ResultadoDeExamen r) => new(
        r.Id, r.Valor, r.Unidad, r.Rango, r.Marca, r.Nota, r.ArchivoId is not null, r.Liberado, r.CargadoUtc, r.LiberadoUtc);

    private static List<string> Limpios(IReadOnlyList<string>? textos)
        => (textos ?? []).Select(t => (t ?? string.Empty).Trim()).Where(t => t.Length > 0).ToList();
}
