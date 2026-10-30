using System.Net;
using System.Text.Json;
using Synergos.Bff.Core;
using Synergos.Bff.Viajes.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// La confirmación parcial de un viaje POR LA PUERTA (línea 006, historia 3, T029–T030; NV-13 del núcleo): el conductor
/// declarado de lo que <c>TripPartialConfirmTests</c> fija para <c>/v1/trips</c>, contra la agenda, precios y pagos reales.
/// </summary>
/// <remarks>
/// <para><b>Lo que cambia respecto de la ruta directa, a propósito</b> (research R9, S-6): por la puerta la parte del ítem
/// caído se DEVUELVE sola, con el precio que cotizó el servidor —<c>No_se_devuelve_plata_sola</c> sigue siendo la regla de
/// <c>/v1/trips</c>, donde la ordena el CMS—. La política es del sitio (cabecera de negocio), nunca del navegador.</para>
///
/// <para>El cierre que falla se inyecta en el cable: la confirmación del n-ésimo apartado contesta
/// <c>booking.hold_expired</c>, como un apartado que venció entre cobrar y confirmar.</para>
/// </remarks>
public sealed class ParcialDelViajeTests
{
    /// <summary>Rechaza en el cable las confirmaciones de la agenda cuyo orden esté en <paramref name="cuales"/> (1, 2, 3).</summary>
    private static Func<HttpRequestMessage, string?> FallaLaConfirmacion(params int[] cuales)
    {
        var confirmaciones = 0;
        return m => m.Method == HttpMethod.Post && m.RequestUri!.AbsolutePath.EndsWith("/confirm", StringComparison.Ordinal)
                    && cuales.Contains(Interlocked.Increment(ref confirmaciones))
            ? "booking.hold_expired"
            : null;
    }

    private static async Task<(ViajesReservaReal R, string Id)> AbiertoCon(Func<HttpRequestMessage, string?> falla, string politica)
    {
        var r = await new ViajesReservaReal(falla).ConLosTres();
        var (abrio, abierta) = await r.Abrir(politica: politica);
        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
        return (r, abierta.GetProperty("id").GetString()!);
    }

    private static async Task<JsonElement> Cobro(ViajesReservaReal r, string id)
        => await r.Leer("payments", $"v1/payments/{r.Dato(id, "cobroId")}");

    private static decimal Devuelto(JsonElement cobro) => cobro.GetProperty("refunded").GetProperty("amount").GetDecimal();

    // ── Escenario 1: lo que salió se queda; lo caído se suelta y se devuelve su parte ───────────────

    [Fact]
    public async Task Con_la_politica_parcial_el_auto_que_no_confirma_se_suelta_se_devuelve_su_parte_y_el_viaje_queda_confirmado()
    {
        var (r, id) = await AbiertoCon(FallaLaConfirmacion(3), ExtensionDeViajes.Parcial);
        using var arnes = r;

        var (cerro, cerrada) = await r.Cerrar(id);

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal(0, cerrada.GetProperty("compensacionesPendientes").GetInt32());

        // Lo dice el servidor, ítem por ítem: qué no se cumplió y cuánto le toca.
        var itinerario = cerrada.GetProperty("datos").GetProperty("itinerario").EnumerateArray().ToList();
        Assert.False(itinerario[0].TryGetProperty("noCumplido", out _));
        Assert.False(itinerario[1].TryGetProperty("noCumplido", out _));
        Assert.True(itinerario[2].GetProperty("noCumplido").GetBoolean());
        Assert.Equal(200_000m, itinerario[2].GetProperty("devuelto").GetProperty("amount").GetDecimal());
        Assert.Equal(new[] { 300_000m, 700_000m, 200_000m },
            itinerario.Select(p => p.GetProperty("precio").GetProperty("amount").GetDecimal()));

        // La plata: cobrado entero, devuelta la parte del auto, una vez.
        var cobro = await Cobro(r, id);
        Assert.Equal(1_200_000m, cobro.GetProperty("amount").GetProperty("amount").GetDecimal());
        Assert.Equal(200_000m, Devuelto(cobro));

        // La agenda: el vuelo y el hotel confirmados; el auto, suelto.
        Assert.Equal(2, (await r.Reservas()).Count(x => x.Estado == "Confirmed"));
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Auto, ViajesReservaReal.Salida.AddHours(2), ViajesReservaReal.Salida.AddDays(2)));

        // Y el aviso salió: un viaje parcial también está confirmado.
        Assert.Single(await r.Avisos());
        Assert.Contains(r.Sagas.Find(id)!.Compensations, c => c.Kind == DevolverParteDelItem.Clase && !c.IsPending);
    }

    // ── Escenario 2: la misma falla en un sitio «todo o nada» ──────────────────────────────────────

    [Fact]
    public async Task Con_todo_o_nada_el_mismo_fallo_deshace_el_viaje_entero_como_siempre()
    {
        var (r, id) = await AbiertoCon(FallaLaConfirmacion(3), ViajesReservaReal.TodoONada);
        using var arnes = r;

        var (cerro, cerrada) = await r.Cerrar(id);

        Assert.NotEqual(HttpStatusCode.OK, cerro);
        var saga = r.Sagas.Find(id)!;
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        Assert.DoesNotContain(saga.Compensations, c => c.Kind == DevolverParteDelItem.Clase);
        Assert.Equal(1_200_000m, Devuelto(await Cobro(r, id)));
        Assert.DoesNotContain(await r.Reservas(), x => x.Estado == "Confirmed");
        Assert.True(cerrada.ValueKind == JsonValueKind.Object, cerrada.ToString());
    }

    // ── Escenario 3: ningún ítem confirmable ────────────────────────────────────────────────────────

    [Fact]
    public async Task Con_parcial_y_ningun_item_confirmable_el_viaje_falla_y_se_devuelve_lo_cobrado_una_sola_vez()
    {
        var (r, id) = await AbiertoCon(FallaLaConfirmacion(1, 2, 3), ExtensionDeViajes.Parcial);
        using var arnes = r;

        var (cerro, cerrada) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.Conflict, cerro);
        Assert.Equal("flow.nada_cumplido", cerrada.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        // Las tres partes y el resto de la devolución común suman lo cobrado, ni un peso más (SC-004).
        var cobro = await Cobro(r, id);
        Assert.Equal(cobro.GetProperty("amount").GetProperty("amount").GetDecimal(), Devuelto(cobro));
        Assert.DoesNotContain(await r.Reservas(), x => x.Estado == "Confirmed");
        Assert.Empty(await r.Avisos());
    }

    // ── Escenario 4: lo que no se pudo hacer queda pendiente, no se pierde ─────────────────────────

    /// <summary>
    /// Si la devolución de la parte no sale, el viaje termina igual (lo confirmado ya es de quien viaja) pero la devolución
    /// NO se da por hecha: queda pendiente y la respuesta lo dice.
    /// </summary>
    /// <remarks>
    /// Quién la reintenta es del núcleo: hoy el barrido sólo recorre sagas que se están deshaciendo, y ésta está completada
    /// (lo dice <c>FlowRunner</c>, con la misma limitación de <c>TripFlow</c>). Pedido como NV-17 en
    /// <c>necesidades-del-nucleo.md</c>.
    /// </remarks>
    [Fact]
    public async Task Si_la_devolucion_de_la_parte_falla_queda_pendiente_y_el_viaje_lo_dice()
    {
        var devoluciones = 0;
        var confirmacion = FallaLaConfirmacion(3);
        var (r, id) = await AbiertoCon(m =>
            m.Method == HttpMethod.Post && m.RequestUri!.AbsolutePath.EndsWith("/refund", StringComparison.Ordinal)
            && Interlocked.Increment(ref devoluciones) == 1
                ? "payments.unavailable"
                : confirmacion(m), ExtensionDeViajes.Parcial);
        using var arnes = r;

        var (cerro, cerrada) = await r.Cerrar(id);

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal(1, cerrada.GetProperty("compensacionesPendientes").GetInt32());
        var pendiente = Assert.Single(r.Sagas.Find(id)!.Compensations, c => c.Kind == DevolverParteDelItem.Clase);
        Assert.True(pendiente.IsPending, "La devolución de la parte que no salió tiene que quedar pendiente.");
        Assert.Equal(0m, Devuelto(await Cobro(r, id)));
        // Y el auto quedó suelto igual: lo que falló es la plata, no el cupo.
        Assert.Equal(0, await r.Ocupados(ViajesReservaReal.Auto, ViajesReservaReal.Salida.AddHours(2), ViajesReservaReal.Salida.AddDays(2)));
    }

    // ── Escenario 5: repetir confirmar no repite nada ───────────────────────────────────────────────

    [Fact]
    public async Task Repetir_cerrar_un_viaje_parcial_no_reintenta_lo_caido_ni_devuelve_dos_veces()
    {
        var (r, id) = await AbiertoCon(FallaLaConfirmacion(3), ExtensionDeViajes.Parcial);
        using var arnes = r;
        await r.Cerrar(id);
        var escriturasEnLaAgenda = r.Escrituras("booking");

        var (cerro, cerrada) = await r.Cerrar(id);

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal(escriturasEnLaAgenda, r.Escrituras("booking"));
        Assert.Equal(200_000m, Devuelto(await Cobro(r, id)));
        Assert.Single(await r.Avisos());
    }

    // ── Escenario 6: la política y el monto no los pone el navegador ────────────────────────────────

    /// <summary>
    /// La política es una entrada de NEGOCIO y el total sale de la cotización: un cuerpo que los traiga no se acepta, antes
    /// de apartar ni cobrar nada. Lo que quien viaja no puede decidir no se le deja decir.
    /// </summary>
    [Theory]
    [InlineData("politica")]
    [InlineData("total")]
    public async Task Una_politica_o_un_total_en_el_cuerpo_no_se_aceptan_antes_de_tocar_nada(string campo)
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var cuerpo = new Dictionary<string, object>
        {
            ["items"] = ViajesReservaReal.ItinerarioDePrueba(),
            [campo] = campo == "politica" ? ExtensionDeViajes.Parcial : ViajesReservaReal.Pesos(1),
        };
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ViajesReservaReal.Flujo) { Content = System.Net.Http.Json.JsonContent.Create(cuerpo) };
        peticion.Headers.Add("Idempotency-Key", "viaje-del-navegador");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, ViajesReservaReal.Sujeto("u-1"));
        ViajesReservaReal.ConNegocio(peticion, ViajesReservaReal.TodoONada);

        using var abrio = await r.Orquestador.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.BadRequest, abrio.StatusCode);
        Assert.Equal("viajes.entrada_invalida", JsonDocument.Parse(await abrio.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        Assert.Equal(0, r.Escrituras("booking") + r.Escrituras("payments"));
    }

    // ── Lo que no depende de una capacidad ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1_200_000, 1_200_000, 200_000, 200_000)] // sin descuento: su precio
    [InlineData(1_000_000, 1_200_000, 200_000, 166_666)] // con descuento: reparte, hacia abajo
    [InlineData(1_380_000, 1_200_000, 700_000, 805_000)] // con impuesto: también lo reparte
    public void La_parte_de_un_item_es_total_por_precio_entre_subtotal_hacia_abajo(
        decimal total, decimal subtotal, decimal precio, decimal parte)
    {
        Assert.Equal(Money.Of(parte, "COP"),
            ExtensionDeViajes.Parte(Money.Of(total, "COP"), Money.Of(subtotal, "COP"), Money.Of(precio, "COP")));
    }

    [Fact]
    public void Sin_precio_sin_subtotal_o_sin_cobro_no_hay_parte_que_devolver()
    {
        var cop = (decimal m) => Money.Of(m, "COP");
        Assert.Null(ExtensionDeViajes.Parte(cop(100), cop(100), null));
        Assert.Null(ExtensionDeViajes.Parte(cop(100), cop(0), cop(50)));
        Assert.Null(ExtensionDeViajes.Parte(cop(0), cop(100), cop(50)));
        Assert.Null(ExtensionDeViajes.Parte(cop(100), cop(100), Money.Of(50, "USD")));
    }

    [Fact]
    public void Las_partes_de_todos_los_items_nunca_pasan_de_lo_cobrado()
    {
        var precios = new[] { 333_333m, 333_333m, 333_334m };
        var total = Money.Of(999_999m, "COP");
        var subtotal = Money.Of(precios.Sum(), "COP");

        var suma = precios.Sum(p => ExtensionDeViajes.Parte(total, subtotal, Money.Of(p, "COP"))!.Value.Amount);

        Assert.True(suma <= total.Amount, $"Las partes suman {suma} y se cobró {total.Amount}.");
    }

    [Theory]
    [InlineData("parcial", true)]
    [InlineData(" Parcial ", true)]
    [InlineData("todo-o-nada", false)]
    [InlineData("parcialmente", false)]
    [InlineData(null, false)]
    public void Solo_la_politica_parcial_suelta(string? politica, bool suelta)
    {
        var ctx = new Synergos.Bff.Core.Flow.FlowContext().Set("politica", politica);
        Assert.Equal(suelta, new ExtensionDeViajes().SueltaSiFalla(null!, ctx));
    }

    [Fact]
    public void La_nota_del_aviso_dice_que_no_se_confirmo_y_cuanto_se_devuelve_y_nada_si_todo_salio()
    {
        var inicio = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var vuelo = new ParadaDelViaje("V", "Vuelo", inicio, inicio.AddHours(2), Money.Of(300_000m, "COP"));
        var auto = new ParadaDelViaje("A", "Auto económico", inicio, inicio.AddDays(2), Money.Of(200_000m, "COP"));

        Assert.Equal(string.Empty, PasoNotaDelCierre.Nota([vuelo, auto]));
        Assert.Equal(" No se pudo confirmar: Auto económico. Te devolvemos su parte: 200.000 COP.",
            PasoNotaDelCierre.Nota([vuelo, auto with { NoCumplido = true, Devuelto = Money.Of(200_000m, "COP") }]));
    }
}
