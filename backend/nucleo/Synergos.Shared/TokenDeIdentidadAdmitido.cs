using Microsoft.AspNetCore.Builder;

namespace Synergos.Shared;

/// <summary>
/// Metadato de un endpoint: admite el token de identidad en la cabecera
/// <see cref="IdentityTokens.HeaderName"/>.
/// </summary>
/// <remarks>
/// <para><b>Existe por la misma razón que <see cref="LlaveDeIdempotenciaRequerida"/>.</b> La
/// cabecera se lee a mano del <c>HttpRequest</c>, así que nada en la firma del endpoint dice que la
/// usa y el documento OpenAPI la callaba, aunque cambia la respuesta: un token presente que la
/// capacidad no puede comprobar es un 400 <c>identity.token_not_verifiable</c>, y uno válido sube la
/// afirmación a <c>IdentityToken</c> (ADR 0140, F2). Con el metadato el contrato la publica, y una
/// sonda contra el host comprueba que la declarada sea la que el endpoint lee.</para>
///
/// <para><b>Siempre opcional</b>: sin token la capacidad resuelve con lo que el llamador declara
/// (<see cref="IdentityAssertions.Resolve"/>), o lo deja en «no consta». Por eso no lleva un
/// <c>Siempre</c> como la llave.</para>
/// </remarks>
public sealed record TokenDeIdentidadAdmitido;

/// <summary>Declara en un endpoint que lee el token de identidad.</summary>
public static class TokenDeIdentidadExtensions
{
    /// <summary>Añade <see cref="TokenDeIdentidadAdmitido"/> al endpoint.</summary>
    /// <param name="builder">El endpoint.</param>
    public static RouteHandlerBuilder ConTokenDeIdentidad(this RouteHandlerBuilder builder)
        => builder.WithMetadata(new TokenDeIdentidadAdmitido());
}
