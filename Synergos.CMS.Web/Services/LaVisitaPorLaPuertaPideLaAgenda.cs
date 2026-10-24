using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Con la ruta directa en el motor en proceso, ningún sitio declara la visita por la puerta (008-propiedades-visita,
/// FR-019): el CMS no arranca y lo dice.
/// </summary>
/// <remarks>
/// <para><b>Por qué.</b> Mientras la puerta no tenga su lectura de franjas (NB-2/NB-3 del núcleo, 008-P3), la pantalla
/// lee las franjas de la ruta directa. Con <c>Synergos:Realty:Mode=Stub</c> esas franjas salen del almacén del CMS, no de
/// la agenda donde aparta la puerta: la pantalla ofrecería franjas que la agenda no conoce y dos visitas podrían caer a
/// la misma hora, una por cada cupo.</para>
///
/// <para><b>Sólo se registra en esa rama</b> del composer (el interruptor la decide al componer): con <c>Api</c> los dos
/// caminos preguntan a la misma agenda y no hay nada que impedir. Se retira cuando la lectura vaya por la puerta.</para>
/// </remarks>
public sealed class LaVisitaPorLaPuertaPideLaAgenda : IValidateOptions<RealtyFeatureSettings>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RealtyFeatureSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var conPuerta = new List<string>();
        if (options.Para(null).VisitPath == CaminoDelFlujo.Puerta) conPuerta.Add("los valores base");
        foreach (var clave in options.Sitios.Keys)
        {
            if (Guid.TryParse(clave, out var sitio) && options.Para(sitio).VisitPath == CaminoDelFlujo.Puerta)
            {
                conPuerta.Add($"el sitio {clave}");
            }
        }

        return conPuerta.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Synergos:Features:Realty:VisitPath es «puerta» en {string.Join(", ", conPuerta)} y Synergos:Realty:Mode no es «Api»: "
                + "la pantalla leería las franjas del motor en proceso, que no son las de la agenda donde aparta la puerta. "
                + "Pon Synergos:Realty:Mode=Api o deja la visita en «directa».");
    }
}
