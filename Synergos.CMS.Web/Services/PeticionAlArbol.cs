namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lo que un cliente <c>Http*</c> le puede decir a la pieza sobre UNA petición (#178).
/// </summary>
/// <remarks>
/// <para><b>Existe por una sola razón, medida con la capacidad viva y no supuesta.</b> La pieza
/// (<c>ClienteDelArbolDeServicios</c>) repite una petición repetible —método seguro o
/// <c>Idempotency-Key</c>— cuando la capacidad dice <c>transient: true</c>, porque eso es lo
/// que la bandera promete. <c>Api.Payments</c> al AUTORIZAR no lo cumple: registra el intento
/// fallido, CIERRA la llave con él, y contesta 503 <c>transient: true</c>. El reintento con la
/// misma llave recibe entonces el intento guardado —201 con estado <c>Failed</c>—, y el cliente
/// lee «el banco dijo que no» donde lo que pasó es que la pasarela no estaba. Es la mentira cara
/// que <see cref="RechazoDelArbolDeServicios"/> existe para no decir.</para>
///
/// <para><b>El veto es del cliente y no de la pieza</b> porque la pieza no puede saberlo: la
/// bandera dice una cosa y la llave otra, y sólo quien conoce la operación sabe cuál manda. El
/// arreglo de fondo es de la capacidad —o deja la llave abierta ante un rechazo pasajero, o no lo
/// marca pasajero—, y es una decisión de plata (con una pasarela que tardó, un segundo intento
/// con la llave abierta podría autorizar dos veces), así que no se toma desde acá.</para>
/// </remarks>
public static class PeticionAlArbol
{
    private static readonly HttpRequestOptionsKey<bool> Vetada = new("synergos.arbol.no-se-repite");

    /// <summary>
    /// La pieza no repite esta petición, aunque lleve llave y la capacidad diga que es pasajero.
    /// </summary>
    public static HttpRequestMessage NoSeRepite(this HttpRequestMessage peticion)
    {
        ArgumentNullException.ThrowIfNull(peticion);
        peticion.Options.Set(Vetada, true);
        return peticion;
    }

    /// <summary>Si el cliente vetó el reintento de esta petición.</summary>
    public static bool EstaVetada(HttpRequestMessage peticion)
        => peticion.Options.TryGetValue(Vetada, out var vetada) && vetada;
}
