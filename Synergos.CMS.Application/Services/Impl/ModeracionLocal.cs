using System.Text.Json;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Moderacion;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// La moderación en <c>Local</c> (012-blogs-social, I5): los casos en el almacén común del CMS
/// (<see cref="IJsonEntityStore"/>, familia <see cref="Familia"/>), un caso abierto por objetivo. <c>Api</c> será
/// <c>Api.Moderation</c>, con las mismas reglas: un motivo para denunciar, y quién y por qué para decidir.
/// </summary>
/// <remarks>
/// Quien denuncia se guarda por dentro para que la misma persona no cuente dos veces, y no sale por ninguna lectura.
/// Las escrituras van de una en una: dos denuncias simultáneas del mismo objetivo abrirían dos casos.
/// </remarks>
public sealed class ModeracionLocal : IModeracion, IDisposable
{
    /// <summary>La familia de los casos en el almacén.</summary>
    public const string Familia = "moderacion-casos";

    /// <summary>Lo más largo de un motivo, el de <c>Api.Moderation</c>.</summary>
    public const int LargoMaximoDelMotivo = 500;

    private static readonly JsonSerializerOptions ComoSeGuarda = new(JsonSerializerDefaults.Web);

    private readonly IJsonEntityStore _almacen;
    private readonly TimeProvider _reloj;
    private readonly SemaphoreSlim _turno = new(1, 1);

    /// <summary>Sobre el almacén común y el reloj del CMS.</summary>
    public ModeracionLocal(IJsonEntityStore almacen, TimeProvider reloj)
    {
        _almacen = almacen ?? throw new ArgumentNullException(nameof(almacen));
        _reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
    }

    /// <inheritdoc />
    public void Dispose() => _turno.Dispose();

    /// <inheritdoc />
    public async Task<CasoDeModeracion> DenunciarAsync(
        DenunciaNueva denuncia,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(denuncia);
        ArgumentException.ThrowIfNullOrWhiteSpace(denuncia.Objetivo.Tipo);
        ArgumentException.ThrowIfNullOrWhiteSpace(denuncia.Objetivo.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(denuncia.Denunciante);
        var motivo = Motivo(denuncia.Motivo);
        var denunciante = denuncia.Denunciante.Trim();

        await _turno.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var abierto = (await TodosAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(c => c.Decision is null && EsDe(c, denuncia.Objetivo));
            if (abierto is not null)
            {
                if (!abierto.Denunciantes.Contains(denunciante, StringComparer.OrdinalIgnoreCase))
                {
                    abierto = abierto with { Denunciantes = [.. abierto.Denunciantes, denunciante] };
                    await GuardarAsync(abierto, cancellationToken).ConfigureAwait(false);
                }

                return abierto.ComoCaso();
            }

            var nuevo = new CasoGuardado(
                Id: Guid.NewGuid().ToString("N"),
                Tipo: denuncia.Objetivo.Tipo.Trim(),
                ObjetivoId: denuncia.Objetivo.Id.Trim(),
                Resumen: denuncia.Resumen.Trim(),
                Enlace: string.IsNullOrWhiteSpace(denuncia.Enlace) ? null : denuncia.Enlace.Trim(),
                Motivo: motivo,
                Denunciantes: [denunciante],
                Denunciado: _reloj.GetUtcNow(),
                Decision: null,
                Quien: null,
                PorQue: null,
                Decidido: null);
            await GuardarAsync(nuevo, cancellationToken).ConfigureAwait(false);
            return nuevo.ComoCaso();
        }
        finally
        {
            _turno.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CasoDeModeracion>> AbiertosAsync(
        int cuantos,
        CancellationToken cancellationToken = default)
        => (await TodosAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => c.Decision is null)
            .OrderBy(c => c.Denunciado)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .Take(Math.Clamp(cuantos, 1, 200))
            .Select(c => c.ComoCaso())
            .ToList();

    /// <inheritdoc />
    public async Task<CasoDeModeracion?> CasoAsync(string id, CancellationToken cancellationToken = default)
        => (await LeerAsync(id, cancellationToken).ConfigureAwait(false))?.ComoCaso();

    /// <inheritdoc />
    public async Task<DecisionTomada> DecidirAsync(
        string id,
        DecisionDeModeracion decision,
        QuienModera quien,
        string porQue,
        CancellationToken cancellationToken = default)
    {
        // Una moderación anónima es indistinguible de un borrado arbitrario, y una sin motivo no se puede revisar.
        ArgumentNullException.ThrowIfNull(quien);
        ArgumentException.ThrowIfNullOrWhiteSpace(quien.Llave);
        ArgumentException.ThrowIfNullOrWhiteSpace(quien.Nombre);
        var motivo = Motivo(porQue);

        await _turno.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var caso = await LeerAsync(id, cancellationToken).ConfigureAwait(false);
            if (caso is null)
            {
                return new DecisionTomada(ResultadoDeDecidir.NoExiste, null);
            }

            if (caso.Decision is { } yaDecidida)
            {
                return new DecisionTomada(
                    yaDecidida == decision ? ResultadoDeDecidir.Decidido : ResultadoDeDecidir.YaDecididoDistinto,
                    caso.ComoCaso());
            }

            var decidido = caso with
            {
                Decision = decision,
                Quien = quien.Nombre.Trim(),
                QuienLlave = quien.Llave.Trim(),
                PorQue = motivo,
                Decidido = _reloj.GetUtcNow(),
            };
            await GuardarAsync(decidido, cancellationToken).ConfigureAwait(false);
            return new DecisionTomada(ResultadoDeDecidir.Decidido, decidido.ComoCaso());
        }
        finally
        {
            _turno.Release();
        }
    }

    private static string Motivo(string? motivo)
    {
        var texto = motivo?.Trim() ?? string.Empty;
        if (texto.Length == 0 || texto.Length > LargoMaximoDelMotivo)
        {
            throw new ArgumentException(
                $"El motivo es obligatorio y no pasa de {LargoMaximoDelMotivo} caracteres.",
                nameof(motivo));
        }

        return texto;
    }

    private static bool EsDe(CasoGuardado caso, ObjetivoModerable objetivo)
        => string.Equals(caso.Tipo, objetivo.Tipo.Trim(), StringComparison.Ordinal)
            && string.Equals(caso.ObjetivoId, objetivo.Id.Trim(), StringComparison.Ordinal);

    private async Task<IReadOnlyList<CasoGuardado>> TodosAsync(CancellationToken cancellationToken)
        => (await _almacen.ListAsync(Familia, cancellationToken).ConfigureAwait(false))
            .Select(json => JsonSerializer.Deserialize<CasoGuardado>(json, ComoSeGuarda))
            .OfType<CasoGuardado>()
            .ToList();

    private async Task<CasoGuardado?> LeerAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParseExact(id.Trim(), "N", out _))
        {
            return null;
        }

        var json = await _almacen.ReadAsync(Familia, id.Trim(), cancellationToken).ConfigureAwait(false);
        return json is null ? null : JsonSerializer.Deserialize<CasoGuardado>(json, ComoSeGuarda);
    }

    private Task GuardarAsync(CasoGuardado caso, CancellationToken cancellationToken)
        => _almacen.WriteAsync(Familia, caso.Id, JsonSerializer.Serialize(caso, ComoSeGuarda), cancellationToken);

    /// <summary>Un caso tal como se guarda: con quienes lo denunciaron, que no salen.</summary>
    private sealed record CasoGuardado(
        string Id,
        string Tipo,
        string ObjetivoId,
        string Resumen,
        string? Enlace,
        string Motivo,
        IReadOnlyList<string> Denunciantes,
        DateTimeOffset Denunciado,
        DecisionDeModeracion? Decision,
        string? Quien,
        string? PorQue,
        DateTimeOffset? Decidido,
        string? QuienLlave = null)
    {
        public CasoDeModeracion ComoCaso() => new(
            Id,
            new ObjetivoModerable(Tipo, ObjetivoId),
            Resumen,
            Enlace,
            Motivo,
            Denunciantes.Count,
            Denunciado,
            Decision,
            Quien,
            PorQue,
            Decidido);
    }
}

/// <summary>
/// Lo retirado por moderación que no tiene dónde marcarse en su propio almacén (las publicaciones de la red), en el
/// almacén común (012-blogs-social, I5). Se lee en cada página del feed, así que se guarda en memoria al leerlo la
/// primera vez y se actualiza al retirar: un despliegue tiene un solo CMS.
/// </summary>
public sealed class RegistroDeRetirados : IRegistroDeRetirados, IDisposable
{
    /// <summary>La familia de lo retirado en el almacén.</summary>
    public const string Familia = "moderacion-retirados";

    private static readonly JsonSerializerOptions ComoSeGuarda = new(JsonSerializerDefaults.Web);

    private readonly IJsonEntityStore _almacen;
    private readonly SemaphoreSlim _turno = new(1, 1);
    private Dictionary<string, HashSet<string>>? _porTipo;

    /// <summary>Sobre el almacén común.</summary>
    public RegistroDeRetirados(IJsonEntityStore almacen)
    {
        _almacen = almacen ?? throw new ArgumentNullException(nameof(almacen));
    }

    /// <inheritdoc />
    public void Dispose() => _turno.Dispose();

    /// <inheritdoc />
    public async Task RetirarAsync(ObjetivoModerable objetivo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objetivo);
        ArgumentException.ThrowIfNullOrWhiteSpace(objetivo.Tipo);
        ArgumentException.ThrowIfNullOrWhiteSpace(objetivo.Id);
        var tipo = objetivo.Tipo.Trim();
        var id = objetivo.Id.Trim();

        await _turno.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var porTipo = await CargarAsync(cancellationToken).ConfigureAwait(false);
            if (porTipo.TryGetValue(tipo, out var ids) && ids.Contains(id))
            {
                return;
            }

            // La clave es una huella: un id de cualquier vertical puede traer lo que un nombre de fichero no admite.
            await _almacen.WriteAsync(
                Familia,
                Huella(tipo, id),
                JsonSerializer.Serialize(new Retirado(tipo, id), ComoSeGuarda),
                cancellationToken).ConfigureAwait(false);
            if (!porTipo.TryGetValue(tipo, out ids))
            {
                porTipo[tipo] = ids = new HashSet<string>(StringComparer.Ordinal);
            }

            ids.Add(id);
        }
        finally
        {
            _turno.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> RetiradosAsync(string tipo, CancellationToken cancellationToken = default)
    {
        await _turno.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var porTipo = await CargarAsync(cancellationToken).ConfigureAwait(false);
            return porTipo.TryGetValue(tipo, out var ids)
                ? new HashSet<string>(ids, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }
        finally
        {
            _turno.Release();
        }
    }

    private async Task<Dictionary<string, HashSet<string>>> CargarAsync(CancellationToken cancellationToken)
    {
        if (_porTipo is not null)
        {
            return _porTipo;
        }

        var porTipo = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var json in await _almacen.ListAsync(Familia, cancellationToken).ConfigureAwait(false))
        {
            if (JsonSerializer.Deserialize<Retirado>(json, ComoSeGuarda) is { } retirado)
            {
                if (!porTipo.TryGetValue(retirado.Tipo, out var ids))
                {
                    porTipo[retirado.Tipo] = ids = new HashSet<string>(StringComparer.Ordinal);
                }

                ids.Add(retirado.Id);
            }
        }

        return _porTipo = porTipo;
    }

    private static string Huella(string tipo, string id)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{tipo}\n{id}"))).ToLowerInvariant();

    private sealed record Retirado(string Tipo, string Id);
}

/// <summary>
/// El stream de la red sin lo que la moderación retiró (012-blogs-social, I5): no sale en ningún feed, ni en el
/// catálogo, ni al pedirlo por su id. Envuelve al stream que haya; publicar sigue igual.
/// </summary>
public sealed class StreamSinRetirados : IContentStream, IDisposable
{
    /// <summary>El tipo moderable de una publicación de la red.</summary>
    public const string TipoDeLaPublicacion = "blogs.publicacion";

    private readonly IContentStream _stream;
    private readonly IRegistroDeRetirados _retirados;

    /// <summary>Sobre el stream de la red y el registro de lo retirado.</summary>
    public StreamSinRetirados(IContentStream stream, IRegistroDeRetirados retirados)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _retirados = retirados ?? throw new ArgumentNullException(nameof(retirados));
    }

    /// <inheritdoc />
    public async Task<ContentStreamPage> GetFeedAsync(FeedQuery query, CancellationToken cancellationToken = default)
    {
        var pagina = await _stream.GetFeedAsync(query, cancellationToken).ConfigureAwait(false);
        var retirados = await _retirados.RetiradosAsync(TipoDeLaPublicacion, cancellationToken).ConfigureAwait(false);
        return retirados.Count == 0
            ? pagina
            : pagina with { Items = pagina.Items.Where(i => !retirados.Contains(i.Id)).ToList() };
    }

    /// <inheritdoc />
    public async Task<ContentStreamItem?> GetItemAsync(string id, CancellationToken cancellationToken = default)
    {
        var item = await _stream.GetItemAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        var retirados = await _retirados.RetiradosAsync(TipoDeLaPublicacion, cancellationToken).ConfigureAwait(false);
        return retirados.Contains(item.Id) ? null : item;
    }

    /// <inheritdoc />
    public Task<ContentStreamItem> CreateAsync(NewContentItem item, CancellationToken cancellationToken = default)
        => _stream.CreateAsync(item, cancellationToken);

    /// <summary>
    /// Libera el stream que envuelve si es suyo: el decorador de lo autorado lo crea el composer para éste, y antes lo
    /// liberaba el contenedor. El del stub es del contenedor y no es liberable.
    /// </summary>
    public void Dispose() => (_stream as IDisposable)?.Dispose();
}
