namespace Synergos.CMS.Interfaces;

/// <summary>
/// Avisa EN VIVO a los clientes suscritos a un canal (T7). Es el seam transversal del
/// realtime: los verticales publican hechos aquí y no saben nada del transporte.
/// </summary>
/// <remarks>
/// <para><b>Un canal es una cadena con dueño</b>, no un tema libre: quien lo publica
/// decide su forma (<c>eventos:checkin:{eventId}</c>) y quien lo sirve decide quién
/// puede leerlo. La autorización NO vive aquí — vive en el endpoint que abre el flujo,
/// y por diseño es <b>fail-closed</b>: un canal sin política declarada no se puede leer.
/// Sin eso, el realtime sería una puerta trasera a los datos que las tres olas
/// anteriores acaban de cerrar.</para>
/// <para><b>Publicar es best-effort y NUNCA debe tumbar la operación de negocio.</b> Un
/// check-in válido sigue siendo válido aunque no haya nadie escuchando o falle el envío:
/// el hecho ya ocurrió y está persistido. Misma regla que los side-effects post-commit
/// (ADR 0037/0106).</para>
/// <para>El fan-out es EN PROCESO: un deploy = un origen (no hay multi-tenant, y
/// tampoco varias instancias hoy), así que no hace falta un broker externo. Si algún día
/// se escala horizontalmente, este mismo seam se implementa sobre Redis/backplane sin
/// tocar a los verticales — que es justo para lo que existe.</para>
/// </remarks>
public interface IRealtimeNotifier
{
    /// <summary>
    /// Publica <paramref name="payloadJson"/> en <paramref name="channel"/> bajo el
    /// nombre de evento <paramref name="eventName"/>. No lanza si no hay suscriptores.
    /// </summary>
    Task PublishAsync(
        string channel,
        string eventName,
        string payloadJson,
        CancellationToken cancellationToken = default);
}
