using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.PropertyEditors;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// El set de iconos del sitio: los nombres que el editor elige en el desplegable de icono
/// (<c>DTSelectIcono</c>, el de <c>icon-label</c>).
/// </summary>
/// <remarks>
/// <para><b>No es una lista nueva: es la del DataType.</b> El desplegable es la única copia del set
/// en el CMS, y del lado del UI el gate de vocabulario la cruza con <c>NOMBRES_DE_ICONO</c> del
/// design system. Un resolver que recibe un icono escrito a mano —en el JSON de un TextArea, donde
/// no hay desplegable que lo cierre— pregunta acá.</para>
///
/// <para>Se lee de la caché de configuraciones de Umbraco por la Key del DataType, la misma que en
/// uSync (lo vigila un test). Si el DataType no se puede leer, <see cref="Nombres"/> devuelve
/// <c>null</c>: no saber cuál es el set no es saber que un nombre no está en él, y quien llama deja
/// pasar el nombre —el elemento lo filtra con su lista—.</para>
/// </remarks>
public sealed class IconosDelSistema
{
    /// <summary>La Key de <c>DTSelectIcono</c> en <c>uSync/v9/DataTypes/DTSelectIcono.config</c>.</summary>
    public static readonly Guid DataType = Guid.Parse("dcda1b5e-e239-4617-ac11-d40bfdd7dd11");

    private readonly IDataTypeConfigurationCache _configuraciones;

    public IconosDelSistema(IDataTypeConfigurationCache configuraciones) => _configuraciones = configuraciones;

    /// <summary>Los nombres del set, en minúsculas; <c>null</c> si el DataType no se puede leer.</summary>
    public IReadOnlySet<string>? Nombres()
    {
        var configuracion = _configuraciones.GetConfigurationAs<ValueListConfiguration>(DataType);
        if (configuracion is null)
        {
            return null;
        }

        return configuracion.Items
            .Select(i => i.Value?.Trim().ToLowerInvariant())
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
