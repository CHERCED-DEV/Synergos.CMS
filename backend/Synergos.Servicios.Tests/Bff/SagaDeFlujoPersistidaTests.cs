using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El fichero de una saga genérica tiene una forma fija (núcleo N1), como el de una compra de entradas
/// (<see cref="TicketingSagaPersistidaTests"/>).
/// </summary>
/// <remarks>
/// <para><b>Por qué se fija desde el primer día.</b> Lo que se guarda lo leen las sagas en vuelo y, en una
/// vuelta atrás, un binario anterior. Una propiedad pública nueva saldría en el fichero sin que ninguna
/// suite lo notara; aquí se nota.</para>
///
/// <para><b>Cada valor lleva su tipo</b>: leerlo no depende de que la definición siga diciendo lo mismo.
/// Y lo que se reservó lleva su cantidad, que es lo que necesita reponer existencias ya consumidas.</para>
/// </remarks>
public sealed class SagaDeFlujoPersistidaTests
{
    private const string Fijado =
        """{"id":"cita-1","flujo":"muestra.cita","dueno":{"kind":"muestra.paciente","id":"u-1"},"status":0,"datos":{"total":{"tipo":"dinero","valor":{"amount":50000,"currency":"COP"}},"cobroId":{"tipo":"texto","valor":"pg1"},"promocion":{"tipo":"texto?","valor":null}},"reservas":{"apartar":[{"holdId":"bh1","closeTargetId":"bh1","cantidad":null}],"autorizar":[{"holdId":"pg1","closeTargetId":"pg1","cantidad":null}],"existencias":[{"holdId":"ih1","closeTargetId":"po1","cantidad":3}]},"compensations":[{"id":"c1","kind":"booking.liberar","targetId":"bh1","reason":"cita no confirmada","attempts":0,"nextAttemptUtc":null,"lastError":null,"doneAtUtc":null,"isPending":true,"isStuck":false}],"lastError":null,"startedAtUtc":"2026-10-15T10:00:00+00:00","alertedAtUtc":null,"alertsSent":0}""";

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static SagaDeFlujo Poblada() => new(
        "cita-1",
        "muestra.cita",
        Ref.Create("muestra.paciente", "u-1"),
        SagaStatus.Running,
        new Dictionary<string, ValorDeFlujo>(StringComparer.Ordinal)
        {
            ["total"] = new("dinero", J("""{"amount":50000,"currency":"COP"}""")),
            ["cobroId"] = new("texto", J("\"pg1\"")),
            ["promocion"] = new("texto?", null),
        },
        new Dictionary<string, IReadOnlyList<ReservaDeFlujo>>(StringComparer.Ordinal)
        {
            ["apartar"] = new[] { new ReservaDeFlujo("bh1", "bh1") },
            ["autorizar"] = new[] { new ReservaDeFlujo("pg1", "pg1") },
            ["existencias"] = new[] { new ReservaDeFlujo("ih1", "po1", 3) },
        },
        new[] { new Compensation("c1", "booking.liberar", "bh1", "cita no confirmada") },
        null,
        new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero));

    private static string Raiz() => Path.Combine(Path.GetTempPath(), "saga-flujo-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void El_fichero_de_una_saga_generica_tiene_su_forma_fijada()
    {
        var raiz = Raiz();
        try
        {
            new FileSystemSagaStore<SagaDeFlujo>(Options.Create(new SagaStorageOptions { Root = raiz })).Put(Poblada());

            var fichero = Directory.EnumerateFiles(Path.Combine(raiz, "sagas"), "*.json").Single();

            Assert.Equal(Fijado, File.ReadAllText(fichero));
        }
        finally
        {
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }

    [Fact]
    public void Una_saga_generica_guardada_se_lee_de_vuelta_con_sus_valores_y_sus_reservas()
    {
        var raiz = Raiz();
        try
        {
            var opciones = Options.Create(new SagaStorageOptions { Root = raiz });
            new FileSystemSagaStore<SagaDeFlujo>(opciones).Put(Poblada());

            var leida = new FileSystemSagaStore<SagaDeFlujo>(opciones).Find("cita-1")!;

            Assert.Equal(Ref.Create("muestra.paciente", "u-1"), ((ISagaConDueno)leida).Dueno);
            Assert.Equal("pg1", leida.Datos["cobroId"].Valor!.Value.GetString());
            Assert.Null(leida.Datos["promocion"].Valor);
            var existencias = ((IHoldLedger)leida).Legs("existencias");
            Assert.Equal(new HoldLeg("ih1", "po1", 3), Assert.Single(existencias));
            var sinNada = leida with { Reservas = new Dictionary<string, IReadOnlyList<ReservaDeFlujo>> { ["x"] = [] } };
            Assert.Empty(((IHoldLedger)sinNada).Legs("x"));
            Assert.Throws<InvalidOperationException>(() => ((IHoldLedger)leida).Legs("cobrar"));
        }
        finally
        {
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }

    [Fact]
    public void Una_reserva_de_un_paso_que_la_saga_no_guarda_no_se_anota()
    {
        var saga = Poblada().ConReserva("apartar", new ReservaDeFlujo("bh2", "bh2"));

        Assert.Equal(2, saga.Reservas["apartar"].Count);
        Assert.Throws<InvalidOperationException>(() => saga.ConReserva("cobrar", new ReservaDeFlujo("x", "x")));
    }

    [Fact]
    public void El_numero_son_los_ultimos_ocho_alfanumericos_como_en_el_aviso()
    {
        Assert.Equal("CD12EF34", SagaDeFlujo.NumeroDe("ab-cd-12-ef-34"));
        Assert.Equal("ABC", SagaDeFlujo.NumeroDe("a-b-c"));
    }
}
