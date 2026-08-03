namespace Synergos.Core;

/// <summary>
/// Una referencia <b>opaca</b> a algo que vive fuera de quien la guarda.
/// </summary>
/// <param name="Kind">
/// De qué tipo es, en el vocabulario de quien la creó: <c>"salud.profesional"</c>,
/// <c>"travel.habitacion"</c>, <c>"eventos.butaca"</c>. <b>Una capacidad no lo interpreta.</b>
/// </param>
/// <param name="Id">El identificador dentro de ese vocabulario.</param>
/// <remarks>
/// <para><b>Este es el tipo que sostiene toda la agnosticidad.</b> <c>Api.Booking</c> reserva
/// <c>Ref("salud.profesional", "dr-123")</c> de 10:00 a 10:30 y no sabe qué es un profesional.
/// No le importa — y <b>si algún día le importara, dejaría de servirle a Travel</b>.</para>
///
/// <para><b>La regla, y no admite excepciones:</b> una capacidad <i>guarda y devuelve</i> un
/// <c>Ref</c>; <b>nunca ramifica sobre <see cref="Kind"/></b>. Ese
/// <c>if (kind == "salud.profesional")</c> es exactamente cómo muere una capacidad agnóstica:
/// entra un martes como atajo y para cuando se nota, el octavo dominio ya no puede reutilizarla
/// y se copió el código. Tiene gate propio en <c>BackendSegregationTests</c>.</para>
///
/// <para>El corolario práctico: <b>el dato específico del dominio no viaja a la capacidad</b>.
/// La especialidad del médico, la tarifa del hotel y la fila de la butaca viven en su
/// orquestador. Lo que viaja es el <c>Ref</c> y lo que la capacidad sí entiende — una ventana,
/// un cupo, un monto.</para>
/// </remarks>
public sealed record Ref(string Kind, string Id)
{
    /// <summary>Separador de la forma textual <c>kind:id</c>.</summary>
    private const char Separator = ':';

    /// <summary>Construye validando. Ninguna de las dos partes puede ir vacía.</summary>
    /// <exception cref="ArgumentException">Si falta el tipo o el identificador.</exception>
    public static Ref Create(string kind, string id)
    {
        // Un Ref a medias es peor que ninguno: se guarda sin ruido y explota al resolverlo,
        // lejos de donde se creó y sin rastro de quién lo puso.
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Un Ref necesita tipo.", nameof(kind));
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Un Ref necesita identificador.", nameof(id));
        return new Ref(kind.Trim(), id.Trim());
    }

    /// <summary>
    /// Forma textual <c>kind:id</c>, para claves de almacén y logs.
    /// </summary>
    /// <remarks>
    /// El <see cref="Id"/> puede contener <c>:</c> —un URN, una ruta— así que
    /// <see cref="Parse"/> corta en el PRIMER separador y no en el último. Partir por el
    /// último devolvería un tipo distinto del guardado, silenciosamente.
    /// </remarks>
    public override string ToString() => $"{Kind}{Separator}{Id}";

    /// <summary>Lee la forma textual de <see cref="ToString"/>.</summary>
    public static bool TryParse(string? text, out Ref? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var cut = text.IndexOf(Separator);
        if (cut <= 0 || cut == text.Length - 1) return false;

        value = new Ref(text[..cut], text[(cut + 1)..]);
        return true;
    }

    /// <inheritdoc cref="TryParse"/>
    /// <exception cref="FormatException">Si el texto no tiene la forma <c>kind:id</c>.</exception>
    public static Ref Parse(string text)
        => TryParse(text, out var value) && value is not null
            ? value
            : throw new FormatException($"'{text}' no tiene la forma kind:id.");
}
