using System.Text.Json;
using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Gobierno;

/// <summary>
/// El proceso del trámite es UN dato (009-gobierno-tramite, FR-001, FR-007 y FR-009): la definición que se publica en
/// <c>Api.Workflow</c> vive en <c>tools/provisionar.procesos.json</c>, y todo lo que decide sobre un expediente se
/// cruza con ella.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> Medido el 2026-10-16: la tabla del motor en proceso y la definición publicada
/// decían cosas distintas en 3 de 9 pares estado×decisión, y nada lo veía, porque la definición vivía en un heredoc
/// de bash que ningún gate leía (D-3, D-4).</para>
/// </remarks>
public sealed class ProcesoDelTramiteTests
{
    private const string V1 = "gov.tramite";
    private const string V2 = "gov.tramite.v2";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private sealed record Transicion(string Name, string From, string To, IReadOnlyList<string> RequiredRoles);

    private sealed record Definicion(
        string Key, string InitialState, IReadOnlyList<string> FinalStates, IReadOnlyList<Transicion> Transitions);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Las definiciones del fichero de procesos; las entradas que sólo llevan <c>_</c> son comentarios.</summary>
    private static IReadOnlyList<Definicion> DelFichero()
    {
        var ruta = Path.Combine(RepoRoot(), "tools", "provisionar.procesos.json");
        Assert.True(File.Exists(ruta), $"No existe {ruta}: el proceso del trámite es un dato del repositorio (FR-009).");

        using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        return doc.RootElement.EnumerateArray()
            .Where(e => e.EnumerateObject().Any(p => p.Name != "_"))
            .Select(e => e.Deserialize<Definicion>(Json)!)
            .ToList();
    }

    private static Definicion Clave(string clave)
        => DelFichero().SingleOrDefault(d => d.Key == clave)
           ?? throw new Xunit.Sdk.XunitException($"El fichero de procesos no declara «{clave}».");

    private static string Huella(Definicion d) => string.Join("|",
        d.Key, d.InitialState, string.Join(",", d.FinalStates),
        string.Join(";", d.Transitions.Select(t => $"{t.Name}:{t.From}>{t.To}[{string.Join(",", t.RequiredRoles)}]")));

    /// <summary>
    /// La v1 ya está publicada, y una definición publicada no se reescribe: el fichero la conserva sin cambiarle nada.
    /// </summary>
    /// <remarks>
    /// <para>Antes se comparaba con el heredoc DEF_GOB de provisionar.sh, que era quien la publicaba. Desde NP-1 el guion
    /// publica desde este fichero (lo prueba ProvisionWiringTests corriéndolo), así que la referencia es la v1 que ya salió:
    /// cambiarla aquí no la cambiaría en Api.Workflow —contesta workflow.key_taken— y el orquestador cruzaría sus pasos
    /// contra un proceso que no es el publicado.</para>
    ///
    /// <para>Es también lo que deja volver a una versión anterior con el catálogo de ahora (deploy-remoto.sh): las
    /// definiciones se añaden, no se reescriben.</para>
    /// </remarks>
    [Fact]
    public void La_v1_publicada_sigue_en_el_fichero_sin_cambiar_nada()
    {
        Assert.Equal(
            "gov.tramite|submitted|approved,rejected|approve:submitted>approved[funcionario];approve:in-review>approved[funcionario];"
            + "approve:info-requested>approved[funcionario];reject:submitted>rejected[funcionario];"
            + "request-info:submitted>info-requested[funcionario]",
            Huella(Clave(V1)));
    }

    [Fact] // FR-005: los cinco estados de siempre, la inicial y las dos finales.
    public void La_v2_usa_los_estados_que_el_CMS_sabe_representar()
    {
        var v2 = Clave(V2);
        var estados = v2.Transitions.SelectMany(t => new[] { t.From, t.To })
            .Append(v2.InitialState).Concat(v2.FinalStates).Distinct().ToList();

        foreach (var estado in estados)
        {
            Assert.True(GovStatusSlugs.TryParse(estado, out var parseado) && GovStatusSlugs.ToSlug(parseado) == estado,
                $"La v2 nombra «{estado}», que el CMS no sabe representar: el expediente quedaría en un estado sin traducción.");
        }

        Assert.Equal("submitted", v2.InitialState);
        Assert.Equal(new[] { "approved", "rejected" }, v2.FinalStates.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact] // FR-006: toda transición exige un rol, para que la guarda de la capacidad rechace a la otra cara.
    public void Toda_transicion_de_la_v2_exige_el_rol_de_una_cara()
    {
        foreach (var t in Clave(V2).Transitions)
        {
            Assert.True(t.RequiredRoles.Count == 1 && (t.RequiredRoles[0] is "funcionario" or "ciudadano"),
                $"«{t.Name}» desde «{t.From}» exige [{string.Join(",", t.RequiredRoles)}]: tiene que exigir «funcionario» o «ciudadano».");
        }
    }

    // ── Los dos motores contra la v2 (T010, FR-007 y FR-010) ───────────────────────────

    private static readonly string[] EstadosAbiertos = { "submitted", "in-review", "info-requested" };
    private static readonly string[] Acciones = { "take", "approve", "reject", "request-info", "resubmit" };
    private static readonly string[] Caras = { "funcionario", "ciudadano" };

    /// <summary>
    /// Lo que contesta el motor en proceso a una acción en un estado: <c>ok</c>, <c>no-op</c> (el destino ya es el
    /// actual: idempotente) o <c>409</c>; <c>no-existe</c> si su tabla no la nombra.
    /// </summary>
    private static string EfectoDelMotorEnProceso(string estado, string accion)
    {
        if (!StubCaseWorkflowService.Tabla.TryGetValue(accion, out var regla))
        {
            return "no-existe";
        }
        if (GovStatusSlugs.ToSlug(regla.To) == estado)
        {
            return "no-op";
        }
        return regla.From.Any(o => GovStatusSlugs.ToSlug(o) == estado) ? "ok" : "409";
    }

    /// <summary>
    /// Lo que contesta el cliente de la capacidad con esa definición: el destino por (acción, origen) o por la acción
    /// (para reconocer la idempotencia), y la legalidad la juzga la capacidad con la transición desde el estado.
    /// </summary>
    private static string EfectoDeLaDefinicion(Definicion d, string estado, string accion)
    {
        var legal = d.Transitions.FirstOrDefault(t => t.Name == accion && t.From == estado);
        var destino = (legal ?? d.Transitions.FirstOrDefault(t => t.Name == accion))?.To;
        if (destino is null)
        {
            return "no-existe";
        }
        if (destino == estado)
        {
            return "no-op";
        }
        return legal is not null ? "ok" : "409";
    }

    /// <summary>
    /// Los dos motores contestan lo mismo en los 15 pares estado abierto × acción (SC-001).
    /// </summary>
    /// <remarks>
    /// Medido el 2026-10-16 con la v1: 3 de 9 pares divergían —con la capacidad conectada no se podía rechazar después
    /// de pedir información— y ningún test lo veía, porque cada motor se probaba contra sí mismo.
    /// </remarks>
    [Fact]
    public void El_motor_en_proceso_contesta_lo_mismo_que_la_v2_en_los_15_pares()
    {
        var v2 = Clave(V2);
        var divergen = (
            from estado in EstadosAbiertos
            from accion in Acciones
            let enProceso = EfectoDelMotorEnProceso(estado, accion)
            let capacidad = EfectoDeLaDefinicion(v2, estado, accion)
            where enProceso != capacidad
            select $"({estado}, {accion}): en proceso «{enProceso}», con la capacidad «{capacidad}»").ToList();

        Assert.True(divergen.Count == 0,
            $"Divergen {divergen.Count} de {EstadosAbiertos.Length * Acciones.Length} pares:\n" + string.Join("\n", divergen));
    }

    /// <summary>
    /// Las acciones que se sirven son exactamente las transiciones legales de la v2 desde cada estado para cada cara.
    /// </summary>
    /// <remarks>
    /// <para>Se calculan con el proceso del motor en proceso —el que sirve un clon limpio— y se comparan con lo que
    /// la v2 permite, leído aparte del calculador: así una cara cambiada en la tabla, una acción de más o una de menos
    /// se ven aquí, y no como un botón que la capacidad rechaza (SC-002).</para>
    /// </remarks>
    [Fact]
    public void Las_acciones_servidas_son_exactamente_las_legales_de_la_v2_por_estado_y_cara()
    {
        var v2 = Clave(V2);
        var estados = EstadosAbiertos.Concat(v2.FinalStates).ToList();

        foreach (var estado in estados)
        {
            foreach (var cara in Caras)
            {
                var legales = v2.FinalStates.Contains(estado)
                    ? new List<string>()
                    : v2.Transitions.Where(t => t.From == estado && t.RequiredRoles.Contains(cara))
                        .Select(t => t.Name).Distinct().ToList();

                var servidas = AccionesDelExpediente.Calcular(
                    StubCaseWorkflowService.Proceso, StubCaseWorkflowService.EstadosFinales, estado, cara);

                Assert.True(legales.SequenceEqual(servidas),
                    $"En «{estado}» a la cara «{cara}» se sirve [{string.Join(", ", servidas)}] y la v2 permite [{string.Join(", ", legales)}].");
            }
        }
    }

    [Fact] // los finales del motor en proceso son los de la v2: lo que uno da por cerrado el otro también
    public void Los_finales_del_motor_en_proceso_son_los_de_la_v2()
    {
        Assert.Equal(
            Clave(V2).FinalStates.OrderBy(s => s, StringComparer.Ordinal),
            StubCaseWorkflowService.EstadosFinales.OrderBy(s => s, StringComparer.Ordinal));
    }
}
