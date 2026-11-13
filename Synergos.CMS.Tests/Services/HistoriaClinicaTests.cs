using Synergos.CMS.Application.Salud;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Las reglas puras de la historia de I2 (003-salud-cita, T057–T060): lo que el paciente declara entra como declarado y
/// no toca lo validado, el médico valida de forma idempotente, y el resumen se lee sin las claves de nadie.
/// </summary>
public sealed class HistoriaClinicaTests
{
    private static readonly Guid Paciente = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Medico = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Ahora = new(2026, 11, 4, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Hoy = new(2026, 11, 4);

    private static PatientRecord Historia(ResumenDeSalud? resumen = null) => new(
        Guid.NewGuid(), Paciente, "Camila Restrepo", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        "Control anual", "Sin hallazgos", "Paciente estable", Ahora.AddDays(-30), Medico, 3, false, resumen);

    private static DatoDeSalud Validado(string id, string tipo, string descripcion, string? fecha = null) => new(
        id, tipo, descripcion, fecha, EstadosDelDato.Validado, HistoriaClinica.OrigenMedico, Ahora.AddDays(-10), Medico,
        Ahora.AddDays(-10), Medico);

    /// <summary>Ids predecibles: así una prueba puede decir cuál es el dato nuevo.</summary>
    private static Func<string> Ids()
    {
        var n = 0;
        return () => $"nuevo-{++n}";
    }

    [Fact]
    public void Declarar_ReemplazaElPreRegistro_YLoClinicoEntraDeclaradoPorElPaciente()
    {
        var checkIn = new CheckInDelPaciente(
            Documento: "  CC   52.123.456 ", Aseguradora: "EPS Sura", Medicamentos: new[] { "Losartán 50 mg", "losartán 50 MG" },
            MotivoDeLaVisita: "Control", Condiciones: new[] { "Hipertensión" }, Alergias: new[] { "Penicilina", " " },
            Vacunas: new[] { new VacunaDeclarada("Influenza", "2026-04-10") });

        var nueva = HistoriaClinica.Declarar(Historia(), checkIn, Paciente, Ahora, Ids());

        Assert.Equal("CC 52.123.456", nueva.PreRegistro!.Documento);
        Assert.Equal(new[] { "Losartán 50 mg" }, nueva.PreRegistro.Medicamentos);
        Assert.Equal(Ahora, nueva.PreRegistro.DeclaradoUtc);
        var datos = nueva.Resumen!.Datos;
        Assert.Equal(
            new[] { ("condicion", "Hipertensión", (string?)null), ("alergia", "Penicilina", null), ("vacuna", "Influenza", "2026-04-10") },
            datos.Select(d => (d.Tipo, d.Descripcion, d.Fecha)));
        Assert.All(datos, d =>
        {
            Assert.Equal(EstadosDelDato.Declarado, d.Estado);
            Assert.Equal(HistoriaClinica.OrigenPaciente, d.Origen);
            Assert.Equal(Paciente, d.RegistradoPor);
            Assert.Null(d.ValidadoPor);
        });
        // Lo clínico del médico no se toca.
        Assert.Equal("Paciente estable", nueva.AssessmentNotes);
    }

    [Fact]
    public void Declarar_NoTocaLoValidado_NiLoDuplica()
    {
        var validado = Validado("v-1", TiposDeDatoDeSalud.Alergia, "Penicilina");
        var historia = Historia(new ResumenDeSalud(new[] { validado }));

        var nueva = HistoriaClinica.Declarar(
            historia, new CheckInDelPaciente(Alergias: new[] { "penicilina", "Ibuprofeno" }), Paciente, Ahora, Ids());

        var datos = nueva.Resumen!.Datos;
        Assert.Same(validado, datos[0]);
        Assert.Equal(2, datos.Count);
        Assert.Equal(("Ibuprofeno", EstadosDelDato.Declarado), (datos[1].Descripcion, datos[1].Estado));
    }

    [Fact]
    public void Declarar_OtraVez_ReemplazaLoPendiente_YLoQueRepiteConservaSuId()
    {
        var primero = HistoriaClinica.Declarar(
            Historia(), new CheckInDelPaciente(Alergias: new[] { "Penicilina", "Látex" }), Paciente, Ahora, Ids());
        var idDeLaPenicilina = primero.Resumen!.Datos.Single(d => d.Descripcion == "Penicilina").Id;

        var segundo = HistoriaClinica.Declarar(
            primero, new CheckInDelPaciente(Alergias: new[] { "Penicilina" }), Paciente, Ahora.AddDays(1), Ids());

        var dato = Assert.Single(segundo.Resumen!.Datos);
        Assert.Equal(idDeLaPenicilina, dato.Id);
        Assert.Equal(Ahora, dato.RegistradoUtc);
    }

    [Fact]
    public void Declarar_OtraVez_NoSeLlevaLoQueElPacienteDeclaroYElMedicoYaValido()
    {
        var declarada = HistoriaClinica.Declarar(
            Historia(), new CheckInDelPaciente(Alergias: new[] { "Penicilina" }), Paciente, Ahora, Ids());
        var (validada, _) = HistoriaClinica.Validar(declarada, "nuevo-1", Medico, Ahora.AddHours(1));

        // El paciente vuelve a hacer su check-in y ya no la nombra: lo validado es de la historia, no de su lista.
        var otra = HistoriaClinica.Declarar(
            validada, new CheckInDelPaciente(Alergias: new[] { "Látex" }), Paciente, Ahora.AddDays(1), Ids());

        Assert.Equal(
            new[] { ("Penicilina", EstadosDelDato.Validado), ("Látex", EstadosDelDato.Declarado) },
            otra.Resumen!.Datos.Select(d => (d.Descripcion, d.Estado)));
    }

    [Fact]
    public void Validar_DejaElDatoValidadoPorElMedico_YOtraVezNoCambiaNada()
    {
        var declarada = HistoriaClinica.Declarar(
            Historia(), new CheckInDelPaciente(Alergias: new[] { "Penicilina" }), Paciente, Ahora, Ids());

        var (validada, resultado) = HistoriaClinica.Validar(declarada, "nuevo-1", Medico, Ahora.AddHours(1));
        var (otraVez, segundo) = HistoriaClinica.Validar(validada, "nuevo-1", Medico, Ahora.AddHours(2));

        Assert.Equal(ResultadoDeValidar.Validado, resultado);
        var dato = Assert.Single(validada.Resumen!.Datos);
        Assert.Equal((EstadosDelDato.Validado, (Guid?)Medico, (DateTime?)Ahora.AddHours(1)), (dato.Estado, dato.ValidadoPor, dato.ValidadoUtc));
        Assert.Equal(Paciente, dato.RegistradoPor);
        Assert.Equal(ResultadoDeValidar.YaEstabaValidado, segundo);
        Assert.Same(validada, otraVez);
    }

    [Fact]
    public void Validar_UnDatoQueNoEsta_NoExiste_YNoCambiaLaHistoria()
    {
        var historia = Historia(new ResumenDeSalud(new[] { Validado("v-1", TiposDeDatoDeSalud.Condicion, "Asma") }));

        var (misma, resultado) = HistoriaClinica.Validar(historia, "otro", Medico, Ahora);

        Assert.Equal(ResultadoDeValidar.NoExiste, resultado);
        Assert.Same(historia, misma);
        Assert.Equal(ResultadoDeValidar.NoExiste, HistoriaClinica.Validar(Historia(), "v-1", Medico, Ahora).Resultado);
    }

    public static TheoryData<CheckInDelPaciente, string> CheckInsQueNoSeGuardan() => new()
    {
        { new CheckInDelPaciente(Documento: new string('9', 201)), "documento" },
        { new CheckInDelPaciente(Alergias: Enumerable.Range(0, 31).Select(i => $"a{i}").ToList()), "alergias" },
        { new CheckInDelPaciente(Medicamentos: new[] { new string('x', 201) }), "medicamentos" },
        { new CheckInDelPaciente(Vacunas: new[] { new VacunaDeclarada("  ") }), "vacunas" },
        { new CheckInDelPaciente(Vacunas: new[] { new VacunaDeclarada("Influenza", "2026-11-05") }), "vacunas" },
        { new CheckInDelPaciente(Vacunas: new[] { new VacunaDeclarada("Influenza", "10/04/2026") }), "vacunas" },
    };

    [Theory, MemberData(nameof(CheckInsQueNoSeGuardan))]
    public void ErrorDelCheckIn_NombraElCampoQueHayQueCorregir(CheckInDelPaciente checkIn, string campo)
    {
        Assert.StartsWith($"{campo}:", HistoriaClinica.ErrorDelCheckIn(checkIn, Hoy), StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorDelCheckIn_UnoVacio_OUnoCompletoDeHoy_SeGuardan()
    {
        Assert.Null(HistoriaClinica.ErrorDelCheckIn(new CheckInDelPaciente(), Hoy));
        Assert.Null(HistoriaClinica.ErrorDelCheckIn(
            new CheckInDelPaciente(
                Documento: new string('9', 200), Vacunas: new[] { new VacunaDeclarada("Influenza", "2026-11-04") },
                Alergias: Enumerable.Range(0, 30).Select(i => $"a{i}").ToList()),
            Hoy));
    }

    [Fact]
    public void ParaLeer_OrdenaPorTipo_DiceSiHayResumen_YNoLlevaLasClavesDeNadie()
    {
        var historia = HistoriaClinica.Declarar(
            Historia(new ResumenDeSalud(new[] { Validado("v-1", TiposDeDatoDeSalud.Vacuna, "Tétanos", "2020-01-01") })),
            new CheckInDelPaciente(Alergias: new[] { "Penicilina" }, Condiciones: new[] { "Asma" }), Paciente, Ahora, Ids());

        var leido = HistoriaClinica.ParaLeer(historia);

        Assert.True(leido.HayResumen);
        Assert.Equal(new[] { "condicion", "alergia", "vacuna" }, leido.Datos.Select(d => d.Tipo));
        Assert.DoesNotContain(typeof(DatoParaLeer).GetProperties(), p => p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?));
        Assert.False(HistoriaClinica.ParaLeer(Historia()).HayResumen);
        Assert.Empty(HistoriaClinica.ParaLeer(Historia()).Datos);
    }
}
