using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ISocialProfileProjection"/> — proyección Member → perfil
/// social (dominio Blogs) STUB sobre el catálogo de perfiles sembrado en
/// <see cref="SocialDemoSeed"/>. Resuelve un autor por <c>handle</c> o
/// <c>actorId</c> para el header del perfil. Determinista y consistente con el
/// resto de los stubs sociales (mismos actores).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero Umbraco/AspNetCore (ADR
/// 0002). El adapter real proyecta sobre <c>IMemberRoster*</c> + un store de
/// perfiles, implementando la misma seam sin tocar el módulo Angular. ADR 0075.
/// </remarks>
public sealed class StubSocialProfileProjection : ISocialProfileProjection
{
    public Task<SocialProfile?> GetByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return Task.FromResult<SocialProfile?>(null);
        }

        var needle = handle.Trim().TrimStart('@');
        var profile = SocialDemoSeed.Profiles.FirstOrDefault(p =>
            string.Equals(p.Handle, needle, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(profile);
    }

    public Task<SocialProfile?> GetByActorIdAsync(string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return Task.FromResult<SocialProfile?>(null);
        }

        var profile = SocialDemoSeed.Profiles.FirstOrDefault(p =>
            string.Equals(p.ActorId, actorId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(profile);
    }
}
