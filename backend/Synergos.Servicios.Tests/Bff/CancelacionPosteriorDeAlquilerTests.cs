using System.Net;
using System.Text.Json;
using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Cancelar lo confirmado como operación posterior al cierre (línea 005, T044; NN-8, aclaración D-005-NN8):
/// <c>alquiler.cancelacion</c> contra Booking, Pricing y Payments reales.
/// </summary>
/// <remarks>
/// Dos andamios por siete días: 420 000 de alquiler y 2 × 400 000 de garantía. Dos unidades a propósito: con una sola,
/// cancelar «la primera reserva» y cancelar «todas» darían lo mismo.
/// </remarks>
public sealed class CancelacionPosteriorDeAlquilerTests
{
    private const decimal Alquiler = 420_000m;

    private static async Task<(RentaDeAlquilerReal Renta, string Id)> Rentado(
        decimal garantia = 400_000m, Func<HttpRequestMessage, string?>? falla = null)
    {
        var r = new RentaDeAlquilerReal(falla);
        Assert.Equal(HttpStatusCode.OK, (await r.PublicarOferta(garantia: garantia)).Estado);
        var (estado, cuerpo) = await r.Rentar(pedido: RentaDeAlquilerReal.Pedido(unidades: 2));
        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        return (r, cuerpo.GetProperty("id").GetString()!);
    }

    private static string? Dato(RentaDeAlquilerReal r, string id, string nombre)
        => r.Motor.Find(id)!.Datos.GetValueOrDefault(nombre)?.Valor is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static async Task<JsonElement> Cobro(RentaDeAlquilerReal r, string id) => await r.Leer("payments", $"v1/payments/{id}");

    private static async Task<decimal> Devolvible(RentaDeAlquilerReal r, string cobro)
        => (await Cobro(r, cobro)).GetProperty("refundable").GetProperty("amount").GetDecimal();

    private static async Task<List<string>> EstadosDeLasReservas(RentaDeAlquilerReal r)
        => (await r.Lista("booking", $"v1/reservations?resourceId={await r.Recurso()}"))
            .Select(x => x.GetProperty("status").GetString()!).ToList();

    private static string Codigo(JsonElement problema) => problema.GetProperty("code").GetString()!;

    private static int Cancelaciones(RentaDeAlquilerReal r)
        => r.Motor.DeDueno(Ref.Create("alquiler.arrendatario", "u-1")).Count(s => s.Flujo == "alquiler.cancelacion");

    [Fact]
    public async Task La_noche_anterior_suelta_las_dos_unidades_devuelve_lo_cobrado_y_anula_la_garantia()
    {
        var (r, id) = await Rentado();
        using var _ = r;
        var cobro = Dato(r, id, "cobroId")!;
        var garantia = Dato(r, id, "garantiaId")!;

        // 23:30 en Bogotá del día anterior: en UTC ya es el día de inicio.
        r.EnBogota(RentaDeAlquilerReal.DiaDeInicio().AddDays(-1), 23, 30);
        var (estado, cuerpo) = await r.CancelarPorElFlujo(id);

        Assert.True(estado == HttpStatusCode.Created, cuerpo.ToString());
        Assert.Equal("Completed", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(Alquiler, cuerpo.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        Assert.Equal(["Cancelled", "Cancelled"], await EstadosDeLasReservas(r));
        Assert.Equal(0, await r.Tomadas());
        Assert.Equal(0m, await Devolvible(r, cobro));
        Assert.Equal(1, r.Cable.Veces("POST", $"^/v1/payments/{cobro}/refund$"));
        Assert.Equal("Voided", (await Cobro(r, garantia)).GetProperty("status").GetString());
        Assert.Equal(SagaStatus.Completed, r.Motor.Find(id)!.Status);
    }

    [Fact]
    public async Task Sin_garantia_cancela_igual_y_no_pide_anular_nada()
    {
        var (r, id) = await Rentado(garantia: 0m);
        using var _ = r;
        Assert.Null(Dato(r, id, "garantiaId"));
        var cobro = Dato(r, id, "cobroId")!;

        var (estado, cuerpo) = await r.CancelarPorElFlujo(id);

        Assert.True(estado == HttpStatusCode.Created, cuerpo.ToString());
        Assert.Equal(0, r.Cable.Veces("POST", "^/v1/payments/[^/]*/void$"));
        Assert.Equal(0m, await Devolvible(r, cobro));
        Assert.Equal(["Cancelled", "Cancelled"], await EstadosDeLasReservas(r));
    }

    [Fact]
    public async Task Desde_el_dia_de_inicio_se_rechaza_sin_mover_nada_ni_escribir_la_cancelacion()
    {
        var (r, id) = await Rentado();
        using var _ = r;
        var garantia = Dato(r, id, "garantiaId")!;
        r.EnBogota(RentaDeAlquilerReal.DiaDeInicio(), 0, 10);
        var antes = r.Cable.Llamadas.Count;

        var (estado, cuerpo) = await r.CancelarPorElFlujo(id);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("alquiler.already_started", Codigo(cuerpo));
        Assert.Empty(r.Cable.Llamadas.Skip(antes));
        Assert.Equal(0, Cancelaciones(r));
        Assert.Equal("Authorized", (await Cobro(r, garantia)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Con_la_devolucion_declarada_lo_resuelve_el_personal_y_a_un_ajeno_no_se_le_encuentra()
    {
        var (r, id) = await Rentado();
        using var _ = r;
        var antes = r.Cable.Llamadas.Count;

        var (ajeno, noEsSuyo) = await r.CancelarPorElFlujo(id, sujeto: RentaDeAlquilerReal.Sujeto("u-2"));
        Assert.Equal(HttpStatusCode.NotFound, ajeno);
        Assert.Equal("flow.original_no_encontrada", Codigo(noEsSuyo));

        Assert.Equal(HttpStatusCode.OK, (await r.Declarar(id)).Estado);
        var (declarada, rechazo) = await r.CancelarPorElFlujo(id);
        Assert.Equal(HttpStatusCode.Conflict, declarada);
        Assert.Equal("alquiler.not_cancellable", Codigo(rechazo));
        Assert.Empty(r.Cable.Llamadas.Skip(antes));
    }

    [Fact]
    public async Task Repetida_devuelve_la_misma_una_vez_y_la_liquidacion_la_ve_cancelada()
    {
        var (r, id) = await Rentado();
        using var _ = r;
        var cobro = Dato(r, id, "cobroId")!;

        var (primera, una) = await r.CancelarPorElFlujo(id);
        var (segunda, otra) = await r.CancelarPorElFlujo(id);

        Assert.Equal(HttpStatusCode.Created, primera);
        Assert.Equal(HttpStatusCode.Created, segunda);
        Assert.Equal(una.GetProperty("id").GetString(), otra.GetProperty("id").GetString());
        Assert.Equal(1, r.Cable.Veces("POST", $"^/v1/payments/{cobro}/refund$"));

        var antes = r.Cable.Llamadas.Count;
        Assert.Equal("alquiler.not_declarable", Codigo((await r.Declarar(id)).Cuerpo));
        Assert.Equal("alquiler.not_returnable", Codigo((await r.Confirmar(id, 0m)).Cuerpo));
        var (personal, vista) = await r.CancelarConfirmado(id, porPersonal: true, penalidad: 50_000m);
        Assert.Equal(HttpStatusCode.OK, personal);
        Assert.Equal("cancelled", vista.GetProperty("state").GetString());
        Assert.Empty(r.Cable.Llamadas.Skip(antes));
    }

    [Fact]
    public async Task Si_la_devolucion_falla_a_medias_repetir_termina_sin_devolver_dos_veces()
    {
        var veces = 0;
        var (r, id) = await Rentado(falla: q => CableQueAnota.Es(q, "POST", "^/v1/payments/[^/]+/refund$")
            && Interlocked.Increment(ref veces) == 1 ? "payments.provider_unavailable" : null);
        using var _ = r;
        var cobro = Dato(r, id, "cobroId")!;
        var garantia = Dato(r, id, "garantiaId")!;

        var (fallida, rechazo) = await r.CancelarPorElFlujo(id);
        Assert.Equal(HttpStatusCode.Conflict, fallida);
        Assert.Equal("payments.provider_unavailable", Codigo(rechazo));
        Assert.Equal(0, Cancelaciones(r));
        Assert.Equal(["Cancelled", "Cancelled"], await EstadosDeLasReservas(r));

        var (estado, cuerpo) = await r.CancelarPorElFlujo(id);

        Assert.True(estado == HttpStatusCode.Created, cuerpo.ToString());
        Assert.Equal(0m, await Devolvible(r, cobro));
        Assert.Equal(Alquiler, (await Cobro(r, cobro)).GetProperty("amount").GetProperty("amount").GetDecimal());
        Assert.Equal("Voided", (await Cobro(r, garantia)).GetProperty("status").GetString());
        Assert.Equal(1, Cancelaciones(r));
    }

    [Fact]
    public async Task Una_renta_que_no_deja_ver_sus_reservas_no_se_da_por_cancelada()
    {
        var (r, id) = await Rentado();
        using var _ = r;
        var original = r.Motor.Find(id)!;
        r.Motor.Put(original.WithCompensations(original.Compensations.Where(c => c.Kind != "booking.cancelar").ToList()));
        var antes = r.Cable.Llamadas.Count;

        var (estado, cuerpo) = await r.CancelarPorElFlujo(id);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("alquiler.units_not_found", Codigo(cuerpo));
        Assert.Empty(r.Cable.Llamadas.Skip(antes));
        Assert.Equal(["Confirmed", "Confirmed"], await EstadosDeLasReservas(r));
    }
}
