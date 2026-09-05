using Synergos.Core;

namespace Synergos.Api.Audit.Domain;

/// <summary>
/// Una entrada de la bitácora: quién hizo qué, sobre qué, y cuándo.
/// </summary>
/// <param name="Id">Identificador de la entrada.</param>
/// <param name="Actor">Quién actuó.</param>
/// <param name="Action">Qué hizo, en verbo estable — <c>reservation.cancelled</c>.</param>
/// <param name="Target">Sobre qué, opaco.</param>
/// <param name="AtUtc">Cuándo lo registró <b>este servicio</b>, no el origen.</param>
/// <param name="Details">Contexto adicional, en texto plano.</param>
/// <param name="ActedWith">
/// Con qué se afirmó la identidad del actor. <b>Es lo que separa un asiento que se puede
/// sostener de uno que no</b>, y sin ello el arreglo del defecto #72 sería invisible: los
/// asientos nuevos valdrían más que los viejos y nada lo diría.
/// </param>
/// <remarks>
/// <para><b>El reloj lo pone el servicio, no el origen.</b> Un origen con la hora corrida —o
/// malintencionado— podría escribir entradas en el pasado y romper el orden de una investigación,
/// que es precisamente para lo que existe una bitácora.</para>
///
/// <para><b>Blindada contra reescribir el pasado, y hasta #72 abierta a FABRICARLO.</b> No hay
/// <c>PUT</c> ni <c>DELETE</c> —eso está desde el principio— pero el actor y sus roles llegaban
/// del cuerpo de la petición sin que nadie los comprobara: con la llave compartida se escribía un
/// asiento a nombre de quien fuera, con los roles que fuera, y quedaba permanente. Un registro
/// inmutable de asientos falsificables es peor que no tenerlo: es una mentira con aspecto de
/// prueba, y justo la pieza que alguien va a citar cuando haya que demostrar quién hizo qué.</para>
///
/// <para><b>Null en <see cref="ActedWith"/> significa «no consta»</b>, que es la verdad sobre los
/// asientos anteriores. Rellenarlos con <c>CmsSession</c> sería inventar una afirmación que nadie
/// hizo —el defecto #42 con otro disfraz— y además obligaría a reescribir un registro append-only,
/// que es lo único que lo inutiliza del todo.</para>
///
/// <para><b>Los detalles son texto y no un objeto libre.</b> Una bitácora que acepta cualquier
/// forma termina siendo un vertedero donde nadie puede buscar, y —peor— por donde se filtran
/// datos personales que nadie decidió guardar. Texto plano obliga a que quien escribe elija qué
/// vale la pena conservar.</para>
/// </remarks>
public sealed record AuditEntry(
    string Id,
    Actor Actor,
    string Action,
    Ref Target,
    DateTimeOffset AtUtc,
    IReadOnlyDictionary<string, string> Details,
    IdentityAssertion? ActedWith = null);
