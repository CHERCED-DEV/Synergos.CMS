using System.Net;
using System.Text.Json;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Viajes.Domain;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// Lo de quien vende sobre un viaje ya confirmado (línea 006, historias 4 y 5; lote 6, 001-P6 y 001-P8): devolver una
/// parte (<c>viajes.devolucion</c>) y cancelarlo con su penalidad (<c>viajes.cancelacion</c>) son flujos POSTERIORES del
/// personal, que leen la original con <c>flow.leer-original</c>, contra las capacidades reales.
/// </summary>
/// <remarks>
/// Lo que se mira es lo que las capacidades dicen haber hecho (lo devuelto en Payments, el estado de cada reserva en la
/// agenda), no lo que contestó el flujo; y la original no se toca: sigue completada, de quien viajó.
/// </remarks>
public sealed class PosterioresDelViajeTests
{
    private static string Id(JsonElement cuerpo) => cuerpo.GetProperty("id").GetString()!;

    /// <summary>El viaje de los tres ítems (1.200.000), abierto y cerrado por quien viaja.</summary>
    private static async Task<(string Id, string Cobro)> Completado(ViajesReservaReal r, string llave = "viaje-1")
    {
        var (_, abierto) = await r.Abrir(llave: llave);
        var id = Id(abierto);
        var (estado, cerrado) = await r.Cerrar(id);
        Assert.True(estado == HttpStatusCode.OK, cerrado.ToString());
        return (id, r.Dato(id, "cobroId"));
    }

    private static async Task<JsonElement> Pago(ViajesReservaReal r, string cobro) => await r.Leer("payments", $"v1/payments/{cobro}");

    private static decimal Devuelto(JsonElement pago) => pago.GetProperty("refunded").GetProperty("amount").GetDecimal();

    // ── viajes.devolucion (T026) ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_personal_devuelve_una_parte_de_un_viaje_completado_y_la_capacidad_dice_cuanto()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (viaje, cobro) = await Completado(r);

        var (estado, devolucion) = await r.Devolver(viaje, ViajesReservaReal.Pesos(200_000m), "devolucion-1");

        Assert.True(estado == HttpStatusCode.Created, devolucion.ToString());
        Assert.Equal("Completed", devolucion.GetProperty("estado").GetString());
        var pago = await Pago(r, cobro);
        Assert.Equal(200_000m, Devuelto(pago));
        Assert.Equal(1_000_000m, pago.GetProperty("refundable").GetProperty("amount").GetDecimal());

        // La original no se toca: sigue completada, con sus tres reservas.
        var (_, original) = await r.Consultar(viaje);
        Assert.Equal("Completed", original.GetProperty("estado").GetString());
        Assert.All(await r.Reservas(), x => Assert.Equal("Confirmed", x.Estado));
    }

    [Fact]
    public async Task Reintentar_la_misma_devolucion_no_devuelve_dos_veces()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (viaje, cobro) = await Completado(r);

        var (_, primera) = await r.Devolver(viaje, ViajesReservaReal.Pesos(200_000m), "devolucion-1");
        var (estado, otra) = await r.Devolver(viaje, ViajesReservaReal.Pesos(200_000m), "devolucion-1");

        Assert.True(estado is HttpStatusCode.OK or HttpStatusCode.Created, otra.ToString());
        Assert.Equal(Id(primera), Id(otra));
        var pago = await Pago(r, cobro);
        Assert.Equal(200_000m, Devuelto(pago));
        Assert.Equal(1, pago.GetProperty("refunds").GetArrayLength());
    }

    [Fact]
    public async Task Pasarse_de_lo_cobrado_es_el_rechazo_de_Payments_sin_devolver_nada()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (viaje, cobro) = await Completado(r);

        var (estado, rechazo) = await r.Devolver(viaje, ViajesReservaReal.Pesos(1_300_000m), "devolucion-1");

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal("payments.refund_over_captured", rechazo.GetProperty("code").GetString());
        Assert.Equal(0m, Devuelto(await Pago(r, cobro)));
    }

    [Fact]
    public async Task Un_viaje_no_completado_no_se_devuelve_deshacerlo_es_de_la_compensacion()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (_, abierto) = await r.Abrir();
        var enCurso = Id(abierto);

        var (estado1, r1) = await r.Devolver(enCurso, ViajesReservaReal.Pesos(100_000m), "devolucion-1");
        var (estado2, r2) = await r.Devolver("no-existe", ViajesReservaReal.Pesos(100_000m), "devolucion-2");

        Assert.Equal((HttpStatusCode.Conflict, PasoLeerOriginal.NoCompletada), (estado1, r1.GetProperty("code").GetString()));
        Assert.Equal((HttpStatusCode.NotFound, PasoLeerOriginal.NoEncontrada), (estado2, r2.GetProperty("code").GetString()));
        Assert.Equal("Authorized", (await Pago(r, r.Dato(enCurso, "cobroId"))).GetProperty("status").GetString());
    }

    // ── viajes.cancelacion (T027) ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_lo_confirmado_cancela_cada_reserva_y_devuelve_el_total_menos_la_penalidad()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (viaje, cobro) = await Completado(r);

        var (estado, cancelacion) = await r.CancelarConfirmado(viaje, ViajesReservaReal.Pesos(200_000m), "cancelacion-1");

        Assert.True(estado == HttpStatusCode.Created, cancelacion.ToString());
        Assert.Equal("Completed", cancelacion.GetProperty("estado").GetString());
        Assert.Equal(1_000_000m, cancelacion.GetProperty("datos").GetProperty("aDevolver").GetProperty("amount").GetDecimal());
        Assert.All(await r.Reservas(), x => Assert.Equal("Cancelled", x.Estado));
        Assert.Equal(3, (await r.Reservas()).Count);
        Assert.Equal(1_000_000m, Devuelto(await Pago(r, cobro)));
        var s = ViajesReservaReal.Salida;
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Hotel, s.AddHours(5), s.AddDays(2)));
    }

    [Fact]
    public async Task Una_penalidad_mayor_que_el_total_es_bad_penalty_sin_cancelar_ni_devolver_nada()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (viaje, cobro) = await Completado(r);

        var (estado, rechazo) = await r.CancelarConfirmado(viaje, ViajesReservaReal.Pesos(1_500_000m), "cancelacion-1");

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal(PasoMenosRetencion.PenalidadMala, rechazo.GetProperty("code").GetString());
        Assert.All(await r.Reservas(), x => Assert.Equal("Confirmed", x.Estado));
        Assert.Equal(0m, Devuelto(await Pago(r, cobro)));
    }

    [Fact]
    public async Task Con_la_penalidad_entera_cancela_las_reservas_y_no_pide_una_devolucion_de_cero()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (viaje, cobro) = await Completado(r);

        var (estado, cancelacion) = await r.CancelarConfirmado(viaje, ViajesReservaReal.Pesos(1_200_000m), "cancelacion-1");

        Assert.True(estado == HttpStatusCode.Created, cancelacion.ToString());
        Assert.All(await r.Reservas(), x => Assert.Equal("Cancelled", x.Estado));
        var pago = await Pago(r, cobro);
        Assert.Equal(0m, Devuelto(pago));
        Assert.Equal(0, pago.GetProperty("refunds").GetArrayLength());
    }

    [Fact]
    public async Task Un_viaje_gratis_se_cancela_sin_cobro_que_devolver()
    {
        using var r = await new ViajesReservaReal().ConProducto(ViajesReservaReal.Hotel, 0m);
        var noches = ViajesReservaReal.Item(ViajesReservaReal.Hotel, ViajesReservaReal.Salida, ViajesReservaReal.Salida.AddDays(1));
        var (_, abierto) = await r.Abrir(items: [noches]);
        var viaje = Id(abierto);
        Assert.Equal(HttpStatusCode.OK, (await r.Cerrar(viaje)).Estado);

        var (estado, cancelacion) = await r.CancelarConfirmado(viaje, ViajesReservaReal.Pesos(0m), "cancelacion-1");

        Assert.True(estado == HttpStatusCode.Created, cancelacion.ToString());
        Assert.Equal(["Cancelled"], (await r.Reservas()).Select(x => x.Estado).ToArray());
    }

    /// <summary>
    /// Si la devolución se cae al cancelar, NINGUNA saga se queda esperando (ADR 0140, la regla de forma): la cancelación
    /// no se completa y quien opera la reintenta con la MISMA llave; las reservas ya canceladas no estorban y la plata
    /// vuelve una sola vez. Es lo que en <c>/v1/trips</c> hacía la compensación pendiente del barrido.
    /// </summary>
    [Fact]
    public async Task Si_la_devolucion_se_cae_reintentar_con_la_misma_llave_devuelve_una_sola_vez()
    {
        var caida = true;
        using var r = await new ViajesReservaReal(falla: m =>
            caida && m.Method == HttpMethod.Post && m.RequestUri!.AbsolutePath.EndsWith("/refund", StringComparison.Ordinal)
                ? "payments.unreachable"
                : null).ConLosTres();
        var (viaje, cobro) = await Completado(r);

        var (primero, _) = await r.CancelarConfirmado(viaje, ViajesReservaReal.Pesos(200_000m), "cancelacion-1");
        Assert.NotEqual(HttpStatusCode.Created, primero);
        Assert.Equal(0m, Devuelto(await Pago(r, cobro)));

        caida = false;
        var (segundo, cancelacion) = await r.CancelarConfirmado(viaje, ViajesReservaReal.Pesos(200_000m), "cancelacion-1");

        Assert.True(segundo is HttpStatusCode.OK or HttpStatusCode.Created, cancelacion.ToString());
        Assert.Equal("Completed", cancelacion.GetProperty("estado").GetString());
        Assert.All(await r.Reservas(), x => Assert.Equal("Cancelled", x.Estado));
        var pago = await Pago(r, cobro);
        Assert.Equal(1_000_000m, Devuelto(pago));
        Assert.Equal(1, pago.GetProperty("refunds").GetArrayLength());
    }

    /// <summary>
    /// Primero se cancela cada reserva y DESPUÉS se devuelve: si la agenda no deja cancelar una, no se devolvió nada. Al
    /// revés, la plata volvería con una reserva todavía confirmada.
    /// </summary>
    [Fact]
    public async Task Si_la_agenda_no_deja_cancelar_una_reserva_no_se_devuelve_nada()
    {
        var cancelaciones = 0;
        using var r = await new ViajesReservaReal(falla: m =>
            m.Method == HttpMethod.Post && m.RequestUri!.AbsolutePath.StartsWith("/v1/reservations/", StringComparison.Ordinal)
            && m.RequestUri.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal)
            && Interlocked.Increment(ref cancelaciones) == 2
                ? "booking.not_cancellable"
                : null).ConLosTres();
        var (viaje, cobro) = await Completado(r);

        var (estado, rechazo) = await r.CancelarConfirmado(viaje, ViajesReservaReal.Pesos(200_000m), "cancelacion-1");

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal("booking.not_cancellable", rechazo.GetProperty("code").GetString());
        Assert.Equal(0m, Devuelto(await Pago(r, cobro)));
    }
}
