using System.Collections.Concurrent;
using System.Text.Json;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="IIdentidadPublica"/> sobre los miembros de Umbraco: el nombre de la ficha del miembro, la llave por
/// <see cref="SeudonimoDePersona"/> y la regla de <see cref="CaraPublica"/>. Para volver de una llave o un alias al
/// correo, lo anotado en <see cref="IJsonEntityStore"/> (ADR 0105).
/// </summary>
/// <remarks>
/// <para><b>Dos documentos por persona</b>: uno por su llave, con su correo y su alias, y otro por su alias, con su
/// llave. El segundo existe porque un perfil se abre por el alias que sale en la tarjeta.</para>
///
/// <para><b>Un alias ya anotado no se pisa.</b> Dos personas con el mismo nombre y los mismos seis primeros caracteres
/// de la llave (una vez en dieciséis millones) compartirían alias: la primera lo conserva, y a la segunda se la sigue
/// encontrando por su llave. Pisarlo le daría a la segunda el perfil de la primera.</para>
///
/// <para>Lo que ya se anotó en este proceso no se vuelve a leer: anotar ocurre en cada acción de la persona.</para>
/// </remarks>
public sealed class IdentidadPublicaDeMiembros : IIdentidadPublica
{
    /// <summary>Familia de los documentos por llave pública (→ <c>App_Data/syn-identidad-publica/</c>).</summary>
    internal const string PorLlave = "identidad-publica";

    /// <summary>Familia de los documentos por alias.</summary>
    internal const string PorAlias = "identidad-publica-alias";

    private const int LargoMaximoDeUnAlias = 48;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IMemberService _miembros;
    private readonly IJsonEntityStore _store;
    private readonly ConcurrentDictionary<string, string> _anotadas = new(StringComparer.Ordinal);

    /// <summary>Sobre los miembros de Umbraco y el almacén común.</summary>
    public IdentidadPublicaDeMiembros(IMemberService miembros, IJsonEntityStore store)
    {
        _miembros = miembros ?? throw new ArgumentNullException(nameof(miembros));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public IdentidadPublica De(string correo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correo);
        var limpio = correo.Trim();
        return CaraPublica.De(SeudonimoDePersona.De(limpio), _miembros.GetByEmail(limpio)?.Name);
    }

    /// <inheritdoc />
    public async Task RecordarAsync(string correo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correo);
        var limpio = correo.Trim();
        var cara = De(limpio);
        if (_anotadas.TryGetValue(cara.Llave, out var anotado) && string.Equals(anotado, cara.Alias, StringComparison.Ordinal))
        {
            return;
        }

        var porLlave = Leer<AnotacionPorLlave>(await _store.ReadAsync(PorLlave, cara.Llave, cancellationToken).ConfigureAwait(false));
        if (porLlave is null
            || !string.Equals(porLlave.Alias, cara.Alias, StringComparison.Ordinal)
            || !string.Equals(porLlave.Correo, limpio, StringComparison.Ordinal))
        {
            await _store.WriteAsync(
                PorLlave, cara.Llave, JsonSerializer.Serialize(new AnotacionPorLlave(cara.Llave, limpio, cara.Alias), Json), cancellationToken)
                .ConfigureAwait(false);
        }

        var porAlias = Leer<AnotacionPorAlias>(await _store.ReadAsync(PorAlias, cara.Alias, cancellationToken).ConfigureAwait(false));
        if (porAlias is null)
        {
            await _store.WriteAsync(
                PorAlias, cara.Alias, JsonSerializer.Serialize(new AnotacionPorAlias(cara.Alias, cara.Llave), Json), cancellationToken)
                .ConfigureAwait(false);
        }

        _anotadas[cara.Llave] = cara.Alias;
    }

    /// <inheritdoc />
    public async Task<string?> CorreoDeAsync(string llaveOAlias, CancellationToken cancellationToken = default)
    {
        var valor = llaveOAlias?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(valor) || valor.Length > LargoMaximoDeUnAlias || !valor.All(EsDeUnAlias))
        {
            return null;
        }

        var llave = valor;
        if (!CaraPublica.EsLlave(valor))
        {
            var porAlias = Leer<AnotacionPorAlias>(await _store.ReadAsync(PorAlias, valor, cancellationToken).ConfigureAwait(false));
            if (porAlias is null)
            {
                return null;
            }
            llave = porAlias.Llave;
        }

        var porLlave = Leer<AnotacionPorLlave>(await _store.ReadAsync(PorLlave, llave, cancellationToken).ConfigureAwait(false));
        return string.IsNullOrWhiteSpace(porLlave?.Correo) ? null : porLlave.Correo;
    }

    // Un alias y una llave son minúsculas, dígitos y guiones: lo demás no se busca en el almacén.
    private static bool EsDeUnAlias(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';

    private static T? Leer<T>(string? json)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record AnotacionPorLlave(string Llave, string Correo, string Alias);

    private sealed record AnotacionPorAlias(string Alias, string Llave);
}
