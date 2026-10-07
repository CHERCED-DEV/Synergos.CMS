using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Core;

/// <summary>
/// El largo de la llave con que se ABRE una saga: la <c>Idempotency-Key</c> que manda el llamador y
/// que el orquestador usa de identificador.
/// </summary>
/// <remarks>
/// <para><b>Por qué no son 128.</b> La llave no se usa tal cual: es la semilla de las de cada paso.
/// <see cref="SagaEngine{TSaga}.Abrir"/> le puede añadir <c>#n</c> tras una compra deshecha, y cada
/// paso deriva <c>{saga}|{paso}</c> con <see cref="SagaExtensions.KeyFor"/> o con la entrada del
/// intérprete. Con la llave de 128 que publicaba el contrato, <c>{saga}|hold:{item}</c> pasaba de 128
/// a partir de 91 caracteres, <see cref="IdempotencyKey.Of"/> lanzaba, y la compra moría en un 500
/// que nadie publicaba (medido, ADR 0140 F2).</para>
///
/// <para><b>La cuenta:</b> 128 − <c>#100</c> (4) − <c>|</c> (1) − <see cref="PasoMaximo"/> (40) = 83.
/// Se rechaza en el borde, con el 400 de <see cref="IdempotencyHeader.TryRead"/>, y no se acorta
/// la derivación con un hash: las llaves de paso ya viajaron a las capacidades en sagas vivas, y
/// cambiarlas haría que un reintento tras el despliegue no reconociera lo que ya hizo.</para>
///
/// <para>Es una regla de la máquina y no de un dominio: los cuatro orquestadores abren su saga con
/// la llave del llamador, y los cuatro la declaran con <see cref="ConLlaveDeSaga"/>.</para>
/// </remarks>
public static class LlaveDeSaga
{
    /// <summary>
    /// Cuántos intentos deshechos admite <see cref="SagaEngine{TSaga}.Abrir"/> sobre la misma llave:
    /// el último se llama <c>{llave}#100</c>.
    /// </summary>
    public const int IntentosTrasDeshacer = 100;

    /// <summary>
    /// El paso más largo que una saga cuelga de su identificador: una palabra de hasta ocho
    /// caracteres con sus dos puntos (<c>restock:</c>, <c>refund:</c>, <c>hold:</c>) y un
    /// identificador de 32, el <c>Guid</c> "n" con que nombran sus recursos las capacidades y la
    /// saga sus compensaciones.
    /// </summary>
    public const int PasoMaximo = 8 + 32;

    /// <summary>El largo más grande de la llave que abre una saga.</summary>
    public static int MaxLength { get; } =
        IdempotencyKey.MaxLength
        - ("#" + IntentosTrasDeshacer.ToString(CultureInfo.InvariantCulture)).Length
        - 1
        - PasoMaximo;

    /// <summary>
    /// Declara que el endpoint abre una saga con la <c>Idempotency-Key</c>: la exige, y acepta
    /// hasta <see cref="MaxLength"/>.
    /// </summary>
    public static RouteHandlerBuilder ConLlaveDeSaga(this RouteHandlerBuilder builder)
        => builder.ConLlaveDeIdempotencia(siempre: true, maxLength: MaxLength);
}
