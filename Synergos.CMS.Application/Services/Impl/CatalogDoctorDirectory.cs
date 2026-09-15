using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El directorio de profesionales servido desde el CONTENIDO que autoró el editor. Es el
/// <see cref="IDoctorDirectory"/> que se registra cuando
/// <c>Synergos:Catalog:Sources:Salud = cms</c>.
/// </summary>
/// <remarks>
/// <b>Es la versión SIMPLE de la forma de dos capas</b>, igual que
/// <see cref="CatalogStayContentProvider"/>: una sola capa, la del contenido.
/// <see cref="IDoctorDirectory"/> es de SOLO LECTURA —lista y trae por id, nada más—, así que no
/// existe un segundo autor que publique por fuera del backoffice y no hay nada que fusionar.
/// Prescindir aquí del <c>IJsonEntityStore</c> no es una omisión: un overlay durable sin escritor
/// sería el campo que promete algo que nadie cumple.
///
/// <para><b>El orden y el filtro se calcan de <see cref="StubDoctorDirectory"/> y no se
/// reinventan.</b> Si ordenar o filtrar cambiara según de dónde salen los profesionales, sería
/// una regresión invisible que sólo aparecería al mover una línea de configuración — que es la
/// peor forma de aparecer.</para>
///
/// <para>Lógica pura: cero <c>Umbraco.Cms.*</c> y cero <c>Microsoft.AspNetCore.*</c>
/// (ADR 0002).</para>
/// </remarks>
public sealed class CatalogDoctorDirectory : IDoctorDirectory
{
    private readonly ICatalogSource<MedicalDoctor> _source;

    public CatalogDoctorDirectory(ICatalogSource<MedicalDoctor> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async Task<IReadOnlyList<MedicalDoctor>> ListAsync(
        string? specialty = null, CancellationToken cancellationToken = default)
    {
        var todos = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);
        var ordenados = todos.OrderBy(d => d.FullName, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(specialty))
        {
            return ordenados.ToList();
        }

        var s = specialty.Trim();
        return ordenados
            .Where(d => d.Specialty.Contains(s, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<MedicalDoctor?> GetAsync(
        string doctorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(doctorId))
        {
            return null;
        }

        var todos = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);

        // Ordinal, como el diccionario del stub: el slug es un identificador, y dos slugs que
        // sólo se diferencian en mayúsculas son dos profesionales distintos para Api.Booking.
        return todos.FirstOrDefault(d => string.Equals(d.Id, doctorId, StringComparison.Ordinal));
    }
}
