using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Las salidas propias de la renta declarada (línea 005, T011): N unidades idénticas no comparten llave (NN-3) y
/// la garantía se cierra sin capturarse (NN-4).
/// </summary>
public sealed class SalidasPropiasDeAlquilerTests
{
    private static readonly DateTimeOffset Retiro = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Semana = TimeWindow.Of(Retiro, Retiro.AddDays(7));
    private static readonly Ref Inquilino = Ref.Create("alquiler.arrendatario", "m-1");

    private static readonly PasoDef Apartar = new(
        "apartar", "alquiler.apartar-unidad", ["recursoId", "ventana", "inquilino", "u.unidad"], ["u.apartadoId"],
        null, "hold", null, null, null);

    private static readonly PasoDef Conservar = new(
        "conservar", "alquiler.conservar-garantia", ["garantiaId"], [], null, null, null, "retener", null);

    /// <summary>Una agenda que anota con qué llave le apartaron y devuelve un apartado por llave.</summary>
    private sealed class AgendaQueAnota : IBookingPort
    {
        public List<string> Llaves { get; } = [];
        public string? Rechaza { get; init; }

        public Task<Result<string>> HallarAsync(Ref sujeto, CancellationToken ct) => Task.FromResult(Result.Ok("rs-1"));

        public Task<Result<string>> ApartarAsync(string recurso, TimeWindow ventana, Ref paraQuien, IdempotencyKey llave, CancellationToken ct)
        {
            Llaves.Add(llave.Value);
            return Task.FromResult(Rechaza is { } codigo
                ? Result.Rejected<string>(Rejection.Conflict(codigo, "guionado"))
                : Result.Ok($"bh-{Llaves.Count}"));
        }

        public Task<Result<string>> ConfirmarAsync(string apartado, IdempotencyKey llave, CancellationToken ct)
            => Task.FromResult(Result.Ok("rv-1"));
    }

    private static FlowContext Contexto() => new FlowContext()
        .Set("recursoId", "rs-1").Set("ventana", Semana).Set("inquilino", Inquilino).Set("garantiaId", "pg-garantia");

    private static Task<SalidaDePaso> ApartarUnidad(IBookingPort agenda, int unidad)
        => new PasoApartarUnidad(agenda).EjecutarAsync(new EntradaDePaso(
            "alq-1", Ref.Create("alquiler.renta", "alq-1"), Apartar, Contexto(), "u", new FlowContext().Set("unidad", unidad)),
            CancellationToken.None);

    [Fact]
    public async Task Dos_unidades_del_mismo_equipo_en_las_mismas_fechas_apartan_con_llaves_distintas()
    {
        var agenda = new AgendaQueAnota();

        var primera = await ApartarUnidad(agenda, 1);
        var segunda = await ApartarUnidad(agenda, 2);

        Assert.Equal(["alq-1|hold:1", "alq-1|hold:2"], agenda.Llaves);
        Assert.Equal("bh-1", primera.Reservado!.Id);
        Assert.Equal("bh-2", segunda.Reservado!.Id);
        Assert.Equal("bh-2", segunda.Valores[0]);
    }

    [Fact]
    public async Task Un_no_de_la_agenda_vuelve_tal_cual_y_no_reserva_nada()
    {
        var r = await ApartarUnidad(new AgendaQueAnota { Rechaza = "booking.at_capacity" }, 1);

        Assert.Equal(PasoResultado.Abortar, r.Control);
        Assert.Equal("booking.at_capacity", r.Rechazo!.Code);
        Assert.Null(r.Reservado);
    }

    [Fact]
    public async Task Conservar_la_garantia_sigue_sin_tocar_ninguna_capacidad()
    {
        var r = await new PasoConservarGarantia().EjecutarAsync(new EntradaDePaso(
            "alq-1", Ref.Create("alquiler.renta", "alq-1"), Conservar, Contexto(), null, null), CancellationToken.None);

        Assert.Equal(PasoResultado.Continuar, r.Control);
        Assert.Empty(r.Valores);
        Assert.Null(r.Reservado);
    }

    [Fact]
    public void Las_salidas_declaran_lo_que_el_flujo_les_pide()
    {
        var pasos = AlquilerSalidasPropias.Pasos(new AgendaQueAnota()).ToDictionary(p => p.Tipo);

        Assert.Equal(LlaveRequerida.PorItem, pasos["alquiler.apartar-unidad"].Llave);
        Assert.Equal(4, pasos["alquiler.apartar-unidad"].Lecturas);
        Assert.Equal(LlaveRequerida.Ninguna, pasos["alquiler.conservar-garantia"].Llave);
        Assert.Equal(1, pasos["alquiler.conservar-garantia"].Lecturas);
    }
}
