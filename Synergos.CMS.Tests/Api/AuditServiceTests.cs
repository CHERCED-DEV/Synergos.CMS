using Synergos.Api.Audit.Domain;
using Synergos.Api.Audit.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Cubre <see cref="AuditService"/> — la composición de reglas, almacén y orden.
/// </summary>
/// <remarks>
/// Las reglas puras ya están probadas aparte. Lo que se prueba acá es lo que <b>solo</b> falla al
/// componerlas: quién pone el reloj, si un reintento reescribe, y si la consulta devuelve lo que
/// promete. Es exactamente la clase de defecto que en <c>Api.Booking</c> encontró un proceso vivo
/// y no la suite.
/// </remarks>
public sealed class AuditServiceTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    private sealed class MemoriaStore : IAuditStore, IIdempotencyLedger
    {
        private readonly List<AuditEntry> _entries = new();
        private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);

        /// <summary>Cuántas veces se escribió. Delata un reintento que crea una segunda entrada.</summary>
        public int Escrituras { get; private set; }

        public AuditEntry? Find(string id) => _entries.FirstOrDefault(e => e.Id == id);

        public IReadOnlyList<AuditEntry> Query(Ref? target, string? actorId, TimeWindow window)
            => _entries
                .Where(e => window.Contains(e.AtUtc)
                            && (target is null || e.Target == target)
                            && (string.IsNullOrEmpty(actorId) || e.Actor.Principal.Id == actorId))
                .OrderByDescending(e => e.AtUtc)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .ToList();

        public void Append(AuditEntry entry)
        {
            _entries.Add(entry);
            Escrituras++;
        }

        public string? Find(string scope, IdempotencyKey key) => _keys.GetValueOrDefault($"{scope}|{key.Value}");
        public void Remember(string scope, IdempotencyKey key, string resultId) => _keys[$"{scope}|{key.Value}"] = resultId;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly Ref Historia = Ref.Create("salud.historia", "h-1");
    private static readonly Actor Medico = Actor.Of(Ref.Create("identity.member", "m-1"), "clinico");

    private static (AuditService Svc, MemoriaStore Store, RelojFalso Reloj) Nuevo()
    {
        var store = new MemoriaStore();
        var reloj = new RelojFalso(Ahora);
        return (new AuditService(store, store, reloj), store, reloj);
    }

    private static IdempotencyKey Llave(string s) => IdempotencyKey.Of(s);

    private static TimeWindow Semana => TimeWindow.Of(Ahora.AddDays(-7), Ahora.AddDays(7));

    [Fact]
    public void El_reloj_lo_pone_el_SERVICIO_y_no_el_origen()
    {
        // Un origen con la hora corrida —o malintencionado— podría escribir entradas en el
        // pasado y romper el orden de una investigación, que es para lo que existe la bitácora.
        var (svc, _, reloj) = Nuevo();
        reloj.Avanzar(TimeSpan.FromHours(3));

        var entrada = svc.Append(Medico, "historia.consultada", Historia, null, Llave("k1"));

        Assert.True(entrada.IsOk);
        Assert.Equal(Ahora.AddHours(3), entrada.Value.AtUtc);
    }

    [Fact]
    public void Reintentar_con_la_MISMA_llave_no_escribe_una_segunda_entrada()
    {
        var (svc, store, _) = Nuevo();

        var a = svc.Append(Medico, "historia.consultada", Historia, null, Llave("misma"));
        var b = svc.Append(Medico, "historia.consultada", Historia, null, Llave("misma"));

        Assert.Equal(a.Value.Id, b.Value.Id);
        Assert.Equal(1, store.Escrituras);
    }

    [Fact]
    public void Una_entrada_RECHAZADA_no_llega_al_almacen()
    {
        // Si la validación corriera después de escribir, la bitácora acumularía entradas que
        // ella misma considera inválidas — y nadie las podría consultar ni borrar.
        var (svc, store, _) = Nuevo();

        var bad = svc.Append(Actor.Anonymous, "algo.paso", Historia, null, Llave("k1"));

        Assert.False(bad.IsOk);
        Assert.Equal(0, store.Escrituras);
    }

    [Fact]
    public void Una_llave_gastada_por_una_entrada_RECHAZADA_sigue_libre()
    {
        // La llave se recuerda DESPUÉS de escribir. Si se gastara al validar, corregir la
        // petición y reintentar con la misma llave devolvería un idempotency_orphan y el
        // llamador quedaría sin manera de completar la operación.
        var (svc, _, _) = Nuevo();

        svc.Append(Actor.Anonymous, "algo.paso", Historia, null, Llave("misma"));
        var buena = svc.Append(Medico, "algo.paso", Historia, null, Llave("misma"));

        Assert.True(buena.IsOk, $"la llave quedó inutilizable: {buena.Rejection}");
    }

    [Fact]
    public void La_consulta_filtra_por_OBJETIVO()
    {
        var (svc, _, _) = Nuevo();
        var otra = Ref.Create("salud.historia", "h-2");
        svc.Append(Medico, "a", Historia, null, Llave("k1"));
        svc.Append(Medico, "b", otra, null, Llave("k2"));

        var r = svc.Query(Historia, null, Semana, 0, 10);

        Assert.True(r.IsOk);
        Assert.Single(r.Value.Items);
        Assert.Equal("a", r.Value.Items[0].Action);
    }

    [Fact]
    public void La_consulta_filtra_por_ACTOR()
    {
        var (svc, _, _) = Nuevo();
        var otro = Actor.Of(Ref.Create("identity.member", "m-2"));
        svc.Append(Medico, "a", Historia, null, Llave("k1"));
        svc.Append(otro, "b", Historia, null, Llave("k2"));

        var r = svc.Query(null, "m-1", Semana, 0, 10);

        Assert.Single(r.Value.Items);
        Assert.Equal("a", r.Value.Items[0].Action);
    }

    [Fact]
    public void La_ventana_DEJA_FUERA_lo_que_no_cae_dentro()
    {
        var (svc, _, reloj) = Nuevo();
        svc.Append(Medico, "vieja", Historia, null, Llave("k1"));
        reloj.Avanzar(TimeSpan.FromDays(30));
        svc.Append(Medico, "nueva", Historia, null, Llave("k2"));

        var ultimaSemana = svc.Query(Historia, null,
            TimeWindow.Of(Ahora.AddDays(23), Ahora.AddDays(31)), 0, 10);

        Assert.Single(ultimaSemana.Value.Items);
        Assert.Equal("nueva", ultimaSemana.Value.Items[0].Action);
    }

    [Fact]
    public void Lo_mas_RECIENTE_sale_primero()
    {
        // Una investigación se lee de arriba abajo empezando por lo último que pasó.
        var (svc, _, reloj) = Nuevo();
        foreach (var n in new[] { "una", "dos", "tres" })
        {
            svc.Append(Medico, n, Historia, null, Llave(n));
            reloj.Avanzar(TimeSpan.FromMinutes(1));
        }

        var r = svc.Query(Historia, null, Semana, 0, 10);

        Assert.Equal(new[] { "tres", "dos", "una" }, r.Value.Items.Select(e => e.Action));
    }

    [Fact]
    public void El_tope_de_filas_acota_aunque_pidan_mas()
    {
        // Una bitácora crece sin parar; una consulta no puede. Sin el techo, pedir limit=100000
        // devolvería el rastro entero por HTTP.
        var (svc, _, _) = Nuevo();
        for (var i = 0; i < 5; i++) svc.Append(Medico, $"a{i}", Historia, null, Llave($"k{i}"));

        var r = svc.Query(Historia, null, Semana, 0, AuditService.MaxRows + 1_000);

        Assert.True(r.Value.Items.Count <= AuditService.MaxRows);
        Assert.Equal(5, r.Value.Total);
    }

    [Fact]
    public void El_total_cuenta_TODO_y_no_solo_la_pagina()
    {
        // Confundirlos produce el clásico "página 2 de 1".
        var (svc, _, _) = Nuevo();
        for (var i = 0; i < 5; i++) svc.Append(Medico, $"a{i}", Historia, null, Llave($"k{i}"));

        var r = svc.Query(Historia, null, Semana, 0, 2);

        Assert.Equal(2, r.Value.Items.Count);
        Assert.Equal(5, r.Value.Total);
        Assert.True(r.Value.HasMore);
    }

    [Fact]
    public void Los_detalles_vuelven_INTACTOS()
    {
        var (svc, _, _) = Nuevo();
        var detalles = new Dictionary<string, string> { ["motivo"] = "solicitud del paciente" };

        var e = svc.Append(Medico, "historia.compartida", Historia, detalles, Llave("k1"));

        Assert.Equal("solicitud del paciente", e.Value.Details["motivo"]);
    }
}
