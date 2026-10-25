using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Educacion.Clients;

/// <summary>Lo que la OFERTA de los cursos necesita de precios: fijar el precio de un curso o de un plan.</summary>
/// <remarks>
/// <para><b>Sólo la oferta</b>: la matrícula cotiza y cobra con los adaptadores comunes del núcleo (<c>PuertosHttp</c>)
/// y no pasa por aquí. Es código del vertical que la oferta genérica (P12, NM-1) dejará listo para retirar.</para>
/// </remarks>
public sealed class CapacidadesDeLaOferta(IHttpClientFactory clientes) : CapabilityClients(clientes)
{
    public const string Pricing = "pricing";

    /// <summary>Fija el precio de <paramref name="sujeto"/>; con <paramref name="hasta"/> deja de valer desde ese instante.</summary>
    public Task<Result<PrecioDto>> FijarPrecioAsync(Ref sujeto, Money precio, DateTimeOffset? hasta, IdempotencyKey llave, CancellationToken ct)
        => Post<PrecioDto>(Pricing, "v1/prices", new
        {
            subjectKind = sujeto.Kind,
            subjectId = sujeto.Id,
            amount = new { amount = precio.Amount, currency = precio.Currency },
            taxRateBasisPoints = 0,
            validTo = hasta,
        }, llave, ct);
}

/// <summary>Un precio publicado, en lo que la oferta lee.</summary>
public sealed record PrecioDto(string Id, DateTimeOffset? ValidTo);
