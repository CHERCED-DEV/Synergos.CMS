using System.Net;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La compra de entradas avisa al comprador como un paso del flujo, y un aviso que no sale NO devuelve
/// la plata (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>El orquestador de verdad</b> —su <c>Program</c>, su JSON, su endpoint— contra las cuatro
/// capacidades de verdad en el arnés (<see cref="CompraDeEventosReal"/>). Un doble que contesta 201 a
/// lo que sea no vería que un aviso rechazado compensa la saga, ni que el correo se cuela en el disco
/// o en el log.</para>
///
/// <para><b>Lo que se mira es lo que queda</b>: el cobro en Payments, la entrega en Notifications, el
/// <c>.eml</c> en la carpeta, el fichero de la saga y lo que el proceso escribió en su log.</para>
/// </remarks>
public sealed class TicketingAvisoTests
{
    private const string Correo = "ana.compradora@ejemplo.co";

    private static readonly string Ana = CompraDeEventosReal.Sujeto("m1");

    /// <summary>Abre y cierra una compra de Ana, sin comisión; devuelve la saga y cómo terminó.</summary>
    private static async Task<(string Id, HttpStatusCode Cierre, System.Text.Json.JsonElement Compra)> Comprar(
        CompraDeEventosReal compra, string? contacto)
    {
        var (abierta, cuerpo) = await compra.Abrir(Ana, CompraDeEventosReal.Negocio(0m));
        Assert.True(abierta == HttpStatusCode.Created, $"abrir: {(int)abierta} {cuerpo}");
        var id = cuerpo.GetProperty("id").GetString()!;

        var (cierre, cerrada) = await compra.Sobre(id, "confirm", Ana, contacto);
        return (id, cierre, cerrada);
    }

    [Fact]
    public async Task Con_el_transporte_que_rechaza_la_compra_se_completa_cobrada_y_sin_devolucion()
    {
        // El transporte por defecto de Api.Notifications rechaza (transport_not_configured, no
        // transitorio). Con el aviso como un paso más, eso deshacía la saga y devolvía la plata.
        using var compra = new CompraDeEventosReal(conRecogida: false);

        var (id, cierre, cerrada) = await Comprar(compra, CompraDeEventosReal.Contacto(Correo));

        Assert.Equal(HttpStatusCode.OK, cierre);
        Assert.Equal("Completed", cerrada.GetProperty("status").GetString());
        Assert.Equal(0, cerrada.GetProperty("pendingCompensations").GetInt32());

        var cobro = Assert.Single(await compra.Lista("payments", $"v1/payments?forKind=eventos.compra&forId={id}"));
        Assert.Equal("Captured", cobro.GetProperty("status").GetString());

        // Lo que la capacidad sí dejó: el intento, en Failed, con su motivo.
        var entrega = Assert.Single(await compra.Lista("notifications", "v1/deliveries?toKind=eventos.comprador&toId=m1"));
        Assert.Equal("Failed", entrega.GetProperty("status").GetString());

        // Y el log del orquestador nombra la saga y el código, sin la dirección.
        Assert.Contains(compra.Log.Lineas, l => l.Contains(id, StringComparison.Ordinal)
                                                && l.Contains("notifications.transport_not_configured", StringComparison.Ordinal));
        Assert.DoesNotContain(compra.Log.Lineas, l => l.Contains(Correo, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Correo, compra.SagaGuardada(id), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Con_la_recogida_el_aviso_le_llega_al_contacto_UNA_vez_y_no_queda_en_la_saga()
    {
        using var compra = new CompraDeEventosReal(conRecogida: true);

        var (id, cierre, _) = await Comprar(compra, CompraDeEventosReal.Contacto(Correo));
        Assert.Equal(HttpStatusCode.OK, cierre);

        // Confirmar otra vez una compra completada no vuelve a correr pasos: no hay segundo aviso.
        var (repetida, _) = await compra.Sobre(id, "confirm", Ana, CompraDeEventosReal.Contacto(Correo));
        Assert.Equal(HttpStatusCode.OK, repetida);

        var entrega = Assert.Single(await compra.Lista("notifications", "v1/deliveries?toKind=eventos.comprador&toId=m1"));
        Assert.Equal(Correo, entrega.GetProperty("address").GetString());
        Assert.Equal("Accepted", entrega.GetProperty("status").GetString());
        var eml = File.ReadAllText(Assert.Single(Directory.GetFiles(compra.Buzon, "*.eml")));
        Assert.Contains(Correo, eml, StringComparison.Ordinal);

        Assert.DoesNotContain(Correo, compra.SagaGuardada(id), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ana María", compra.SagaGuardada(id), StringComparison.Ordinal);
        Assert.DoesNotContain(compra.Log.Lineas, l => l.Contains(Correo, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Cuenta cuántas veces el orquestador le pide el cliente a cada capacidad: una por llamada.</summary>
    private sealed class QueCuenta(IHttpClientFactory interno) : IHttpClientFactory
    {
        private readonly Dictionary<string, int> _pedidos = new(StringComparer.Ordinal);

        public int Pedidos(string capacidad) { lock (_pedidos) return _pedidos.GetValueOrDefault(capacidad); }

        public HttpClient CreateClient(string name)
        {
            lock (_pedidos) _pedidos[name] = _pedidos.GetValueOrDefault(name) + 1;
            return interno.CreateClient(name);
        }
    }

    [Fact]
    public async Task Una_compra_gratis_se_completa_sin_una_sola_llamada_a_Payments()
    {
        // Por el orquestador, un cobro de cero daba 400 payments.zero_amount: omitir_si_cero.
        QueCuenta? cuenta = null;
        using var compra = new CompraDeEventosReal(precio: 0m, envolver: f => cuenta = new QueCuenta(f));

        var (id, cierre, cerrada) = await Comprar(compra, contacto: null);

        Assert.Equal(HttpStatusCode.OK, cierre);
        Assert.Equal("Completed", cerrada.GetProperty("status").GetString());
        Assert.Equal(0m, cerrada.GetProperty("total").GetProperty("amount").GetDecimal());
        Assert.Equal(0, cuenta!.Pedidos("payments"));
        Assert.Empty(await compra.Lista("payments", $"v1/payments?forKind=eventos.compra&forId={id}"));
    }

    [Fact]
    public async Task Un_contacto_ilegible_se_rechaza_antes_de_mover_plata()
    {
        using var compra = new CompraDeEventosReal();

        var (id, cierre, rechazo) = await Comprar(compra, contacto: "%%no-es-base64url%%");

        Assert.Equal(HttpStatusCode.BadRequest, cierre);
        Assert.Equal("eventos.contacto_invalido", rechazo.GetProperty("code").GetString());
        var cobro = Assert.Single(await compra.Lista("payments", $"v1/payments?forKind=eventos.compra&forId={id}"));
        Assert.Equal("Authorized", cobro.GetProperty("status").GetString());
    }
}
