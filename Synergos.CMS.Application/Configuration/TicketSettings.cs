namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// El artefacto de Eventos —la entrada y su QR— sección <c>Synergos:Eventos:Ticket</c>.
/// </summary>
/// <remarks>
/// <para><b>Por qué ANIDADA bajo la del vertical y no hermana de ella</b> (#154). Esto vivía en
/// <c>Synergos:Events</c> mientras la transacción vivía en <c>Synergos:Eventos</c>: dos secciones
/// a <b>una letra</b> de distancia, enlazadas por composers distintos, y un dedazo entre ellas
/// <b>no falla</b> — el binder de .NET descarta en silencio lo que no mapea
/// (<c>feedback_a_key_in_the_wrong_section_is_a_key_nobody_reads</c>). Medido sobre las 43
/// secciones que el CMS enlaza, era el <b>único</b> par a esa distancia.</para>
///
/// <para><b>Y anidar es lo que sobrevive al día que el sello se cablee.</b> El de Educación ya
/// cruzó a <c>Api.Signing</c> (HU #45) y le hizo falta su propio <c>Mode</c>/<c>BaseUrl</c>/
/// <c>ApiKey</c>; acá <c>Synergos:Eventos:Mode</c> ya es del eje 2, así que un hermano plano no
/// tendría dónde ponerlos. Es la forma que el árbol ya usa para un sub-asunto del vertical que se
/// cabla por su cuenta: <c>Synergos:Gob:Notifications</c> y <c>Synergos:Gob:Payments</c>.</para>
///
/// <para><b>POCO propio y no un campo de <c>EventosSettings</c>, que era la salida obvia.</b> Ese
/// POCO lo recibe <c>HttpEventTicketingService</c> —el cliente del eje 2— y no tiene por qué
/// llevar dentro la llave con la que se firma nada; es
/// <c>feedback_pii_decision_lives_in_the_seam_type</c> aplicado a un secreto: lo que un tipo
/// carga es una propiedad del tipo, no una convención que alguien recuerde.</para>
/// </remarks>
public sealed class TicketSettings
{
    /// <summary>
    /// Secreto con el que se firma el token del QR de las entradas (T9).
    /// </summary>
    /// <remarks>
    /// <para><b>Vacío NO significa "no firmar".</b> Si no se configura, el host genera
    /// una llave aleatoria la primera vez y la guarda cifrada en el almacén privado
    /// (ADR 0109), así que las entradas siguen firmadas y —esto es lo que importa— el QR
    /// sobrevive a un reinicio. Poblarlo es lo correcto en producción: permite rotar la
    /// llave y compartirla entre instancias.</para>
    /// <para>No se pone un default literal a propósito: un secreto escrito en el repo es
    /// un secreto conocido, y un token firmado con una llave pública es falsificable
    /// mientras aparenta lo contrario.</para>
    /// </remarks>
    public string SigningSecret { get; init; } = string.Empty;
}
