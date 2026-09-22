namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Los tres casos que el compose NO tiene, y que son los que deciden si el gate sirve (#152).
/// </summary>
/// <remarks>
/// <para><b>Por qué hacen falta aparte.</b> `compose.prod.yml` declara `replicas: 1` en los
/// veinticinco servicios, así que el gate que lo lee **nunca ejercita la rama que importa**: pasa
/// en verde sin mirar una sola decisión. Mutarlo subiendo un servicio a dos y restaurarlo prueba
/// un caso y se lo lleva; estos tres se quedan.</para>
///
/// <para>Son literalmente los tres que el #152 nombró como criterio de salida: una capacidad con
/// turno escala, un orquestador no, y <c>Api.Sessions</c> escala <b>por otra razón</b> — su
/// almacén añade líneas y nunca lee-modifica-escribe, así que no comparte grano con nadie.</para>
/// </remarks>
public sealed class EscaladoDeReplicasTests
{
    private static readonly IReadOnlySet<string> ConTurno =
        new HashSet<string>(StringComparer.Ordinal) { "api-booking", "api-inventory" };

    /// <summary>Los que guardan por `JsonCollectionStore` — `api-sessions` NO está, y es el punto.</summary>
    private static readonly IReadOnlySet<string> AlmacenCompartido =
        new HashSet<string>(StringComparer.Ordinal) { "api-booking", "api-inventory", "api-huerfana" };

    private static IReadOnlyList<string> Medir(params (string, int)[] declarado)
        => ComposeStackTests.QuienNoPuedeEscalar(declarado, ConTurno, AlmacenCompartido);

    [Fact]
    public void Una_capacidad_CON_turno_puede_escalar()
    {
        // Es lo que el #112 construyó y lo que el gate viejo prohibía: fichero por documento más
        // turno de escritura entre procesos. Verificado allí en vivo — 400 ajustes, 400 unidades.
        Assert.Empty(Medir(("api-booking", 2)));
    }

    [Fact]
    public void Un_orquestador_NO_puede_escalar_aunque_sus_sagas_vivan_en_el_mismo_almacen()
    {
        // Heredaron el fichero por documento, así que dos réplicas que avancen sagas DISTINTAS ya
        // no se borran. Lo que sigue abierto es la MISMA saga desde dos réplicas, y eso pide un
        // turno por saga (ISagaLease, #34) que nadie escribió todavía.
        var malas = Medir(("bff-tienda", 2));

        Assert.Single(malas);
        Assert.Contains("POR SAGA", malas[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Api_Sessions_escala_por_OTRA_razon_y_hay_que_saber_cual()
    {
        // No tiene turno —y no es olvido—: su almacén AÑADE líneas a un fichero por día y nunca
        // lee-modifica-escribe, que es el único patrón que ya era seguro entre réplicas. Si algún
        // día pasara a `JsonCollectionStore` sin turno, el caso de abajo es el que la caza.
        Assert.Empty(Medir(("api-sessions", 2)));
    }

    [Fact]
    public void Una_capacidad_que_comparte_grano_y_NO_tiene_turno_se_rechaza()
    {
        // El caso que distingue «escala porque es append-only» de «escala porque nadie miró».
        // Sin este test, `Api_Sessions_escala…` pasaría igual con la regla borrada.
        var malas = Medir(("api-huerfana", 2));

        Assert.Single(malas);
        Assert.Contains("UseStoreWriteGate(", malas[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Con_UNA_replica_no_se_rechaza_nada()
    {
        // El estado de hoy: veinticinco servicios en 1. Un gate que se quejara igual sería uno
        // que enseña a ignorarse.
        Assert.Empty(Medir(("bff-tienda", 1), ("api-huerfana", 1), ("cms", 1)));
    }

    [Fact]
    public void Lo_que_no_es_capacidad_ni_orquestador_se_rechaza_nombrando_su_razon()
    {
        var malas = Medir(("cms", 2));

        Assert.Single(malas);
        Assert.Contains("SQLite", malas[0], StringComparison.Ordinal);
    }
}
