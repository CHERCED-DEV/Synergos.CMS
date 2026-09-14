using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El contrato de <c>&lt;synergos-ehr&gt;</c> contra <see cref="EhrController"/>, medido por donde
/// falla: <b>el CUERPO que manda la UI</b> y <b>la FORMA del JSON que el borde devuelve</b>.
/// </summary>
/// <remarks>
/// <para><b>Por qué estos tests y no otros.</b> <see cref="EhrController"/> no tenía ninguno —1.000
/// líneas, 18 endpoints— y los que hubieran sido naturales de escribir no habrían visto nada: un
/// test que construye <c>AddEncounterBody</c> a mano ejercita el record que el controller decidió
/// declarar, no el JSON que el navegador envía. Y ahí estaba todo el daño: <c>System.Text.Json</c>
/// <b>descarta en silencio</b> lo que no mapea, así que una clave con otro nombre no es un error
/// visible —es un campo vacío corriente abajo— y un tipo que no encaja es un <b>400 constante</b>
/// que el cliente de la UI tapa con su valor optimista. El paciente leía «Solicitada», el clínico
/// veía su nota en pantalla, y el servidor no había guardado nada.</para>
///
/// <para>Por eso cada caso de escritura <b>deserializa el JSON literal de la UI</b> —copiado de
/// <c>ehr-api.client.ts</c>— y cada caso de lectura <b>serializa la respuesta</b> y mira las claves.
/// Es lo único que cruza los dos árboles sin compartir código (ADR 0083: la UI es la fuente del
/// contrato).</para>
/// </remarks>
public sealed class EhrContractDriftTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly IPatientRegistry _patients = Substitute.For<IPatientRegistry>();
    private readonly IDoctorDirectory _doctors = Substitute.For<IDoctorDirectory>();
    private readonly IClinicalRecordService _records = Substitute.For<IClinicalRecordService>();
    private readonly IClinicalPrescriptionService _prescriptions = Substitute.For<IClinicalPrescriptionService>();
    private readonly IClinicalSchedulingService _scheduling = Substitute.For<IClinicalSchedulingService>();
    private readonly IClinicalResultsProvider _results = Substitute.For<IClinicalResultsProvider>();
    private readonly IClinicalMedicationService _medications = Substitute.For<IClinicalMedicationService>();
    private readonly IClinicalOrderService _orders = Substitute.For<IClinicalOrderService>();
    private readonly IClinicalBillingService _billing = Substitute.For<IClinicalBillingService>();
    private readonly IEhrInBasketService _inBasket = Substitute.For<IEhrInBasketService>();
    private readonly IMessagingService _messaging = Substitute.For<IMessagingService>();

    private EhrController BuildSut() => new(
        _patients, _doctors, _records, _prescriptions, _scheduling,
        _results, _medications, _orders, _billing, _inBasket, _messaging);

    private static T Bind<T>(string uiJson) => JsonSerializer.Deserialize<T>(uiJson, Web)!;

    private static EhrPatient Paciente(string id, string? tratante) => new(
        Id: id, FullName: "Jorge Medina", DocumentId: "CC 1", Gender: "M",
        DateOfBirth: new DateOnly(1970, 1, 1), AgeYears: 56, Phone: "", Email: "",
        City: "Bogotá", BloodType: "O+", Allergies: Array.Empty<string>(),
        ChronicConditions: Array.Empty<string>(), PrimaryDoctorId: tratante, AvatarUrl: null);

    // ══════════ ESCRITURA · la nota clínica ══════════

    /// <summary>
    /// El cuerpo literal de <c>saveEncounter()</c>: <c>objective</c> es el objeto de signos
    /// vitales, porque es lo que este mismo borde DEVUELVE en <c>SoapDto.Objective</c>.
    /// </summary>
    private const string EncounterJsonDeLaUi = """
    {
      "patientId": "pat-jorge-medina",
      "soap": {
        "subjective": "Refiere buena adherencia.",
        "objective": {
          "systolic": 128, "diastolic": 82, "heartRate": 74,
          "temperature": 36.6, "weight": 72, "height": 162, "glucose": 104
        },
        "assessment": "Hipertensión controlada.",
        "plan": "Continuar losartán 50 mg."
      }
    }
    """;

    [Fact]
    public void Encounter_ElCuerpoDeLaUi_SiquieraSeDeserializa()
    {
        // Con `objective` tipado como string esto lanzaba JsonException → 400 en TODA petición.
        var body = Bind<EhrController.AddEncounterBody>(EncounterJsonDeLaUi);

        Assert.Equal("pat-jorge-medina", body.PatientId);
        Assert.Equal("Hipertensión controlada.", body.Soap.Assessment);
    }

    [Fact]
    public async Task Encounter_LosVitalesQueMandaLaUi_LleganAlSeam()
    {
        var body = Bind<EhrController.AddEncounterBody>(EncounterJsonDeLaUi);
        AddEncounterRequest? capturada = null;
        _records.AddEncounterAsync(Arg.Any<AddEncounterRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturada = call.Arg<AddEncounterRequest>();
                return Task.FromResult(new ClinicalEncounter(
                    "enc-1", "pat-jorge-medina", "doc-1", "Dra. X", DateTime.UtcNow,
                    "Control", capturada!.Soap, null, true));
            });

        var result = await BuildSut().AddEncounter(body, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(capturada);
        // El punto entero: una nota guardada SIN los signos vitales que el clínico tecleó es
        // una nota clínica incompleta que nadie ve incompleta.
        Assert.NotNull(capturada!.Soap.Vitals);
        Assert.Equal(128, capturada.Soap.Vitals!.SystolicMmHg);
        Assert.Equal(82, capturada.Soap.Vitals.DiastolicMmHg);
        Assert.Equal(74, capturada.Soap.Vitals.HeartRateBpm);
        Assert.Equal(36.6, capturada.Soap.Vitals.TemperatureC);
        Assert.Equal(104, capturada.Soap.Vitals.GlucoseMgDl);
    }

    [Fact]
    public void Encounter_LaFormaLegacy_SigueValiendo()
    {
        // `objective` como texto + `vitals` aparte: el contrato con el que nació el borde.
        var body = Bind<EhrController.AddEncounterBody>("""
        {
          "patientId": "p1",
          "soap": {
            "subjective": "s", "objective": "TA 128/82", "assessment": "a", "plan": "p",
            "vitals": { "systolicMmHg": 128, "diastolicMmHg": 82 }
          }
        }
        """);

        Assert.Equal("TA 128/82", body.Soap.ObjectiveText());
        Assert.Equal(128, body.Soap.ResolveVitals()!.SystolicMmHg);
    }

    // ══════════ ESCRITURA · la receta ══════════

    [Fact]
    public async Task Prescription_DrugYDose_LleganAlSeam()
    {
        var body = Bind<EhrController.AddPrescriptionBody>("""
        {
          "patientId": "pat-jorge-medina",
          "items": [{ "drug": "Losartán", "dose": "50 mg", "frequency": "Cada 12 horas", "durationDays": 90 }]
        }
        """);
        AddPrescriptionRequest? capturada = null;
        _prescriptions.AddAsync(Arg.Any<AddPrescriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturada = call.Arg<AddPrescriptionRequest>();
                return Task.FromResult(new EhrPrescription(
                    "rx-1", "pat-jorge-medina", "doc-1", "Dra. X", DateTime.UtcNow,
                    capturada!.Items, "active"));
            });

        var result = await BuildSut().AddPrescription(body, default);

        Assert.IsType<OkObjectResult>(result);
        // Una receta guardada sin nombre de medicamento no falla: se guarda vacía.
        Assert.Equal("Losartán", capturada!.Items[0].MedicationName);
        Assert.Equal("50 mg", capturada.Items[0].Dosage);
    }

    // ══════════ ESCRITURA · el resurtido ══════════

    [Fact]
    public async Task Refill_ClavesDeLaUi_NoContesta400()
    {
        var body = Bind<EhrController.RefillBody>("""
        { "medicationId": "med-losartan", "patientId": "pat-jorge-medina" }
        """);
        RefillRequest? capturada = null;
        _medications.RequestRefillAsync(Arg.Any<RefillRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturada = call.Arg<RefillRequest>();
                return Task.FromResult(new EhrRefillRequest(
                    "rf-1", "pat-jorge-medina", "med-losartan", "Losartán",
                    "doc-1", DateTime.UtcNow, "pending", null));
            });

        var result = await BuildSut().Refill(body, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("pat-jorge-medina", capturada!.PatientId);
        Assert.Equal("med-losartan", capturada.MedicationId);
    }

    // ══════════ ESCRITURA · el mensaje al equipo de salud ══════════

    [Fact]
    public async Task Message_User_EsElRemitente()
    {
        var body = Bind<EhrController.SendMessageBody>("""
        { "threadId": "th-1", "body": "¿Puedo tomar el losartán en la noche?", "user": "pat-jorge-medina" }
        """);
        _messaging.ReplyAsync("th-1", "pat-jorge-medina", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MessageThread(
                "th-1", "clinical:msg", new[] { "pat-jorge-medina", "doc-1" },
                Array.Empty<ThreadMessage>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var result = await BuildSut().SendMessage(body, default);

        Assert.IsType<OkObjectResult>(result);
        await _messaging.Received(1).ReplyAsync("th-1", "pat-jorge-medina", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ══════════ ESCRITURA · la orden clínica ══════════

    [Fact]
    public async Task Order_PatientIdYKind_ConElTratanteDelPadron()
    {
        _patients.GetAsync("pat-jorge-medina", Arg.Any<CancellationToken>())
            .Returns(Paciente("pat-jorge-medina", "doc-carlos-mejia"));
        PlaceOrderRequest? capturada = null;
        _orders.PlaceAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturada = call.Arg<PlaceOrderRequest>();
                return Task.FromResult(new EhrClinicalOrder(
                    "ord-1", "pat-jorge-medina", "doc-carlos-mejia", "Dr. Mejía",
                    "prescription", "Losartán", DateTime.UtcNow, "placed"));
            });
        var body = Bind<EhrController.PlaceOrderBody>("""
        { "patientId": "pat-jorge-medina", "kind": "prescription", "detail": "Losartán" }
        """);

        var result = await BuildSut().PlaceOrder(body, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("pat-jorge-medina", capturada!.PatientId);
        Assert.Equal("prescription", capturada.Type);
        // El prescriptor no viaja en el cuerpo: se resuelve del padrón, no se inventa.
        Assert.Equal("doc-carlos-mejia", capturada.ProviderId);
    }

    [Fact]
    public async Task Order_SinTratante_LoDiceEnVezDeInventarlo()
    {
        _patients.GetAsync("pat-sin-medico", Arg.Any<CancellationToken>())
            .Returns(Paciente("pat-sin-medico", null));
        var body = Bind<EhrController.PlaceOrderBody>("""
        { "patientId": "pat-sin-medico", "kind": "lab", "detail": "Hemograma" }
        """);

        var result = await BuildSut().PlaceOrder(body, default);

        Assert.IsType<BadRequestObjectResult>(result);
        await _orders.DidNotReceive().PlaceAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());
    }

    // ══════════ ESCRITURA · la cita ══════════

    [Fact]
    public async Task Appointment_SlotComoObjeto_SeAgenda()
    {
        // `{ date, time }` es lo que el borde DEVUELVE en AppointmentDto.Slot; mandarlo de
        // vuelta hacía reventar el binding y el 400 quedaba tapado por la cita optimista.
        var body = Bind<EhrController.BookAppointmentBody>("""
        { "patientId": "p1", "doctorId": "d1", "slot": { "date": "2026-09-18", "time": "08:00" } }
        """);

        Assert.Equal(new DateTime(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc), body.ResolveSlotUtc()!.Value.ToUniversalTime());

        BookAppointmentRequest? capturada = null;
        _scheduling.BookAsync(Arg.Any<BookAppointmentRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturada = call.Arg<BookAppointmentRequest>();
                return Task.FromResult(new ClinicalAppointment(
                    "appt-1", "p1", "Jorge", "d1", "Dra. X", "Cardiología",
                    capturada!.Slot, capturada.Slot.AddMinutes(30), "booked", "resv-1"));
            });

        var result = await BuildSut().BookAppointment(body, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(capturada);
    }

    [Fact]
    public void Appointment_SlotComoInstante_SigueValiendo()
    {
        var body = Bind<EhrController.BookAppointmentBody>("""
        { "patientId": "p1", "doctorId": "d1", "slot": "2026-09-18T08:00:00Z" }
        """);

        Assert.Equal(new DateTime(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc), body.ResolveSlotUtc()!.Value.ToUniversalTime());
    }

    // ══════════ LECTURA · la historia clínica ══════════

    [Fact]
    public async Task Chart_History_EsLaListaDeNotas_YElResumenTieneNombrePropio()
    {
        _patients.GetAsync("p1", Arg.Any<CancellationToken>()).Returns(Paciente("p1", "doc-1"));
        _records.GetHistoryAsync("p1", Arg.Any<CancellationToken>()).Returns(new ClinicalHistory(
            "p1", "Control", new[] { "HTA" }, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), null, null, 1));
        _records.GetEncountersAsync("p1", Arg.Any<CancellationToken>()).Returns(new[]
        {
            new ClinicalEncounter("enc-1", "p1", "doc-1", "Dra. X", DateTime.UtcNow, "Control",
                new SoapNote("s", string.Empty, "a", "p"), null, true),
        });
        _prescriptions.GetForPatientAsync("p1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EhrPrescription>());

        var payload = Assert.IsType<OkObjectResult>(await BuildSut().Patient("p1", default)).Value;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload, Web));
        var root = json.RootElement;

        // La pestaña «Notas» lee chart.history[] — un objeto ahí la deja vacía contra el
        // servidor y llena contra el mock, que es la peor de las dos.
        Assert.Equal(JsonValueKind.Array, root.GetProperty("history").ValueKind);
        Assert.Equal("enc-1", root.GetProperty("history")[0].GetProperty("id").GetString());
        // El resumen no se perdió: tiene nombre propio porque el mismo no puede ser los dos.
        Assert.Equal("Control", root.GetProperty("clinicalHistory").GetProperty("chiefComplaint").GetString());
    }

    [Fact]
    public async Task Chart_SoapObjective_SaleComoObjetoDeVitales()
    {
        // La simetría que hacía falta probar: lo que sale por `objective` es lo que tiene que
        // poder volver a entrar por `objective`.
        _patients.GetAsync("p1", Arg.Any<CancellationToken>()).Returns(Paciente("p1", "doc-1"));
        _records.GetEncountersAsync("p1", Arg.Any<CancellationToken>()).Returns(new[]
        {
            new ClinicalEncounter("enc-1", "p1", "doc-1", "Dra. X", DateTime.UtcNow, "Control",
                new SoapNote("s", string.Empty, "a", "p", new ClinicalVitals(SystolicMmHg: 128)), null, true),
        });
        _prescriptions.GetForPatientAsync("p1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EhrPrescription>());

        var payload = Assert.IsType<OkObjectResult>(await BuildSut().Patient("p1", default)).Value;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload, Web));

        var objective = json.RootElement.GetProperty("history")[0].GetProperty("soap").GetProperty("objective");
        Assert.Equal(JsonValueKind.Object, objective.ValueKind);
        Assert.Equal(128, objective.GetProperty("systolic").GetDouble());
    }
}
