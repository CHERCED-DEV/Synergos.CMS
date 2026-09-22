using Synergos.Core;

namespace Synergos.Bff.Core;

/// <summary>
/// Con qué fuerza afirma un orquestador la identidad de quien nombra, cuando le habla a una
/// capacidad que lo exige.
/// </summary>
/// <remarks>
/// <para><b>El suelo es también el techo, y eso NO es una limitación que haya que levantar.</b>
/// Un orquestador no propaga credenciales —un token es una credencial con RELOJ y una saga es
/// trabajo con DURACIÓN, así que a media compensación estaría vencido
/// (<c>an_orchestrator_cites_a_record_it_does_not_relay_a_credential</c>)— de modo que lo único
/// que puede afirmar con honestidad es <see cref="IdentityAssertion.CmsSession"/>: «nos fiamos de
/// quien llama», o sea la <i>ausencia</i> de comprobación. Declarar algo más fuerte sin
/// presentarlo lo rechaza la propia capacidad con <c>assertion_not_proven</c>, que es
/// exactamente el defecto #42 y la razón de que el campo exista.</para>
///
/// <para><b>Y NO declarar nada tampoco es una opción</b>: la capacidad rechaza con
/// <c>{prefijo}.access_requires_identity</c> antes de tocar su almacén, así que el paso no
/// ocurre. Es la misma decisión que el asiento de bitácora del CMS tomó en la HU #15 —«un
/// asiento que no registra ninguna afirmación se vuelve un hueco: ruido en vez de rastro»—
/// aplicada al cobro.</para>
///
/// <para><b>Vive acá, una vez, y no cinco veces dentro de cada <c>*Capabilities</c>.</b> El
/// SUJETO y la POLÍTICA son los mismos en los cinco —no hay variación por vertical, porque la
/// razón es estructural y no de dominio— así que cinco copias serían cinco sitios donde el día
/// que esto cambie cambia uno solo
/// (<c>a_rule_written_inside_its_only_obedient_copy_is_a_rule_nobody_reads</c>). Hay gate
/// (<c>AfirmacionDeOrquestadorTests</c>), que es lo que reemplaza a la nota.</para>
///
/// <para><b>El disparador para reabrir esto</b>, escrito para no tener que adivinarlo: que una
/// capacidad detrás de un orquestador necesite saber <b>CÓMO</b> se identificó la persona y no
/// sólo quién es. Ni siquiera entonces se propaga el token — se cita lo que el registro de otra
/// capacidad dice que fue (<c>Cart.OpenedWith</c>), guardado como afirmación de segunda mano.
/// </para>
/// </remarks>
public static class OrchestratorAssertion
{
    /// <summary>
    /// Lo que un orquestador declara en el cuerpo, tal cual lo parsea la capacidad.
    /// </summary>
    /// <remarks>
    /// Va por <c>nameof</c> y no como literal: renombrar el miembro del enum tiene que romper el
    /// build acá, no dejar a los cinco orquestadores mandando una cadena que ya no existe y
    /// recibiendo un 400 en el primer cobro de verdad.
    /// </remarks>
    public const string Declared = nameof(IdentityAssertion.CmsSession);
}
