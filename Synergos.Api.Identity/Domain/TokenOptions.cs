namespace Synergos.Api.Identity.Domain;

/// <summary>
/// Cuánto vale un token y hasta cuándo se puede renovar — sección <c>Identity:Tokens</c>.
/// </summary>
/// <remarks>
/// <para><b>Los dos números juntos son la decisión, no cada uno por su lado.</b> Quince minutos
/// solos serían un adorno si la renovación no tuviera techo: quien se hiciera con un token lo
/// renovaría para siempre, de a quince minutos. Y un techo solo, sin vigencia corta, dejaría un
/// token robado operando ocho horas.</para>
///
/// <para><b>Y el precio de los quince minutos está pagado a conciencia:</b> los roles viajan
/// dentro del token para que una capacidad pueda verificar sin llamar a nadie, así que revocar un
/// rol tarda lo que quede de vigencia. Ése es el costo de no convertir a <c>Api.Identity</c> en
/// el punto único de fallo de las veinte capacidades (HU #14 §3.2).</para>
/// </remarks>
public sealed class TokenOptions
{
    /// <summary>Cuánto vale un token emitido. Quince minutos (HU #14).</summary>
    public int LifetimeMinutes { get; set; } = 15;

    /// <summary>
    /// Cuánto puede durar la sesión entera renovando. Ocho horas: una jornada.
    /// </summary>
    /// <remarks>
    /// Se cuenta desde que la sesión empezó, no desde el último token — si se contara desde el
    /// último, no sería un techo: sería la misma vigencia con otro nombre.
    /// </remarks>
    public int MaxSessionMinutes { get; set; } = 480;

    /// <summary>Las llaves de firma, por <c>kid</c>. Se verifica con todas.</summary>
    /// <remarks>
    /// <b>NO es la llave compartida</b>, y mezclarlas haría que quien puede llamar a un servicio
    /// pudiera además fabricar identidades. Van por configuración de entorno, como aquélla.
    /// </remarks>
    public IDictionary<string, string> Keys { get; set; } = new Dictionary<string, string>();

    /// <summary>Con cuál se FIRMA. Rotar es añadir una llave y mover esto.</summary>
    public string ActiveKeyId { get; set; } = "k1";
}
