using Microsoft.AspNetCore.Builder;
using Synergos.Core;

namespace Synergos.Shared;

/// <summary>
/// Metadato de un endpoint: lee la cabecera <see cref="IdempotencyHeader.Name"/>.
/// </summary>
/// <param name="Siempre">
/// Si la exige en toda petición, o sólo en el caso que decide el cuerpo (el ajuste RELATIVO de
/// existencias la exige y el absoluto no: repetir «hay 47» no cambia nada).
/// </param>
/// <param name="MaxLength">
/// El largo más grande que el endpoint ACEPTA: <see cref="IdempotencyKey.MaxLength"/>, salvo donde
/// la llave no se usa tal cual sino como semilla de otras —un orquestador abre la saga con ella y
/// le cuelga el sufijo de cada paso—, que acepta menos. Lo leen los dos lados del mismo número:
/// <see cref="IdempotencyHeader.TryRead"/> rechaza la más larga, y el contrato publicado lo
/// escribe como <c>maxLength</c>.
/// </param>
/// <remarks>
/// <para><b>Existe porque la cabecera se lee a mano.</b> <see cref="IdempotencyHeader.TryRead"/>
/// recibe el <c>HttpRequest</c>, así que nada en la firma del endpoint dice que la usa y el
/// documento OpenAPI no podía publicarla (ADR 0140, F2). Con el metadato, el contrato le dice al
/// consumidor dónde la tiene que mandar, y el gate de compatibilidad puede ver al que la omite.</para>
///
/// <para><b>Declarar no es exigir</b>, y por eso hay una sonda contra el host real que manda cada
/// operación SIN la cabecera: lo declarado tiene que ser lo que el endpoint exige, en los dos
/// sentidos. Funciona porque la llave se resuelve antes que cualquier regla (CLAUDE.md §0.B.16).</para>
///
/// <para><b>Y el largo publicado es el aceptado</b>, porque los dos salen de aquí. Antes el contrato
/// publicaba 128 para todos y <c>Bff.Eventos</c> aceptaba 128, pero con más de 90 la compra moría
/// en un 500 al derivar la llave de apartar: <c>{saga}|hold:{item}</c> pasaba de 128 (medido, ADR
/// 0140 F2).</para>
///
/// <para>Vive aquí, junto a <see cref="IdempotencyHeader"/>, por la misma razón que él: lo usa todo
/// host que lee la cabecera, y una copia por capacidad derivaría.</para>
/// </remarks>
public sealed record LlaveDeIdempotenciaRequerida(bool Siempre, int MaxLength = IdempotencyKey.MaxLength);

/// <summary>Declara en un endpoint que lee la cabecera de idempotencia.</summary>
public static class LlaveDeIdempotenciaExtensions
{
    /// <summary>Añade <see cref="LlaveDeIdempotenciaRequerida"/> al endpoint.</summary>
    /// <param name="builder">El endpoint.</param>
    /// <param name="siempre">Ver <see cref="LlaveDeIdempotenciaRequerida.Siempre"/>.</param>
    /// <param name="maxLength">Ver <see cref="LlaveDeIdempotenciaRequerida.MaxLength"/>.</param>
    public static RouteHandlerBuilder ConLlaveDeIdempotencia(
        this RouteHandlerBuilder builder, bool siempre = true, int maxLength = IdempotencyKey.MaxLength)
    {
        if (maxLength is < 1 or > IdempotencyKey.MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength,
                $"Una llave acepta entre 1 y {IdempotencyKey.MaxLength} caracteres: más larga no cabe en {nameof(IdempotencyKey)}.");
        }

        return builder.WithMetadata(new LlaveDeIdempotenciaRequerida(siempre, maxLength));
    }
}
