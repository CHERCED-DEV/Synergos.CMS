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
    private static readonly HttpRequestOptionsKey<bool> PorLaSaga = new("synergos.arbol.repetible-por-la-saga");

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

    /// <summary>
    /// La pieza puede repetir esta escritura aunque no lleve <c>Idempotency-Key</c>: la operación es idempotente por la
    /// saga del orquestador, que saca de ella sus propias llaves (núcleo, NP-4 de 005, D-005-NP4).
    /// </summary>
    /// <remarks>
    /// <para><b>Es lo simétrico de <see cref="NoSeRepite"/></b>, y por la misma razón es del cliente: sólo quien conoce la
    /// operación sabe que repetirla devuelve lo mismo. Devolver un equipo alquilado o cancelar una renta corren sobre la
    /// saga, y una segunda llamada encuentra hecho lo que hizo la primera.</para>
    ///
    /// <para><b>Sin esto, el cliente mandaba una llave que nadie lee</b> sólo para que la pieza se atreviera a repetir, y
    /// el contrato del orquestador —honesto— no la declara. El veto sigue mandando: una petición marcada también con
    /// <see cref="NoSeRepite"/> no se repite.</para>
    /// </remarks>
    public static HttpRequestMessage RepetiblePorLaSaga(this HttpRequestMessage peticion)
    {
        ArgumentNullException.ThrowIfNull(peticion);
        peticion.Options.Set(PorLaSaga, true);
        return peticion;
    }

    /// <summary>Si el cliente marcó esta petición como repetible por la saga.</summary>
    public static bool EsRepetiblePorLaSaga(HttpRequestMessage peticion)
        => peticion.Options.TryGetValue(PorLaSaga, out var marcada) && marcada;
}
