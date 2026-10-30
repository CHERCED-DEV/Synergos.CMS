using Synergos.Bff.Core.Flow;
using Synergos.Bff.Tienda.Clients;
using Synergos.Core;

namespace Synergos.Bff.Tienda.Domain;

/// <summary>
/// El cliente de envíos de los pasos del despacho (013-seller-operaciones, interino de NS-1).
/// </summary>
/// <remarks>
/// Los pasos propios de un orquestador declarado se instancian ANTES de que exista el contenedor —se le pasan a
/// <c>AddOrquestadorDeclarado</c>—, y el cliente HTTP de la capacidad vive en él. Así que los pasos reciben esto, y
/// <c>Program</c> lo conecta al arrancar. Muere con NS-1: cuando seguir un envío sea un paso común sobre los puertos del
/// núcleo, no hará falta.
/// </remarks>
public sealed class EnviosDelDespacho
{
    private TiendaCapabilities? _capacidades;

    /// <summary>Lo conecta <c>Program</c> con el cliente del contenedor, después de construirlo.</summary>
    public void Conectar(TiendaCapabilities capacidades) => _capacidades = capacidades;

    /// <summary>Mueve el envío a <paramref name="estado"/>, con su nota.</summary>
    public Task<Result<ShipmentDto>> SeguirAsync(string envio, string estado, string? nota, CancellationToken ct)
        => (_capacidades ?? throw new InvalidOperationException("Los pasos del despacho no se conectaron al cliente de envíos al arrancar."))
            .TrackShipmentAsync(envio, estado, nota, ct);
}

/// <summary>
/// Los pasos interinos del despacho (013-seller-operaciones, R2; interinos de NS-1, <c>fulfillment.seguir</c>): mover el
/// envío de una compra en la capacidad de envíos y escribir el estado en que quedó.
/// </summary>
/// <remarks>
/// <para><b>Con NS-1 son un paso común</b>, con el estado como dato constante del flujo. Hasta que llegue, cada estado tiene
/// su paso, y sólo difieren en el estado y en si leen la guía.</para>
///
/// <para><b>«Ya estaba» es hecho.</b> Pedir el estado en que el envío ya está contesta 409
/// <c>fulfillment.already_&lt;estado&gt;</c>: es el reintento de una respuesta perdida, u otra persona del personal que dio la
/// misma orden, y no un error. Lo demás —una transición que no se permite, un envío cerrado, la capacidad caída— sube
/// con su código.</para>
///
/// <para><b>Sin llave, y escriben</b> (NR-8): seguir un envío no la lleva, porque su idempotencia es el estado.</para>
/// </remarks>
public abstract class PasoDelEnvio(EnviosDelDespacho envios) : IPaso
{
    public abstract string Tipo { get; }

    /// <summary>El estado de la capacidad al que lleva el envío.</summary>
    public abstract string Estado { get; }

    public abstract int Lecturas { get; }
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public bool ActuaSinLlave => true;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    /// <summary>La nota del evento: la guía, si el paso la lee.</summary>
    protected virtual string? Nota(EntradaDePaso entrada) => null;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var movido = await envios.SeguirAsync(entrada.Lee<string>(0), Estado, Nota(entrada), ct).ConfigureAwait(false);
        if (movido.IsOk) return SalidaDePaso.Sigue(movido.Value.Status);
        return movido.Rejection!.Code == $"fulfillment.already_{Estado.ToLowerInvariant()}"
            ? SalidaDePaso.Sigue(Estado)
            : SalidaDePaso.Rechaza(movido.Rejection);
    }
}

/// <summary><c>tienda.envio-en-camino</c>: lee el envío y la guía; lo deja en camino, con la guía como nota.</summary>
public sealed class PasoEnvioEnCamino(EnviosDelDespacho envios) : PasoDelEnvio(envios)
{
    public override string Tipo => "tienda.envio-en-camino";
    public override string Estado => "InTransit";
    public override int Lecturas => 2;
    protected override string? Nota(EntradaDePaso entrada) => entrada.Lee<string?>(1)?.Trim() is { Length: > 0 } guia ? guia : null;
}

/// <summary><c>tienda.envio-entregado</c>: lee el envío y lo da por entregado.</summary>
public sealed class PasoEnvioEntregado(EnviosDelDespacho envios) : PasoDelEnvio(envios)
{
    public override string Tipo => "tienda.envio-entregado";
    public override string Estado => "Delivered";
    public override int Lecturas => 1;
}

/// <summary>Los pasos del despacho, para sumarlos a los del orquestador.</summary>
public static class PasosDelDespacho
{
    public static IPaso[] Propios(EnviosDelDespacho envios) => [new PasoEnvioEnCamino(envios), new PasoEnvioEntregado(envios)];
}
