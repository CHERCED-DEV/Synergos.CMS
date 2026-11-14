using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Salud.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Cancelar una cita YA confirmada (003-salud-cita, I6, la ficha F4): el flujo posterior <c>salud.cancelacion</c> contra
/// Booking y Payments reales en el arnés de la cita. La agenda se cancela primero; la devolución, según la antelación y la
/// política del sitio que manda la puerta.
/// </summary>
public sealed class CancelacionDeLaCitaTests
{
    private sealed class Reloj : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    /// <summary>Agenda y confirma una cita de <paramref name="sujeto"/> con el reloj de hoy; devuelve su id, su reserva y su cobro.</summary>
    private static async Task<(string Id, string Reserva, string? Cobro)> Confirmada(CitaDeSaludReal c, string sujeto = "u-1", string llave = "cita-1")
    {
        var (_, abierta) = await c.Abrir(llave, sujeto);
        var id = abierta.GetProperty("id").GetString()!;
        var (cerro, cerrada) = await c.Cerrar(id, sujeto);
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        var datos = c.Servicios.GetRequiredService<Synergos.Bff.Core.SagaEngine<Synergos.Bff.Core.Flow.SagaDeFlujo>>().Find(id)!.Datos;
        var cobro = datos.TryGetValue("cobroId", out var g) && g.Valor is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
        return (id, cerrada.GetProperty("datos").GetProperty("reservaId").GetString()!, cobro);
    }

    private static async Task<string> EstadoDeLaReserva(CitaDeSaludReal c, string reserva)
        => (await c.Leer("booking", $"v1/reservations/{reserva}")).GetProperty("status").GetString()!;

    private static async Task<decimal> Devuelto(CitaDeSaludReal c, string cobro)
        => (await c.Leer("payments", $"v1/payments/{cobro}")).GetProperty("refunded").GetProperty("amount").GetDecimal();

    [Theory]
    [InlineData(48, 24, 2, 50, 50_000)]
    [InlineData(5, 24, 2, 50, 25_000)]
    [InlineData(1, 24, 2, 50, 0)]
    [InlineData(48, 72, 2, 80, 40_000)]
    public async Task Cancelar_cancela_la_reserva_y_devuelve_segun_la_antelacion_y_la_politica_del_sitio(
        int horasAntes, int totalHoras, int parcialHoras, int porcentaje, int esperado)
    {
        var reloj = new Reloj();
        using var c = new CitaDeSaludReal(reloj: reloj);
        var (id, reserva, cobro) = await Confirmada(c);

        reloj.Ahora = CitaDeSaludReal.Inicio.AddHours(-horasAntes);
        var (estado, cancelacion) = await c.CancelarConfirmada(id, totalHoras: totalHoras, parcialHoras: parcialHoras, porcentaje: porcentaje);

        Assert.True(estado == HttpStatusCode.Created, cancelacion.ToString());
        Assert.Equal("Completed", cancelacion.GetProperty("estado").GetString());
        Assert.Equal(id, cancelacion.GetProperty("datos").GetProperty("cita").GetString());
        var aDevolver = cancelacion.GetProperty("datos").GetProperty("aDevolver");
        Assert.Equal(esperado, aDevolver.ValueKind == JsonValueKind.Null ? 0m : aDevolver.GetProperty("amount").GetDecimal());
        Assert.Equal("Cancelled", await EstadoDeLaReserva(c, reserva));
        Assert.Equal(esperado, await Devuelto(c, cobro!));
        if (esperado == 0) Assert.DoesNotContain(c.Llamadas, l => l.EndsWith("/refund", StringComparison.Ordinal));
    }

    [Fact]
    public async Task La_agenda_se_cancela_antes_de_devolver_la_plata()
    {
        var reloj = new Reloj();
        using var c = new CitaDeSaludReal(reloj: reloj);
        var (id, _, _) = await Confirmada(c);
        reloj.Ahora = CitaDeSaludReal.Inicio.AddDays(-2);

        await c.CancelarConfirmada(id);

        var llamadas = c.Llamadas.ToList();
        Assert.True(llamadas.FindIndex(l => l.StartsWith("POST /v1/reservations/", StringComparison.Ordinal) && l.EndsWith("/cancel", StringComparison.Ordinal))
            < llamadas.FindIndex(l => l.EndsWith("/refund", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Una_cita_que_ya_empezo_no_se_cancela_ni_se_toca_nada()
    {
        var reloj = new Reloj();
        using var c = new CitaDeSaludReal(reloj: reloj);
        var (id, reserva, cobro) = await Confirmada(c);
        reloj.Ahora = CitaDeSaludReal.Inicio.AddMinutes(1);

        var (estado, cuerpo) = await c.CancelarConfirmada(id);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal(PasoDevolucionPorAntelacion.YaEmpezo, cuerpo.GetProperty("code").GetString());
        Assert.Equal("Confirmed", await EstadoDeLaReserva(c, reserva));
        Assert.Equal(0m, await Devuelto(c, cobro!));
    }

    [Fact]
    public async Task La_cita_de_otro_paciente_no_existe_para_el()
    {
        var reloj = new Reloj();
        using var c = new CitaDeSaludReal(reloj: reloj);
        var (id, reserva, cobro) = await Confirmada(c);
        reloj.Ahora = CitaDeSaludReal.Inicio.AddDays(-2);

        var (estado, cuerpo) = await c.CancelarConfirmada(id, sujeto: "u-2");

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("flow.original_no_encontrada", cuerpo.GetProperty("code").GetString());
        Assert.Equal("Confirmed", await EstadoDeLaReserva(c, reserva));
        Assert.Equal(0m, await Devuelto(c, cobro!));
    }

    [Fact]
    public async Task Repetir_con_la_misma_llave_no_devuelve_dos_veces_y_con_otra_la_agenda_dice_que_ya_estaba_cancelada()
    {
        var reloj = new Reloj();
        using var c = new CitaDeSaludReal(reloj: reloj);
        var (id, _, cobro) = await Confirmada(c);
        reloj.Ahora = CitaDeSaludReal.Inicio.AddDays(-2);

        var (_, primera) = await c.CancelarConfirmada(id);
        var (_, repetida) = await c.CancelarConfirmada(id);
        var (otraVez, otra) = await c.CancelarConfirmada(id, llave: "cancelar-2");

        Assert.Equal(primera.GetProperty("id").GetString(), repetida.GetProperty("id").GetString());
        Assert.NotEqual(HttpStatusCode.Created, otraVez);
        Assert.Equal("booking.already_cancelled", otra.GetProperty("code").GetString());
        Assert.Equal(50_000m, await Devuelto(c, cobro!));
    }

    [Fact]
    public async Task Si_la_devolucion_se_cae_tras_cancelar_el_reintento_con_la_misma_llave_devuelve_una_vez()
    {
        var reloj = new Reloj();
        var caidas = new List<string>();
        using var c = new CitaDeSaludReal(reloj: reloj, cae: caidas);
        var (id, reserva, cobro) = await Confirmada(c);
        reloj.Ahora = CitaDeSaludReal.Inicio.AddDays(-2);

        caidas.Add("POST /v1/payments/*/refund");
        var (cayo, _) = await c.CancelarConfirmada(id);
        Assert.NotEqual(HttpStatusCode.Created, cayo);
        Assert.Equal("Cancelled", await EstadoDeLaReserva(c, reserva));
        Assert.Equal(0m, await Devuelto(c, cobro!));

        caidas.Clear();
        var (estado, cancelacion) = await c.CancelarConfirmada(id);

        Assert.True(estado == HttpStatusCode.Created, cancelacion.ToString());
        Assert.Equal("Completed", cancelacion.GetProperty("estado").GetString());
        Assert.Equal(50_000m, await Devuelto(c, cobro!));
    }

    [Fact]
    public async Task Una_cita_sin_copago_se_cancela_sin_devolver_nada()
    {
        var reloj = new Reloj();
        using var c = new CitaDeSaludReal(precio: null, reloj: reloj);
        var (id, reserva, cobro) = await Confirmada(c);
        Assert.Null(cobro);
        reloj.Ahora = CitaDeSaludReal.Inicio.AddDays(-2);

        var (estado, cancelacion) = await c.CancelarConfirmada(id);

        Assert.True(estado == HttpStatusCode.Created, cancelacion.ToString());
        Assert.Equal(JsonValueKind.Null, cancelacion.GetProperty("datos").GetProperty("aDevolver").ValueKind);
        Assert.Equal("Cancelled", await EstadoDeLaReserva(c, reserva));
        Assert.DoesNotContain(c.Llamadas, l => l.EndsWith("/refund", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Una_politica_del_sitio_que_no_lo_es_rechaza_antes_de_cancelar()
    {
        var reloj = new Reloj();
        using var c = new CitaDeSaludReal(reloj: reloj);
        var (id, reserva, _) = await Confirmada(c);
        reloj.Ahora = CitaDeSaludReal.Inicio.AddDays(-2);

        var (estado, cuerpo) = await c.CancelarConfirmada(id, totalHoras: 2, parcialHoras: 24);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal(PasoDevolucionPorAntelacion.PoliticaInvalida, cuerpo.GetProperty("code").GetString());
        Assert.Equal("Confirmed", await EstadoDeLaReserva(c, reserva));
    }

    [Fact]
    public async Task Listar_da_las_cancelaciones_del_paciente_con_su_cita_y_lo_devuelto()
    {
        var reloj = new Reloj();
        using var c = new CitaDeSaludReal(reloj: reloj);
        var (id, _, _) = await Confirmada(c);
        reloj.Ahora = CitaDeSaludReal.Inicio.AddHours(-5);
        await c.CancelarConfirmada(id);

        var (_, suyas) = await c.ListarCancelaciones();
        var (_, ajenas) = await c.ListarCancelaciones("u-2");

        var item = Assert.Single(suyas.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("datos").GetProperty("cita").GetString());
        Assert.Equal(25_000m, item.GetProperty("datos").GetProperty("aDevolver").GetProperty("amount").GetDecimal());
        Assert.Empty(ajenas.GetProperty("items").EnumerateArray());
    }
}

/// <summary>La regla de la devolución, sola: los bordes de cada tramo y el redondeo hacia abajo.</summary>
public sealed class PoliticaDeDevolucionTests
{
    private static readonly DateTimeOffset Inicio = new(2027, 3, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly PoliticaDeDevolucion DeSiempre = new(24, 2, 50);

    [Theory]
    [InlineData(24 * 60, 50_000)]
    [InlineData(24 * 60 - 1, 25_000)]
    [InlineData(2 * 60, 25_000)]
    [InlineData(2 * 60 - 1, 0)]
    public void Cada_tramo_empieza_en_su_hora_exacta(int minutosAntes, int esperado)
    {
        var devuelto = DeSiempre.Devolucion(Money.Of(50_000m, "COP"), Inicio, Inicio.AddMinutes(-minutosAntes));
        Assert.Equal(esperado, devuelto?.Amount ?? 0m);
    }

    [Fact]
    public void La_parte_se_redondea_hacia_abajo_y_nunca_devuelve_de_mas()
    {
        var devuelto = new PoliticaDeDevolucion(24, 2, 33).Devolucion(Money.Of(50_001m, "COP"), Inicio, Inicio.AddHours(-5));
        Assert.Equal(Money.Of(16_500.33m, "COP"), devuelto);
    }

    [Theory]
    [InlineData(-1, 2, 50)]
    [InlineData(24, -1, 50)]
    [InlineData(2, 24, 50)]
    [InlineData(24, 2, 101)]
    [InlineData(24, 2, -1)]
    public void Una_politica_mal_hecha_dice_por_que(int total, int parcial, int porcentaje)
        => Assert.NotNull(new PoliticaDeDevolucion(total, parcial, porcentaje).Problema());
}
