using System.Globalization;
using System.Text.Json;
using Umbraco.Cms.Core.Models;

namespace Synergos.CMS.Web.Services.Reservas;

/// <summary>
/// La composición «reservable» de una ficha (<c>compReservable</c>, 011-booking-reserva), leída igual para lo
/// que pinta el elemento y lo que se publica como oferta.
/// </summary>
/// <remarks>
/// <para><b>Un solo lector de días y horas</b>: el resolver lee la ficha publicada y el publicador la que llega
/// en la notificación; si cada uno tradujera «lunes» por su cuenta, el elemento ofrecería días que la agenda no
/// abre.</para>
/// </remarks>
public static class FichaReservable
{
    /// <summary>La propiedad que marca la ficha como reservable: sólo la tiene quien lleva la composición.</summary>
    public const string Marca = "reservable";

    private static readonly IReadOnlyDictionary<string, DayOfWeek> Dias = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
    {
        ["lunes"] = DayOfWeek.Monday, ["martes"] = DayOfWeek.Tuesday, ["miércoles"] = DayOfWeek.Wednesday,
        ["miercoles"] = DayOfWeek.Wednesday, ["jueves"] = DayOfWeek.Thursday, ["viernes"] = DayOfWeek.Friday,
        ["sábado"] = DayOfWeek.Saturday, ["sabado"] = DayOfWeek.Saturday, ["domingo"] = DayOfWeek.Sunday,
    };

    /// <summary>Los días que marcó el editor, sin repetir y en el orden de la semana; lo que no es un día se ignora.</summary>
    public static IReadOnlyList<DayOfWeek> DiasDe(IEnumerable<string>? valores)
        => (valores ?? [])
            .Select(d => Dias.TryGetValue(d.Trim(), out var dia) ? dia : (DayOfWeek?)null)
            .OfType<DayOfWeek>()
            .Distinct()
            .OrderBy(d => ((int)d + 6) % 7)
            .ToList();

    /// <summary>Una hora <c>HH:mm</c>, o <c>null</c>.</summary>
    public static TimeOnly? Hora(string? texto)
        => TimeOnly.TryParseExact(texto?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora) ? hora : null;

    /// <summary>Si el contenido lleva la composición (tiene la marca).</summary>
    public static bool LaLleva(IContentBase contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        return contenido.HasProperty(Marca);
    }

    /// <summary>
    /// Lo que la ficha guardada declara para reservarse, o lo que le falta. <c>null</c> si no está marcada
    /// reservable.
    /// </summary>
    /// <param name="ficha">La ficha, como llega en la notificación.</param>
    public static LecturaDeLaFicha? Leer(IContentBase ficha)
    {
        ArgumentNullException.ThrowIfNull(ficha);
        if (!LaLleva(ficha) || !Verdadero(ficha.GetValue(Marca))) return null;

        var faltan = new List<string>();
        var duracion = Entero(ficha.GetValue("duracionMinutos"));
        if (duracion is not > 0) faltan.Add("duración");
        var dias = DiasDe(Lista(ficha.GetValue("diasDeAtencion")));
        if (dias.Count == 0) faltan.Add("días de atención");
        var abre = Hora(ficha.GetValue("abre") as string);
        var cierra = Hora(ficha.GetValue("cierra") as string);
        if (abre is null || cierra is null || abre >= cierra) faltan.Add("horario (abre antes de cerrar, HH:mm)");
        var cupos = Entero(ficha.GetValue("cupos"));
        if (cupos is not >= 1) faltan.Add("cupos (mínimo 1)");
        if (!PrecioAutorado.EsInequivoco(ficha.GetValue("productPriceBase") as string, out var precio)) faltan.Add("precio (sólo dígitos)");

        return new LecturaDeLaFicha(
            ficha.Key.ToString("D"),
            ficha.Name ?? string.Empty,
            duracion ?? 0,
            dias,
            abre ?? default,
            cierra ?? default,
            cupos ?? 0,
            Math.Max(0, Entero(ficha.GetValue("plazoCancelacionHoras")) ?? 0),
            precio,
            faltan);
    }

    private static bool Verdadero(object? valor) => valor switch
    {
        bool b => b,
        int i => i == 1,
        string s => s.Trim() is "1" or "true" or "True",
        _ => false,
    };

    private static int? Entero(object? valor) => valor switch
    {
        int i => i,
        long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
        decimal d when d == decimal.Truncate(d) && d is >= int.MinValue and <= int.MaxValue => (int)d,
        string s when int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        _ => null,
    };

    /// <summary>Lo que guarda una lista de casillas: un arreglo JSON, o una lista separada por comas en datos viejos.</summary>
    private static IEnumerable<string> Lista(object? valor)
    {
        if (valor is IEnumerable<string> lista) return lista;
        if (valor is not string texto || string.IsNullOrWhiteSpace(texto)) return [];
        texto = texto.Trim();
        if (texto.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(texto) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
        return texto.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

/// <summary>Una ficha reservable tal como la guardó el editor, con lo que le falta para ofrecerse.</summary>
/// <param name="Servicio">La clave de la ficha (su <c>Key</c>): la identidad del servicio en la agenda.</param>
/// <param name="Faltan">Lo que falta o no se entiende. Vacío: se puede ofrecer.</param>
public sealed record LecturaDeLaFicha(
    string Servicio,
    string Nombre,
    int DuracionMinutos,
    IReadOnlyList<DayOfWeek> Dias,
    TimeOnly Abre,
    TimeOnly Cierra,
    int Cupos,
    int PlazoCancelacionHoras,
    decimal Precio,
    IReadOnlyList<string> Faltan)
{
    /// <summary>Si se puede ofrecer: no le falta nada.</summary>
    public bool Completa => Faltan.Count == 0;
}
