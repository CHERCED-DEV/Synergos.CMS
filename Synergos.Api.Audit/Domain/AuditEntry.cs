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
/// <remarks>
/// <para><b>El reloj lo pone el servicio, no el origen.</b> Un origen con la hora corrida —o
/// malintencionado— podría escribir entradas en el pasado y romper el orden de una investigación,
/// que es precisamente para lo que existe una bitácora.</para>
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
    IReadOnlyDictionary<string, string> Details);
