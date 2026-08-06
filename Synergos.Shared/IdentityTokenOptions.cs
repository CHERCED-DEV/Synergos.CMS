namespace Synergos.Shared;

/// <summary>
/// Cuánto vale un token, hasta cuándo se renueva y con qué se firma — sección <c>IdentityTokens</c>.
/// </summary>
/// <remarks>
/// <para><b>Los dos números juntos son la decisión, no cada uno por su lado.</b> Quince minutos
/// solos serían un adorno si la renovación no tuviera techo: quien se hiciera con un token lo
/// renovaría para siempre, de a quince minutos. Y un techo solo, sin vigencia corta, dejaría un
/// token robado operando ocho horas.</para>
///
/// <para><b>La sección se llama igual en TODOS los servicios</b>, y no es cosmética: la llave de
/// firma es la misma para quien emite y para quien verifica, así que darle un nombre por servicio
/// invitaría a configurar una en un sitio y otra en otro — y el síntoma sería un token válido que
/// una capacidad rechaza, que es de los peores de diagnosticar.</para>
///
/// <para><b>Quien solo VERIFICA ignora las vigencias</b> y usa nada más las llaves. Están en el
/// mismo sitio porque separarlas obligaría a dos secciones para la misma decisión.</para>
///
/// <para><b>Y el precio de los quince minutos está pagado a conciencia:</b> los roles viajan
/// dentro del token para que una capacidad pueda verificar sin llamar a nadie, así que revocar un
/// rol tarda lo que quede de vigencia. Ése es el costo de no convertir a <c>Api.Identity</c> en
/// el punto único de fallo de las veinte capacidades (HU #14 §3.2).</para>
/// </remarks>
public sealed class IdentityTokenOptions
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
