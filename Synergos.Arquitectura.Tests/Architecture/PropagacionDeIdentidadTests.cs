namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El orquestador NO propaga la identidad de quien llama: la DERIVA del registro de una
/// capacidad que ya la verificó (HU #14, la decisión que faltaba).
/// </summary>
/// <remarks>
/// <para><b>La pregunta.</b> El CMS le presenta identidad a las capacidades con las que habla
/// directo —<c>Api.Workflow</c>, <c>Api.Messaging</c>, <c>Api.Audit</c>, <c>Api.Cart</c>— y a
/// <c>Api.Orders</c> y <c>Api.Payments</c> no le habla: le hablan los cuatro orquestadores. Así
/// que o el orquestador reenvía la cabecera <c>X-Synergos-Identity</c>, o esas dos capacidades se
/// quedan sin forma de saber quién actuó. Nadie había contestado, y el campo <c>Payer</c> de
/// <c>Api.Payments</c> depende de la respuesta.</para>
///
/// <para><b>La respuesta es que no se propaga, y NO es por dificultad: propagar tiene la forma
/// equivocada.</b> Un token es una credencial CON RELOJ; una saga es trabajo CON DURACIÓN. Las
/// dos cosas no componen, y se ve en tres sitios distintos:</para>
///
/// <para><b>Uno: el token vence y la saga le sobrevive.</b> Vigencia de 15 minutos;
/// <c>Sweep:AbandonAfterMinutes</c> se mide en decenas, y compensar lo hace un barrido horas
/// después, sin nadie al teclado. Propagar daría un <c>authorize</c> firmado y un <c>void</c> o
/// un <c>refund</c> sin firmar — la afirmación presente justo donde no pasó nada irreversible y
/// ausente justo donde la plata vuelve. Eso es PEOR que no tener ninguna: el asiento fuerte del
/// primer paso hace leer el débil del último como una degradación que alguien eligió.</para>
///
/// <para><b>Dos: para que sobreviviera habría que GUARDARLO.</b> El barrido no tiene a quién
/// pedirle un token nuevo —no hay sesión detrás—, así que la única forma de que compense firmando
/// es que la saga haya persistido la credencial. Eso pone un bearer en el disco del orquestador,
/// por cada saga, mientras la saga viva, en un almacén que además se respalda. Es exactamente la
/// forma que este repo ya rechazó para el <c>.env</c> del servidor: custodia de secretos
/// disfrazada de datos. Por eso el segundo diente mira el almacén y no el cable.</para>
///
/// <para><b>Y tres: el sujeto no cuadraría igual.</b> Lo que vuelve prueba a un token es que la
/// capacidad rechaza el que nombra a otro (<c>token_subject_mismatch</c>). Pero los pasos de una
/// saga nombran a sujetos DISTINTOS: <c>authorize</c> nombra al pagador, <c>hold</c> nombra
/// <c>tienda.compra/{sagaId}</c> y el pedido nombra al comprador. Un token reenviado sólo puede
/// probar uno de ellos. Propagar no sería «identidad para Payments»: sería identidad para un
/// campo, con el resto igual que antes y con el aspecto de estar resuelto.</para>
///
/// <para><b>Lo que se hace en vez de eso, y ya estaba a medio construir.</b>
/// <c>Bff.Tienda</c> no le cree al llamador quién compra: lee el dueño de la canasta de
/// <c>Api.Cart</c> (<c>PurchaseFlow</c>), y ese dueño se estableció presentando un token
/// verificado (HU #14, séptima rebanada). O sea que el comprador que llega a <c>Api.Orders</c> y
/// a <c>Api.Payments</c> YA está anclado, sin que ninguna credencial cruce el orquestador. La
/// diferencia es la que da la regla: <b>reenviar una credencial</b> contra <b>citar un
/// registro</b>. Una credencial vence, hay que custodiarla y sólo prueba un sujeto; un registro
/// no vence, no es secreto y lo puede volver a leer cualquiera que tenga la llave compartida.</para>
///
/// <para><b>La regla, entonces:</b> un orquestador nunca nombra a una persona por su propia
/// palabra — la nombra citando el registro de una capacidad que ya la verificó.</para>
///
/// <para><b>Y lo que esto decide sobre <c>Api.Orders</c> y <c>Api.Payments</c>:</b> hoy NO les
/// toca puerta de identidad. Dárselas sería abrirles un campo que nadie puede llenar —el único
/// que las llama es un orquestador que, por esta decisión, no presenta— y un
/// <c>PaidWith</c> que dijera siempre lo mismo es
/// <c>feedback_no_read_without_a_write_path</c> por el lado de la escritura. La excepción es el
/// cobro que NO pasa por orquestador —la tasa de un trámite, que va directo (#27)—, y ése sí se
/// cableó.</para>
///
/// <para><b>El disparador para volver a abrir esto</b> está escrito para que no haya que
/// adivinarlo: el día que una capacidad detrás de un orquestador necesite saber CÓMO se
/// identificó la persona —y no sólo quién es—, citar el registro deja de alcanzar, porque la
/// afirmación vive en la capacidad de al lado. Lo que corresponde entonces NO es propagar el
/// token del CMS: es que el orquestador cite lo que aquel registro dice que fue
/// (<c>Cart.OpenedWith</c>) y que la capacidad lo guarde como afirmación DE SEGUNDA MANO,
/// distinta de la que ella misma verificó. Eso es un trabajo con su propio ticket.</para>
/// </remarks>
public sealed class PropagacionDeIdentidadTests
{
    /// <summary>La cabecera que el CMS presenta y que un orquestador NO reenvía.</summary>
    private const string CabeceraDeIdentidad = "X-Synergos-Identity";

    /// <summary>Los cuatro orquestadores construidos, más el motor que comparten.</summary>
    private static readonly string[] Orquestadores =
        { "Synergos.Bff.Core", "Synergos.Bff.Tienda", "Synergos.Bff.Salud", "Synergos.Bff.Eventos", "Synergos.Bff.Viajes" };

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

    /// <summary>
    /// La fuente SIN comentarios.
    /// </summary>
    /// <remarks>
    /// Es obligatorio y no cosmético: este mismo fichero explica la decisión nombrando la
    /// cabecera, y un gate que leyera los comentarios se pondría rojo por su propia
    /// documentación. Es la trampa de <c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>.
    /// </remarks>
    private static string SinComentarios(string ruta)
    {
        var dentroDeBloque = false;
        var limpias = new List<string>();
        foreach (var linea in File.ReadAllLines(ruta))
        {
            var t = linea.TrimStart();
            if (dentroDeBloque)
            {
                if (t.Contains("*/", StringComparison.Ordinal)) dentroDeBloque = false;
                continue;
            }
            if (t.StartsWith("/*", StringComparison.Ordinal))
            {
                if (!t.Contains("*/", StringComparison.Ordinal)) dentroDeBloque = true;
                continue;
            }
            if (t.StartsWith("//", StringComparison.Ordinal)) continue;

            var i = linea.IndexOf("//", StringComparison.Ordinal);
            limpias.Add(i >= 0 ? linea[..i] : linea);
        }
        return string.Join('\n', limpias);
    }

    /// <summary>Todos los .cs de un proyecto, saltándose lo generado.</summary>
    private static IReadOnlyList<string> FuentesDe(string proyecto)
    {
        var raiz = Path.Combine(RepoRoot(), proyecto);
        Assert.True(Directory.Exists(raiz), $"No existe {proyecto}: revisar este gate.");

        var fuentes = Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.True(fuentes.Count > 0, $"{proyecto} no tiene fuentes: revisar este gate.");
        return fuentes;
    }

    // ── Diente 1: la decisión ───────────────────────────────────────────────

    /// <summary>Ningún orquestador reenvía la identidad de quien llamó.</summary>
    /// <remarks>
    /// <b>Se escribe estando en VERDE</b>, que es cuando un gate es gratis —sin excepciones que
    /// negociar y sin nadie esperando—, igual que el de «ninguna capacidad llama a otra» (#49).
    /// El día que alguien decida propagar, esto se pone rojo y le obliga a leer el porqué antes
    /// de hacerlo; que es todo lo que un gate de decisión tiene que conseguir.
    /// </remarks>
    [Fact]
    public void Ningun_orquestador_reenvia_la_cabecera_de_identidad()
    {
        var culpables = new List<string>();

        foreach (var proyecto in Orquestadores)
        {
            foreach (var fuente in FuentesDe(proyecto))
            {
                if (SinComentarios(fuente).Contains(CabeceraDeIdentidad, StringComparison.OrdinalIgnoreCase))
                {
                    culpables.Add(Path.GetRelativePath(RepoRoot(), fuente));
                }
            }
        }

        Assert.True(culpables.Count == 0,
            "Un orquestador está reenviando la identidad de quien llamó:\n  "
            + string.Join("\n  ", culpables)
            + "\n\nEso NO se decidió así. Un token vence en 15 minutos y una saga le sobrevive, "
            + "así que el paso que compensa —horas después, desde el barrido, sin nadie al "
            + "teclado— llegaría sin firmar mientras el que autorizó llegó firmado: la "
            + "afirmación fuerte donde no pasó nada y ninguna donde la plata vuelve. "
            + "Lo que corresponde es DERIVAR a la persona del registro de una capacidad que ya "
            + "la verificó, como hace Bff.Tienda con el dueño de la canasta. "
            + "Ver el <remarks> de esta clase.");
    }

    // ── Diente 2: cómo se revierte en silencio ──────────────────────────────

    /// <summary>Ninguna saga guarda una credencial.</summary>
    /// <remarks>
    /// <para><b>Este es el diente que de verdad hace falta</b>, y el primero solo no lo cubre.
    /// Quien quiera propagar se va a chocar enseguida con que el barrido no tiene token, y la
    /// salida que se le va a ocurrir es guardarlo en la saga para que el barrido lo use. Eso NO
    /// se lee como «reenviar una credencial»: se lee como añadir un campo a un record.</para>
    ///
    /// <para>Y el precio es que el almacén de sagas —un <c>JsonCollectionStore</c> en un volumen
    /// que se respalda— pasa a tener bearers en reposo de toda la gente que compró. Un respaldo
    /// deja de ser una copia de datos y pasa a ser un llavero.</para>
    /// </remarks>
    [Fact]
    public void Ninguna_saga_guarda_una_credencial()
    {
        // Sobre el nombre del campo y no sobre su tipo: una credencial guardada es siempre un
        // `string`, así que el tipo no distingue nada y el nombre sí.
        string[] huelenACredencial = { "Token", "Bearer", "Credential", "Credencial", "Jwt" };

        var culpables = new List<string>();
        var vistas = 0;

        foreach (var proyecto in Orquestadores)
        {
            foreach (var fuente in FuentesDe(proyecto))
            {
                // Sólo la LISTA DE PARÁMETROS del record de saga, que es lo que se persiste.
                // Buscar el olor en el fichero entero da falsos positivos que suenan a defecto y
                // no lo son: `CancellationToken` contiene «Token», y con eso el gate se ponía
                // rojo señalando a SagaMachinery.cs. Un gate que grita donde no hay nada se
                // desactiva a la semana.
                var parametros = ParametrosDeSaga(SinComentarios(fuente));
                if (parametros is null) continue;
                vistas++;

                var nombres = NombresDeParametros(parametros);

                foreach (var olor in huelenACredencial)
                {
                    var culpa = nombres.FirstOrDefault(n => n.Contains(olor, StringComparison.OrdinalIgnoreCase));
                    if (culpa is not null)
                    {
                        culpables.Add($"{Path.GetRelativePath(RepoRoot(), fuente)} → «{culpa}»");
                    }
                }
            }
        }

        // Que el gate haya MIRADO algo. Sin esto, un cambio en cómo se declara una saga lo
        // dejaría contando cero ficheros y pasando en verde para siempre.
        Assert.True(vistas == 4,
            $"Se esperaban 4 records de saga (Tienda, Salud, Eventos, Viajes) y se vieron {vistas}. "
            + "Si nació un orquestador, súmalo; si cambió cómo se declara una saga, este gate dejó "
            + "de mirar y hay que arreglarlo — contar cero y pasar en verde es el peor resultado.");

        Assert.True(culpables.Count == 0,
            "Una saga está guardando algo con pinta de credencial:\n  "
            + string.Join("\n  ", culpables)
            + "\n\nEl almacén de sagas se respalda. Un bearer ahí dentro convierte la copia de "
            + "datos en un llavero, que es la forma que este repo ya rechazó para el .env del "
            + "servidor. Si lo que hacía falta era que el barrido pudiera firmar, la respuesta "
            + "no es guardar el token: es que la persona se DERIVE del registro de una "
            + "capacidad. Ver el <remarks> de esta clase.");
    }

    // ── Diente 3: la mitad positiva ─────────────────────────────────────────

    /// <summary>
    /// <c>Bff.Tienda</c> sigue DERIVANDO al comprador de la canasta, y no aceptándolo.
    /// </summary>
    /// <remarks>
    /// <para><b>Es la mitad que puede regresar en silencio.</b> El primer diente prohíbe algo que
    /// nadie ha escrito todavía; éste protege algo que YA está bien y que se desarma con un
    /// cambio que parece una comodidad: añadirle <c>BuyerKind</c>/<c>BuyerId</c> a
    /// <c>BuyRequest</c> «para no tener que leer la canasta». El día que eso pase, el comprador
    /// que llega a <c>Api.Orders</c> y a <c>Api.Payments</c> vuelve a ser la palabra de quien
    /// llamó — el defecto #42 una capa más allá, y sin que nada se caiga.</para>
    ///
    /// <para>Por eso son las DOS afirmaciones y no una: que el cuerpo no lo nombre, y que el
    /// flujo lo lea de la canasta. Sólo la primera dejaría pasar que se derivara de cualquier
    /// otra cosa; sólo la segunda dejaría pasar que se aceptara y además se leyera.</para>
    /// </remarks>
    [Fact]
    public void La_tienda_deriva_al_comprador_de_la_canasta()
    {
        var contratos = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Bff.Tienda", "Contracts", "TiendaContracts.cs"));

        var peticion = contratos
            .Split('\n')
            .FirstOrDefault(l => l.Contains("record BuyRequest", StringComparison.Ordinal));

        Assert.True(peticion is not null,
            "No se encontró BuyRequest en TiendaContracts.cs: revisar este gate.");

        Assert.False(peticion!.Contains("Buyer", StringComparison.OrdinalIgnoreCase),
            "BuyRequest volvió a nombrar al comprador:\n  " + peticion.Trim()
            + "\n\nEl comprador NO se acepta: se deriva del dueño de la canasta, que Api.Cart "
            + "estableció con un token verificado (HU #14, séptima rebanada). Aceptarlo acá "
            + "devuelve el defecto #42 una capa más allá — quien llame nombra a quien quiera y "
            + "eso llega hasta el Payer de Api.Payments sin que nada falle.");

        var flujo = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.Bff.Tienda", "Domain", "PurchaseFlow.cs"));

        Assert.Contains("cart.Value.OwnerKind", flujo, StringComparison.Ordinal);
        Assert.Contains("cart.Value.OwnerId", flujo, StringComparison.Ordinal);
    }

    // ── Diente 4: la lista, vigilada en los dos sentidos ────────────────────

    /// <summary>
    /// Qué orquestadores todavía nombran a alguien por la palabra de quien llama.
    /// </summary>
    /// <remarks>
    /// <para><b>El conjunto se DERIVA del fichero, no se enumera de memoria</b> — es la tercera
    /// vez que este repo paga por una lista sacada de la cabeza («los seis de
    /// <c>Synergos.Shared</c>», «faltan las otras 16», los tres endpoints de <c>Api.Consent</c>).
    /// La regla mecánica: un <c>*Request</c> que declare un par <c>XKind</c> + <c>XId</c> está
    /// nombrando a alguien —o a algo— por la palabra de quien llama.</para>
    ///
    /// <para><b>Y se vigila en los dos sentidos</b>, como la lista de permisos de <c>HttpClient</c>
    /// del gate de #49. Que aparezca un cuarto rompe el build: sería un orquestador nuevo
    /// naciendo con el defecto. Que uno deje de estar también lo rompe: cuando alguien haga el
    /// trabajo de anclarlo, esta lista y la §11 de <c>CLAUDE.md</c> tienen que moverse en el
    /// mismo commit, o la guía se queda diciendo que falta algo que ya está hecho — que es
    /// exactamente cómo esa sección llegó a decir «UNA» durante once HU.</para>
    ///
    /// <para><b>Por qué los tres siguen así, y no es olvido.</b> Tienda pudo anclarse porque
    /// tiene una canasta: un registro de <c>Api.Cart</c> con dueño verificado, anterior a la
    /// compra. Salud, Eventos y Viajes no tienen ninguno — se entra al flujo nombrando al
    /// paciente, al comprador o al viajero, y no hay registro previo que citar. Darles uno es
    /// trabajo de verdad (¿dónde vive el «carrito» de una cita?), no un cableado, y por eso está
    /// nombrado en vez de dado por hecho.</para>
    /// </remarks>
    [Fact]
    public void La_lista_de_los_que_nombran_por_su_palabra_es_exacta()
    {
        // Los que todavía no pueden derivar, con su razón — ver el <remarks>.
        string[] esperados = { "Synergos.Bff.Salud", "Synergos.Bff.Eventos", "Synergos.Bff.Viajes" };

        var medidos = new List<string>();

        foreach (var proyecto in Orquestadores.Where(p => p != "Synergos.Bff.Core"))
        {
            var contratos = Path.Combine(RepoRoot(), proyecto, "Contracts");
            if (!Directory.Exists(contratos)) continue;

            var nombra = Directory.EnumerateFiles(contratos, "*.cs", SearchOption.AllDirectories)
                .Select(SinComentarios)
                .SelectMany(RecordsDePeticion)
                .Any(TieneParKindId);

            if (nombra) medidos.Add(proyecto);
        }

        Assert.True(
            esperados.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(medidos.OrderBy(x => x, StringComparer.Ordinal)),
            $"La lista de orquestadores que nombran a alguien en su propio cuerpo cambió.\n"
            + $"  esperados: {string.Join(", ", esperados)}\n"
            + $"  medidos:   {string.Join(", ", medidos)}\n\n"
            + "Si aparece uno NUEVO: nace con el defecto #42 una capa más allá — quien llame "
            + "nombra a quien quiera. Deriva a la persona del registro de una capacidad, como "
            + "hace Bff.Tienda con el dueño de la canasta.\n"
            + "Si uno DEJÓ de estar: se hizo el trabajo, y hay que quitarlo de acá y de la §11 "
            + "de CLAUDE.md en el MISMO commit — una guía que se queda diciendo que falta algo "
            + "que ya está hecho es cómo esa sección llegó a decir «UNA» durante once HU.");
    }

    /// <summary>
    /// La lista de parámetros del record de saga del fichero, o <c>null</c> si no declara uno.
    /// </summary>
    /// <remarks>
    /// Se ancla en <c>) : ISaga&lt;</c> —la declaración— y no en <c>ISaga&lt;</c> a secas, que
    /// también aparece como restricción genérica en el motor (<c>SagaEngine</c>,
    /// <c>CompensationSweeper</c>, <c>SagaMachinery</c>) y no persiste nada.
    /// </remarks>
    private static string? ParametrosDeSaga(string fuente)
    {
        var plano = fuente.Replace('\n', ' ');

        var cierre = plano.IndexOf(") : ISaga<", StringComparison.Ordinal);
        if (cierre < 0) return null;

        var declara = plano.LastIndexOf("record ", cierre, StringComparison.Ordinal);
        if (declara < 0) return null;

        var abre = plano.IndexOf('(', declara);
        return abre < 0 || abre > cierre ? null : plano[(abre + 1)..cierre];
    }

    /// <summary>Los NOMBRES de los parámetros de una lista declarada.</summary>
    /// <remarks>
    /// El nombre es la última palabra antes de la coma o del <c>=</c>: delante va el tipo, que
    /// puede llevar <c>?</c>, genéricos o espacios, y detrás puede ir un valor por defecto.
    /// Perder los que tienen valor por defecto es el punto ciego que ya costó una vez
    /// (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>), y acá hay varios
    /// —<c>AlertsSent = 0</c>, <c>Refunded = null</c>—, así que el corte se prueba contra ellos.
    /// </remarks>
    private static IReadOnlyList<string> NombresDeParametros(string parametros)
        => parametros
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2)[0].Trim())
            .Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty)
            .Where(p => p.Length > 0)
            .ToList();

    /// <summary>El cuerpo declarado de cada <c>record …Request(...)</c> del fichero.</summary>
    private static IEnumerable<string> RecordsDePeticion(string fuente)
    {
        // Aplanado: un record puede declarar sus parámetros en varias líneas, y partir por
        // línea perdería justo el par que se busca cuando `XKind` y `XId` caen separados.
        var plano = fuente.Replace('\n', ' ');

        var i = 0;
        while (true)
        {
            var marca = plano.IndexOf("record ", i, StringComparison.Ordinal);
            if (marca < 0) yield break;

            var abre = plano.IndexOf('(', marca);
            var cierra = abre < 0 ? -1 : plano.IndexOf(')', abre);
            if (abre < 0 || cierra < 0) yield break;

            var nombre = plano[(marca + "record ".Length)..abre].Trim();
            if (nombre.EndsWith("Request", StringComparison.Ordinal))
            {
                yield return plano[(abre + 1)..cierra];
            }

            i = cierra + 1;
        }
    }

    /// <summary>¿Este cuerpo declara un par <c>XKind</c> + <c>XId</c>?</summary>
    private static bool TieneParKindId(string cuerpo)
    {
        var nombres = NombresDeParametros(cuerpo).ToHashSet(StringComparer.Ordinal);

        return nombres
            .Where(n => n.EndsWith("Kind", StringComparison.Ordinal) && n.Length > "Kind".Length)
            .Select(n => n[..^"Kind".Length] + "Id")
            .Any(nombres.Contains);
    }
}
