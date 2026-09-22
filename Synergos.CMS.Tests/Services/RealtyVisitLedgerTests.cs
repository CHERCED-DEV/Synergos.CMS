using System.Text.Json;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El EJE 3 de Realty: la constancia de que alguien agendó una visita (#158).
/// </summary>
/// <remarks>
/// <para><b>Por qué ningún test veía esto.</b> Las dos mitades estaban en verde:
/// <c>StubVisitSchedulingServiceTests</c> comprueba que el slot queda apartado y
/// <c>HttpVisitSchedulingServiceTests</c> que el cliente cablea bien contra la capacidad. El
/// hueco está justo en medio —«¿qué queda de ESTE lado cuando el modo está encendido?»— que es
/// el reparto del addendum #116 de <c>feedback_no_read_without_a_write_path</c>.</para>
///
/// <para><b>Y lo que el hallazgo #158 dio por hecho resultó ser sólo la mitad</b>: decía que con
/// <c>Mode=Api</c> no queda nada, y al medirlo tampoco quedaba nada legible con <c>Mode=Stub</c>
/// — lo que ese camino guarda en <c>realty-visits</c> está indexado por <c>{listado}/{slot}</c>,
/// sirve para marcar disponibilidad, y fuera de sus propios tests <b>no lo lee nadie</b>. Así
/// que el defecto no era «se pierde en un modo»: era que el artefacto no existía en ninguno.</para>
///
/// <para><b>El test que de verdad cierra el caso es el que cruza los dos modos</b>, y por eso
/// está acá y no en los de cada implementación: que la misma visita se lea igual la haya
/// agendado el motor en proceso o el cliente cableado. Eso es lo que la invariante del doc 12
/// §3.2 pide y lo que ninguna de las dos suites podía comprobar sola.</para>
/// </remarks>
public sealed class RealtyVisitLedgerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Manana = Ahora.AddDays(1);

    private static RealtyVisitLedger Nuevo(IJsonEntityStore? store = null)
        => new(store ?? new InMemoryJsonEntityStore(), () => Ahora);

    private static VisitContact Ana => new("Ana Ruiz", "Ana.Ruiz@correo.co", "3001234567");

    [Fact]
    public async Task Una_visita_anotada_se_lee_por_su_id()
    {
        var reg = Nuevo();
        await reg.RecordAsync("visit_1", "apto-90", "slot-a", Manana, Ana, "Confirmed");

        var v = await reg.GetAsync("visit_1");

        Assert.NotNull(v);
        Assert.Equal("apto-90", v!.ListingId);
        Assert.Equal("slot-a", v.SlotId);
        Assert.Equal(Manana, v.StartUtc);
        Assert.Equal("Confirmed", v.Status);
        Assert.Equal(Ahora, v.BookedAtUtc);
    }

    [Fact]
    public async Task La_bandeja_de_una_persona_trae_SOLO_lo_suyo()
    {
        var reg = Nuevo();
        await reg.RecordAsync("visit_1", "apto-90", "slot-a", Manana, Ana, "Confirmed");
        await reg.RecordAsync("visit_2", "casa-7", "slot-b", Manana, new VisitContact("Beto", "beto@correo.co"), "Confirmed");

        var suyas = await reg.ForVisitorAsync("ana.ruiz@correo.co");

        // Si esto trajera las dos, la bandeja diría a qué hora va a estar OTRA persona en una
        // dirección concreta. Es el mismo IDOR que Eventos cerró quitando el `?holder=`.
        Assert.Single(suyas);
        Assert.Equal("visit_1", suyas[0].VisitId);
    }

    [Fact]
    public async Task El_correo_no_distingue_mayusculas_al_guardar_ni_al_leer()
    {
        // El índice es una comparación EXACTA, así que normalizar sólo en un lado no falla:
        // devuelve una bandeja vacía, que se lee como «no agendaste nada».
        var reg = Nuevo();
        await reg.RecordAsync("visit_1", "apto-90", "slot-a", Manana, Ana, "Confirmed");

        Assert.Single(await reg.ForVisitorAsync("ANA.RUIZ@CORREO.CO"));
        Assert.Single(await reg.ForVisitorAsync("  ana.ruiz@correo.co  "));
    }

    [Fact]
    public async Task La_bandeja_sale_de_la_mas_proxima_a_la_mas_lejana()
    {
        var reg = Nuevo();
        // Anotadas al revés a propósito: si el fixture ya viniera ordenado, quitar el orden
        // pasaría en verde — la regla 7 del repo hermano sobre el dato de prueba.
        await reg.RecordAsync("visit_tarde", "apto-90", "s3", Manana.AddDays(5), Ana, "Confirmed");
        await reg.RecordAsync("visit_pronto", "apto-90", "s1", Manana, Ana, "Confirmed");
        await reg.RecordAsync("visit_medio", "apto-90", "s2", Manana.AddDays(2), Ana, "Confirmed");

        var suyas = await reg.ForVisitorAsync("ana.ruiz@correo.co");

        Assert.Equal(new[] { "visit_pronto", "visit_medio", "visit_tarde" }, suyas.Select(v => v.VisitId));
    }

    [Fact]
    public async Task Una_visita_sin_hora_va_al_final_y_NO_se_inventa_la_fecha()
    {
        // `StartUtc` nulo es «no consta»: el camino que la agendó no supo la hora. Rellenarla
        // con la de hoy daría una fecha plausible y distinta de la que esa persona apuntó —
        // `feedback_a_derived_fallback_must_never_overwrite_what_arrived`.
        var reg = Nuevo();
        await reg.RecordAsync("visit_sin_hora", "apto-90", "s9", null, Ana, "Confirmed");
        await reg.RecordAsync("visit_con_hora", "apto-90", "s1", Manana, Ana, "Confirmed");

        var suyas = await reg.ForVisitorAsync("ana.ruiz@correo.co");

        Assert.Equal(new[] { "visit_con_hora", "visit_sin_hora" }, suyas.Select(v => v.VisitId));
        Assert.Null(suyas[1].StartUtc);
    }

    [Fact]
    public async Task Sin_id_no_se_anota_nada_en_vez_de_fabricar_uno()
    {
        var reg = Nuevo();

        Assert.Null(await reg.RecordAsync(null, "apto-90", "s1", Manana, Ana, "Confirmed"));
        Assert.Empty(await reg.ForVisitorAsync("ana.ruiz@correo.co"));
    }

    [Fact]
    public async Task Repetir_la_misma_visita_no_deja_dos_filas()
    {
        // El seam es idempotente por slot, así que un reintento trae el mismo id: si esto
        // añadiera, la bandeja mostraría la misma visita dos veces tras un doble clic.
        var reg = Nuevo();
        await reg.RecordAsync("visit_1", "apto-90", "s1", Manana, Ana, "Held");
        await reg.RecordAsync("visit_1", "apto-90", "s1", Manana, Ana, "Confirmed");

        var suyas = await reg.ForVisitorAsync("ana.ruiz@correo.co");
        Assert.Single(suyas);
        Assert.Equal("Confirmed", suyas[0].Status);
    }

    [Fact]
    public async Task Un_documento_corrupto_no_tumba_la_bandeja_entera()
    {
        var store = new InMemoryJsonEntityStore();
        var reg = Nuevo(store);
        await reg.RecordAsync("visit_ok", "apto-90", "s1", Manana, Ana, "Confirmed");
        await store.WriteAsync(RealtyVisitLedger.ResourceType, "visit_roto", "{ esto no es json");

        var suyas = await reg.ForVisitorAsync("ana.ruiz@correo.co");

        Assert.Single(suyas);
        Assert.Equal("visit_ok", suyas[0].VisitId);
    }

    [Fact]
    public async Task Sobrevive_a_un_reinicio_del_proceso()
    {
        // Se abre un registro NUEVO sobre el mismo almacén, que es la única forma de tocar el
        // disco: mientras el proceso vive, las lecturas podrían salir de memoria y el defecto
        // #82 —guardar bien y no poder leer— pasaría en verde.
        var store = new InMemoryJsonEntityStore();
        await Nuevo(store).RecordAsync("visit_1", "apto-90", "s1", Manana, Ana, "Confirmed");

        var otro = Nuevo(store);

        Assert.Single(await otro.ForVisitorAsync("ana.ruiz@correo.co"));
    }

    [Fact]
    public async Task La_forma_serializada_conserva_sus_claves()
    {
        // Lo que se vigila es la CLAVE serializada y no el campo: renombrarla compila, y el
        // documento viejo pasa a leerse con el default en silencio (#82 en el almacén).
        var store = new InMemoryJsonEntityStore();
        await Nuevo(store).RecordAsync("visit_1", "apto-90", "s1", Manana, Ana, "Confirmed");

        var json = await store.ReadAsync(RealtyVisitLedger.ResourceType, "visit_1");
        var raiz = JsonDocument.Parse(json!).RootElement;

        foreach (var clave in new[]
                 {
                     "VisitId", "ListingId", "SlotId", "StartUtc", "Mode",
                     "VisitorName", "VisitorEmail", "VisitorPhone", "Status", "BookedAtUtc",
                 })
        {
            Assert.True(raiz.TryGetProperty(clave, out _), $"falta la clave `{clave}` en el documento");
        }
    }

    // ── La modalidad (#160) ──────────────────────────────────────────────────────

    [Fact]
    public async Task La_modalidad_se_guarda_tal_como_la_declaro_quien_agendo()
    {
        var ledger = Nuevo();
        await ledger.RecordAsync("visit_1", "apto-90", "s1", Manana, Ana, "Confirmed", VisitModes.Video);

        // VIDEO: el valor que ningún default produce. Con `in-person` este test pasaría igual
        // con la modalidad tirada y rellenada, que es el defecto que el #160 cierra.
        Assert.Equal(VisitModes.Video, (await ledger.GetAsync("visit_1"))!.Mode);
    }

    [Fact]
    public async Task Sin_modalidad_dice_NO_CONSTA_y_no_presencial()
    {
        var ledger = Nuevo();
        await ledger.RecordAsync("visit_1", "apto-90", "s1", Manana, Ana, "Confirmed");

        // `null` y no `in-person`: es lo que dicen las visitas anteriores al #160, cuando el
        // borde enlazaba la modalidad y nadie la leía. Rellenarlas afirmaría que esa gente
        // pidió que la recibieran en el inmueble — `feedback_an_omitted_key_can_be_an_assertion`.
        Assert.Null((await ledger.GetAsync("visit_1"))!.Mode);
    }

    [Fact]
    public async Task Una_modalidad_que_no_se_reconoce_NO_se_guarda()
    {
        var ledger = Nuevo();
        await ledger.RecordAsync("visit_1", "apto-90", "s1", Manana, Ana, "Confirmed", "presencial");

        // El borde ya la rechazó con un 400; llegar acá con algo raro significa que alguien se
        // saltó la puerta, y anotarlo dejaría en el registro un valor que ninguna pantalla sabe
        // pintar. La segunda guarda, no la primera.
        Assert.Null((await ledger.GetAsync("visit_1"))!.Mode);
    }

    [Fact]
    public async Task Un_documento_anterior_al_160_se_lee_sin_modalidad_y_no_revienta()
    {
        // El JSON que dejó el #158, tal cual: sin la clave `Mode`. Un registro que sólo supiera
        // leer la forma nueva convertiría la bandeja entera de alguien en un hueco.
        var store = new InMemoryJsonEntityStore();
        await store.WriteAsync(RealtyVisitLedger.ResourceType, "visit_viejo", """
            {
              "VisitId": "visit_viejo",
              "ListingId": "apto-90",
              "SlotId": "s1",
              "StartUtc": "2026-09-23T10:00:00+00:00",
              "VisitorName": "Ana Ruiz",
              "VisitorEmail": "ana.ruiz@correo.co",
              "VisitorPhone": "3001234567",
              "Status": "Confirmed",
              "BookedAtUtc": "2026-09-22T10:00:00+00:00"
            }
            """);

        var suyas = await Nuevo(store).ForVisitorAsync("ana.ruiz@correo.co");

        Assert.Single(suyas);
        Assert.Null(suyas[0].Mode);
        Assert.Equal("apto-90", suyas[0].ListingId);
    }
}
