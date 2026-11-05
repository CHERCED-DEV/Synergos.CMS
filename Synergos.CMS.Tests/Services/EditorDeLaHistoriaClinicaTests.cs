using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El editor de la historia (003-salud-cita, I2 T057): dos cambios a la vez sobre la misma historia entran los dos,
/// un cambio que no cambia nada no crea versión, y una historia que no está no se crea por la puerta de atrás.
/// </summary>
public sealed class EditorDeLaHistoriaClinicaTests
{
    private readonly InMemoryPhiStore _store = new();

    private static PatientRecord Historia(Guid key) => new(
        key, Guid.NewGuid(), "Camila Restrepo", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        null, null, null, DateTime.UtcNow, Guid.NewGuid(), 0, false);

    private static DatoDeSalud Alergia(string id) => new(
        id, TiposDeDatoDeSalud.Alergia, id, null, EstadosDelDato.Declarado, "paciente", DateTime.UtcNow, Guid.NewGuid());

    private static PatientRecord ConDato(PatientRecord h, DatoDeSalud dato) =>
        h with { Resumen = new ResumenDeSalud([.. h.Resumen?.Datos ?? Array.Empty<DatoDeSalud>(), dato]) };

    /// <summary>
    /// La carrera, sin relojes: el primer cambio arranca el segundo MIENTRAS tiene la historia leída. El almacén en
    /// memoria contesta en el acto, así que sin candado el segundo lee la versión de antes, guarda la suya, y el
    /// primero la pisa al volver. Con candado el segundo espera, lee lo del primero y suma lo suyo.
    /// </summary>
    [Fact]
    public async Task Dos_cambios_a_la_vez_sobre_la_misma_historia_entran_los_dos()
    {
        var repo = new FileSystemPatientRepository(_store);
        var key = await repo.UpsertAsync(Historia(Guid.Empty), CancellationToken.None);
        var editor = new EditorDeLaHistoriaClinica(repo);

        Task<PatientRecord?>? segundo = null;
        var primero = editor.ModificarAsync(
            key,
            actual =>
            {
                segundo = editor.ModificarAsync(key, h => ConDato(h, Alergia("latex")), CancellationToken.None);
                return ConDato(actual, Alergia("penicilina"));
            },
            CancellationToken.None);
        await primero;
        await segundo!;

        var final = await repo.GetAsync(key, CancellationToken.None);
        Assert.Equal(new[] { "penicilina", "latex" }, final!.Resumen!.Datos.Select(d => d.Id));
    }

    [Fact]
    public async Task Un_cambio_que_no_cambia_nada_no_crea_version()
    {
        var key = Guid.NewGuid();
        var repo = Substitute.For<IPatientRepository>();
        var historia = Historia(key);
        repo.GetAsync(key, Arg.Any<CancellationToken>()).Returns(historia);

        var leida = await new EditorDeLaHistoriaClinica(repo).ModificarAsync(key, h => h, CancellationToken.None);

        Assert.Same(historia, leida);
        await repo.DidNotReceive().UpsertAsync(Arg.Any<PatientRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Una_historia_que_no_esta_o_esta_borrada_no_se_crea_por_aqui()
    {
        var borrada = Guid.NewGuid();
        var repo = Substitute.For<IPatientRepository>();
        repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PatientRecord?)null);
        repo.GetAsync(borrada, Arg.Any<CancellationToken>()).Returns(Historia(borrada) with { IsDeleted = true });
        var editor = new EditorDeLaHistoriaClinica(repo);

        Assert.Null(await editor.ModificarAsync(Guid.NewGuid(), h => ConDato(h, Alergia("x")), CancellationToken.None));
        Assert.Null(await editor.ModificarAsync(borrada, h => ConDato(h, Alergia("x")), CancellationToken.None));
        await repo.DidNotReceive().UpsertAsync(Arg.Any<PatientRecord>(), Arg.Any<CancellationToken>());
    }

    private sealed class InMemoryPhiStore : IPhiStore
    {
        private readonly Dictionary<string, string> _data = new();
        private static string K(string rt, Guid key) => rt + "/" + key.ToString("N");

        public Task WriteAsync(string resourceType, Guid key, string json, CancellationToken ct)
        {
            _data[K(resourceType, key)] = json;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string resourceType, Guid key, CancellationToken ct) =>
            Task.FromResult(_data.TryGetValue(K(resourceType, key), out var v) ? v : null);

        public Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<string>)_data
                .Where(kv => kv.Key.StartsWith(resourceType + "/", StringComparison.Ordinal))
                .Select(kv => kv.Value).ToList());

        public Task<bool> DeleteAsync(string resourceType, Guid key, CancellationToken ct) =>
            Task.FromResult(_data.Remove(K(resourceType, key)));
    }
}
