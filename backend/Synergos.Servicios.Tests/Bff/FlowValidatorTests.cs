using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Eventos.Clients;
using Synergos.Bff.Pasos;
using Synergos.Bff.Eventos.Domain;
using Synergos.CMS.Tests.Architecture;   // Proyectos: dónde vive cada proyecto (#136)

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Un flujo mal declarado no ARRANCA (ADR 0140): ni el orquestador, ni el intérprete.
/// </summary>
/// <remarks>
/// <para><b>Cada mutante parte de la definición de verdad</b>, <c>flujos/eventos.compra.json</c>, y
/// cambia UNA cosa — y antes de juzgar comprueba que el cambio entró: una mutación que no se aplicó
/// deja la definición intacta y el test en verde por la razón equivocada.</para>
///
/// <para><b>Vive acá y no en <c>Synergos.Arquitectura.Tests</c></b>, a propósito: el arranque de
/// verdad necesita referenciar <c>Bff.Eventos</c>, y esa suite está acotada a ver el árbol por la
/// fuente (CLAUDE.md §0.A.9).</para>
/// </remarks>
public sealed class FlowValidatorTests
{
    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("Validar una definición no llama a ninguna capacidad.");
    }

    private static readonly IRegistroDePasos Registro = EventosPasos.Registro(new EventosCapabilities(new SinRed()));

    /// <summary>Lo que el código de Eventos da por hecho de su definición: el mismo que valida el arranque.</summary>
    private static readonly ContratoDelFlujo Contrato = ContratoDelFlujo.De(new EventosFlowBinding(), TicketingFlow.Fases);

    private static string Original()
        => File.ReadAllText(Proyectos.Dir("Synergos.Bff.Eventos", "flujos", "eventos.compra.json"));

    private static string Mutar(string antes, string despues)
    {
        var original = Original();
        Assert.True(original.Contains(antes, StringComparison.Ordinal),
            $"La mutación no entra: la definición ya no dice «{antes}». Revisar este test antes que el validador.");
        return original.Replace(antes, despues, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Errores(string json, ContratoDelFlujo? contrato = null)
        => FlowValidator.Validar(FlujoDef.Leer(json), Registro, contrato ?? Contrato);

    [Fact]
    public void La_definicion_de_eventos_compra_valida_contra_sus_pasos_y_su_saga()
    {
        // El control de todos los de abajo: sin él, un validador que rechazara TODO los pondría
        // en verde a la vez.
        Assert.Empty(Errores(Original()));
        Assert.Empty(FlowValidator.Validar(EventosFlujos.Compra, Registro, Contrato));
    }

    [Fact]
    public void Un_tipo_de_paso_que_nadie_registro_no_pasa()
    {
        var errores = Errores(Mutar("\"tipo\": \"inventory.consumir\"", "\"tipo\": \"inventory.entregar\""));

        Assert.Contains(errores, e => e.Contains("«inventory.entregar»", StringComparison.Ordinal));
    }

    [Fact]
    public void Una_lectura_sin_escritura_previa_no_pasa()
    {
        var errores = Errores(Mutar("\"lee\": [\"paymentId\"]", "\"lee\": [\"cobroId\"]"));

        Assert.Contains(errores, e => e.Contains("lee «cobroId» y nadie lo escribe antes", StringComparison.Ordinal));
    }

    [Fact]
    public void Cerrar_no_lee_lo_que_solo_escribio_abrir_ni_una_entrada_de_abrir()
    {
        // El contexto de «abrir» no se guarda: «cerrar» empieza con lo que la saga reconstruye y
        // nada más. Con un solo conjunto para todas las fases, esto arrancaba y lanzaba DESPUÉS
        // de capturar, con la plata cobrada y la saga sin error.
        var escrito = Errores(Mutar("\"lee\": [\"paymentId\"]", "\"lee\": [\"cotizacion\"]"));
        var entrada = Errores(Mutar("\"lee\": [\"paymentId\"]", "\"lee\": [\"comisionPct\"]"));

        Assert.Contains(escrito, e => e.Contains("«capturar» lee «cotizacion» y nadie lo escribe antes en la fase «cerrar»", StringComparison.Ordinal));
        Assert.Contains(entrada, e => e.Contains("«capturar» lee «comisionPct» y nadie lo escribe antes en la fase «cerrar»", StringComparison.Ordinal));
    }

    [Fact]
    public void La_entrada_tiene_que_ponerla_la_fachada_al_abrir()
    {
        // Renombrar «comprador» en el código —o en el JSON— arrancaba y lanzaba al autorizar, con
        // el aforo apartado. La fachada declara lo que pone; la entrada del JSON tiene que estar ahí.
        var sinComprador = Contrato with { PoneAlAbrir = Contrato.PoneAlAbrir.Where(n => n != "comprador").ToList() };

        var errores = FlowValidator.Validar(EventosFlujos.Compra, Registro, sinComprador);

        Assert.Contains(errores, e => e.Contains("declara la entrada «comprador»", StringComparison.Ordinal));
    }

    [Fact]
    public void Lo_que_cerrar_lee_tiene_que_reconstruirlo_el_binding()
    {
        // La otra mitad: es el binding quien dice qué hay al continuar. Sin «paymentId» entre lo
        // que reconstruye, capturar no tendría qué capturar.
        var sinCobro = Contrato with { Reconstruye = Contrato.Reconstruye.Where(n => n != "paymentId").ToList() };

        var errores = Errores(Original(), sinCobro);

        Assert.Contains(errores, e => e.Contains("«capturar» lee «paymentId»", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_campo_del_item_que_se_lee_antes_de_escribirse_no_pasa()
    {
        // Apartar antes de hallar el pozo: apartar leería un itemId que todavía no existe.
        var errores = Errores(Mutar(
            "\"pasos\": [\"sujeto-pozo\", \"hallar\", \"apartar\"]",
            "\"pasos\": [\"sujeto-pozo\", \"apartar\", \"hallar\"]"));

        Assert.Contains(errores, e => e.Contains("«apartar» lee «linea.itemId»", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_campo_de_la_linea_mal_escrito_no_pasa()
    {
        // Los campos que una línea trae de origen los declara el binding: una errata en el JSON
        // —o una constante renombrada en el C#— ya no arranca para dar 500 en cada compra.
        var errores = Errores(Mutar("\"linea.cantidad\"", "\"linea.cantida\""));

        Assert.Contains(errores, e => e.Contains("«apartar» lee «linea.cantida» y en ese punto del bloque nadie lo escribió", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_bloque_sobre_una_lista_cuyos_campos_nadie_declara_no_pasa()
    {
        var sinCampos = Contrato with { CamposDeItem = new Dictionary<string, IReadOnlyCollection<string>>() };

        var errores = Errores(Original(), sinCampos);

        Assert.Contains(errores, e => e.Contains("repite sobre «lineas» y el código no declara qué campos trae cada ítem", StringComparison.Ordinal));
    }

    [Fact]
    public void Una_lectura_del_contexto_DENTRO_de_un_bloque_sin_escritura_previa_no_pasa()
    {
        var errores = Errores(Mutar("\"lee\": [\"eventId\", \"linea\"]", "\"lee\": [\"eventoId\", \"linea\"]"));

        Assert.Contains(errores, e => e.Contains("«sujeto-pozo» lee «eventoId» y nadie lo escribe antes", StringComparison.Ordinal));
    }

    [Fact]
    public void Una_fase_que_nombra_un_paso_no_declarado_no_pasa()
    {
        // Sin esto arranca y la primera compra lanza buscando el paso. Una errata de un paso que
        // SÍ existe la caza además «declara el paso X y ninguna fase lo nombra»; el fantasma
        // añadido, sólo esto.
        var errores = Errores(Mutar("\"autorizar\"\n    ],", "\"autorizar\",\n      \"fantasma\"\n    ],"));

        Assert.Contains(errores, e => e.Contains("«abrir» nombra el paso «fantasma», que no está declarado", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_segundo_cierre_de_la_misma_reserva_no_pasa_aunque_capture_en_abrir()
    {
        // Las comprobaciones de fase posterior y de bloque se le hacen al cierre que la reserva
        // nombra. Un segundo paso con «cierra_reserva» se las saltaba, y capturaba en la misma
        // llamada que autoriza.
        var errores = Errores(Mutar(
            "\"autorizar\"\n    ],",
            "\"autorizar\",\n      \"capturar-ya\"\n    ],").Replace(
            "\"capturar\": {",
            "\"capturar-ya\": { \"tipo\": \"payments.capturar\", \"lee\": [\"paymentId\"], \"llave\": \"capture\", \"cierra_reserva\": \"autorizar\" },\n    \"capturar\": {",
            StringComparison.Ordinal));

        Assert.Contains(errores, e => e.Contains("«capturar-ya» cierra la reserva de «autorizar», y esa reserva se consuma con «capturar»", StringComparison.Ordinal));
    }

    [Fact]
    public void La_llave_que_declara_un_paso_se_cruza_con_la_que_su_tipo_usa()
    {
        // Autorizar sin «llave» lanzaba con el aforo ya apartado; consumir con una llave que no
        // usa le hace creer a quien lee que no se duplica.
        var sinLlave = Errores(Mutar("\"llave\": \"authorize\"", "\"llave_base\": \"authorize\""));
        var sobra = Errores(Mutar("\"lee\": [\"hold.holdId\"],", "\"lee\": [\"hold.holdId\"], \"llave\": \"consume\","));
        var porItem = Errores(Mutar("\"llave_base\": \"hold\"", "\"llave\": \"hold\""));

        Assert.Contains(sinLlave, e => e.Contains("«autorizar» es de tipo «payments.autorizar», que llama con «llave», y no la declara", StringComparison.Ordinal));
        Assert.Contains(sobra, e => e.Contains("«consumir» declara una llave y «inventory.consumir» no usa ninguna", StringComparison.Ordinal));
        Assert.Contains(porItem, e => e.Contains("«apartar» es de tipo «inventory.apartar», que llama con «llave_base»", StringComparison.Ordinal));
    }

    [Fact]
    public void Las_fases_son_las_que_la_fachada_invoca_en_su_orden()
    {
        // Renombrar «cerrar» arrancaba y cada confirmación lanzaba; reordenarlas haría abrir la
        // saga con la fase que captura.
        var renombrada = Errores(Mutar("\"cerrar\": [", "\"confirmar\": ["));

        Assert.Contains(renombrada, e => e.Contains("declara las fases [abrir, confirmar] y la fachada invoca [abrir, cerrar]", StringComparison.Ordinal));
    }

    [Fact]
    public void Una_fase_repetida_sin_nombre_o_vacia_no_pasa()
    {
        // Construida en C#, que el lector ya rechaza el JSON con la clave repetida: lo que se
        // comprueba es que el validador no dependa de quién armó la definición.
        var f = EventosFlujos.Compra;
        var repetida = f with { Fases = new[] { f.Fases[0], f.Fases[1], f.Fases[1] } };
        var sinNombre = f with { Fases = new[] { f.Fases[0], f.Fases[1] with { Nombre = " " } } };
        var vacia = f with { Fases = new[] { f.Fases[0], f.Fases[1] with { Pasos = Array.Empty<PasoRef>() } } };

        Assert.Contains(FlowValidator.Validar(repetida, Registro, Contrato), e => e.Contains("declara la fase «cerrar» dos veces", StringComparison.Ordinal));
        Assert.Contains(FlowValidator.Validar(sinNombre, Registro, Contrato), e => e.Contains("declara una fase sin nombre", StringComparison.Ordinal));
        Assert.Contains(FlowValidator.Validar(vacia, Registro, Contrato), e => e.Contains("la fase «cerrar» no tiene pasos", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"fases\": {", "\"fases\": {\n    \"cerrar\": [\"capturar\"],", "«cerrar»")]
    [InlineData("\"tipo\": \"payments.capturar\",", "\"tipo\": \"payments.capturar\", \"tipo\": \"payments.capturar\",", "«tipo»")]
    [InlineData("\"pasos\": {", "\"pasos\": {\n    \"capturar\": { \"tipo\": \"payments.capturar\" },", "«capturar»")]
    public void Una_propiedad_repetida_en_cualquier_objeto_no_se_lee(string antes, string despues, string repetida)
    {
        // JSON lo admite: en «pasos» ganaba el último sin aviso, y en «fases» entraban los dos.
        var ex = Assert.Throws<FormatException>(() => FlujoDef.Leer(Mutar(antes, despues)));

        Assert.Contains($"trae {repetida} dos veces", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_reserva_que_la_saga_no_sabe_guardar_no_pasa()
    {
        // Se guarda por el nombre del paso: si el binding no lo conoce, la capacidad reserva y
        // nadie lo anota para deshacerlo. Y la forma tiene que ser la misma.
        var sinAutorizar = Contrato with
        {
            Reservas = Contrato.Reservas.Where(r => r.Key != "autorizar").ToDictionary(r => r.Key, r => r.Value),
        };
        var otraForma = Contrato with
        {
            Reservas = Contrato.Reservas.ToDictionary(r => r.Key, r => r.Key == "apartar" ? FormaDeReserva.Unica : r.Value),
        };

        Assert.Contains(Errores(Original(), sinAutorizar), e => e.Contains("«autorizar» reserva y la saga no sabe guardar lo que reserva", StringComparison.Ordinal));
        Assert.Contains(Errores(Original(), otraForma), e => e.Contains("«apartar» reserva una vez por ítem y el binding la guarda una vez por saga", StringComparison.Ordinal));
    }

    [Fact]
    public void Una_reserva_sin_quien_la_consuma_no_pasa()
    {
        // Sin consumación, la compensación del cobro quedaría armada para siempre: ni se cambiaría
        // de carácter al capturar ni se marcaría hecha.
        var errores = Errores(Mutar("\"consumado_por\": \"capturar\"", "\"consumado_por\": \"cobrar\""));

        Assert.Contains(errores, e => e.Contains("se consuma con «cobrar», que no está declarado", StringComparison.Ordinal));
    }

    [Fact]
    public void Las_lecturas_de_un_paso_se_cruzan_con_lo_que_su_tipo_lee()
    {
        var errores = Errores(Mutar("\"lee\": [\"comprador\", \"total\"]", "\"lee\": [\"comprador\"]"));

        Assert.Contains(errores, e => e.Contains("declara 1 lectura(s) y «payments.autorizar» lee 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_campo_que_el_interprete_no_conoce_no_se_lee()
    {
        // Quien escribe «si» cree haber declarado una condición; si se ignorara, la primera compra
        // le enseñaría que no. (Era «al_fallar» hasta que la F3 lo hizo un campo de verdad.)
        var ex = Assert.Throws<FormatException>(() => FlujoDef.Leer(Mutar(
            "\"llave\": \"capture\",", "\"llave\": \"capture\", \"si\": \"total > 0\",")));

        Assert.Contains("«si»", ex.Message, StringComparison.Ordinal);
    }

    // ── al_fallar y lo efímero (ADR 0140 F3) ────────────────────────────────

    [Fact]
    public void Al_fallar_solo_admite_seguir()
    {
        var ex = Assert.Throws<FormatException>(() => FlujoDef.Leer(Mutar(
            "\"llave\": \"capture\",", "\"llave\": \"capture\", \"al_fallar\": \"soltar-y-seguir\",")));

        Assert.Contains("«al_fallar» sólo admite «seguir»", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Al_fallar_solo_va_en_un_paso_que_no_reserva_ni_cierra_en_la_ultima_fase()
    {
        // Seguir tras un fallo antes del último cierre completaría una saga con algo reservado sin
        // consumir, o consumido sin cobrar.
        var enCapturar = Errores(Mutar("\"llave\": \"capture\",", "\"llave\": \"capture\", \"al_fallar\": \"seguir\","));
        var enAutorizar = Errores(Mutar("\"llave\": \"authorize\",", "\"llave\": \"authorize\", \"al_fallar\": \"seguir\","));
        var enAbrir = Errores(Mutar(
            "{ \"tipo\": \"eventos.revisar-lineas\", \"lee\": [\"lineas\"] }",
            "{ \"tipo\": \"eventos.revisar-lineas\", \"lee\": [\"lineas\"], \"al_fallar\": \"seguir\" }"));

        Assert.Contains(enCapturar, e => e.Contains("«capturar» declara «al_fallar» y reserva o cierra", StringComparison.Ordinal));
        Assert.Contains(enAutorizar, e => e.Contains("«autorizar» declara «al_fallar» y reserva o cierra", StringComparison.Ordinal));
        Assert.Contains(enAbrir, e => e.Contains("«revisar-lineas» declara «al_fallar» y no está en la última fase", StringComparison.Ordinal));
    }

    /// <summary>Un paso de «cerrar» que se puede declarar con al_fallar: lee lo que la saga reconstruye.</summary>
    private static PasoDef Nota(string id, string? cierra = null, IReadOnlyList<string>? escribe = null)
        => new(id, "eventos.total", new[] { "total", "paymentId" }, escribe ?? new[] { "x" }, null, null, null, cierra, null,
            PasoDef.Seguir);

    [Fact]
    public void Al_fallar_va_despues_del_ultimo_cierre_suelto_y_sin_escribir()
    {
        var f = EventosFlujos.Compra;
        var cerrar = f.Fases[1];
        FlujoDef Con(PasoDef paso, IReadOnlyList<PasoRef> elementos)
            => f with
            {
                Fases = new[] { f.Fases[0], cerrar with { Pasos = elementos } },
                Pasos = f.Pasos.Append(new KeyValuePair<string, PasoDef>(paso.Id, paso)).ToDictionary(),
            };

        var alFinal = Nota("nota");
        var alFinalSinEscribir = Nota("nota", escribe: Array.Empty<string>()) with { Tipo = "inventory.consumir", Lee = new[] { "paymentId" } };
        var antes = Con(alFinal, cerrar.Pasos.Prepend(new PasoRef("nota", null)).ToList());
        var despues = Con(alFinal, cerrar.Pasos.Append(new PasoRef("nota", null)).ToList());
        var bien = Con(alFinalSinEscribir, cerrar.Pasos.Append(new PasoRef("nota", null)).ToList());
        var bloque = cerrar.Pasos[1].ParaCada!;
        var enBloque = Con(alFinalSinEscribir with { Lee = new[] { "hold.holdId" } },
            new[] { cerrar.Pasos[0], new PasoRef(null, bloque with { Pasos = bloque.Pasos.Append("nota").ToList() }) });

        Assert.Empty(FlowValidator.Validar(bien, Registro, Contrato));   // el control: así sí vale
        Assert.Contains(FlowValidator.Validar(antes, Registro, Contrato), e => e.Contains("va antes del último cierre de «cerrar»", StringComparison.Ordinal));
        Assert.Contains(FlowValidator.Validar(despues, Registro, Contrato), e => e.Contains("«nota» declara «al_fallar» y escribe", StringComparison.Ordinal));
        Assert.Contains(FlowValidator.Validar(enBloque, Registro, Contrato), e => e.Contains("«nota» declara «al_fallar» dentro de un bloque", StringComparison.Ordinal));
    }

    [Fact]
    public void Omitir_si_cero_va_en_un_paso_que_reserva_y_sobre_algo_que_lee()
    {
        var bien = Errores(Mutar("\"llave\": \"authorize\",", "\"llave\": \"authorize\", \"omitir_si_cero\": \"total\","));
        var noLoLee = Errores(Mutar("\"llave\": \"authorize\",", "\"llave\": \"authorize\", \"omitir_si_cero\": \"cotizacion\","));
        var noReserva = Errores(Mutar("\"llave\": \"capture\",", "\"llave\": \"capture\", \"omitir_si_cero\": \"paymentId\","));

        Assert.Empty(bien);   // el control: así es como lo declara Eventos en la F3
        Assert.Contains(noLoLee, e => e.Contains("«autorizar» se omite si «cotizacion» es cero y no lo lee", StringComparison.Ordinal));
        Assert.Contains(noReserva, e => e.Contains("«capturar» declara «omitir_si_cero» y no reserva nada", StringComparison.Ordinal));
    }

    /// <summary>Los pasos de Eventos más el de aviso: el registro que tendrá Eventos al declararlo (paso 7).</summary>
    private static IRegistroDePasos ConAviso()
        => new RegistroDePasos(Registro.Tipos.Select(t => Registro.Para(t)!).Append(new PasoAvisar(new SinAvisos())));

    private sealed class SinAvisos : INotificationsPort
    {
        public Task<Synergos.Core.Result<string>> AvisarAsync(
            Synergos.Core.Ref destinatario, string direccion, string plantilla, IReadOnlyDictionary<string, string> valores,
            Synergos.Core.IdempotencyKey llave, CancellationToken ct)
            => throw new InvalidOperationException("Validar una definición no avisa a nadie.");
    }

    [Fact]
    public void Un_paso_de_aviso_declara_su_plantilla_y_uno_que_no_avisa_no_declara_ninguna()
    {
        var f = EventosFlujos.Compra;
        var cerrar = f.Fases[1];
        var conEfimera = Contrato with { Efimera = ContactoEnCerrar };
        FlujoDef Con(PasoDef aviso, string? plantillaEnCapturar = null)
            => f with
            {
                Fases = new[] { f.Fases[0], cerrar with { Pasos = cerrar.Pasos.Append(new PasoRef(aviso.Id, null)).ToList() } },
                Pasos = f.Pasos
                    .Select(p => p.Key == "capturar" && plantillaEnCapturar is not null
                        ? new KeyValuePair<string, PasoDef>(p.Key, p.Value with { Plantilla = plantillaEnCapturar })
                        : p)
                    .Append(new KeyValuePair<string, PasoDef>(aviso.Id, aviso))
                    .ToDictionary(),
                Efimera = new Dictionary<string, IReadOnlyList<string>> { ["cerrar"] = new[] { "contacto" } },
            };
        var aviso = new PasoDef("avisar", "notifications.avisar", new[] { "comprador", "contacto", "total" },
            Array.Empty<string>(), "avisar", null, null, null, null, PasoDef.Seguir, null, "eventos.entradas.confirmadas");

        Assert.Empty(FlowValidator.Validar(Con(aviso), ConAviso(), conEfimera));   // el control: así lo declara Eventos
        Assert.Contains(FlowValidator.Validar(Con(aviso with { Plantilla = null }), ConAviso(), conEfimera),
            e => e.Contains("«avisar» es de tipo «notifications.avisar», que avisa con «plantilla», y no la declara", StringComparison.Ordinal));
        Assert.Contains(FlowValidator.Validar(Con(aviso, plantillaEnCapturar: "x"), ConAviso(), conEfimera),
            e => e.Contains("«capturar» declara una plantilla y «payments.capturar» no avisa", StringComparison.Ordinal));
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> ContactoEnCerrar =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal) { ["cerrar"] = new[] { "contacto" } };

    private static string ConEfimera(string json, string fase = "cerrar", string nombre = "contacto")
        => json.Replace(
            "\"entrada\": [\"eventId\", \"comprador\", \"lineas\", \"comisionPct\"],",
            $"\"entrada\": [\"eventId\", \"comprador\", \"lineas\", \"comisionPct\"],\n  \"efimera\": {{ \"{fase}\": [\"{nombre}\"] }},",
            StringComparison.Ordinal);

    [Fact]
    public void Lo_efimero_se_lee_en_su_fase_y_en_ninguna_otra()
    {
        var conEfimera = Contrato with { Efimera = ContactoEnCerrar };
        Assert.Contains("\"efimera\"", ConEfimera(Original()), StringComparison.Ordinal);   // la mutación entró

        var enSuFase = Errores(ConEfimera(Mutar("\"lee\": [\"paymentId\"]", "\"lee\": [\"contacto\"]")), conEfimera);
        var enOtra = Errores(ConEfimera(Mutar("\"lee\": [\"comisionPct\"] }", "\"lee\": [\"contacto\"] }")), conEfimera);

        Assert.DoesNotContain(enSuFase, e => e.Contains("«contacto»", StringComparison.Ordinal));
        Assert.Contains(enOtra, e => e.Contains("«revisar-comision» lee «contacto» y nadie lo escribe antes", StringComparison.Ordinal));
    }

    [Fact]
    public void Lo_efimero_lo_tiene_que_poner_la_fachada_en_una_fase_que_existe_sin_tapar_nada()
    {
        var noLoPone = Errores(ConEfimera(Original()));
        var otraFase = Errores(ConEfimera(Original(), fase: "pagar"), Contrato with { Efimera = ContactoEnCerrar });
        var tapa = Errores(ConEfimera(Original(), nombre: "total"), Contrato with
        {
            Efimera = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal) { ["cerrar"] = new[] { "total" } },
        });

        Assert.Contains(noLoPone, e => e.Contains("declara la efímera «contacto» en «cerrar» y la fachada no la pone", StringComparison.Ordinal));
        Assert.Contains(otraFase, e => e.Contains("declara lo efímero de la fase «pagar», que no existe", StringComparison.Ordinal));
        Assert.Contains(tapa, e => e.Contains("la efímera «total» en «cerrar», y la fase ya empieza con ese nombre", StringComparison.Ordinal));
    }

    [Fact]
    public void Una_saga_sin_la_ranura_que_el_flujo_usa_no_pasa()
    {
        var errores = FlowValidator.Validar(EventosFlujos.Compra, Registro, Contrato with { Saga = typeof(object) });

        Assert.Contains(errores, e => e.Contains(nameof(IHoldLedger), StringComparison.Ordinal));
    }

    [Fact]
    public void Un_catalogo_vacio_no_pasa()
    {
        var resultado = new FlowDefinitionValidator(Registro).Validate(null, new FlowCatalog());

        Assert.True(resultado.Failed);
    }

    // ── El arranque de verdad ───────────────────────────────────────────────

    /// <summary>
    /// Anota si el catálogo se validó y con qué resultado, desde DENTRO del host.
    /// </summary>
    /// <remarks>
    /// <para><b>Por qué un testigo y no la excepción del arranque.</b> Con <i>minimal hosting</i>,
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> arranca el host en el hilo del
    /// <c>Program</c>, y cuando el arranque falla su <c>app.Run()</c> desecha el host. Si eso gana la
    /// carrera al hilo del test, lo que sube es un <see cref="ObjectDisposedException"/> y no la
    /// <see cref="OptionsValidationException"/>: medido, uno de cada ocho en la suite completa.
    /// Un test que juzga por el TIPO de la excepción es un rojo intermitente; el testigo corre
    /// dentro de la validación, antes de la carrera, y lo que anota no depende de quién gane.</para>
    ///
    /// <para><b>Y prueba lo que importa:</b> que se validó AL ARRANCAR. Sin <c>ValidateOnStart</c>,
    /// nadie pide el catálogo hasta la primera compra, y el testigo no ve nada.</para>
    /// </remarks>
    private sealed class Testigo : IValidateOptions<FlowCatalog>
    {
        public ValidateOptionsResult? Visto { get; private set; }

        public ValidateOptionsResult Validate(string? name, FlowCatalog options)
        {
            Visto = new FlowDefinitionValidator(Registro).Validate(name, options);
            return ValidateOptionsResult.Skip;
        }
    }

    private static WebApplicationFactory<TicketingSaga> Orquestador(string raiz, Testigo testigo, FlujoDef? reemplazo = null)
        => new WebApplicationFactory<TicketingSaga>().WithWebHostBuilder(b =>
        {
            b.UseSetting("eventos:Storage:Root", raiz);
            b.UseSetting("Eventos:ApiKey", "llave-de-prueba");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IValidateOptions<FlowCatalog>>(testigo);
                if (reemplazo is not null) s.PostConfigure<FlowCatalog>(c => c.Registrar(reemplazo, Contrato));
            });
        });

    [Fact]
    public async Task El_orquestador_de_Eventos_arranca_con_su_definicion_y_la_valida_al_arrancar()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "flujo-arranque-" + Guid.NewGuid().ToString("N"));
        try
        {
            var testigo = new Testigo();
            using var fabrica = Orquestador(raiz, testigo);
            using var cliente = fabrica.CreateClient();

            using var salud = await cliente.GetAsync(new Uri("/health", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, salud.StatusCode);
            Assert.True(testigo.Visto is { Succeeded: true }, "El catálogo de flujos no se validó al arrancar.");

            // Y la fachada se construye: valida la misma definición contra el mismo registro.
            Assert.NotNull(fabrica.Services.GetRequiredService<TicketingFlow>());
        }
        finally
        {
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }

    [Fact]
    public void El_orquestador_de_Eventos_NO_arranca_con_una_definicion_rota()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "flujo-arranque-" + Guid.NewGuid().ToString("N"));
        try
        {
            var testigo = new Testigo();
            var rota = FlujoDef.Leer(Mutar("\"consumado_por\": \"capturar\"", "\"consumado_por\": \"cobrar\""));
            using var fabrica = Orquestador(raiz, testigo, rota);

            var ex = Record.Exception(() => fabrica.CreateClient());

            Assert.True(ex is not null, "El orquestador arrancó con un flujo roto: llegaría a la primera compra.");
            Assert.True(testigo.Visto is { Failed: true },
                "El arranque falló, pero no porque se validara el catálogo de flujos al arrancar: "
                + ex.GetType().Name);
            Assert.Contains(testigo.Visto!.Failures!, f => f.Contains("«cobrar»", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }
}
