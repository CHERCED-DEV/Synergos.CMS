using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary><see cref="IBuscadorDeMiembros"/> sobre los miembros de Umbraco (I2 T056).</summary>
public sealed class UmbracoBuscadorDeMiembros : IBuscadorDeMiembros
{
    private readonly IMemberService _miembros;

    public UmbracoBuscadorDeMiembros(IMemberService miembros) => _miembros = miembros;

    public Task<Guid?> ClaveDelCorreoAsync(string correo, CancellationToken cancellationToken)
    {
        var limpio = correo?.Trim();
        if (string.IsNullOrEmpty(limpio)) return Task.FromResult<Guid?>(null);
        return Task.FromResult(_miembros.GetByEmail(limpio)?.Key);
    }

    public Task<ContactoDelMiembro?> ContactoAsync(Guid clave, CancellationToken cancellationToken)
    {
        var miembro = clave == Guid.Empty ? null : _miembros.GetByKey(clave);
        return Task.FromResult(miembro is null || string.IsNullOrWhiteSpace(miembro.Email)
            ? null
            : new ContactoDelMiembro(miembro.Email.Trim(), miembro.Name ?? string.Empty));
    }
}
