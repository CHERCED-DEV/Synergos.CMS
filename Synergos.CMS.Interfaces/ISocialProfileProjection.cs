namespace Synergos.CMS.Interfaces;

/// <summary>
/// Proyección Member → perfil social (dominio Blogs — red social, OLA 3):
/// resuelve un autor por <c>handle</c> (@único) o por <c>actorId</c> con su
/// identidad rica (bio, banner, verificado) para el header del perfil. Es la
/// pieza del MOTOR que materializa la pantalla de perfil; el feed del autor sale
/// de <see cref="IContentStream"/> y los conteos de <see cref="ISocialGraphService"/>.
/// </summary>
/// <remarks>
/// El perfil social es una PROYECCIÓN del Member (no un store paralelo de
/// cuentas): <c>ActorId</c> se desacopla del MemberKey (como patientKey↔MemberKey
/// en healthcare) para permitir handles editables / anonimización. Seam
/// stub-first: el default <c>StubSocialProfileProjection</c> (Application,
/// catálogo sembrado en memoria) sirve la demo; el adapter real proyecta sobre
/// <c>IMemberRoster*</c> + un store de perfiles. ADR 0002 + ADR 0075.
/// </remarks>
public interface ISocialProfileProjection
{
    /// <summary>
    /// Resuelve un perfil por su <c>handle</c> (@único, case-insensitive, con o
    /// sin <c>@</c>). Devuelve <c>null</c> si no existe.
    /// </summary>
    Task<SocialProfile?> GetByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resuelve un perfil por su <c>actorId</c>. Devuelve <c>null</c> si no
    /// existe. Usado para enriquecer autores referenciados por id (e.g. en
    /// listas de seguidores).
    /// </summary>
    Task<SocialProfile?> GetByActorIdAsync(string actorId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Perfil social — identidad rica de un autor para el header del perfil.
/// </summary>
/// <param name="ActorId">Clave del actor (desacoplada del MemberKey).</param>
/// <param name="Handle">@handle único (sin el prefijo <c>@</c>).</param>
/// <param name="DisplayName">Nombre visible.</param>
/// <param name="Bio">Biografía corta, o <c>null</c>.</param>
/// <param name="AvatarUrl">URL del avatar, o <c>null</c>.</param>
/// <param name="BannerUrl">URL del banner/header, o <c>null</c>.</param>
/// <param name="Verified">Cuenta verificada.</param>
/// <param name="JoinedUtc">Fecha de alta de la cuenta.</param>
public sealed record SocialProfile(
    string ActorId,
    string Handle,
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    string? BannerUrl,
    bool Verified,
    DateTime JoinedUtc);
