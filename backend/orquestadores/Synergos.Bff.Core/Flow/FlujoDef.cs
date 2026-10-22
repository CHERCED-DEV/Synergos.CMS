using System.Text.Json;

namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Un flujo de negocio DECLARADO: qué entra, en qué fases se parte y qué pasos da cada una (ADR 0140).
/// </summary>
/// <param name="Clave">Cómo se llama el flujo (<c>eventos.compra</c>). Es también el <c>Kind</c> del
/// <see cref="Synergos.Core.Ref"/> con el que sus pasos le dicen a una capacidad para qué reservan.</param>
/// <param name="Entrada">Lo que el llamador pone en el contexto antes del primer paso.</param>
/// <param name="Fases">En el orden del documento: la primera abre la saga y las demás la continúan.</param>
/// <param name="Pasos">Cada paso por su identificador dentro del flujo.</param>
/// <param name="Efimera">Por fase, lo que la fachada pone SÓLO para esa llamada y no se guarda en
/// ningún sitio: la dirección a la que se avisa, que la saga no puede llevar porque no lleva datos
/// personales. Un paso sólo lo lee en su fase.</param>
/// <remarks>
/// <para><b>Por qué un dato y no otro <c>*Flow.cs</c>.</b> El orden de los pasos y la reserva en dos
/// tiempos —anotar cómo se deshace al apartar, cambiarle el carácter al consumir— se escribían a
/// mano en cada orquestador, intercalados en un <c>Select</c>. Declarados, los ejecuta UN intérprete
/// (<see cref="FlowRunner{TSaga}"/>) y el orquestador sólo aporta lo que es suyo: los pasos del
/// dominio y cómo se guarda su saga.</para>
///
/// <para><b>Sin lenguaje de expresiones, a propósito.</b> Un paso lee por NOMBRE lo que otro escribió
/// y nada más: ni plantillas, ni condiciones, ni aritmética. Lo que haría falta calcular es un paso
/// del dominio en C#. Si portar un flujo exigiera expresiones, la definición se estaría volviendo
/// un lenguaje de programación, y eso es el criterio de reapertura de la ADR, no algo que se añade
/// en silencio.</para>
///
/// <para><b>Los tipos y la salida son datos, no expresiones</b> (núcleo N1, supuesto P1). Dicen qué
/// forma tiene cada valor que entra y de dónde viene, y qué se devuelve; ninguno calcula nada. Son lo
/// que deja a un flujo sin fachada propia: el host convierte el cuerpo y las cabeceras de la puerta
/// en los valores que leen los pasos, y publica el contrato tipado desde la misma fuente. Una
/// definición sin <see cref="Tipos"/> sigue siendo válida: se registra con su binding tipado, como
/// Eventos.</para>
/// </remarks>
/// <param name="Tipos">Por nombre de la entrada o de lo efímero, su tipo y su origen. Opcional.</param>
/// <param name="Salida">Lo que el flujo devuelve al abrir, consultar, cotizar y continuar. Opcional.</param>
/// <param name="Lecturas">Las lecturas sin saga, por su nombre en la puerta (núcleo, NB-3 de 011). Opcional.</param>
/// <param name="PosteriorA">Si es una operación posterior al cierre (núcleo, P6 = A del lote 6): la clave del flujo cuya
/// saga completada lee, del mismo orquestador. La lee <c>flow.leer-original</c> y no la toca. Opcional.</param>
/// <param name="Acceso">Quién pide sus operaciones (núcleo, P8 = A del lote 6): sin valor, el dueño; <c>personal</c>, el
/// personal del negocio, y entonces la puerta comprueba su rol y <c>flow.leer-original</c> no aplica la regla de dueño
/// de la original. El sujeto es la persona del personal: el rastro de quién lo hizo. Opcional.</param>
/// <param name="ParaLoPosterior">Lo que la saga deja para una operación posterior al cierre (núcleo, D-011-NB10 y NN-18 de
/// 005): datos por su nombre y, con <c>reservas:&lt;paso&gt;</c>, lo que ese paso reservó. Se guarda con la saga y el
/// contrato no lo publica; <c>flow.leer-original</c> sólo puede leer esto. Opcional.</param>
public sealed record FlujoDef(
    string Clave,
    IReadOnlyList<string> Entrada,
    IReadOnlyList<FaseDef> Fases,
    IReadOnlyDictionary<string, PasoDef> Pasos,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Efimera = null,
    IReadOnlyDictionary<string, TipoDeclarado>? Tipos = null,
    IReadOnlyList<string>? Salida = null,
    IReadOnlyDictionary<string, LecturaDef>? Lecturas = null,
    string? PosteriorA = null,
    string? Acceso = null,
    IReadOnlyList<string>? ParaLoPosterior = null)
{
    /// <summary>Lo que deja para lo posterior: vacío si no declara nada.</summary>
    public IReadOnlyList<string> LoQueDejaParaLoPosterior => ParaLoPosterior ?? Array.Empty<string>();

    /// <summary>El único valor de <see cref="Acceso"/> que no es el del dueño.</summary>
    public const string AccesoDelPersonal = "personal";

    /// <summary>Si sus operaciones las pide el personal (P8) y no el dueño.</summary>
    public bool EsDePersonal => string.Equals(Acceso, AccesoDelPersonal, StringComparison.Ordinal);

    /// <summary>Las lecturas: vacío si no declara ninguna.</summary>
    public IReadOnlyDictionary<string, LecturaDef> LecturasDeclaradas
        => Lecturas ?? new Dictionary<string, LecturaDef>(StringComparer.Ordinal);

    /// <summary>Lo efímero de una fase: vacío si no declara nada.</summary>
    public IReadOnlyList<string> EfimeraDe(string fase)
        => Efimera?.GetValueOrDefault(fase) ?? Array.Empty<string>();

    /// <summary>Lo que devuelve: vacío si no declara nada.</summary>
    public IReadOnlyList<string> LoQueSale => Salida ?? Array.Empty<string>();

    /// <summary>La fase con ese nombre, o rechaza: una fase que no existe es un defecto del llamador.</summary>
    public FaseDef Fase(string nombre)
        => Fases.FirstOrDefault(f => string.Equals(f.Nombre, nombre, StringComparison.Ordinal))
           ?? throw new InvalidOperationException($"El flujo «{Clave}» no tiene la fase «{nombre}».");

    /// <summary>Lee una definición. Rechaza todo campo que el intérprete no conozca.</summary>
    /// <exception cref="FormatException">Si no es JSON, o si su forma no es la de un flujo.</exception>
    public static FlujoDef Leer(string json) => LectorDeFlujo.Leer(json);
}

/// <summary>Una fase: los pasos que se dan seguidos, en ese orden.</summary>
public sealed record FaseDef(string Nombre, IReadOnlyList<PasoRef> Pasos);

/// <summary>
/// Una lectura SIN saga (núcleo, NB-3 de 011): lo que se puede ver antes de abrir —las horas libres— sin apartar
/// ni cobrar nada, y por eso sin dueño.
/// </summary>
/// <param name="Nombre">Su nombre en la puerta, de una lista cerrada (<c>disponibilidad</c>).</param>
/// <param name="Entrada">Lo que llega, con su tipo en <c>tipos</c>: del cuerpo o del negocio, nunca un sujeto.</param>
/// <param name="Pasos">Los pasos, en orden: ninguno reserva, cierra, pide llave ni avisa.</param>
/// <param name="Salida">Lo que devuelve.</param>
/// <remarks>
/// <para><b>No es una fase.</b> Una fase continúa una saga y una lectura no tiene saga: no hay nada que deshacer,
/// ni a quién atarla. Es lo que deja a quien todavía no inició sesión ver las horas antes de reservar.</para>
///
/// <para><b>Con nombre de la puerta</b>, como cotizar: el nombre ES la operación, así que viene de una lista
/// cerrada que comparten el orquestador, la puerta del CMS y el generador del UI.</para>
/// </remarks>
public sealed record LecturaDef(string Nombre, IReadOnlyList<string> Entrada, IReadOnlyList<string> Pasos, IReadOnlyList<string> Salida);

/// <summary>Un elemento de una fase: un paso suelto o un bloque que se repite por ítem.</summary>
/// <param name="Paso">El identificador del paso, si es suelto.</param>
/// <param name="ParaCada">El bloque, si se repite.</param>
public sealed record PasoRef(string? Paso, ParaCadaDef? ParaCada);

/// <summary>
/// Pasos que se repiten, en orden, por cada ítem de <paramref name="Fuente"/>.
/// </summary>
/// <param name="Fuente">Una entrada del flujo que es una lista de ítems, o
/// <c>reservas:&lt;paso&gt;</c>: los apartados que ese paso dejó en la saga.</param>
/// <param name="Como">El nombre del ítem dentro del bloque: <c>linea</c> se lee entero y
/// <c>linea.itemId</c> es un campo suyo.</param>
/// <param name="Pasos">Los pasos del bloque.</param>
/// <remarks>
/// Existe porque apartar aforo es una línea a la vez, y cada apartado se anota compensable en el
/// instante en que existe: si la tercera línea falla, las dos primeras ya tienen quién las suelte.
/// </remarks>
public sealed record ParaCadaDef(string Fuente, string Como, IReadOnlyList<string> Pasos);

/// <summary>Un paso del flujo: qué tipo de paso es, qué lee, qué escribe y qué reserva.</summary>
/// <param name="Id">Su identificador dentro del flujo.</param>
/// <param name="Tipo">El paso registrado que lo ejecuta (<c>payments.autorizar</c>,
/// <c>eventos.revisar-lineas</c>).</param>
/// <param name="Lee">Los nombres que lee, en el orden en que el tipo los pide.</param>
/// <param name="Escribe">Los nombres donde deja lo que produce, en el orden en que lo produce.</param>
/// <param name="Llave">El sufijo FIJO de su llave de idempotencia: <c>{sagaId}|authorize</c>.</param>
/// <param name="LlaveBase">La base de una llave POR ÍTEM: el paso le añade el ítem en C#
/// (<c>{sagaId}|hold:{itemId}</c>). Es la alternativa a una plantilla en el dato.</param>
/// <param name="Reserva">Si el paso reserva algo que después se consuma o se deshace.</param>
/// <param name="CierraReserva">El paso cuya reserva éste consuma. Tiene que ser el que esa reserva
/// nombra en <c>consumado_por</c>: cada reserva tiene un solo cierre.</param>
/// <param name="Motivo">Con qué motivo se deshace la saga si este paso falla.</param>
/// <param name="AlFallar"><c>seguir</c>: si el paso rechaza o lanza, se anota y la fase sigue, sin
/// deshacer nada. Sólo vale después del último cierre de la última fase, en un paso que no reserva ni
/// escribe, o en el cierre de una reserva sin <c>despues</c>: lo que ya se cobró y se consumió no se
/// devuelve porque un aviso no salió. <c>soltar</c> (núcleo N2, NV-2 de Viajes): en el cierre por ítem de
/// un bloque sobre <c>reservas:&lt;paso&gt;</c>, si el paso rechaza y el dominio admite la confirmación
/// parcial, se deshace en el acto ESA reserva y la fase sigue con el siguiente ítem.</param>
/// <param name="OmitirSiCero">Un <c>Money</c> que el paso lee: si vale cero, el paso no corre ni
/// reserva nada, y su cierre se salta solo porque no hay reserva que cerrar. Es lo gratis: un cobro
/// de cero no es un cobro, y <c>Api.Payments</c> lo rechaza.</param>
/// <param name="Plantilla">La clave de la plantilla con que avisa un paso de aviso: el texto lo
/// escribe el dominio y vive en <c>Api.Notifications</c>, el flujo sólo dice cuál.</param>
/// <param name="Proposito">Para qué propósito comprueba el permiso un paso de consentimiento. Es un dato
/// de la definición, como la plantilla del aviso, y no del C# (núcleo N2, NN-3 de Salud).</param>
public sealed record PasoDef(
    string Id,
    string Tipo,
    IReadOnlyList<string> Lee,
    IReadOnlyList<string> Escribe,
    string? Llave,
    string? LlaveBase,
    ReservaDef? Reserva,
    string? CierraReserva,
    string? Motivo,
    string? AlFallar = null,
    string? OmitirSiCero = null,
    string? Plantilla = null,
    string? Proposito = null)
{
    /// <summary><c>al_fallar: seguir</c>: el fallo se anota y la fase sigue.</summary>
    public const string Seguir = "seguir";

    /// <summary><c>al_fallar: soltar</c>: el ítem se suelta y el bloque sigue, si el dominio lo admite.</summary>
    public const string Soltar = "soltar";

    /// <summary>Si un fallo de este paso se anota y la fase sigue.</summary>
    public bool SigueSiFalla => string.Equals(AlFallar, Seguir, StringComparison.Ordinal);

    /// <summary>Si un fallo de este paso suelta su ítem y el bloque sigue (cuando el dominio lo admite).</summary>
    public bool SueltaSiFalla => string.Equals(AlFallar, Soltar, StringComparison.Ordinal);
}

/// <summary>
/// La reserva en dos tiempos: con qué paso se consuma y cómo se deshace antes y después.
/// </summary>
/// <param name="ConsumadoPor">El paso que la consuma, en una fase posterior.</param>
/// <param name="Antes">El <c>Kind</c> de la compensación mientras no se consumió (soltar, anular).</param>
/// <param name="Despues">El <c>Kind</c> una vez consumida (reponer, devolver). Sin él, consumirla deja
/// la compensación hecha: no queda nada que deshacer.</param>
/// <param name="Motivo">El motivo con el que se anota la compensación.</param>
/// <remarks>
/// <b>Es el «cambio de carácter» como forma de primera clase.</b> Liberar una autorización ya
/// capturada lo rechaza <c>Api.Payments</c>, y soltar un apartado ya consumido lo rechaza
/// <c>Api.Inventory</c>: sin reescribir la compensación al consumir, quedaría colgada para siempre
/// por una razón que no tiene nada que ver con el mundo real. Hoy cada <c>*Flow.cs</c> la reescribe
/// a mano; declarada, la reescribe el intérprete.
/// </remarks>
public sealed record ReservaDef(string ConsumadoPor, string Antes, string? Despues, string? Motivo);

/// <summary>De dónde viene un valor que entra al flujo.</summary>
public enum OrigenDelValor
{
    /// <summary>Del cuerpo de la petición, por su nombre.</summary>
    Cuerpo,

    /// <summary>El sujeto que pone la puerta del CMS (<c>X-Synergos-Sujeto</c>): el dueño de la saga.</summary>
    Sujeto,

    /// <summary>Un campo de la configuración de negocio que pone la puerta (<c>X-Synergos-Negocio</c>).</summary>
    Negocio,

    /// <summary>El contacto del aviso que pone la puerta (<c>X-Synergos-Contacto</c>). Sólo efímero.</summary>
    Contacto,

    /// <summary>
    /// Lo que una operación posterior al cierre lee de la saga original (P6 = A): no entra, lo escribe
    /// <c>flow.leer-original</c> al abrir, con el tipo que declara aquí.
    /// </summary>
    Original,
}

/// <summary>El tipo de un valor del cuerpo: el nombre del tipo y, si es una referencia, su clase.</summary>
/// <param name="Tipo">El nombre del tipo de valor (<c>ref</c>, <c>texto?</c>…).</param>
/// <param name="Clase">El <c>kind</c> que admite una referencia (<c>salud.profesional</c>); nulo en lo
/// demás. Es un dato, como la plantilla del aviso: el host sólo lee del cuerpo una referencia de esa clase.</param>
public sealed record TipoDeCampo(string Tipo, string? Clase = null);

/// <summary>El tipo declarado de un valor que entra al flujo, y su origen.</summary>
/// <param name="Origen">De dónde viene.</param>
/// <param name="Tipo">El nombre del tipo de valor (<c>texto</c>, <c>dinero?</c>…), salvo en una lista.
/// En <see cref="OrigenDelValor.Sujeto"/> es <c>ref</c> y en <see cref="OrigenDelValor.Contacto"/>,
/// <c>contacto</c>: los pone el lector.</param>
/// <param name="Campos">Si es una lista de ítems, el tipo de cada campo del ítem; si no, nulo.</param>
/// <param name="Campo">En <see cref="OrigenDelValor.Negocio"/>, el campo de la configuración que se lee.</param>
/// <param name="Clase">Si es una referencia del cuerpo, la clase que admite (<c>{ "tipo": "ref", "kind": … }</c>).</param>
/// <param name="ReservasDe">En <see cref="OrigenDelValor.Original"/>, el paso de la original cuyas reservas se leen
/// (<c>{ "de": "original", "reservas": "apartar" }</c>): una lista de ítems con <see cref="CamposDeLoReservado"/>.</param>
/// <remarks>
/// El lector sólo comprueba la FORMA; que el nombre del tipo exista, y que una referencia del cuerpo
/// declare su clase, lo comprueba el validador contra el registro de tipos del orquestador, que es
/// ampliable (<c>Bff.Pasos</c> registra <c>contacto</c>).
/// </remarks>
public sealed record TipoDeclarado(
    OrigenDelValor Origen, string? Tipo, IReadOnlyDictionary<string, TipoDeCampo>? Campos = null, string? Campo = null,
    string? Clase = null, string? ReservasDe = null)
{
    /// <summary>El campo de un ítem reservado de la original: sobre qué se actúa ahora (lo que produjo su cierre).</summary>
    public const string CampoObjetivo = "objetivo";

    /// <summary>El campo de un ítem reservado de la original: cuántas unidades, si el paso lo dijo.</summary>
    public const string CampoCantidad = "cantidad";

    /// <summary>
    /// Los campos de cada ítem de lo que una original reservó (<see cref="ReservasDe"/>): lo reservado, sobre qué actuar
    /// ahora y cuánto.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, TipoDeCampo> CamposDeLoReservado = new Dictionary<string, TipoDeCampo>(StringComparer.Ordinal)
    {
        [HoldLeg.CampoHold] = new("texto"),
        [CampoObjetivo] = new("texto"),
        [CampoCantidad] = new("entero?"),
    };

    /// <summary>El tipo con que el lector declara un sujeto.</summary>
    public const string TipoDelSujeto = "ref";

    /// <summary>El tipo con que el lector declara un contacto.</summary>
    public const string TipoDelContacto = "contacto";

    /// <summary>Si es una lista de ítems.</summary>
    public bool EsLista => Campos is not null;
}

/// <summary>Lee el JSON de un flujo con la forma exacta que el intérprete ejecuta.</summary>
/// <remarks>
/// <para><b>Estricto con los campos que no conoce.</b> Un campo que el intérprete no lee es una
/// regla que nadie cumple: quien escribe <c>"si"</c> creería haber declarado una condición, y la
/// primera compra le enseñaría que no. Mejor que no arranque. Lo mismo con un valor que no
/// conoce: <c>al_fallar</c> admite <c>seguir</c> y <c>soltar</c>, y nada más.</para>
///
/// <para><b>Y con los que se repiten</b>, por la misma razón. JSON los admite y
/// <see cref="JsonDocument"/> también: en <c>pasos</c> ganaría el último sin aviso, y en
/// <c>fases</c> entrarían los dos —un <c>cerrar</c> repetido por un merge, del que se ejecuta el
/// primero y la saga nunca se completa—. Una de las dos definiciones se estaría ignorando.</para>
/// </remarks>
internal static class LectorDeFlujo
{
    public static FlujoDef Leer(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"La definición del flujo no es JSON válido: {ex.Message}", ex);
        }

        using (doc)
        {
            var raiz = doc.RootElement;
            SinRepetidos(raiz, "el flujo");
            Objeto(raiz, "el flujo");
            SoloEstos(raiz, "el flujo", "clave", "entrada", "fases", "pasos", "efimera", "tipos", "salida", "lecturas", "posterior_a", "acceso", "para_lo_posterior");

            var clave = Texto(raiz, "clave", "el flujo");
            var donde = $"el flujo «{clave}»";

            var entrada = Textos(raiz, "entrada", donde);

            var pasos = new Dictionary<string, PasoDef>(StringComparer.Ordinal);
            var nodoPasos = Requerido(raiz, "pasos", donde);
            Objeto(nodoPasos, $"{donde}, «pasos»");
            foreach (var p in nodoPasos.EnumerateObject())
            {
                pasos[p.Name] = Paso(p.Name, p.Value, donde);
            }

            var fases = new List<FaseDef>();
            var nodoFases = Requerido(raiz, "fases", donde);
            Objeto(nodoFases, $"{donde}, «fases»");
            foreach (var f in nodoFases.EnumerateObject())
            {
                fases.Add(Fase(f.Name, f.Value, donde));
            }

            Dictionary<string, IReadOnlyList<string>>? efimera = null;
            if (raiz.TryGetProperty("efimera", out var nodoEfimera))
            {
                Objeto(nodoEfimera, $"{donde}, «efimera»");
                efimera = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (var f in nodoEfimera.EnumerateObject())
                {
                    efimera[f.Name] = Textos(nodoEfimera, f.Name, $"{donde}, «efimera»");
                }
            }

            Dictionary<string, TipoDeclarado>? tipos = null;
            if (raiz.TryGetProperty("tipos", out var nodoTipos))
            {
                Objeto(nodoTipos, $"{donde}, «tipos»");
                tipos = new Dictionary<string, TipoDeclarado>(StringComparer.Ordinal);
                foreach (var t in nodoTipos.EnumerateObject())
                {
                    tipos[t.Name] = Tipo(t.Value, $"{donde}, el tipo de «{t.Name}»");
                }
            }

            IReadOnlyList<string>? salida = null;
            if (raiz.TryGetProperty("salida", out _))
            {
                salida = Textos(raiz, "salida", donde);
                if (salida.GroupBy(n => n, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } repetida)
                {
                    throw new FormatException($"{donde}: «salida» nombra «{repetida.Key}» dos veces.");
                }
            }

            Dictionary<string, LecturaDef>? lecturas = null;
            if (raiz.TryGetProperty("lecturas", out var nodoLecturas))
            {
                Objeto(nodoLecturas, $"{donde}, «lecturas»");
                lecturas = new Dictionary<string, LecturaDef>(StringComparer.Ordinal);
                foreach (var l in nodoLecturas.EnumerateObject())
                {
                    var aqui = $"{donde}, lectura «{l.Name}»";
                    Objeto(l.Value, aqui);
                    SoloEstos(l.Value, aqui, "entrada", "pasos", "salida");
                    lecturas[l.Name] = new LecturaDef(l.Name, Textos(l.Value, "entrada", aqui), Textos(l.Value, "pasos", aqui), Textos(l.Value, "salida", aqui));
                }
            }

            var posteriorA = raiz.TryGetProperty("posterior_a", out _) ? Texto(raiz, "posterior_a", donde) : null;

            // Una lista cerrada: el dueño es lo que se dice no diciendo nada, y otro acceso mal escrito no puede caer a él.
            var acceso = raiz.TryGetProperty("acceso", out _) ? Texto(raiz, "acceso", donde) : null;
            if (acceso is not null && !string.Equals(acceso, FlujoDef.AccesoDelPersonal, StringComparison.Ordinal))
            {
                throw new FormatException($"{donde}: «acceso» sólo admite «{FlujoDef.AccesoDelPersonal}» (sin él, lo pide el dueño), y trae «{acceso}».");
            }

            IReadOnlyList<string>? paraLoPosterior = null;
            if (raiz.TryGetProperty("para_lo_posterior", out _))
            {
                paraLoPosterior = Textos(raiz, "para_lo_posterior", donde);
                if (paraLoPosterior.GroupBy(n => n, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } repetido)
                {
                    throw new FormatException($"{donde}: «para_lo_posterior» nombra «{repetido.Key}» dos veces.");
                }
            }

            return new FlujoDef(clave, entrada, fases, pasos, efimera, tipos, salida, lecturas, posteriorA, acceso, paraLoPosterior);
        }
    }

    /// <summary>
    /// Un tipo declarado: un texto (el nombre del tipo, del cuerpo) o un objeto con su forma exacta.
    /// </summary>
    /// <remarks>
    /// Los orígenes son una lista cerrada: uno que no está es un valor que nadie pondría, y el paso que lo
    /// lea lanzaría con quien compra esperando. Que el NOMBRE del tipo exista no se mira aquí: el registro
    /// de tipos es del orquestador y lo cruza el validador.
    /// </remarks>
    private static TipoDeclarado Tipo(JsonElement nodo, string donde)
    {
        if (nodo.ValueKind == JsonValueKind.String)
        {
            return new TipoDeclarado(OrigenDelValor.Cuerpo, NombreDeTipo(nodo, donde));
        }

        if (nodo.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{donde}: tiene que ser el nombre de un tipo o un objeto con su forma.");
        }
        if (nodo.TryGetProperty("lista", out var lista))
        {
            SoloEstos(nodo, donde, "lista");
            Objeto(lista, $"{donde}, «lista»");
            var campos = new Dictionary<string, TipoDeCampo>(StringComparer.Ordinal);
            foreach (var c in lista.EnumerateObject())
            {
                campos[c.Name] = DelCuerpo(c.Value, $"{donde}, el campo «{c.Name}»");
            }
            if (campos.Count == 0) throw new FormatException($"{donde}: una «lista» sin campos no trae nada que leer.");
            return new TipoDeclarado(OrigenDelValor.Cuerpo, null, campos);
        }

        if (!nodo.TryGetProperty("de", out _))
        {
            var delCuerpo = DelCuerpo(nodo, donde);
            return new TipoDeclarado(OrigenDelValor.Cuerpo, delCuerpo.Tipo, Clase: delCuerpo.Clase);
        }

        var de = Texto(nodo, "de", donde);
        switch (de)
        {
            case "sujeto":
                SoloEstos(nodo, donde, "de");
                return new TipoDeclarado(OrigenDelValor.Sujeto, TipoDeclarado.TipoDelSujeto);
            case "contacto":
                SoloEstos(nodo, donde, "de");
                return new TipoDeclarado(OrigenDelValor.Contacto, TipoDeclarado.TipoDelContacto);
            case "negocio":
                SoloEstos(nodo, donde, "de", "campo", "tipo");
                return new TipoDeclarado(OrigenDelValor.Negocio,
                    NombreDeTipo(Requerido(nodo, "tipo", donde), $"{donde}, «tipo»"), null, Texto(nodo, "campo", donde));
            case "original" when nodo.TryGetProperty("reservas", out _):
                SoloEstos(nodo, donde, "de", "reservas");
                return new TipoDeclarado(OrigenDelValor.Original, null, TipoDeclarado.CamposDeLoReservado,
                    ReservasDe: Texto(nodo, "reservas", donde));
            case "original":
                SoloEstos(nodo, donde, "de", "tipo");
                return new TipoDeclarado(OrigenDelValor.Original, NombreDeTipo(Requerido(nodo, "tipo", donde), $"{donde}, «tipo»"));
            default:
                throw new FormatException(
                    $"{donde}: «de» admite «sujeto», «negocio», «contacto» u «original», y trae «{de}». Lo que viene en el cuerpo se declara con el nombre del tipo, o con «tipo» y «kind» si es una referencia.");
        }
    }

    /// <summary>
    /// Lo que viene del cuerpo: el nombre del tipo, o <c>{ "tipo", "kind" }</c> para una referencia con su clase.
    /// </summary>
    /// <remarks>
    /// La forma objeto existe para declarar la clase, así que la pide: sin ella diría lo mismo que el nombre solo.
    /// </remarks>
    private static TipoDeCampo DelCuerpo(JsonElement nodo, string donde)
    {
        if (nodo.ValueKind == JsonValueKind.String) return new TipoDeCampo(NombreDeTipo(nodo, donde));
        if (nodo.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{donde}: tiene que ser el nombre de un tipo (un texto no vacío) o {{ \"tipo\", \"kind\" }}.");
        }

        SoloEstos(nodo, donde, "tipo", "kind");
        return new TipoDeCampo(NombreDeTipo(Requerido(nodo, "tipo", donde), $"{donde}, «tipo»"), Texto(nodo, "kind", donde));
    }

    private static string NombreDeTipo(JsonElement nodo, string donde)
        => nodo.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(nodo.GetString())
            ? nodo.GetString()!
            : throw new FormatException($"{donde}: tiene que ser el nombre de un tipo (un texto no vacío).");

    private static FaseDef Fase(string nombre, JsonElement nodo, string donde)
    {
        var aqui = $"{donde}, fase «{nombre}»";
        if (nodo.ValueKind != JsonValueKind.Array) throw new FormatException($"{aqui}: tiene que ser una lista de pasos.");

        var refs = new List<PasoRef>();
        foreach (var e in nodo.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.String)
            {
                refs.Add(new PasoRef(e.GetString()!, null));
                continue;
            }

            Objeto(e, aqui);
            SoloEstos(e, $"{aqui}, bloque", "para_cada", "como", "pasos");
            refs.Add(new PasoRef(null, new ParaCadaDef(
                Texto(e, "para_cada", aqui), Texto(e, "como", aqui), Textos(e, "pasos", aqui))));
        }

        return new FaseDef(nombre, refs);
    }

    private static PasoDef Paso(string id, JsonElement nodo, string donde)
    {
        var aqui = $"{donde}, paso «{id}»";
        Objeto(nodo, aqui);
        SoloEstos(nodo, aqui,
            "tipo", "lee", "escribe", "llave", "llave_base", "reserva", "cierra_reserva", "motivo", "al_fallar",
            "omitir_si_cero", "plantilla", "proposito");

        var alFallar = Opcional(nodo, "al_fallar", aqui);
        if (alFallar is not null && alFallar is not (PasoDef.Seguir or PasoDef.Soltar))
        {
            throw new FormatException($"{aqui}: «al_fallar» sólo admite «{PasoDef.Seguir}» o «{PasoDef.Soltar}», y trae «{alFallar}».");
        }

        ReservaDef? reserva = null;
        if (nodo.TryGetProperty("reserva", out var r))
        {
            var deLaReserva = $"{aqui}, «reserva»";
            Objeto(r, deLaReserva);
            SoloEstos(r, deLaReserva, "consumado_por", "antes", "despues", "motivo");
            reserva = new ReservaDef(
                Texto(r, "consumado_por", deLaReserva), Texto(r, "antes", deLaReserva),
                Opcional(r, "despues", deLaReserva), Opcional(r, "motivo", deLaReserva));
        }

        return new PasoDef(
            id,
            Texto(nodo, "tipo", aqui),
            nodo.TryGetProperty("lee", out _) ? Textos(nodo, "lee", aqui) : Array.Empty<string>(),
            nodo.TryGetProperty("escribe", out _) ? Textos(nodo, "escribe", aqui) : Array.Empty<string>(),
            Opcional(nodo, "llave", aqui),
            Opcional(nodo, "llave_base", aqui),
            reserva,
            Opcional(nodo, "cierra_reserva", aqui),
            Opcional(nodo, "motivo", aqui),
            alFallar,
            Opcional(nodo, "omitir_si_cero", aqui),
            Opcional(nodo, "plantilla", aqui),
            Opcional(nodo, "proposito", aqui));
    }

    /// <summary>Rechaza un nombre de propiedad repetido en CUALQUIER objeto del documento.</summary>
    private static void SinRepetidos(JsonElement nodo, string donde)
    {
        if (nodo.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in nodo.EnumerateArray()) SinRepetidos(e, donde);
            return;
        }
        if (nodo.ValueKind != JsonValueKind.Object) return;

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in nodo.EnumerateObject())
        {
            if (!vistos.Add(p.Name))
            {
                throw new FormatException(
                    $"{donde}: trae «{p.Name}» dos veces. JSON lo admite y una de las dos se ignoraría sin aviso.");
            }
            SinRepetidos(p.Value, $"{donde} › «{p.Name}»");
        }
    }

    private static void Objeto(JsonElement nodo, string donde)
    {
        if (nodo.ValueKind != JsonValueKind.Object) throw new FormatException($"{donde}: tiene que ser un objeto.");
    }

    private static void SoloEstos(JsonElement nodo, string donde, params string[] conocidos)
    {
        foreach (var p in nodo.EnumerateObject())
        {
            if (!conocidos.Contains(p.Name, StringComparer.Ordinal))
            {
                throw new FormatException(
                    $"{donde}: trae «{p.Name}», que el intérprete no conoce. Los que conoce: {string.Join(", ", conocidos)}.");
            }
        }
    }

    private static JsonElement Requerido(JsonElement nodo, string campo, string donde)
        => nodo.TryGetProperty(campo, out var v) ? v : throw new FormatException($"{donde}: falta «{campo}».");

    private static string Texto(JsonElement nodo, string campo, string donde)
    {
        var v = Requerido(nodo, campo, donde);
        return v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : throw new FormatException($"{donde}: «{campo}» tiene que ser un texto no vacío.");
    }

    private static string? Opcional(JsonElement nodo, string campo, string donde)
        => nodo.TryGetProperty(campo, out _) ? Texto(nodo, campo, donde) : null;

    private static IReadOnlyList<string> Textos(JsonElement nodo, string campo, string donde)
    {
        var v = Requerido(nodo, campo, donde);
        if (v.ValueKind != JsonValueKind.Array) throw new FormatException($"{donde}: «{campo}» tiene que ser una lista.");

        return v.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())
                ? e.GetString()!
                : throw new FormatException($"{donde}: «{campo}» sólo admite textos no vacíos."))
            .ToList();
    }
}
