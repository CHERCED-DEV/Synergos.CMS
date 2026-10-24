using System.Text.RegularExpressions;
using Synergos.Core;

namespace Synergos.Bff.Educacion.Domain;

/// <summary>
/// El nombre del precio de un curso o de uno de sus planes en la capacidad de precios: lo usan la oferta, que lo
/// publica, y el paso <c>educacion.linea</c>, que lo cotiza (010-educacion-matricula, R7).
/// </summary>
/// <remarks>
/// <para><b>Vive en un solo sitio a propósito</b>: si la oferta y la línea lo armaran cada una a su modo, la matrícula
/// cotizaría un sujeto que nadie publicó y el curso quedaría «sin precio» sin que nada fallara a la vista.</para>
///
/// <para><b>La combinación curso+plan la valida precios</b>: sólo existe el precio de un plan que la oferta publicó
/// para ESE curso, así que el plan barato de otro curso no tiene precio y la cotización lo rechaza
/// (<c>pricing.price_not_found</c>) antes de autorizar nada.</para>
///
/// <para>Atribuido a la oferta genérica del núcleo (P12, NM-1): cuando llegue con variantes de precio, esto queda
/// listo para retirarse con la decisión del arquitecto.</para>
/// </remarks>
public static partial class SujetoDelPlan
{
    /// <summary>La clase del curso: la MISMA que declara la definición para la referencia del navegador (NR-1).</summary>
    public const string ClaseDelCurso = "educacion.curso";

    /// <summary>La clase del precio de un plan.</summary>
    public const string ClaseDelPlan = "educacion.plan";

    /// <summary>Lo que separa el curso del plan; no puede aparecer en ninguna de las dos claves.</summary>
    public const char Separador = '/';

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identificador();

    /// <summary>Si <paramref name="clave"/> tiene la forma de la clave de un curso o de un plan.</summary>
    public static bool EsClave(string? clave) => clave is not null && Identificador().IsMatch(clave);

    /// <summary>El sujeto del precio: el del curso sin plan, el del plan con él. Un plan vacío es «sin plan».</summary>
    /// <exception cref="ArgumentException">Si una de las dos claves no tiene la forma de una clave.</exception>
    public static Ref De(Ref curso, string? plan)
    {
        ArgumentNullException.ThrowIfNull(curso);
        if (!EsClave(curso.Id)) throw new ArgumentException($"«{curso.Id}» no es la clave de un curso.", nameof(curso));
        if (string.IsNullOrWhiteSpace(plan)) return Ref.Create(ClaseDelCurso, curso.Id);

        var codigo = plan.Trim();
        if (!EsClave(codigo)) throw new ArgumentException($"«{codigo}» no es el código de un plan.", nameof(plan));
        return Ref.Create(ClaseDelPlan, $"{curso.Id}{Separador}{codigo}");
    }
}
