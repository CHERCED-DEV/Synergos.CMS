using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Un <see cref="IJsonEntityStore"/> que guarda cifrado lo que le pasan, delante de otro: cada documento sale por
/// <see cref="IDataProtector"/> con su propósito, como el almacén de la historia (ADR 0098). Es de la librería: la
/// mensajería común lo usa para TODOS sus hilos (003-salud-cita, Q12), y no un almacén aparte de Salud.
/// </summary>
/// <remarks>
/// <para><b>No es un almacén nuevo</b> (ADR 0105): es el mismo, con su misma familia y sus mismas claves; sólo cambia lo
/// que queda en el disco.</para>
///
/// <para><b>Lo de antes se sigue leyendo</b> (SUPERSET): un documento escrito antes del cifrado es JSON en claro, y se
/// devuelve tal cual. La próxima escritura de ese documento ya lo deja cifrado. Lo que no es JSON y no se descifra
/// —corrupto, manipulado o de otra llave— no se devuelve: se registra y se trata como inexistente, como en la historia.</para>
/// </remarks>
public sealed class AlmacenCifrado : IJsonEntityStore
{
    /// <summary>El propósito con que se cifran los hilos de la mensajería común.</summary>
    public const string PropositoDeLaMensajeria = "Synergos.Mensajeria.v1";

    private readonly IJsonEntityStore _adentro;
    private readonly IDataProtector _protector;
    private readonly ILogger<AlmacenCifrado> _logger;

    public AlmacenCifrado(IJsonEntityStore adentro, IDataProtectionProvider proteccion, string proposito, ILogger<AlmacenCifrado> logger)
    {
        ArgumentNullException.ThrowIfNull(adentro);
        ArgumentNullException.ThrowIfNull(proteccion);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposito);
        _adentro = adentro;
        _protector = proteccion.CreateProtector(proposito);
        _logger = logger;
    }

    public Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default)
        => _adentro.WriteAsync(resourceType, key, _protector.Protect(json), cancellationToken);

    public async Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default)
        => Abrir(await _adentro.ReadAsync(resourceType, key, cancellationToken).ConfigureAwait(false), resourceType);

    public async Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default)
    {
        var crudos = await _adentro.ListAsync(resourceType, cancellationToken).ConfigureAwait(false);
        var abiertos = new List<string>(crudos.Count);
        foreach (var crudo in crudos)
        {
            if (Abrir(crudo, resourceType) is { } json) abiertos.Add(json);
        }
        return abiertos;
    }

    public Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default)
        => _adentro.DeleteAsync(resourceType, key, cancellationToken);

    /// <summary>Lo que hay guardado, en claro: lo de antes tal cual, lo cifrado descifrado, y lo demás nada.</summary>
    private string? Abrir(string? guardado, string resourceType)
    {
        if (string.IsNullOrWhiteSpace(guardado)) return null;
        var texto = guardado.TrimStart();
        if (texto.StartsWith('{') || texto.StartsWith('['))
        {
            return guardado;
        }
        try
        {
            return _protector.Unprotect(texto.TrimEnd());
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _logger.LogWarning(ex, "AlmacenCifrado: un documento de {Familia} no se pudo descifrar; se ignora", resourceType);
            return null;
        }
    }
}
