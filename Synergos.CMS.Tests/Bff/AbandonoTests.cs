using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Domain;
using Synergos.Core;
using Compensation = Synergos.Bff.Core.Compensation;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La saga que empezó y nunca confirmó (HU #29).
/// </summary>
/// <remarks>
/// <para><b>El caso que esto cierra:</b> una compra que apartó existencias y autorizó el cobro, y
/// cuyo comprador cerró la pestaña. Sin nadie que vuelva por ella, la autorización se queda
/// reservada en la tarjeta de una persona indefinidamente y la saga se queda en <c>Running</c>
/// para siempre.</para>
///
/// <para><b>Y la trampa, que es la mitad del trabajo:</b> una saga viva lleva sus compensaciones
/// <b>armadas</b>, no pendientes. Un barrido que las confunda deshace compras que iban
/// perfectamente — y lo haría en silencio, porque cada compensación individual «funciona».</para>
/// </remarks>
public sealed class AbandonoTests : IDisposable
{
    // ⚠️ EL ALMACÉN REAL, no un doble. La primera versión de estos tests usaba un
    // `MemoriaSagas` que REIMPLEMENTABA la consulta bajo prueba — así que mutar
    // `FileSystemSagaStore` no ponía nada en rojo: los tests afirmaban sobre su propia copia de
    // la lógica. Tres de cuatro mutaciones pasaron en verde antes de darse cuenta.
    //
    // Es el mismo patrón que ya mordió en este repo con otro disfraz: un test que codifica la
    // misma suposición que el código no vigila nada.
    private readonly string _raiz = Path.Combine(
        Path.GetTempPath(), "syn-sagas-" + Guid.NewGuid().ToString("n"));

    private FileSystemSagaStore<PurchaseSaga> Almacen()
        => new(Microsoft.Extensions.Options.Options.Create(new SagaStorageOptions { Root = _raiz }));

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            try { Directory.Delete(_raiz, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static readonly DateTimeOffset Ahora = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    private static PurchaseSaga Compra(string id, DateTimeOffset empezo, SagaStatus estado = SagaStatus.Running)
        => new(id, Ref.Create("tienda.comprador", "u-1"), "c-1", estado,
            Array.Empty<StockHold>(), null, null, null, Money.Of(119000, "COP"),
            // Una compensación ARMADA: existe porque hay algo que deshacer, y NO es trabajo
            // pendiente mientras la compra siga viva.
            new[] { Compensation.For(TiendaCompensations.ReleaseStockHold, "sh-1", "compra no confirmada") },
            null, empezo);

    private FileSystemSagaStore<PurchaseSaga> Con(params PurchaseSaga[] sagas)
    {
        var store = Almacen();
        foreach (var s in sagas) store.Put(s);
        return store;
    }

    // ── A quién alcanza el plazo ────────────────────────────────────────────

    [Fact]
    public void La_saga_vieja_sin_confirmar_ENTRA_al_barrido()
    {
        // Empezó hace dos horas y sigue en Running: nadie va a volver por ella.
        var store = Con(Compra("vieja", Ahora.AddHours(-2)));

        var alcanzadas = store.StartedBefore(Ahora.AddHours(-1));

        Assert.Single(alcanzadas);
        Assert.Equal("vieja", alcanzadas[0].Id);
    }

    [Fact]
    public void La_saga_RECIENTE_no_se_toca()
    {
        // Es el error caro: abandonar una compra que va bien porque el pago tardó cinco minutos.
        var store = Con(Compra("recien-empezada", Ahora.AddMinutes(-5)));

        Assert.Empty(store.StartedBefore(Ahora.AddHours(-1)));
    }

    [Fact]
    public void Una_saga_VIVA_lleva_sus_compensaciones_ARMADAS_y_eso_NO_es_trabajo()
    {
        // «Armada no es pendiente» (feedback_compensation_is_data). La compra de abajo tiene una
        // compensación anotada —existe algo que deshacer si algo falla— y va perfectamente.
        //
        // Si el barrido de compensaciones la viera como trabajo, soltaría el apartado de una
        // compra en curso: el comprador llegaría a confirmar y no habría stock. Y cada paso
        // individual «funcionaría».
        var store = Con(Compra("va-bien", Ahora.AddMinutes(-5)));

        Assert.Empty(store.WithPendingCompensations());
        Assert.NotEmpty(store.Find("va-bien")!.Compensations);   // armada, sí
    }

    [Fact]
    public void Las_que_YA_terminaron_no_se_abandonan()
    {
        // Completed y Compensated son finales. Y Compensating ya lo barre la otra consulta:
        // «abandonar» algo que ya se está deshaciendo sería contarlo dos veces.
        var store = Con(
            Compra("completada", Ahora.AddDays(-3), SagaStatus.Completed),
            Compra("compensada", Ahora.AddDays(-3), SagaStatus.Compensated),
            Compra("deshaciendo", Ahora.AddDays(-3), SagaStatus.Compensating));

        Assert.Empty(store.StartedBefore(Ahora.AddHours(-1)));
    }

    [Fact]
    public void El_orden_es_por_antiguedad()
    {
        // La más vieja primero: si el barrido se corta a la mitad, lo que se atendió es lo que
        // más tiempo lleva colgado.
        var store = Con(
            Compra("hace-2h", Ahora.AddHours(-2)),
            Compra("hace-5h", Ahora.AddHours(-5)),
            Compra("hace-3h", Ahora.AddHours(-3)));

        // SIN volver a ordenar: lo que se afirma es el orden que devuelve EL ALMACÉN. Ordenar
        // acá probaría el `OrderBy` de este test, no el suyo — y esa fue exactamente la primera
        // versión, que pasaba en verde con el almacén devolviendo cualquier cosa.
        var orden = store.StartedBefore(Ahora.AddHours(-1)).Select(s => s.Id).ToList();

        Assert.Equal(new[] { "hace-5h", "hace-3h", "hace-2h" }, orden);
    }

    // ── El plazo es un compromiso, no una constante ─────────────────────────

    [Fact]
    public void El_plazo_por_defecto_deja_pasar_un_pago_lento()
    {
        // Una hora. Muy corto cancela compras buenas; muy largo deja plata reservada en tarjetas
        // ajenas. Y el stock ya volvió solo mucho antes: el apartado de Api.Inventory vence a los
        // 15 minutos por su propio TTL.
        var opciones = new SweepOptions();

        Assert.Equal(60, opciones.AbandonAfterMinutes);
        Assert.True(opciones.AbandonAfterMinutes > 15,
            "el plazo tiene que ser MAYOR que el TTL del apartado de Api.Inventory (15 min): "
            + "si no, se abandonaría antes de que el stock haya vuelto solo.");
    }

    [Fact]
    public void En_cero_el_abandono_se_APAGA_de_verdad()
    {
        // Un despliegue puede decidir que prefiere no cancelar nada automáticamente. Que exista
        // el interruptor es lo que hace que el default sea una opinión y no una imposición.
        //
        // Se prueba la FUNCIÓN, no el valor: comprobar que la propiedad vale cero no dice nada
        // sobre si el barrido la respeta.
        Assert.Null(new SweepOptions { AbandonAfterMinutes = 0 }.FechaDeAbandono(Ahora));
        Assert.Null(new SweepOptions { AbandonAfterMinutes = -5 }.FechaDeAbandono(Ahora));
    }

    [Fact]
    public void Con_plazo_puesto_la_fecha_limite_es_ahora_menos_el_plazo()
    {
        var limite = new SweepOptions { AbandonAfterMinutes = 90 }.FechaDeAbandono(Ahora);

        Assert.Equal(Ahora.AddMinutes(-90), limite);
    }

    [Fact]
    public void La_cadencia_tiene_PISO()
    {
        // Ponerla en cero convertiría el barrido en un lazo cerrado martillando las capacidades.
        // El piso vive en el barrido (Math.Max(5, …)); acá se fija el default.
        Assert.Equal(60, new SweepOptions().IntervalSeconds);
    }
}
