using System.Globalization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.Listados;
using Umbraco.Cms.Core.Dictionary;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynDataGrid</c> → <see cref="DataGridProps"/>: las filas de la fuente que eligió el
/// editor, consultadas en el servidor con el <c>?q</c> de la página (#196, tanda D).
/// </summary>
/// <remarks>
/// Una fuente que no existe no tumba la página: el listado sale vacío —y lo dice— y queda anotado
/// en el log con el bloque. Las fuentes se descubren por DI: una nueva se registra, no se programa
/// aquí.
/// </remarks>
public sealed class DataGridResolutor : IResolutorSynHost<DataGridProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly IReadOnlyDictionary<string, IFuenteDeListado> _fuentes;
    private readonly ICultureDictionaryFactory _diccionarios;
    private readonly IOptions<ListadosSettings> _listados;
    private readonly IHttpContextAccessor? _peticion;
    private readonly ILogger<DataGridResolutor> _log;

    public DataGridResolutor(
        IPublishedValueFallback fallback,
        IEnumerable<IFuenteDeListado> fuentes,
        ICultureDictionaryFactory diccionarios,
        IOptions<ListadosSettings> listados,
        ILogger<DataGridResolutor> log,
        IHttpContextAccessor? peticion = null)
    {
        _fallback = fallback;
        _fuentes = fuentes.ToDictionary(f => f.Clave, StringComparer.OrdinalIgnoreCase);
        _diccionarios = diccionarios;
        _listados = listados;
        _peticion = peticion;
        _log = log;
    }

    public ElementoResuelto<DataGridProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var clave = editor.Texto("fuente");
        if (clave is null || !_fuentes.TryGetValue(clave, out var fuente))
        {
            if (clave is not null)
            {
                editor.NoEsValido("fuente", clave, string.Join(", ", _fuentes.Keys.Order(StringComparer.Ordinal)));
            }

            return new ElementoResuelto<DataGridProps>(new DataGridProps(Rows: null));
        }

        var cultura = CultureInfo.CurrentUICulture;
        var diccionario = _diccionarios.CreateDictionary(cultura);
        var filas = fuente.Filas(new PeticionDelListado(
            Consulta: Consulta(),
            Cultura: cultura,
            Zona: _listados.Value.Zona() ?? TimeZoneInfo.Utc,
            Rotulo: (clave, respaldo) => diccionario[clave] is { Length: > 0 } texto && texto != clave ? texto : respaldo));

        // Las mismas filas, en HTML: lo que se ve sin JavaScript y lo que indexa un buscador.
        var aria = diccionario["DataGrid.Aria"] is { Length: > 0 } a && a != "DataGrid.Aria" ? a : "Listado";
        return new ElementoResuelto<DataGridProps>(
            new DataGridProps(filas.Count > 0 ? filas : null),
            RespaldoHtml: SynHostFallbackBuilder.Listado(filas, aria));
    }

    /// <summary>El <c>?q</c> de la página, recortado; el buscador la recarga con él.</summary>
    private string? Consulta()
        => _peticion?.HttpContext?.Request.Query["q"].ToString() is { Length: > 0 } q && !string.IsNullOrWhiteSpace(q)
            ? q.Trim()
            : null;
}
