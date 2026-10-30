using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Pasos;

// ── Los pasos de N2: permiso, canasta, pedido y despacho ─────────────────────
// Lo que la compra de la tienda y la cita de salud escribían en su *Flow.cs alrededor de cuatro
// capacidades: Consent, Cart, Orders y Fulfillment. Ninguno sabe qué se compra ni para quién; lo que
// cada uno lee y escribe lo declara la definición.

/// <summary>
/// <c>consent.verificar</c>: lee el sujeto y comprueba que tiene un permiso vigente para el <c>proposito</c>
/// que declara la definición. No escribe nada: si no hay permiso, el rechazo de la capacidad tal cual.
/// </summary>
/// <remarks>
/// <b>El propósito es un dato del flujo</b> (<c>salud.agenda</c>), como la plantilla del aviso, y el
/// validador lo cruza en los dos sentidos (NN-3 de Salud). Va primero: si no hay permiso no se toca nada.
/// </remarks>
public sealed class PasoConsentimientoVerificar(IConsentPort permisos) : IPaso
{
    public string Tipo => "consent.verificar";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public bool UsaProposito => true;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var permiso = await permisos.VerificarAsync(entrada.Lee<Ref>(0), entrada.Paso.Proposito!, ct);
        return permiso.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(permiso.Rejection!);
    }
}

/// <summary>
/// <c>cart.leer</c>: lee la canasta y quién compra, y escribe sus líneas (sujeto y cantidad), lo que se cotiza
/// y se aparta.
/// </summary>
/// <remarks>
/// <para><b>La canasta es la única fuente de qué se compra</b>: copiar las líneas en la petición dejaría que
/// el cliente pidiera algo distinto de lo que tiene en pantalla.</para>
///
/// <para><b>Una canasta de otro no existe para éste</b> (<c>cart.cart_not_found</c>, como un dueño ajeno en la
/// fachada): quien compra la nombra la puerta, y comprar la canasta de otro adivinando su id es justo lo que
/// esto cierra. Una cerrada, vencida o vacía se rechaza con el código que daría la capacidad al cerrarla,
/// antes de apartarle nada a nadie.</para>
/// </remarks>
public sealed class PasoCanastaLeer(ICartPort canastas) : IPaso
{
    public string Tipo => "cart.leer";
    public int Lecturas => 2;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(IReadOnlyList<(Ref Sujeto, int Cantidad)>)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var id = entrada.Lee<string>(0);
        var leida = await canastas.LeerAsync(id, ct);
        if (!leida.IsOk) return SalidaDePaso.Rechaza(leida.Rejection!);

        var canasta = leida.Value;
        if (canasta.Dueno != entrada.Lee<Ref>(1))
        {
            return SalidaDePaso.Rechaza(Rejection.NotFound("cart.cart_not_found", $"No existe la canasta {id}."));
        }
        if (canasta.Cerrada)
        {
            return SalidaDePaso.Rechaza(Rejection.Conflict("cart.already_checked_out", "La canasta ya se convirtió en pedido."));
        }
        if (!canasta.Abierta)
        {
            return SalidaDePaso.Rechaza(Rejection.Conflict("cart.expired", "La canasta venció."));
        }
        if (canasta.Lineas.Count == 0)
        {
            return SalidaDePaso.Rechaza(Rejection.Conflict("cart.empty_cart", "Una canasta vacía no se puede comprar."));
        }

        return SalidaDePaso.Sigue(canasta.Lineas);
    }
}

/// <summary><c>cart.cerrar</c>: cierra la canasta. Sin llave: la capacidad no la lee, y cerrarla otra vez se rechaza.</summary>
/// <remarks>
/// Va de último, y su definición suele declararlo con <c>al_fallar: seguir</c>: una canasta que quedó abierta
/// después de cobrar y despachar es un residuo que su vigencia barre, no una razón para deshacer un envío.
/// </remarks>
public sealed class PasoCanastaCerrar(ICartPort canastas) : IPaso
{
    public string Tipo => "cart.cerrar";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public bool ActuaSinLlave => true;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var cerrada = await canastas.CerrarAsync(entrada.Lee<string>(0), ct);
        return cerrada.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(cerrada.Rejection!);
    }
}

/// <summary>
/// <c>orders.crear</c>: lee quién compra, las líneas con su precio unitario y el total; escribe el pedido.
/// RESERVA: si la compra no se confirma, el pedido se anula (<c>antes</c>).
/// </summary>
/// <remarks>
/// Se registra ANTES de cobrar para que el cobro tenga a qué referirse: un movimiento de plata sin pedido al
/// lado no se puede conciliar. Las líneas llevan el precio que cotizó Pricing, porque el pedido congela el de
/// cada renglón (#122).
/// </remarks>
public sealed class PasoPedidoCrear(IOrdersPort pedidos) : IPaso
{
    public string Tipo => "orders.crear";
    public int Lecturas => 3;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var pedido = await pedidos.CrearAsync(
            entrada.Lee<Ref>(0), entrada.Lee<IReadOnlyList<LineaDePedido>>(1), entrada.Lee<Money>(2), entrada.Llave(), ct);
        return pedido.IsOk ? SalidaDePaso.Reserva(new Reservado(pedido.Value), pedido.Value) : SalidaDePaso.Rechaza(pedido.Rejection!);
    }
}

/// <summary><c>orders.cumplir</c>: da el pedido por cumplido. CIERRA la reserva de <c>orders.crear</c>.</summary>
/// <remarks>
/// <b>Va DESPUÉS de despachar</b>: cumplirlo le quita a <c>Api.Orders</c> la posibilidad de anularlo, así que
/// antes de despachar dejaría la compensación del pedido condenada a fallar. Su definición suele declararlo con
/// <c>al_fallar: seguir</c> (N-3 de Tienda): con la plata cobrada y el envío hecho, un pedido sin cumplir se
/// concilia, no se anula.
/// </remarks>
public sealed class PasoPedidoCumplir(IOrdersPort pedidos) : IPaso
{
    public string Tipo => "orders.cumplir";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public bool ActuaSinLlave => true;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var cumplido = await pedidos.CumplirAsync(entrada.Lee<string>(0), ct);
        return cumplido.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(cumplido.Rejection!);
    }
}

/// <summary>
/// <c>fulfillment.despachar</c>: lee el pedido, la dirección y la transportadora (estas dos, efímeras) y
/// escribe el envío.
/// </summary>
/// <remarks>
/// <para><b>El «para qué» del envío es el pedido</b>, con el vocabulario de la capacidad que lo creó:
/// <c>orders.pedido:{id}</c> (N-2 de Tienda). La tienda imperativa mandaba <c>tienda.pedido</c>; un vertical
/// que necesite su propio vocabulario lo pone en un paso de dominio.</para>
///
/// <para>Es el último paso que puede fallar con todo compensable detrás: si falla, la compra se deshace entera
/// (se devuelve lo cobrado y se repone lo consumido).</para>
/// </remarks>
public sealed class PasoDespachar(IFulfillmentPort envios) : IPaso
{
    /// <summary>El vocabulario del «para qué»: un pedido de <c>Api.Orders</c>.</summary>
    public const string PedidoKind = "orders.pedido";

    public string Tipo => "fulfillment.despachar";
    public int Lecturas => 3;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var envio = await envios.DespacharAsync(
            Ref.Create(PedidoKind, entrada.Lee<string>(0)), entrada.Lee<DireccionDeEnvio>(1), entrada.Lee<string>(2), entrada.Llave(), ct);
        return envio.IsOk ? SalidaDePaso.Sigue(envio.Value) : SalidaDePaso.Rechaza(envio.Rejection!);
    }
}

/// <summary>
/// <c>fulfillment.seguir</c> (núcleo, NS-1 de 013): lleva el envío al <c>estado</c> que declara la definición («en camino»,
/// «entregado»), con una nota si la definición la nombra, y escribe el estado en que quedó.
/// </summary>
/// <remarks>
/// <para><b>El estado es un dato del paso</b>, como la transición de un proceso (009-P3): una constante cruzada al arrancar
/// con los estados de la capacidad, sin expresiones. Enviar y entregar son dos definiciones con el mismo paso.</para>
///
/// <para><b>Sin llave y repetible.</b> La capacidad no lee la llave: su idempotencia es el estado. Que el envío ya esté en
/// ese estado (<c>fulfillment.already_…</c>) cuenta como hecho, así repetir la misma operación no choca (la lección de T052).
/// Otro rechazo —un estado al que no se llega desde el actual, un envío ya cerrado— sube tal cual.</para>
///
/// <para><b>Sin vuelta</b>: un envío no retrocede, así que va al final, detrás de lo que se deshace.</para>
/// </remarks>
public sealed class PasoSeguirEnvio(IFulfillmentPort envios) : IPaso
{
    /// <summary>Los estados de un envío en <c>Api.Fulfillment</c>; una prueba los cruza con los de la capacidad.</summary>
    public static readonly IReadOnlyCollection<string> EstadosDelEnvio = ["Ready", "InTransit", "Delivered", "Failed", "Returned"];

    public string Tipo => "fulfillment.seguir";
    public int Lecturas => 2;
    public int LecturasOpcionales => 1;
    public int Escrituras => 1;
    public int EscriturasOpcionales => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public bool ActuaSinLlave => true;
    public bool SinVuelta => true;
    public IReadOnlyCollection<string>? EstadosAdmitidos => EstadosDelEnvio;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var estado = entrada.Paso.Estado!;
        var nota = entrada.Paso.Lee.Count > 1 ? entrada.Lee<string?>(1) : null;
        var hecho = await envios.SeguirAsync(entrada.Lee<string>(0), estado, nota, ct);
        if (hecho.IsOk) return SalidaDePaso.Sigue(hecho.Value);
        return string.Equals(hecho.Rejection!.Code, YaEstaba(estado), StringComparison.Ordinal)
            ? SalidaDePaso.Sigue(estado)
            : SalidaDePaso.Rechaza(hecho.Rejection!);
    }

    /// <summary>El código con que la capacidad dice que el envío ya estaba en <paramref name="estado"/>.</summary>
    public static string YaEstaba(string estado) => $"fulfillment.already_{estado.ToLowerInvariant()}";
}

/// <summary>
/// <c>fulfillment.leer</c> (núcleo, NS-2 de 013): lee el envío y escribe, en este orden, su destino, la transportadora, el
/// estado y sus eventos. Una definición nombra los primeros que necesite.
/// </summary>
/// <remarks>No escribe en ninguna capacidad: cabe en una lectura sin saga, como el <c>detalle</c> que pide el personal.</remarks>
public sealed class PasoLeerEnvio(IFulfillmentPort envios) : IPaso
{
    public string Tipo => "fulfillment.leer";
    public int Lecturas => 1;
    public int Escrituras => 4;
    public int EscriturasOpcionales => 3;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura =>
        [typeof(DireccionDeEnvio), typeof(string), typeof(string), typeof(IReadOnlyList<EventoDeEnvio>)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var leido = await envios.LeerAsync(entrada.Lee<string>(0), ct);
        return leido.IsOk
            ? SalidaDePaso.Sigue(leido.Value.Destino, leido.Value.Transportadora, leido.Value.Estado, leido.Value.Eventos)
            : SalidaDePaso.Rechaza(leido.Rejection!);
    }
}
