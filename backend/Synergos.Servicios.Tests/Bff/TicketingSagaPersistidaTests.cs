using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Lo que se GUARDA de una compra de entradas no cambia porque su flujo pase a ser un dato (ADR 0140).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta y el oráculo no alcanza.</b> <c>TicketingCompensationTests</c> guarda
/// sus sagas en un diccionario: nunca serializa. Una propiedad pública nueva en
/// <see cref="TicketingSaga"/> —la forma obvia de darle al intérprete lo que lee— saldría en el
/// fichero de cada compra, y las tres suites seguirían en verde. Lo que se rompe es la vuelta
/// atrás: un binario anterior que lee una saga guardada por el nuevo.</para>
///
/// <para><b>La cadena de abajo se capturó ANTES de tocar el record</b>, contra el almacén de
/// verdad (<see cref="FileSystemSagaStore{TSaga}"/>), y no se reescribe para que pase: si cambia,
/// cambió el formato en disco y eso se decide aparte.</para>
/// </remarks>
public sealed class TicketingSagaPersistidaTests
{
    private const string Capturado =
        """{"id":"compra-1","buyer":{"kind":"eventos.comprador","id":"u-1"},"eventId":"e1","status":0,"holds":[{"holdId":"ah-1","itemId":"po-a14","quantity":1,"tier":"vip","seat":"A-14"},{"holdId":"ah-3","itemId":"po-gen","quantity":3,"tier":"general","seat":null}],"paymentId":"pg1","total":{"amount":400000,"currency":"COP","isZero":false,"isNegative":false},"compensations":[{"id":"c1","kind":"ReleaseSeatHold","targetId":"ah-1","reason":"compra no confirmada","attempts":0,"nextAttemptUtc":null,"lastError":null,"doneAtUtc":null,"isPending":true,"isStuck":false},{"id":"c2","kind":"RestockSeats","targetId":"po-gen","reason":"compra no confirmada","attempts":2,"nextAttemptUtc":"2026-08-05T10:04:00+00:00","lastError":"Unavailable/inventory.unreachable: ca\u00EDda","doneAtUtc":null,"isPending":true,"isStuck":false},{"id":"c3","kind":"VoidPayment","targetId":"pg1","reason":"compra no confirmada","attempts":0,"nextAttemptUtc":null,"lastError":null,"doneAtUtc":"2026-08-05T10:05:00+00:00","isPending":false,"isStuck":false}],"lastError":"Conflict/payments.declined: guionado","startedAtUtc":"2026-08-05T10:00:00+00:00","alertedAtUtc":"2026-08-05T10:06:00+00:00","alertsSent":1}""";

    private static TicketingSaga Poblada() => new(
        "compra-1",
        Ref.Create("eventos.comprador", "u-1"),
        "e1",
        SagaStatus.Running,
        new[]
        {
            new SeatHold("ah-1", "po-a14", 1, "vip", "A-14"),
            new SeatHold("ah-3", "po-gen", 3, "general", null),
        },
        "pg1",
        Money.Of(400_000m, "COP"),
        new[]
        {
            new Compensation("c1", EventosCompensations.ReleaseSeatHold, "ah-1", "compra no confirmada"),
            new Compensation("c2", EventosCompensations.RestockSeats, "po-gen", "compra no confirmada",
                Attempts: 2, NextAttemptUtc: new DateTimeOffset(2026, 8, 5, 10, 4, 0, TimeSpan.Zero),
                LastError: "Unavailable/inventory.unreachable: caída"),
            new Compensation("c3", EventosCompensations.VoidPayment, "pg1", "compra no confirmada",
                DoneAtUtc: new DateTimeOffset(2026, 8, 5, 10, 5, 0, TimeSpan.Zero)),
        },
        "Conflict/payments.declined: guionado",
        new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 5, 10, 6, 0, TimeSpan.Zero),
        1);

    [Fact]
    public void El_fichero_de_una_compra_es_el_MISMO_que_antes_del_flujo_declarado()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "saga-json-" + Guid.NewGuid().ToString("N"));
        try
        {
            new FileSystemSagaStore<TicketingSaga>(Options.Create(new SagaStorageOptions { Root = raiz }))
                .Put(Poblada());

            var fichero = Directory.EnumerateFiles(Path.Combine(raiz, "sagas"), "*.json").Single();

            Assert.Equal(Capturado, File.ReadAllText(fichero));
        }
        finally
        {
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }

    [Fact]
    public void Una_compra_guardada_se_lee_de_vuelta_con_su_localidad_y_su_butaca()
    {
        // Tier y Seat no los lee ningún test del oráculo, y son el contrato hacia el CMS: lo que
        // la respuesta de la compra le dice al comprador que apartó.
        var raiz = Path.Combine(Path.GetTempPath(), "saga-json-" + Guid.NewGuid().ToString("N"));
        try
        {
            var opciones = Options.Create(new SagaStorageOptions { Root = raiz });
            new FileSystemSagaStore<TicketingSaga>(opciones).Put(Poblada());

            var leida = new FileSystemSagaStore<TicketingSaga>(opciones).Find("compra-1")!;

            Assert.Equal(2, leida.Holds.Count);
            Assert.Equal(("vip", "A-14"), (leida.Holds[0].Tier, leida.Holds[0].Seat));
            Assert.Null(leida.Holds[1].Seat);
            Assert.Equal("pg1", leida.PaymentId);
            Assert.Equal(3, leida.Compensations.Count);
            Assert.Equal(EventosCompensations.RestockSeats, leida.Compensations[1].Kind);
        }
        finally
        {
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }
}
