using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>
/// Los mensajes directos de Blogs en la bandeja genérica (012-blogs-social, I4): los hilos de contexto <c>dm</c>, que
/// abre <c>POST api/blogs/message</c>. El asunto es el nombre público de la contraparte, y cada participante sale por
/// su alias, el mismo con el que se enlaza su perfil.
/// </summary>
/// <remarks>
/// La mensajería guarda a un miembro por su correo, que no se publica (I0): un miembro sale por su cara pública, y la
/// semilla de la demo, que no es miembro, por su perfil, como en la interina (<c>GET api/blogs/messages</c>).
/// </remarks>
public sealed class ContextoDeMensajesDeBlogs : ContextoDeMensajesPorPrefijo
{
    /// <summary>El contexto de un mensaje directo.</summary>
    public const string Prefijo = "dm";

    /// <summary>El asunto de un hilo cuya contraparte no se conoce.</summary>
    public const string AsuntoSinContraparte = "Mensaje directo";

    private readonly IIdentidadPublica _identidad;
    private readonly ISocialProfileProjection _perfiles;

    /// <summary>Sobre la cara pública de los miembros y los perfiles de la red.</summary>
    public ContextoDeMensajesDeBlogs(IIdentidadPublica identidad, ISocialProfileProjection perfiles)
        : base(CatalogoDeBlogs.Vertical, Prefijo)
    {
        _identidad = identidad ?? throw new ArgumentNullException(nameof(identidad));
        _perfiles = perfiles ?? throw new ArgumentNullException(nameof(perfiles));
    }

    /// <inheritdoc />
    public override Task<string> AsuntoAsync(string contexto, CancellationToken cancellationToken = default)
        => Task.FromResult(AsuntoSinContraparte);

    /// <inheritdoc />
    public override async Task<string> AsuntoAsync(
        string contexto,
        string contraparte,
        CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(contraparte)
            ? AsuntoSinContraparte
            : (await CaraAsync(contraparte, cancellationToken).ConfigureAwait(false)).Nombre;

    /// <inheritdoc />
    public override async Task<string> ParticipanteAsync(string participante, CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(participante)
            ? string.Empty
            : (await CaraAsync(participante, cancellationToken).ConfigureAwait(false)).Alias;

    private async Task<(string Alias, string Nombre)> CaraAsync(string participante, CancellationToken cancellationToken)
    {
        if (CaraPublica.EsCorreo(participante))
        {
            var cara = _identidad.De(participante);
            return (cara.Alias, cara.Nombre);
        }

        var perfil = await _perfiles.GetByActorIdAsync(participante, cancellationToken).ConfigureAwait(false);
        return perfil is null ? (participante, participante) : (perfil.Handle, perfil.DisplayName);
    }
}
