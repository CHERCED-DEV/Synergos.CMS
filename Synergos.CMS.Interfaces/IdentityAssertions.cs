namespace Synergos.CMS.Interfaces;

/// <summary>
/// Con qué fuerza se afirmó que quien actúa es quien dice ser.
/// </summary>
/// <remarks>
/// <para><b>Nació como <c>IdentityAssertions</c> en la HU #62</b>, con un solo consumidor: el acuse
/// de un acto administrativo. Sube acá al aparecer el SEGUNDO —el asiento del intento rechazado
/// (HU #15)— que es cuando <c>CLAUDE.md</c> §17 dice que algo se promueve. Un mes antes habría
/// sido adivinar; un consumidor después, dos copias que hay que acordarse de cambiar a la vez.
/// Es la misma promoción que <c>IdentityAssertion</c> hizo en el árbol de servicios, subiendo de
/// <c>Api.Messaging</c> a <c>Synergos.Core</c> por esta misma razón.</para>
///
/// <para><b>Y sigue siendo <c>string</c>, no el tipo de <c>Synergos.Core</c></b>: el árbol del CMS
/// no referencia el de servicios —se hablan sólo por HTTP (<c>CLAUDE.md</c> §11)— y meter aquel
/// tipo abriría la primera flecha de ensamblado entre los dos. El precio es esta nota; la
/// alternativa era romper el principio por comodidad.</para>
///
/// <para><b>Lo que NO hay es un valor por defecto.</b> Vacío significa «no consta», y ésa es la
/// verdad sobre todo lo que se registró antes de que existiera esta noción. Rellenarlo con
/// <see cref="CmsSession"/> inventaría una comprobación que nadie hizo — el defecto #42 con otro
/// disfraz, sobre un dato que existe justamente para sostener quién hizo qué.</para>
/// </remarks>
public static class IdentityAssertions
{
    /// <summary>No consta. Lo de antes de que esto existiera, y lo que no lo registra.</summary>
    public const string None = "";

    /// <summary>Lo afirma nuestra propia sesión. Es lo que hay sin identidad verificable.</summary>
    public const string CmsSession = "CmsSession";

    /// <summary>Lo respalda un token emitido y <b>verificado</b> por <c>Api.Identity</c>.</summary>
    /// <remarks>
    /// Que el despliegue <i>sepa emitir</i> un token no basta para escribir esto: la fuerza de una
    /// afirmación es la de lo que alguien <b>verificó</b>, no la de lo que se podría haber
    /// presentado. Quien lo escribe es quien comprobó el token, y en los caminos que no lo
    /// presentan lo honesto sigue siendo <see cref="CmsSession"/>.
    /// </remarks>
    public const string IdentityToken = "IdentityToken";
}
