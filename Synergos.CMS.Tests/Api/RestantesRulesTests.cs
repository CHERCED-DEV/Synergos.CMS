using Synergos.Core;
using Consent = Synergos.Api.Consent.Domain;
using Eng = Synergos.Api.Engagement.Domain;
using Ful = Synergos.Api.Fulfillment.Domain;
using Geo = Synergos.Api.Geo.Domain;
using Inv = Synergos.Api.Inventory.Domain;
using Mod = Synergos.Api.Moderation.Domain;
using Msg = Synergos.Api.Messaging.Domain;
using Sign = Synergos.Api.Signing.Domain;
using Wf = Synergos.Api.Workflow.Domain;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Cubre las reglas de las nueve capacidades del cierre del catálogo.
/// </summary>
/// <remarks>
/// Un fichero para las nueve y no nueve ficheros: lo que se prueba de cada una es su puñado de
/// reglas propias, y tenerlas juntas hace evidente que <b>todas rechazan por su cuenta</b> — que
/// es el filtro del doc 07 §1 y lo único que las separa de ser una base de datos con HTTP.
/// </remarks>
public sealed class RestantesRulesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private static Ref R(string kind, string id) => Ref.Create(kind, id);

    // ── CONSENT ─────────────────────────────────────────────────────────────

    [Fact]
    public void Consent_distingue_NUNCA_OTORGADO_de_VENCIDO_y_de_REVOCADO()
    {
        // Los tres llevan a acciones distintas: pedirlo, renovarlo, o respetarlo. Un rechazo
        // genérico dejaría al llamador sin saber cuál de las tres hacer.
        var vigente = new Consent.ConsentGrant("g", R("i.m", "1"), "salud.agenda", "v1", Ahora.AddDays(-1));
        var vencido = vigente with { ExpiresAtUtc = Ahora.AddHours(-1) };
        var revocado = vigente with { RevokedAtUtc = Ahora.AddHours(-1) };

        Assert.Equal("consent.not_granted", Consent.ConsentRules.CheckActive(null, "salud.agenda", Ahora)?.Code);
        Assert.Equal("consent.expired", Consent.ConsentRules.CheckActive(vencido, "salud.agenda", Ahora)?.Code);
        Assert.Equal("consent.revoked", Consent.ConsentRules.CheckActive(revocado, "salud.agenda", Ahora)?.Code);
        Assert.Null(Consent.ConsentRules.CheckActive(vigente, "salud.agenda", Ahora));
    }

    [Fact]
    public void Consent_exige_la_VERSION_del_texto_aceptado()
    {
        // Sin ella, "dio consentimiento" no dice a QUÉ: el día que la política cambie no habrá
        // manera de saber quién aceptó cuál.
        Assert.Equal("consent.policy_version_required",
            Consent.ConsentRules.CheckGrant("salud.agenda", "  ", null, Ahora)?.Code);
        Assert.Equal("consent.bad_purpose", Consent.ConsentRules.CheckGrant("  ", "v1", null, Ahora)?.Code);
    }

    // ── SIGNING ─────────────────────────────────────────────────────────────

    private static Sign.SigningKey Llave(string purpose = "eventos.entrada", DateTimeOffset? retirada = null)
        => new("k1", purpose, Sign.SigningRules.NewSecret(), Ahora.AddDays(-1), retirada);

    [Fact]
    public void Signing_verifica_una_firma_propia_y_rechaza_la_ajena()
    {
        var a = Llave();
        var b = Llave() with { Id = "k2", Secret = Sign.SigningRules.NewSecret() };
        var token = Sign.SigningRules.Sign(a, "entrada-42", Ahora.AddHours(1));

        Assert.Null(Sign.SigningRules.Verify(a, token, Ahora));
        Assert.Equal("signing.bad_signature", Sign.SigningRules.Verify(b, token, Ahora)?.Code);
    }

    [Fact]
    public void Signing_no_deja_correr_el_vencimiento()
    {
        // Va DENTRO de lo firmado: si viajara aparte, un token de una hora sería eterno.
        var key = Llave();
        var token = Sign.SigningRules.Sign(key, "entrada-42", Ahora.AddHours(1));
        var corrido = token.Replace(Ahora.AddHours(1).ToUnixTimeSeconds().ToString(), Ahora.AddYears(1).ToUnixTimeSeconds().ToString(), StringComparison.Ordinal);

        Assert.Equal("signing.bad_signature", Sign.SigningRules.Verify(key, corrido, Ahora)?.Code);
    }

    [Fact]
    public void Signing_comprueba_la_FIRMA_antes_que_el_vencimiento()
    {
        // Al revés, un token vencido diría "vencido" aunque su firma fuera inventada — y eso
        // confirma que el identificador de llave existe.
        var key = Llave();
        var vencido = Sign.SigningRules.Sign(key, "x", Ahora.AddHours(-1));

        Assert.Equal("signing.expired", Sign.SigningRules.Verify(key, vencido, Ahora)?.Code);
        Assert.Equal("signing.bad_signature", Sign.SigningRules.Verify(key, vencido[..^4] + "AAAA", Ahora)?.Code);
    }

    [Fact]
    public void Signing_sobrevive_a_un_payload_con_el_separador()
    {
        // Va en base64url justamente por esto: partir mal el token haría verificar una cosa
        // distinta de la que se firmó.
        var key = Llave();
        var payload = "a.b.c.d";
        var token = Sign.SigningRules.Sign(key, payload, Ahora.AddHours(1));

        Assert.Null(Sign.SigningRules.Verify(key, token, Ahora));
        Assert.True(Sign.SigningRules.TryRead(token, out _, out _, out var leido));
        Assert.Equal(payload, leido);
    }

    // ── INVENTORY ───────────────────────────────────────────────────────────

    private static Inv.StockItem Stock(int onHand, params Inv.StockHold[] holds)
        => new("s1", R("shop.sku", "1"), onHand, Array.Empty<Inv.StockUnit>(), holds);

    private static Inv.StockHold Apartado(int cantidad, params string[] unidades)
        => new("h1", cantidad, unidades, R("orders.pedido", "o1"), Ahora.AddMinutes(10));

    [Fact]
    public void Inventory_cuenta_lo_APARTADO_como_no_disponible()
    {
        var item = Stock(10, Apartado(4));

        Assert.Equal(6, item.Available(Ahora));
        Assert.Null(Inv.InventoryRules.CheckAvailable(item, 6, Ahora));
        Assert.Equal("inventory.insufficient_stock", Inv.InventoryRules.CheckAvailable(item, 7, Ahora)?.Code);
    }

    [Fact]
    public void Inventory_libera_lo_apartado_cuando_el_apartado_VENCE()
    {
        var item = Stock(10, Apartado(4));

        Assert.Equal(10, item.Available(Ahora.AddMinutes(11)));
    }

    [Fact]
    public void Inventory_distingue_unidad_INEXISTENTE_de_unidad_TOMADA()
    {
        // Llevan a acciones distintas: una es un error del llamador, la otra una carrera perdida
        // que se resuelve eligiendo otra butaca.
        var conMapa = new Inv.StockItem("s1", R("eventos.zona", "A"), 3,
            new[] { new Inv.StockUnit("A-1", 1, 1), new Inv.StockUnit("A-2", 1, 2), new Inv.StockUnit("A-3", 1, 3) },
            new[] { Apartado(1, "A-1") });

        Assert.Equal("inventory.unit_not_found", Inv.InventoryRules.CheckUnits(conMapa, new[] { "Z-9" }, Ahora)?.Code);
        Assert.Equal("inventory.unit_taken", Inv.InventoryRules.CheckUnits(conMapa, new[] { "A-1" }, Ahora)?.Code);
        Assert.Null(Inv.InventoryRules.CheckUnits(conMapa, new[] { "A-2" }, Ahora));
    }

    [Fact]
    public void Inventory_no_deja_bajar_el_total_por_debajo_de_lo_apartado()
    {
        // Dejaría apartados que nadie puede cumplir, y el error saldría al entregar en vez de al
        // ajustar.
        var item = Stock(10, Apartado(4));

        Assert.Equal("inventory.below_held", Inv.InventoryRules.CheckAdjust(item, 3, Ahora)?.Code);
        Assert.Null(Inv.InventoryRules.CheckAdjust(item, 4, Ahora));
    }

    // ── WORKFLOW ────────────────────────────────────────────────────────────

    private static Wf.WorkflowDefinition Def() => new("d1", "gov.tramite", "borrador", new[] { "aprobado", "rechazado" },
        new[]
        {
            new Wf.TransitionRule("radicar", "borrador", "radicado", Array.Empty<string>()),
            new Wf.TransitionRule("aprobar", "radicado", "aprobado", new[] { "funcionario" }),
        });

    private static Wf.WorkflowInstance Inst(string estado) =>
        new("i1", "gov.tramite", R("gov.expediente", "e1"), estado, Array.Empty<Wf.HistoryEntry>(), Ahora);

    [Fact]
    public void Workflow_solo_deja_las_transiciones_DEFINIDAS_desde_el_estado_actual()
    {
        var ciudadano = Actor.Of(R("i.m", "1"));

        Assert.True(Wf.WorkflowRules.Resolve(Def(), Inst("borrador"), "radicar", ciudadano).IsOk);

        var mal = Wf.WorkflowRules.Resolve(Def(), Inst("borrador"), "aprobar", ciudadano);
        Assert.Equal("workflow.transition_not_allowed", mal.Rejection!.Code);
        // El mensaje dice qué SÍ se puede: sin eso, el cliente adivina.
        Assert.Contains("radicar", mal.Rejection.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_aplica_la_GUARDA_por_rol()
    {
        // Es lo que hace que esta capacidad sirva a Gobierno: radicar lo hace el ciudadano y
        // aprobar el funcionario.
        var ciudadano = Actor.Of(R("i.m", "1"));
        var funcionario = Actor.Of(R("i.m", "2"), "funcionario");

        Assert.Equal("workflow.role_required",
            Wf.WorkflowRules.Resolve(Def(), Inst("radicado"), "aprobar", ciudadano).Rejection!.Code);
        Assert.True(Wf.WorkflowRules.Resolve(Def(), Inst("radicado"), "aprobar", funcionario).IsOk);
    }

    [Fact]
    public void Workflow_no_deja_salir_de_un_estado_FINAL()
    {
        var r = Wf.WorkflowRules.Resolve(Def(), Inst("aprobado"), "radicar", Actor.Of(R("i.m", "1")));

        Assert.Equal("workflow.instance_closed", r.Rejection!.Code);
    }

    [Fact]
    public void Workflow_rechaza_una_definicion_AMBIGUA()
    {
        // Dos transiciones con el mismo nombre desde el mismo estado harían que la acción
        // llevara a un sitio u otro según el orden del fichero.
        var ambigua = new[]
        {
            new Wf.TransitionRule("x", "a", "b", Array.Empty<string>()),
            new Wf.TransitionRule("x", "a", "c", Array.Empty<string>()),
        };

        Assert.Equal("workflow.ambiguous_transition",
            Wf.WorkflowRules.CheckDefinition("k", "a", Array.Empty<string>(), ambigua)?.Code);
    }

    [Fact]
    public void Workflow_rechaza_un_estado_inicial_sin_salida()
    {
        var sinSalida = new[] { new Wf.TransitionRule("x", "b", "c", Array.Empty<string>()) };

        Assert.Equal("workflow.initial_is_dead_end",
            Wf.WorkflowRules.CheckDefinition("k", "a", Array.Empty<string>(), sinSalida)?.Code);
    }

    // ── MESSAGING ───────────────────────────────────────────────────────────

    private static Msg.MessageThread Hilo(bool cerrado = false)
        => new("t1", R("salud.cita", "c1"), new[] { R("i.m", "1"), R("i.m", "2") }, cerrado, Ahora);

    [Fact]
    public void Messaging_comprueba_la_PERTENENCIA_antes_que_el_cierre()
    {
        // Al revés, alguien ajeno al hilo aprendería que existe y que se cerró.
        var ajeno = R("i.m", "99");

        Assert.Equal("messaging.not_a_participant",
            Msg.MessagingRules.CheckPost(Hilo(cerrado: true), ajeno, "hola", Array.Empty<Ref>())?.Code);
        Assert.Equal("messaging.thread_closed",
            Msg.MessagingRules.CheckPost(Hilo(cerrado: true), R("i.m", "1"), "hola", Array.Empty<Ref>())?.Code);
    }

    [Fact]
    public void Messaging_admite_un_mensaje_solo_con_ADJUNTOS()
    {
        var conAdjunto = Msg.MessagingRules.CheckPost(Hilo(), R("i.m", "1"), null, new[] { R("documents.doc", "d1") });
        var vacio = Msg.MessagingRules.CheckPost(Hilo(), R("i.m", "1"), "  ", Array.Empty<Ref>());

        Assert.Null(conAdjunto);
        Assert.Equal("messaging.empty_message", vacio?.Code);
    }

    [Fact]
    public void Messaging_exige_al_menos_DOS_participantes()
    {
        // Un hilo de una sola persona es una nota, no una conversación.
        Assert.Equal("messaging.too_few_participants",
            Msg.MessagingRules.CheckOpen(new[] { R("i.m", "1") })?.Code);
        Assert.Equal("messaging.duplicate_participants",
            Msg.MessagingRules.CheckOpen(new[] { R("i.m", "1"), R("i.m", "1") })?.Code);
    }

    // ── ENGAGEMENT ──────────────────────────────────────────────────────────

    [Fact]
    public void Engagement_exige_de_cada_clase_lo_que_esa_clase_necesita()
    {
        Assert.Equal("engagement.body_required", Eng.EngagementRules.CheckShape(Eng.EngagementKind.Comment, "  ", null, null)?.Code);
        Assert.Equal("engagement.rating_required", Eng.EngagementRules.CheckShape(Eng.EngagementKind.Rating, null, null, null)?.Code);
        Assert.Equal("engagement.reaction_code_required", Eng.EngagementRules.CheckShape(Eng.EngagementKind.Reaction, null, null, "  ")?.Code);
        Assert.Null(Eng.EngagementRules.CheckShape(Eng.EngagementKind.Favorite, null, null, null));
    }

    [Fact]
    public void Engagement_acota_el_puntaje()
    {
        Assert.Equal("engagement.rating_out_of_range", Eng.EngagementRules.CheckShape(Eng.EngagementKind.Rating, null, 0, null)?.Code);
        Assert.Equal("engagement.rating_out_of_range", Eng.EngagementRules.CheckShape(Eng.EngagementKind.Rating, null, 6, null)?.Code);
        Assert.Null(Eng.EngagementRules.CheckShape(Eng.EngagementKind.Rating, null, 5, null));
    }

    [Fact]
    public void Engagement_deja_comentar_varias_veces_pero_no_VALORAR_dos()
    {
        // Si se pudiera valorar diez veces, el promedio dejaría de significar nada — y es
        // justamente la métrica que la gente mira para decidir.
        var existente = new Eng.Engagement("e1", R("i.m", "1"), R("shop.sku", "1"),
            Eng.EngagementKind.Rating, null, 5, null, null, true, Ahora);

        Assert.Null(Eng.EngagementRules.CheckDuplicate(Eng.EngagementKind.Comment, existente));
        Assert.Equal("engagement.already_rating", Eng.EngagementRules.CheckDuplicate(Eng.EngagementKind.Rating, existente)?.Code);
    }

    // ── GEO ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Geo_rechaza_puntos_fuera_del_planeta()
    {
        Assert.Equal("geo.bad_latitude", Geo.GeoRules.CheckCoordinates(91, 0)?.Code);
        Assert.Equal("geo.bad_longitude", Geo.GeoRules.CheckCoordinates(0, 181)?.Code);
        Assert.Equal("geo.coordinates_required", Geo.GeoRules.CheckCoordinates(null, 0)?.Code);
        Assert.Null(Geo.GeoRules.CheckCoordinates(6.24, -75.58));   // Medellín
    }

    [Fact]
    public void Geo_mide_con_el_SEMIVERSENO_y_no_como_si_el_mundo_fuera_plano()
    {
        // Medellín (6.2442, -75.5812) a Bogotá (4.7110, -74.0721): unos 240 km reales. Tratar
        // los grados como plano daría un número muy distinto, y "los 5 km más cercanos" traería
        // cosas a 7 km hacia el este.
        var km = Geo.GeoRules.DistanceKm(6.2442, -75.5812, 4.7110, -74.0721);

        Assert.InRange(km, 230, 250);
        Assert.Equal(0, Geo.GeoRules.DistanceKm(6.2442, -75.5812, 6.2442, -75.5812), 6);
    }

    [Fact]
    public void Geo_acota_el_radio()
    {
        // Sin tope, un radio de veinte mil kilómetros devuelve el índice entero.
        Assert.Null(Geo.GeoRules.CheckRadius(Geo.GeoRules.MaxRadiusKm));
        Assert.Equal("geo.bad_radius", Geo.GeoRules.CheckRadius(Geo.GeoRules.MaxRadiusKm + 1)?.Code);
        Assert.Equal("geo.bad_radius", Geo.GeoRules.CheckRadius(0)?.Code);
    }

    // ── FULFILLMENT ─────────────────────────────────────────────────────────

    private static Ful.Address Direccion(string? ciudad = "Medellín", string? pais = "CO")
        => new("Cra 70 #44-30", null, ciudad ?? string.Empty, "Antioquia", null, pais ?? string.Empty, "3001234567");

    private static Ful.Shipment Envio(Ful.ShipmentStatus estado)
        => new("s1", R("orders.pedido", "o1"), Direccion(), "servientrega", "TR-1",
            estado, Array.Empty<Ful.TrackingEvent>(), Ahora);

    [Fact]
    public void Fulfillment_no_exige_codigo_postal_pero_si_ciudad_y_pais()
    {
        // En Colombia mucha gente no sabe su código postal, y exigirlo bloquearía entregas
        // perfectamente posibles.
        Assert.Null(Ful.FulfillmentRules.CheckAddress(Direccion()));
        Assert.Equal("fulfillment.address_city_required", Ful.FulfillmentRules.CheckAddress(Direccion(ciudad: " "))?.Code);
        Assert.Equal("fulfillment.address_country_required", Ful.FulfillmentRules.CheckAddress(Direccion(pais: "Colombia"))?.Code);
    }

    [Fact]
    public void Fulfillment_no_deja_RETROCEDER_desde_un_estado_final()
    {
        // Los eventos de la transportadora llegan tarde y desordenados: sin esto, el cliente
        // vería "en tránsito" un paquete que ya tiene en la mano.
        Assert.Equal("fulfillment.shipment_closed",
            Ful.FulfillmentRules.CheckTransition(Envio(Ful.ShipmentStatus.Delivered), Ful.ShipmentStatus.InTransit)?.Code);
        Assert.Null(Ful.FulfillmentRules.CheckTransition(Envio(Ful.ShipmentStatus.InTransit), Ful.ShipmentStatus.Delivered));
    }

    [Fact]
    public void Fulfillment_no_salta_de_listo_a_entregado()
    {
        Assert.Equal("fulfillment.transition_not_allowed",
            Ful.FulfillmentRules.CheckTransition(Envio(Ful.ShipmentStatus.Ready), Ful.ShipmentStatus.Delivered)?.Code);
    }

    // ── MODERATION ──────────────────────────────────────────────────────────

    private static Mod.ModerationCase Caso(Mod.CaseStatus estado)
        => new("c1", R("engagement.comentario", "e1"), R("i.m", "9"), "spam",
            estado, estado == Mod.CaseStatus.Decided ? Mod.Decision.Remove : null,
            estado == Mod.CaseStatus.Decided ? R("i.m", "mod") : null, "por spam", Ahora, Ahora);

    [Fact]
    public void Moderation_exige_QUIEN_decide_y_POR_QUE()
    {
        // Una moderación anónima y sin motivo es indistinguible de un borrado arbitrario.
        Assert.Equal("moderation.moderator_required",
            Mod.ModerationRules.CheckDecidable(Caso(Mod.CaseStatus.Open), null, "porque sí")?.Code);
        Assert.Equal("moderation.note_required",
            Mod.ModerationRules.CheckDecidable(Caso(Mod.CaseStatus.Open), R("i.m", "mod"), "  ")?.Code);
        Assert.Null(Mod.ModerationRules.CheckDecidable(Caso(Mod.CaseStatus.Open), R("i.m", "mod"), "spam evidente"));
    }

    [Fact]
    public void Moderation_no_deja_REDECIDIR_en_silencio()
    {
        // Dos moderadores podrían resolver lo mismo al revés y el último ganaría sin que nadie
        // lo notara.
        Assert.Equal("moderation.already_decided",
            Mod.ModerationRules.CheckDecidable(Caso(Mod.CaseStatus.Decided), R("i.m", "otro"), "no, dejarlo")?.Code);
    }

    [Fact]
    public void Moderation_exige_motivo_en_el_reporte()
    {
        Assert.Equal("moderation.bad_reason", Mod.ModerationRules.CheckReport("  ")?.Code);
        Assert.Null(Mod.ModerationRules.CheckReport("spam"));
    }
}
