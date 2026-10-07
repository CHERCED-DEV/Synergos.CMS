using Microsoft.AspNetCore.Builder;

namespace Synergos.Shared;

/// <summary>
/// Metadato de un endpoint: lee la cabecera <see cref="IdempotencyHeader.Name"/>.
/// </summary>
/// <param name="Siempre">
/// Si la exige en toda petición, o sólo en el caso que decide el cuerpo (el ajuste RELATIVO de
/// existencias la exige y el absoluto no: repetir «hay 47» no cambia nada).
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
/// <para>Vive aquí, junto a <see cref="IdempotencyHeader"/>, por la misma razón que él: lo usa todo
/// host que lee la cabecera, y una copia por capacidad derivaría.</para>
/// </remarks>
public sealed record LlaveDeIdempotenciaRequerida(bool Siempre);

/// <summary>Declara en un endpoint que lee la cabecera de idempotencia.</summary>
public static class LlaveDeIdempotenciaExtensions
{
    /// <summary>Añade <see cref="LlaveDeIdempotenciaRequerida"/> al endpoint.</summary>
    /// <param name="builder">El endpoint.</param>
    /// <param name="siempre">Ver <see cref="LlaveDeIdempotenciaRequerida.Siempre"/>.</param>
    public static RouteHandlerBuilder ConLlaveDeIdempotencia(this RouteHandlerBuilder builder, bool siempre = true)
        => builder.WithMetadata(new LlaveDeIdempotenciaRequerida(siempre));
}
