namespace Synergos.CMS.Interfaces;

/// <summary>
/// La configuración de negocio de una funcionalidad que rige la petición en curso (ADR 0137): la de
/// su sección <c>Synergos:Features:&lt;X&gt;</c>, con el override del siteRoot de la petición
/// fusionado encima.
/// </summary>
/// <typeparam name="TNegocio">Los valores ya fusionados de la funcionalidad (<c>NegocioDeEventos</c>).</typeparam>
/// <remarks>
/// <para><b>Una fuente, dos lectores.</b> La lee el resolver del elemento para lo que se MUESTRA y la
/// leen los motores que la APLICAN o la COBRAN. Medido en el piloto (#194): con la comisión sólo en el
/// bundle, quien compraba veía un total y se le cobraba otro.</para>
/// <para><b>El sitio es el del hostname</b>, como lo resuelve el router de Umbraco, y no el de la
/// página: una API no tiene página, y lo que se muestra y lo que se aplica tienen que salir de la
/// MISMA regla. Sin dominio que case rigen los valores base.</para>
/// </remarks>
public interface INegocioDelSitio<out TNegocio>
    where TNegocio : class
{
    /// <summary>La configuración que rige la petición en curso.</summary>
    TNegocio Actual();
}
