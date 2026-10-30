using System.Text.Json;
using Synergos.Core;

namespace Synergos.Bff.Core.Flow;

/// <summary>
/// El binding de un flujo sin saga propia, derivado de su definición (núcleo N1): lo que un orquestador
/// escribía a mano en su <c>*FlowBinding</c> (Eventos: 105 líneas), aquí sale de los datos.
/// </summary>
/// <remarks>
/// <para><b>Qué se guarda</b>: lo que leen las fases que continúan antes de escribirlo
/// (<see cref="Reconstruye"/>) y lo que el flujo declara como salida. Nada más: lo demás del contexto es
/// transitorio, y lo efímero (la dirección del aviso) no se guarda nunca.</para>
///
/// <para><b>Cuándo</b>: en cada escritura de la saga (<see cref="AlGuardar"/>), con lo que el contexto
/// tenga en ese instante. Así el cobro que acaba de autorizarse queda guardado en la MISMA escritura que
/// su compensación: si el proceso se cae después, la fase que cierra tiene de dónde leerlo.</para>
///
/// <para><b>Con qué tipo</b>: el de <c>tipos</c> para lo que entra, y el que declara el paso que lo
/// escribe (<see cref="IPaso.TiposDeEscritura"/>), anulable, para lo que produce un paso —un paso que se
/// omite deja nulo lo que escribiría—. Que todo lo guardado tenga un tipo que se pueda guardar lo
/// comprueba el validador al arrancar, no la primera compra.</para>
/// </remarks>
public sealed class BindingDeFlujo : IFlowBinding<SagaDeFlujo>
{
    private readonly FlujoDef _flujo;
    private readonly TiposDeValor _tipos;
    private readonly IRegistroDePasos _pasos;
    private readonly IExtensionDelFlujo? _extension;
    private readonly string? _sujeto;

    /// <param name="flujo">La definición, con sus <c>tipos</c>.</param>
    /// <param name="tipos">Los tipos de valor que conoce el orquestador.</param>
    /// <param name="pasos">Los pasos registrados: de ellos sale el tipo de lo que escriben.</param>
    /// <param name="deshacer">Los tipos de compensación que sabe deshacer el ejecutor de la saga genérica.</param>
    /// <param name="extension">Lo que contesta el vertical cuando un cierre por ítem declara <c>al_fallar: soltar</c>
    /// (NV-13); nula si nadie extiende el flujo.</param>
    public BindingDeFlujo(
        FlujoDef flujo, TiposDeValor tipos, IRegistroDePasos pasos, IReadOnlyCollection<string> deshacer, IExtensionDelFlujo? extension = null)
    {
        ArgumentNullException.ThrowIfNull(flujo);
        ArgumentNullException.ThrowIfNull(tipos);
        ArgumentNullException.ThrowIfNull(pasos);
        ArgumentNullException.ThrowIfNull(deshacer);
        _flujo = flujo;
        _tipos = tipos;
        _pasos = pasos;
        _extension = extension;
        Deshacer = deshacer;

        _sujeto = flujo.Entrada.FirstOrDefault(n => flujo.Tipos?.GetValueOrDefault(n)?.Origen == OrigenDelValor.Sujeto);
        // Lo que la extensión lee o reescribe al soltar lo tiene la fase que cierra sólo si la saga lo guarda (NV-13).
        Reconstruye = LoQueSeReconstruye(flujo)
            .Union((extension?.Lee ?? []).Concat(extension?.Escribe ?? []), StringComparer.Ordinal)
            .ToList();
        Guarda = Reconstruye.Union(flujo.LoQueSale, StringComparer.Ordinal)
            .Union(flujo.LoQueDejaParaLoPosterior.Where(n => !n.StartsWith(FlowValidator.Reservas, StringComparison.Ordinal)), StringComparer.Ordinal)
            .ToList();
        CamposDeItem = flujo.Entrada
            .Where(n => flujo.Tipos?.GetValueOrDefault(n) is { EsLista: true })
            .ToDictionary(n => n, n => (IReadOnlyCollection<string>)flujo.Tipos![n].Campos!.Keys.ToList(), StringComparer.Ordinal);
        Reservas = ReservasDe(flujo);
        Efimera = (flujo.Efimera ?? new Dictionary<string, IReadOnlyList<string>>())
            .ToDictionary(e => e.Key, e => (IReadOnlyCollection<string>)e.Value, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> PoneAlAbrir => _flujo.Entrada;

    public IReadOnlyCollection<string> Reconstruye { get; }

    /// <summary>Lo que se guarda en la saga: lo que se reconstruye, la salida y lo que deja para lo posterior.</summary>
    public IReadOnlyList<string> Guarda { get; }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> CamposDeItem { get; }

    public IReadOnlyDictionary<string, FormaDeReserva> Reservas { get; }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Efimera { get; }

    public TiposDeValor Tipos => _tipos;

    public IReadOnlyCollection<string> Deshacer { get; }

    public IReadOnlyCollection<string>? ArmaLaExtension => _extension?.Arma;

    /// <summary>El nombre de la entrada que es el dueño (el sujeto de la puerta).</summary>
    public string Sujeto => _sujeto ?? throw new InvalidOperationException(
        $"El flujo «{_flujo.Clave}» no declara qué entrada es el sujeto ({{ \"de\": \"sujeto\" }}).");

    /// <summary>
    /// El tipo con que se guarda <paramref name="nombre"/>: el declarado si entra, o el del paso que lo
    /// escribe (anulable). <c>null</c> si no se sabe.
    /// </summary>
    public TipoResuelto? TipoDe(string nombre) => TipoDe(_flujo, _tipos, _pasos, nombre);

    /// <inheritdoc cref="TipoDe(string)"/>
    /// <remarks>Estático porque el validador hace la misma pregunta sin un binding a mano.</remarks>
    public static TipoResuelto? TipoDe(FlujoDef flujo, TiposDeValor tipos, IRegistroDePasos pasos, string nombre)
    {
        ArgumentNullException.ThrowIfNull(flujo);
        ArgumentNullException.ThrowIfNull(tipos);
        ArgumentNullException.ThrowIfNull(pasos);

        if (flujo.Tipos?.GetValueOrDefault(nombre) is { } declarado)
        {
            return declarado.EsLista ? null : tipos.Resolver(declarado.Tipo!, declarado.Clase);
        }

        var escritos = flujo.Pasos.Values
            .SelectMany(p => p.Escribe.Select((n, i) => (Nombre: n, Clr: pasos.Para(p.Tipo)?.TiposDeEscritura?.ElementAtOrDefault(i))))
            .Where(e => string.Equals(e.Nombre, nombre, StringComparison.Ordinal))
            .Select(e => e.Clr)
            .Distinct()
            .ToList();

        return escritos is [{ } clr] && tipos.PorClr(clr) is { } tipo ? new TipoResuelto(tipo, Anulable: true) : null;
    }

    public SagaDeFlujo Crear(string sagaId, FlowContext ctx, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var saga = new SagaDeFlujo(
            sagaId, _flujo.Clave, ctx.Get<Ref>(Sujeto), SagaStatus.Running,
            new Dictionary<string, ValorDeFlujo>(StringComparer.Ordinal),
            Reservas.Keys.ToDictionary(p => p, _ => (IReadOnlyList<ReservaDeFlujo>)Array.Empty<ReservaDeFlujo>(), StringComparer.Ordinal),
            Array.Empty<Compensation>(), null, ahora);
        return AlGuardar(saga, ctx);
    }

    public FlowContext Leer(SagaDeFlujo saga)
    {
        ArgumentNullException.ThrowIfNull(saga);
        var ctx = new FlowContext();
        foreach (var nombre in Guarda)
        {
            if (saga.Datos.GetValueOrDefault(nombre) is not { } guardado)
            {
                // Nunca se escribió (la fase saltó antes): como el `PaymentId` nulo de una compra sin cobro.
                ctx.Set(nombre, null);
                continue;
            }

            var tipo = _tipos.Resolver(guardado.Tipo)
                ?? throw new InvalidOperationException($"La saga {saga.Id} guarda «{nombre}» como «{guardado.Tipo}», un tipo que nadie registró.");
            ctx.Set(nombre, tipo.Leer(guardado.Valor, $"la saga {saga.Id}, «{nombre}»"));
        }
        return ctx;
    }

    public SagaDeFlujo ConReserva(SagaDeFlujo saga, string paso, HoldLeg reservado, FlowContext? item)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(reservado);
        return saga.ConReserva(paso, new ReservaDeFlujo(reservado.HoldId, reservado.CloseTargetId, reservado.Cantidad));
    }

    /// <summary>
    /// Anota en la reserva cerrada sobre qué quedó (<see cref="ReservaDeFlujo.Cerrada"/>): lo que produjo el cierre o, si
    /// no produjo nada, su objetivo de siempre. Va en la MISMA escritura que el cambio de su compensación (NV-3).
    /// </summary>
    public SagaDeFlujo ConCierre(SagaDeFlujo saga, string reservante, HoldLeg cerrada, string? objetivo)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(cerrada);
        if (!saga.Reservas.TryGetValue(reservante, out var reservas)) return saga;

        var anotada = false;
        var nuevas = reservas.Select(r =>
        {
            if (anotada || r.Cerrada is not null || !string.Equals(r.HoldId, cerrada.HoldId, StringComparison.Ordinal)) return r;
            anotada = true;
            return r with { Cerrada = objetivo ?? r.CloseTargetId };
        }).ToList();

        return anotada
            ? saga with
            {
                Reservas = new Dictionary<string, IReadOnlyList<ReservaDeFlujo>>(saga.Reservas, StringComparer.Ordinal) { [reservante] = nuevas },
            }
            : saga;
    }

    /// <summary>Si ESTA saga suelta el ítem cuyo cierre falló: lo contesta la extensión del vertical; sin ella, no (NV-13).</summary>
    public bool SueltaSiFalla(SagaDeFlujo saga, FlowContext ctx)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(ctx);
        return _extension?.SueltaSiFalla(saga, ctx) ?? false;
    }

    /// <summary>
    /// Lo que la extensión hace con el ítem soltado, con lo que reescribió en el contexto ya guardado en la saga: lo que
    /// arma se ejecuta en el acto con esta saga, y tiene que encontrar ahí lo que necesita (NV-13).
    /// </summary>
    public SagaDeFlujo ConItemSoltado(SagaDeFlujo saga, string reservante, HoldLeg soltada, FlowContext ctx)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(ctx);
        return _extension is null ? saga : AlGuardar(_extension.ConItemSoltado(saga, ctx, reservante, soltada), ctx);
    }

    /// <summary>La saga genérica guarda la fase en duda en su record (001-P10 = A).</summary>
    public bool GuardaLaDuda => true;

    public string? FaseEnDuda(SagaDeFlujo saga)
    {
        ArgumentNullException.ThrowIfNull(saga);
        return saga.FaseEnDuda;
    }

    public SagaDeFlujo ConFaseEnDuda(SagaDeFlujo saga, string? fase)
    {
        ArgumentNullException.ThrowIfNull(saga);
        return saga with { FaseEnDuda = fase };
    }

    public SagaDeFlujo ConError(SagaDeFlujo saga, string? falla)
    {
        ArgumentNullException.ThrowIfNull(saga);
        return saga with { LastError = falla };
    }

    /// <summary>La saga con lo que el contexto tiene de lo que se guarda; la MISMA instancia si no cambió nada.</summary>
    public SagaDeFlujo AlGuardar(SagaDeFlujo saga, FlowContext ctx)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(ctx);

        var cambios = new Dictionary<string, ValorDeFlujo>(StringComparer.Ordinal);
        foreach (var nombre in Guarda.Where(ctx.Has))
        {
            var tipo = TipoDe(nombre)
                ?? throw new InvalidOperationException($"El flujo «{_flujo.Clave}» guarda «{nombre}» y no se sabe su tipo.");
            var valor = ctx.Get<object?>(nombre);
            if (valor is not null && !tipo.Tipo.Clr.IsInstanceOfType(valor))
            {
                throw new InvalidOperationException(
                    $"El flujo «{_flujo.Clave}» guarda «{nombre}» como «{tipo.Declarado}» y el contexto trae {valor.GetType().Name}.");
            }

            var nuevo = new ValorDeFlujo(tipo.Declarado, tipo.Escribir(valor) is { } json ? JsonSerializer.SerializeToElement(json) : null);
            if (saga.Datos.GetValueOrDefault(nombre) is not { } viejo || !Igual(viejo, nuevo)) cambios[nombre] = nuevo;
        }

        return cambios.Count == 0 ? saga : saga.ConDatos(cambios);
    }

    private static bool Igual(ValorDeFlujo a, ValorDeFlujo b)
        => string.Equals(a.Tipo, b.Tipo, StringComparison.Ordinal)
           && string.Equals(a.Valor?.GetRawText(), b.Valor?.GetRawText(), StringComparison.Ordinal);

    /// <summary>
    /// Lo que cada fase que continúa lee antes de escribirlo, sin los campos de un ítem ni lo efímero
    /// de esa fase: lo único que tiene al empezar es lo que la saga reconstruye.
    /// </summary>
    public static IReadOnlyList<string> LoQueSeReconstruye(FlujoDef flujo)
    {
        ArgumentNullException.ThrowIfNull(flujo);
        var salida = new List<string>();

        foreach (var fase in flujo.Fases.Skip(1))
        {
            var escritos = new HashSet<string>(flujo.EfimeraDe(fase.Nombre), StringComparer.Ordinal);
            foreach (var elemento in fase.Pasos)
            {
                var bloque = elemento.ParaCada;
                if (bloque is not null && !bloque.Fuente.StartsWith(FlowValidator.Reservas, StringComparison.Ordinal))
                {
                    Necesita(bloque.Fuente);
                }

                foreach (var id in bloque?.Pasos ?? [elemento.Paso!])
                {
                    if (!flujo.Pasos.TryGetValue(id, out var paso)) continue;
                    // Con los valores que declara un aviso (NM-2): el título del curso que se lee al cerrar se guarda al abrir.
                    foreach (var nombre in paso.LoQueLee)
                    {
                        if (bloque is not null
                            && (string.Equals(nombre, bloque.Como, StringComparison.Ordinal) || Ambito.Campo(nombre, bloque.Como) is not null))
                        {
                            continue;
                        }
                        Necesita(nombre);
                    }
                    foreach (var nombre in paso.Escribe.Where(n => bloque is null || Ambito.Campo(n, bloque.Como) is null))
                    {
                        escritos.Add(nombre);
                    }
                }
            }

            void Necesita(string nombre)
            {
                if (!escritos.Contains(nombre) && !salida.Contains(nombre, StringComparer.Ordinal)) salida.Add(nombre);
            }
        }

        return salida;
    }

    /// <summary>Cada paso que reserva, una vez por saga si va suelto y una por ítem si va en un bloque.</summary>
    private static IReadOnlyDictionary<string, FormaDeReserva> ReservasDe(FlujoDef flujo)
    {
        var reservas = new Dictionary<string, FormaDeReserva>(StringComparer.Ordinal);
        foreach (var elemento in flujo.Fases.SelectMany(f => f.Pasos))
        {
            foreach (var id in elemento.ParaCada?.Pasos ?? [elemento.Paso!])
            {
                if (flujo.Pasos.TryGetValue(id, out var paso) && paso.Reserva is not null)
                {
                    reservas[id] = elemento.ParaCada is null ? FormaDeReserva.Unica : FormaDeReserva.PorItem;
                }
            }
        }
        return reservas;
    }
}
