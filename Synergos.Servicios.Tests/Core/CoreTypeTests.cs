using Synergos.Core;

namespace Synergos.CMS.Tests.Core;

/// <summary>
/// Cubre los tipos de <c>Synergos.Core</c> — el vocabulario que hablan las dieciséis capacidades.
/// </summary>
/// <remarks>
/// <para>Lo que se prueba aquí no es "el record guarda lo que le pasan": es <b>la regla que cada
/// tipo carga</b>. Si esas reglas no están probadas, los tipos son records con comentarios
/// bonitos y la garantía se pierde en la primera capacidad que los use.</para>
/// </remarks>
public sealed class CoreTypeTests
{
    // ── Money ───────────────────────────────────────────────────────────────

    [Fact]
    public void Sumar_monedas_distintas_LANZA_en_vez_de_adivinar()
    {
        // Es la razón entera de que Money exista. Un decimal suelto permite sumar pesos con
        // dólares sin que nada se queje, y el error no sale en un test: sale en un estado de
        // cuenta. Que lance y no que convierta es deliberado — no hay conversión correcta sin
        // una tasa.
        var pesos = Money.Of(1000m, "COP");
        var dolares = Money.Of(5m, "USD");

        Assert.Throws<InvalidOperationException>(() => pesos + dolares);
        Assert.Throws<InvalidOperationException>(() => pesos - dolares);
        Assert.Throws<InvalidOperationException>(() => pesos.CompareTo(dolares));
    }

    [Fact]
    public void La_moneda_se_normaliza_a_mayusculas()
    {
        // Sin esto, "cop" y "COP" serían monedas distintas y sumarlas lanzaría — un fallo
        // absurdo en producción por una diferencia de tipeo en configuración.
        Assert.Equal("COP", Money.Of(10m, "cop").Currency);
        Assert.Equal(Money.Of(30m, "COP"), Money.Of(10m, "cop") + Money.Of(20m, "COP"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("PESOS")]
    [InlineData("CO")]
    public void Un_codigo_de_moneda_invalido_no_construye(string codigo)
    {
        Assert.Throws<ArgumentException>(() => Money.Of(1m, codigo));
    }

    [Fact]
    public void Redondea_a_la_par_y_no_siempre_hacia_arriba()
    {
        // Redondear siempre hacia arriba sesga los totales al alza, y sobre miles de líneas
        // eso es dinero que aparece de la nada.
        Assert.Equal(2.2m, Money.Of(2.25m, "USD").Round(1).Amount);
        Assert.Equal(2.4m, Money.Of(2.35m, "USD").Round(1).Amount);
    }

    [Fact]
    public void Sumar_una_lista_vacia_da_cero_en_la_moneda_pedida()
    {
        var total = Money.Sum(Array.Empty<Money>(), "COP");

        Assert.True(total.IsZero);
        Assert.Equal("COP", total.Currency);
    }

    // ── TimeWindow ──────────────────────────────────────────────────────────

    [Fact]
    public void Dos_ventanas_CONTIGUAS_no_se_solapan()
    {
        // El error clásico de las agendas. Con intervalos cerrados, 10:00–10:30 y 10:30–11:00
        // "chocan" en el instante 10:30 y la agenda rechaza reservas perfectamente válidas.
        var manana = Ventana(10, 0, 10, 30);
        var siguiente = Ventana(10, 30, 11, 0);

        Assert.False(manana.Overlaps(siguiente));
        Assert.False(siguiente.Overlaps(manana));
    }

    [Fact]
    public void Un_solapamiento_de_verdad_se_detecta_en_los_dos_sentidos()
    {
        var a = Ventana(10, 0, 11, 0);
        var b = Ventana(10, 30, 11, 30);

        Assert.True(a.Overlaps(b));
        Assert.True(b.Overlaps(a));
    }

    [Fact]
    public void Una_ventana_invertida_o_vacia_NO_construye()
    {
        // Una ventana invertida no es "nada reservado": es un error de quien la construyó, y
        // dejarla pasar lo convierte en "no hay disponibilidad" investigado durante horas.
        var inicio = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => TimeWindow.Of(inicio, inicio.AddHours(-1)));
        Assert.Throws<ArgumentException>(() => TimeWindow.Of(inicio, inicio));
    }

    [Fact]
    public void El_fin_queda_FUERA_por_ser_exclusivo()
    {
        var v = Ventana(10, 0, 11, 0);

        Assert.True(v.Contains(v.Start));
        Assert.False(v.Contains(v.End));
    }

    [Fact]
    public void El_reloj_se_PASA_y_no_se_lee_por_dentro()
    {
        // Un tipo que mira DateTimeOffset.UtcNow por su cuenta no se puede probar sin esperar,
        // y la mitad de los errores de agenda son de borde temporal — justo los que hay que
        // poder reproducir.
        var v = Ventana(10, 0, 11, 0);

        Assert.False(v.HasEnded(v.End.AddTicks(-1)));
        Assert.True(v.HasEnded(v.End));
    }

    private static TimeWindow Ventana(int h1, int m1, int h2, int m2) => TimeWindow.Of(
        new DateTimeOffset(2026, 3, 1, h1, m1, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 3, 1, h2, m2, 0, TimeSpan.Zero));

    // ── Ref ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_Ref_con_id_que_contiene_dos_puntos_sobrevive_la_ida_y_vuelta()
    {
        // Partir por el ÚLTIMO separador devolvería un tipo distinto del guardado, en
        // silencio. Un id puede ser un URN o una ruta.
        var original = Ref.Create("salud.profesional", "urn:npi:1234:5678");

        var vuelta = Ref.Parse(original.ToString());

        Assert.Equal(original, vuelta);
        Assert.Equal("salud.profesional", vuelta.Kind);
        Assert.Equal("urn:npi:1234:5678", vuelta.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin-separador")]
    [InlineData(":sin-tipo")]
    [InlineData("sin-id:")]
    public void Un_Ref_a_medias_no_parsea(string texto)
    {
        Assert.False(Ref.TryParse(texto, out _));
        Assert.Throws<FormatException>(() => Ref.Parse(texto));
    }

    [Theory]
    [InlineData("", "id")]
    [InlineData("  ", "id")]
    [InlineData("kind", "")]
    public void Un_Ref_a_medias_no_se_CONSTRUYE(string kind, string id)
    {
        // Guardarlo a medias es peor que no guardarlo: explota al resolverlo, lejos de donde
        // se creó y sin rastro de quién lo puso.
        Assert.Throws<ArgumentException>(() => Ref.Create(kind, id));
    }

    // ── Rejection ───────────────────────────────────────────────────────────

    [Fact]
    public void Solo_Unavailable_es_transitorio()
    {
        // La decisión de reintentar se toma UNA vez, aquí. Un Conflict tienta —"el cupo se
        // puede liberar"— pero reintentarlo en un lazo es una tormenta contra un servicio que
        // ya dijo que no.
        Assert.True(Rejection.Unavailable("x", "y").IsTransient);

        foreach (var kind in Enum.GetValues<RejectionKind>().Where(k => k != RejectionKind.Unavailable))
        {
            Assert.False(new Rejection(kind, "x", "y").IsTransient, $"{kind} no puede ser transitorio");
        }
    }

    // ── Result ──────────────────────────────────────────────────────────────

    [Fact]
    public void Leer_el_valor_de_un_rechazo_LANZA_en_vez_de_dar_null()
    {
        // Devolver default aquí sería el peor de los mundos: el llamador seguiría con un cero
        // o un null que parece un dato y no lo es.
        Result<int> r = Rejection.Conflict("booking.slot_taken", "el cupo ya se tomó");

        Assert.False(r.IsOk);
        var boom = Assert.Throws<InvalidOperationException>(() => r.Value);
        Assert.Contains("booking.slot_taken", boom.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_Result_es_o_valor_o_rechazo_nunca_los_dos()
    {
        Result<string> ok = "listo";

        Assert.True(ok.IsOk);
        Assert.Null(ok.Rejection);
        Assert.Equal("listo", ok.Value);
    }

    [Fact]
    public void Map_transforma_el_valor_y_deja_pasar_el_rechazo_INTACTO()
    {
        // Si Map perdiera el rechazo, el orquestador vería un fallo genérico donde había una
        // causa concreta — y la compensación correcta depende de cuál era.
        var rechazo = Rejection.Expired("booking.hold_expired", "el hold venció");
        Result<int> malo = rechazo;

        var mapeado = malo.Map(n => n.ToString());

        Assert.False(mapeado.IsOk);
        Assert.Same(rechazo, mapeado.Rejection);
    }

    // ── Actor ───────────────────────────────────────────────────────────────

    [Fact]
    public void El_anonimo_es_un_VALOR_y_no_un_null()
    {
        // Un null aquí se propaga hasta que alguien lo desreferencia, y el sitio donde revienta
        // nunca es el sitio donde faltaba.
        Assert.True(Actor.Anonymous.IsAnonymous);
        Assert.Empty(Actor.Anonymous.Roles);
        Assert.False(Actor.Anonymous.HasAnyRole("admin"));
    }

    [Fact]
    public void Los_roles_no_distinguen_mayusculas()
    {
        // Tenerlo distinto a los dos lados de la red sería una trampa preparada: el CMS ya
        // compara roles sin sensibilidad a mayúsculas.
        var actor = Actor.Of(Ref.Create("identity.member", "m-1"), "Admin", " editor ", "");

        Assert.True(actor.HasAnyRole("admin"));
        Assert.True(actor.HasAnyRole("EDITOR"));
        Assert.Equal(2, actor.Roles.Count);   // el vacío se descarta
    }

    // ── IdempotencyKey ──────────────────────────────────────────────────────

    [Fact]
    public void Una_llave_vacia_no_construye()
    {
        // Una llave vacía no protege de nada, y da la sensación de que sí — que es peor.
        Assert.Throws<ArgumentException>(() => IdempotencyKey.Of("   "));
    }

    [Fact]
    public void Las_partes_se_unen_con_separador_para_que_no_colisionen()
    {
        // ("ab","c") y ("a","bc") concatenadas darían la misma llave, y serían dos
        // operaciones distintas compartiendo protección: una de las dos se perdería.
        Assert.NotEqual(IdempotencyKey.From("ab", "c"), IdempotencyKey.From("a", "bc"));
    }

    // ── Page ────────────────────────────────────────────────────────────────

    [Fact]
    public void HasMore_usa_el_total_y_no_lo_que_vino()
    {
        // Confundir "cuántos hay" con "cuántos vinieron" produce el clásico "página 2 de 1".
        var pagina = new Page<int>(new[] { 1, 2, 3 }, Total: 10, Offset: 0);
        var ultima = new Page<int>(new[] { 9, 10 }, Total: 10, Offset: 8);

        Assert.True(pagina.HasMore);
        Assert.False(ultima.HasMore);
        Assert.False(Page<int>.Empty().HasMore);
    }
}
