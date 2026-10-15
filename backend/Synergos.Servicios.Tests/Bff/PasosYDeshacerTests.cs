using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Los pasos de agenda y el deshacer común (núcleo N1), contra puertos falsos: las llaves que mandan, el
/// rechazo que devuelven, y cómo se deshace cada tipo.
/// </summary>
public sealed class PasosYDeshacerTests
{
    private static readonly Ref Paciente = Ref.Create("prueba.paciente", "u-1");
    private static readonly TimeWindow Hora = TimeWindow.Of(
        new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero));

    /// <summary>La saga más larga que admite el borde: lo que cuelgue de ella tiene que caber en una llave.</summary>
    private static readonly string SagaLarga = new string('s', LlaveDeSaga.MaxLength - 4) + "#100";

    private sealed class Agenda : IBookingPort, IDeshacerAgenda, IDeshacerCobros, IDeshacerExistencias, IDeshacerPedidos
    {
        public List<string> Llamadas { get; } = [];
        public Rejection? Rechaza { get; set; }
        public Money Devolvible { get; set; } = Money.Of(1000m, "COP");

        public Task<Result<string>> HallarAsync(Ref sujeto, CancellationToken ct) => Hacer("rs-1", $"hallar {sujeto}");
        public Task<Result<string>> ApartarAsync(string recurso, TimeWindow ventana, Ref paraQuien, IdempotencyKey llave, CancellationToken ct)
            => Hacer("bh-1", $"apartar {llave.Value}");
        public Task<Result<string>> ConfirmarAsync(string apartado, IdempotencyKey llave, CancellationToken ct) => Hacer("rv-1", $"confirmar {llave.Value}");
        public Task<Result<string>> LiberarAsync(string apartado, CancellationToken ct) => Hacer(apartado, $"liberar {apartado}");
        public Task<Result<string>> CancelarAsync(string reserva, CancellationToken ct) => Hacer(reserva, $"cancelar {reserva}");
        public Task<Result<string>> AnularAsync(string cobro, CancellationToken ct) => Hacer(cobro, $"anular {cobro}");
        public Task<Result<Money>> DevolvibleAsync(string cobro, CancellationToken ct) => Task.FromResult(Result.Ok(Devolvible));
        public Task<Result<string>> DevolverAsync(string cobro, Money monto, string motivo, IdempotencyKey llave, CancellationToken ct)
            => Hacer(cobro, $"devolver {cobro} {monto} {llave.Value}");
        public Task<Result<string>> SoltarAsync(string apartado, CancellationToken ct) => Hacer(apartado, $"soltar {apartado}");
        public Task<Result<string>> ReponerAsync(string pozo, int cantidad, IdempotencyKey llave, CancellationToken ct)
            => Hacer(pozo, $"reponer {pozo} {cantidad}");
        Task<Result<string>> IDeshacerPedidos.CancelarAsync(string pedido, CancellationToken ct) => Hacer(pedido, $"anular-pedido {pedido}");

        private Task<Result<string>> Hacer(string valor, string llamada)
        {
            Llamadas.Add(llamada);
            return Task.FromResult(Rechaza is { } r ? Result.Rejected<string>(r) : Result.Ok(valor));
        }
    }

    private static EntradaDePaso Entrada(string tipo, string[] lee, string? llave, string? llaveBase, FlowContext ctx, string sagaId = "cita-1")
        => new(sagaId, Ref.Create("prueba.cita", sagaId),
            new PasoDef("p", tipo, lee, ["x"], llave, llaveBase, null, null, null), ctx, null, null);

    private static FlowContext Ctx() => new FlowContext().Set("recurso", "rs-1").Set("ventana", Hora).Set("paciente", Paciente).Set("apartado", "bh-1");

    [Fact]
    public async Task Apartar_llama_con_la_llave_FIJA_de_la_cita_y_reserva_lo_que_aparta()
    {
        var agenda = new Agenda();

        var salida = await new PasoAgendaApartar(agenda).EjecutarAsync(
            Entrada("booking.apartar", ["recurso", "ventana", "paciente"], "hold", null, Ctx()), CancellationToken.None);

        Assert.Equal("apartar cita-1|hold", Assert.Single(agenda.Llamadas));
        Assert.Equal(new Reservado("bh-1"), salida.Reservado);
        Assert.Equal(new object?[] { "bh-1" }, salida.Valores);
    }

    [Fact]
    public async Task Apartar_por_item_usa_el_recurso_y_la_ventana_y_cabe_en_la_llave_con_la_saga_mas_larga()
    {
        var agenda = new Agenda();
        var paso = new PasoAgendaApartarPorItem(agenda);

        await paso.EjecutarAsync(Entrada("booking.apartar-por-item", ["recurso", "ventana", "paciente"], null, "hold", Ctx(), SagaLarga), CancellationToken.None);
        var otraHora = Ctx().Set("ventana", TimeWindow.Of(Hora.End, Hora.End.AddMinutes(30)));
        await paso.EjecutarAsync(Entrada("booking.apartar-por-item", ["recurso", "ventana", "paciente"], null, "hold", otraHora, SagaLarga), CancellationToken.None);

        Assert.Equal(2, agenda.Llamadas.Distinct().Count());
        Assert.All(agenda.Llamadas, l => Assert.True(l.Length - "apartar ".Length <= IdempotencyKey.MaxLength, l));
        Assert.Equal($"apartar {SagaLarga}|hold:{PasosDeAgenda.Discriminante("rs-1", Hora)}", agenda.Llamadas[0]);
        Assert.Equal(32, PasosDeAgenda.Discriminante("rs-1", Hora).Length);
    }

    [Fact]
    public async Task Confirmar_cierra_y_produce_la_reserva_sobre_la_que_se_cancela_despues()
    {
        var agenda = new Agenda();

        var salida = await new PasoAgendaConfirmar(agenda).EjecutarAsync(
            Entrada("booking.confirmar", ["apartado"], "confirm", null, Ctx()), CancellationToken.None);

        Assert.Equal("confirmar cita-1|confirm", Assert.Single(agenda.Llamadas));
        Assert.Equal("rv-1", salida.Cerrado);
        Assert.Equal(new object?[] { "rv-1" }, salida.Valores);
    }

    [Fact]
    public async Task Un_no_de_la_agenda_vuelve_tal_cual()
    {
        var agenda = new Agenda { Rechaza = Rejection.Conflict("booking.slot_taken", "guionado") };

        var salida = await new PasoAgendaApartar(agenda).EjecutarAsync(
            Entrada("booking.apartar", ["recurso", "ventana", "paciente"], "hold", null, Ctx()), CancellationToken.None);

        Assert.Equal(PasoResultado.Abortar, salida.Control);
        Assert.Equal("booking.slot_taken", salida.Rechazo?.Code);
    }

    // ── El deshacer común ───────────────────────────────────────────────────

    private static SagaDeFlujo Saga(params Compensation[] compensaciones) => new(
        "cita-1", "prueba.cita", Paciente, SagaStatus.Compensating,
        new Dictionary<string, ValorDeFlujo>(),
        new Dictionary<string, IReadOnlyList<ReservaDeFlujo>>
        {
            ["apartar"] = [new ReservaDeFlujo("ih-1", "po-1", 2), new ReservaDeFlujo("ih-2", "po-1", 5)],
        },
        compensaciones, null, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Reponer_devuelve_la_cantidad_de_SU_reserva_aunque_dos_cierren_en_el_mismo_pozo()
    {
        var agenda = new Agenda();
        var deshacer = new DeshacerComun(agenda, agenda, agenda, agenda, []);
        var primera = new Compensation("c1", TiposDeDeshacer.ReponerExistencias, "po-1", "m");
        var segunda = new Compensation("c2", TiposDeDeshacer.ReponerExistencias, "po-1", "m");
        var saga = Saga(primera, segunda);

        Assert.Null(await deshacer.UndoAsync(saga, segunda, CancellationToken.None));
        Assert.Null(await deshacer.UndoAsync(saga, primera, CancellationToken.None));

        Assert.Equal(new[] { "reponer po-1 5", "reponer po-1 2" }, agenda.Llamadas);
    }

    [Fact]
    public async Task Devolver_lo_ya_devuelto_se_da_por_hecho_y_lo_que_queda_va_con_llave_determinista()
    {
        var agenda = new Agenda { Devolvible = Money.Zero("COP") };
        var deshacer = new DeshacerComun(agenda, agenda, agenda, agenda, []);
        var devolver = new Compensation("c9", TiposDeDeshacer.DevolverCobro, "pg-1", "m");

        Assert.Null(await deshacer.UndoAsync(Saga(devolver), devolver, CancellationToken.None));
        Assert.Empty(agenda.Llamadas);

        agenda.Devolvible = Money.Of(700m, "COP");
        Assert.Null(await deshacer.UndoAsync(Saga(devolver), devolver, CancellationToken.None));
        Assert.Equal("devolver pg-1 700 COP cita-1|refund:c9", Assert.Single(agenda.Llamadas));
    }

    [Fact]
    public async Task Cada_tipo_estandar_va_a_su_puerto_y_uno_desconocido_se_rechaza_sin_reintento()
    {
        var agenda = new Agenda();
        var deshacer = new DeshacerComun(agenda, agenda, agenda, agenda, []);

        foreach (var (tipo, objetivo) in new[]
                 {
                     (TiposDeDeshacer.LiberarAgenda, "bh-1"), (TiposDeDeshacer.CancelarReserva, "rv-1"),
                     (TiposDeDeshacer.AnularCobro, "pg-1"), (TiposDeDeshacer.SoltarExistencias, "ih-1"), (TiposDeDeshacer.CancelarPedido, "pd-1"),
                 })
        {
            var c = new Compensation("c", tipo, objetivo, "m");
            Assert.Null(await deshacer.UndoAsync(Saga(c), c, CancellationToken.None));
        }

        Assert.Equal(new[] { "liberar bh-1", "cancelar rv-1", "anular pg-1", "soltar ih-1", "anular-pedido pd-1" }, agenda.Llamadas);
        var rara = new Compensation("c", "booking.soltar", "x", "m");
        var r = await deshacer.UndoAsync(Saga(rara), rara, CancellationToken.None);
        Assert.Equal(DeshacerComun.Desconocida, r?.Code);
        Assert.False(r!.IsTransient);
        Assert.Equal(7, deshacer.Tipos.Count);
    }
}
