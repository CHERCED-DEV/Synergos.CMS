using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Viajes.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// Lo que se GUARDA de un viaje de <c>/v1/trips</c> no cambia porque el orquestador sume el flujo declarado
/// (línea 006, T002).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta y el oráculo no alcanza.</b> <c>TripCompensationTests</c> guarda sus sagas en un
/// diccionario: nunca serializa. Una propiedad pública nueva en <see cref="TripSaga"/> saldría en el fichero de
/// cada viaje y las tres suites seguirían en verde. Lo que se rompe es la vuelta atrás: un binario anterior que
/// lee un viaje guardado por el nuevo, y el CMS, que lee lo que <c>/v1/trips</c> devuelve de esa saga.</para>
///
/// <para><b>La cadena de abajo se capturó ANTES de tocar el orquestador</b> (master <c>72d90518</c>), contra el
/// almacén de verdad (<see cref="FileSystemSagaStore{TSaga}"/>), y no se reescribe para que pase: si cambia,
/// cambió el formato en disco y eso se decide aparte.</para>
/// </remarks>
public sealed class TripSagaPersistidaTests
{
    private const string Capturado =
        """{"id":"viaje-1","traveller":{"kind":"viajes.viajero","id":"u-1"},"status":1,"holds":[{"holdId":"h-1","resourceId":"rec-vuelo","window":{"start":"2026-09-01T10:00:00+00:00","end":"2026-09-01T12:00:00+00:00","duration":"02:00:00"},"productRef":"vuelo/AV-8020","productLabel":"Bogot\u00E1 \u2192 Cartagena","reservationId":"res-1","unfulfilled":false},{"holdId":"h-2","resourceId":"rec-hotel","window":{"start":"2026-09-01T15:00:00+00:00","end":"2026-09-03T11:00:00+00:00","duration":"1.20:00:00"},"productRef":"hotel/caribe/DBL","productLabel":"Hotel Caribe \u2014 doble","reservationId":"res-2","unfulfilled":false},{"holdId":"h-3","resourceId":"rec-auto","window":{"start":"2026-09-01T12:00:00+00:00","end":"2026-09-03T10:00:00+00:00","duration":"1.22:00:00"},"productRef":"auto/economico/CTG","productLabel":"Auto econ\u00F3mico","reservationId":null,"unfulfilled":true}],"paymentId":"pg1","total":{"amount":1200000,"currency":"COP","isZero":false,"isNegative":false},"retained":{"amount":200000,"currency":"COP","isZero":false,"isNegative":false},"compensations":[{"id":"c1","kind":"CancelReservation","targetId":"res-1","reason":"viaje no confirmado","attempts":0,"nextAttemptUtc":null,"lastError":null,"doneAtUtc":"2026-08-05T10:05:00+00:00","isPending":false,"isStuck":false},{"id":"c2","kind":"ReleaseBookingHold","targetId":"h-3","reason":"viaje no confirmado","attempts":2,"nextAttemptUtc":"2026-08-05T10:04:00+00:00","lastError":"Unavailable/booking.unavailable: ca\u00EDda","doneAtUtc":null,"isPending":true,"isStuck":false},{"id":"c3","kind":"RefundPayment","targetId":"pg1","reason":"viaje no confirmado","attempts":0,"nextAttemptUtc":null,"lastError":null,"doneAtUtc":"2026-08-05T10:05:00+00:00","isPending":false,"isStuck":false}],"lastError":"Conflict/booking.hold_expired: guionado","startedAtUtc":"2026-08-05T10:00:00+00:00","alertedAtUtc":"2026-08-05T10:06:00+00:00","alertsSent":1,"partialConfirm":true,"refunded":{"amount":300000,"currency":"COP","isZero":false,"isNegative":false}}""";

    private static readonly DateTimeOffset Inicio = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    private static TripSaga Poblada() => new(
        "viaje-1",
        Ref.Create("viajes.viajero", "u-1"),
        SagaStatus.Completed,
        new[]
        {
            new ItemHold("h-1", "rec-vuelo", TimeWindow.Of(Inicio.AddDays(27), Inicio.AddDays(27).AddHours(2)),
                "vuelo/AV-8020", "Bogotá → Cartagena", ReservationId: "res-1"),
            new ItemHold("h-2", "rec-hotel", TimeWindow.Of(Inicio.AddDays(27).AddHours(5), Inicio.AddDays(29).AddHours(1)),
                "hotel/caribe/DBL", "Hotel Caribe — doble", ReservationId: "res-2"),
            new ItemHold("h-3", "rec-auto", TimeWindow.Of(Inicio.AddDays(27).AddHours(2), Inicio.AddDays(29)),
                "auto/economico/CTG", "Auto económico", Unfulfilled: true),
        },
        "pg1",
        Money.Of(1_200_000m, "COP"),
        Money.Of(200_000m, "COP"),
        new[]
        {
            new Compensation("c1", ViajesCompensations.CancelReservation, "res-1", "viaje no confirmado",
                DoneAtUtc: Inicio.AddMinutes(5)),
            new Compensation("c2", ViajesCompensations.ReleaseBookingHold, "h-3", "viaje no confirmado",
                Attempts: 2, NextAttemptUtc: Inicio.AddMinutes(4), LastError: "Unavailable/booking.unavailable: caída"),
            new Compensation("c3", ViajesCompensations.RefundPayment, "pg1", "viaje no confirmado",
                DoneAtUtc: Inicio.AddMinutes(5)),
        },
        "Conflict/booking.hold_expired: guionado",
        Inicio,
        Inicio.AddMinutes(6),
        1,
        PartialConfirm: true,
        Refunded: Money.Of(300_000m, "COP"));

    [Fact]
    public void El_fichero_de_un_viaje_es_el_MISMO_que_antes_del_flujo_declarado()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "viaje-json-" + Guid.NewGuid().ToString("N"));
        try
        {
            new FileSystemSagaStore<TripSaga>(Options.Create(new SagaStorageOptions { Root = raiz })).Put(Poblada());

            var fichero = Directory.EnumerateFiles(Path.Combine(raiz, "sagas"), "*.json").Single();

            Assert.Equal(Capturado, File.ReadAllText(fichero));
        }
        finally
        {
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }

    [Fact]
    public void Un_viaje_guardado_se_lee_de_vuelta_con_sus_items_y_su_reserva()
    {
        // ProductRef, ProductLabel, la reserva y «no cumplido» no los lee ningún test del oráculo, y son el
        // contrato hacia el CMS: lo que la respuesta del viaje le dice a quien vendió que se reservó.
        var raiz = Path.Combine(Path.GetTempPath(), "viaje-json-" + Guid.NewGuid().ToString("N"));
        try
        {
            var opciones = Options.Create(new SagaStorageOptions { Root = raiz });
            new FileSystemSagaStore<TripSaga>(opciones).Put(Poblada());

            var leida = new FileSystemSagaStore<TripSaga>(opciones).Find("viaje-1")!;

            Assert.Equal(3, leida.Holds.Count);
            Assert.Equal(("vuelo/AV-8020", "res-1"), (leida.Holds[0].ProductRef, leida.Holds[0].ReservationId));
            Assert.True(leida.Holds[2].Unfulfilled);
            Assert.Null(leida.Holds[2].ReservationId);
            Assert.Equal(200_000m, leida.Retained.Amount);
            Assert.Equal(300_000m, leida.Refunded!.Value.Amount);
            Assert.True(leida.PartialConfirm);
            Assert.Equal(ViajesCompensations.ReleaseBookingHold, leida.Compensations[1].Kind);
            Assert.True(leida.Compensations[1].IsPending);
        }
        finally
        {
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }
}
