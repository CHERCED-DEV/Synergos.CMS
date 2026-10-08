using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Lo que una definición de flujo tiene que cumplir para que el orquestador ARRANQUE.
/// </summary>
/// <remarks>
/// <para><b>Un flujo mal escrito no llega a la primera compra.</b> Un tipo de paso que nadie
/// registró, una lectura sin escritura previa o una reserva que nadie consuma no fallan al leer el
/// JSON: fallan con un comprador esperando, y la de la reserva ni siquiera falla — deja una
/// compensación armada para siempre. Por eso se comprueba al arrancar
/// (<see cref="FlowDefinitionValidator"/>) y otra vez al construir el intérprete, que es lo que
/// cubre a quien lo arma sin contenedor.</para>
///
/// <para><b>Contra el CÓDIGO, no sólo contra sí misma.</b> Lo que una fase tiene al empezar, los
/// campos de un ítem de la entrada, las reservas que la saga sabe guardar y las fases que la
/// fachada invoca los pone el C# del orquestador, y el JSON sólo los nombra. Por eso se valida con
/// el <see cref="ContratoDelFlujo"/>: sin él, una errata en un nombre que pone el código pasa el
/// arranque.</para>
///
/// <para><b>Lo que NO comprueba, dicho:</b> los nombres que <see cref="IFlowBinding{TSaga}.Crear"/>
/// lee del contexto al sembrar la saga. Si faltan, la saga no nace y la compra falla antes de
/// reservar nada; no queda nada a medias.</para>
/// </remarks>
public static class FlowValidator
{
    /// <summary>La fuente de un bloque que repite sobre las reservas de un paso: <c>reservas:apartar</c>.</summary>
    public const string Reservas = "reservas:";

    /// <summary>Los problemas de <paramref name="flujo"/>, vacío si no tiene ninguno.</summary>
    /// <param name="flujo">La definición.</param>
    /// <param name="registro">Los pasos que el orquestador tiene registrados.</param>
    /// <param name="contrato">Lo que el código del orquestador da por hecho de ella.</param>
    public static IReadOnlyList<string> Validar(FlujoDef flujo, IRegistroDePasos registro, ContratoDelFlujo contrato)
    {
        ArgumentNullException.ThrowIfNull(flujo);
        ArgumentNullException.ThrowIfNull(registro);
        ArgumentNullException.ThrowIfNull(contrato);

        var errores = new List<string>();

        RevisarFases(flujo, contrato, errores);
        RevisarEfimera(flujo, contrato, errores);
        RevisarTipos(flujo, registro, errores);
        var donde = RevisarOrden(flujo, contrato, errores);

        foreach (var id in flujo.Pasos.Keys.Where(id => !donde.ContainsKey(id)).Order(StringComparer.Ordinal))
        {
            errores.Add($"declara el paso «{id}» y ninguna fase lo nombra: suele ser un nombre mal escrito en la fase.");
        }

        RevisarReservas(flujo, donde, contrato, errores);
        RevisarAlFallar(flujo, donde, errores);

        return errores.Select(e => $"El flujo «{flujo.Clave}»: {e}").ToList();
    }

    /// <summary>
    /// Lo efímero es de UNA fase, lo pone la fachada, y no puede tapar lo que la fase ya tiene.
    /// </summary>
    /// <remarks>
    /// Que no tape importa más de lo que parece: la entrada de una fase que continúa gana sobre lo
    /// reconstruido, así que una efímera llamada como algo que la saga guarda —el total— dejaría a
    /// la fachada reescribir lo que se cobra.
    /// </remarks>
    private static void RevisarEfimera(FlujoDef flujo, ContratoDelFlujo contrato, List<string> errores)
    {
        foreach (var (fase, nombres) in flujo.Efimera ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            var f = flujo.Fases.ToList().FindIndex(x => string.Equals(x.Nombre, fase, StringComparison.Ordinal));
            if (f < 0)
            {
                errores.Add($"declara lo efímero de la fase «{fase}», que no existe.");
                continue;
            }

            var alEmpezar = f == 0 ? flujo.Entrada : contrato.Reconstruye;
            foreach (var nombre in nombres)
            {
                if (!contrato.EfimeraDe(fase).Contains(nombre))
                {
                    errores.Add($"declara la efímera «{nombre}» en «{fase}» y la fachada no la pone ahí "
                        + $"({nameof(ContratoDelFlujo.Efimera)}).");
                }
                if (alEmpezar.Contains(nombre))
                {
                    errores.Add($"declara la efímera «{nombre}» en «{fase}», y la fase ya empieza con ese nombre: "
                        + "lo que pusiera la fachada taparía lo que la saga guarda.");
                }
            }
        }
    }

    /// <summary>
    /// <c>al_fallar: seguir</c> sólo donde seguir no deja nada a medias: después del último cierre.
    /// </summary>
    /// <remarks>
    /// <para>Antes de ese punto, seguir tras un fallo completaría una saga con algo reservado sin
    /// consumir, o consumido sin cobrar. Después, ya no queda nada que deshacer, y deshacer por un
    /// aviso que no salió devolvería la plata de una compra que sí se hizo.</para>
    ///
    /// <para>Por eso además no reserva, no cierra, no va en un bloque y no escribe: lo que escribiera
    /// un paso que puede fallar sin parar la fase no tendría con qué contar quien lo lea.</para>
    /// </remarks>
    private static void RevisarAlFallar(
        FlujoDef flujo, Dictionary<string, (int Fase, ParaCadaDef? Bloque)> donde, List<string> errores)
    {
        foreach (var paso in flujo.Pasos.Values.Where(p => p.AlFallar is not null))
        {
            var aqui = $"«{paso.Id}» declara «al_fallar»";
            if (paso.Reserva is not null || paso.CierraReserva is not null)
            {
                errores.Add($"{aqui} y reserva o cierra una reserva: seguir dejaría la saga a medias.");
            }
            if (paso.Escribe.Count > 0)
            {
                errores.Add($"{aqui} y escribe: si falla, quien lea lo que escribe no tendría qué leer.");
            }

            if (!donde.TryGetValue(paso.Id, out var u)) continue;

            var ultima = flujo.Fases.Count - 1;
            if (u.Fase != ultima)
            {
                errores.Add($"{aqui} y no está en la última fase: después de él todavía se reserva o se cierra algo.");
                continue;
            }
            if (u.Bloque is not null)
            {
                errores.Add($"{aqui} dentro de un bloque: sólo vale como paso suelto, después del último cierre.");
                continue;
            }

            var elementos = flujo.Fases[ultima].Pasos;
            var posicion = elementos.ToList().FindIndex(e => string.Equals(e.Paso, paso.Id, StringComparison.Ordinal));
            var ultimoCierre = elementos.ToList().FindLastIndex(e =>
                (e.ParaCada?.Pasos ?? new[] { e.Paso! }).Any(id =>
                    flujo.Pasos.TryGetValue(id, out var p) && p.CierraReserva is not null));
            if (posicion < ultimoCierre)
            {
                errores.Add($"{aqui} y va antes del último cierre de «{flujo.Fases[ultima].Nombre}»: seguir "
                    + "completaría la saga con una reserva sin consumir.");
            }
        }
    }

    private static void RevisarFases(FlujoDef flujo, ContratoDelFlujo contrato, List<string> errores)
    {
        if (flujo.Fases.Count == 0) errores.Add("no declara ninguna fase.");

        var vistas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fase in flujo.Fases)
        {
            if (string.IsNullOrWhiteSpace(fase.Nombre))
            {
                errores.Add("declara una fase sin nombre: nadie puede invocarla.");
            }
            else if (!vistas.Add(fase.Nombre))
            {
                errores.Add($"declara la fase «{fase.Nombre}» dos veces: sólo se ejecutaría la primera, y la saga no "
                    + "llegaría nunca a la última.");
            }

            if (fase.Pasos.Count == 0)
            {
                errores.Add($"la fase «{fase.Nombre}» no tiene pasos: si es la última, completaría la saga sin cerrar nada.");
            }
        }

        // En el MISMO orden: la primera abre la saga, y es la que la fachada invoca para abrir. Una
        // fase que la fachada no invoca no se ejecuta nunca, y lo que se consumiera en ella quedaría
        // armado; una que invoca y no existe lanza con el comprador esperando.
        var definidas = flujo.Fases.Select(f => f.Nombre).ToList();
        if (!definidas.SequenceEqual(contrato.Fases, StringComparer.Ordinal))
        {
            errores.Add($"declara las fases [{string.Join(", ", definidas)}] y la fachada invoca "
                + $"[{string.Join(", ", contrato.Fases)}]: tienen que ser las mismas y en el mismo orden.");
        }

        // La entrada es lo único que la fase que abre tiene antes del primer paso, y la pone el
        // código de la fachada: un nombre que el JSON declara y el código no pone lanzaría en el
        // paso que lo lea —al autorizar, con el aforo ya apartado—. Inclusión y no igualdad: que la
        // fachada ponga algo que este flujo no usa no rompe nada.
        foreach (var nombre in flujo.Entrada.Where(n => !contrato.PoneAlAbrir.Contains(n)))
        {
            errores.Add($"declara la entrada «{nombre}» y la fachada no la pone al abrir "
                + $"({nameof(ContratoDelFlujo.PoneAlAbrir)}: {string.Join(", ", contrato.PoneAlAbrir)}).");
        }
    }

    private static void RevisarTipos(FlujoDef flujo, IRegistroDePasos registro, List<string> errores)
    {
        foreach (var paso in flujo.Pasos.Values)
        {
            if (registro.Para(paso.Tipo) is not { } tipo)
            {
                errores.Add($"el paso «{paso.Id}» es de tipo «{paso.Tipo}», y ese tipo no está registrado.");
            }
            else
            {
                if (tipo.Lecturas != paso.Lee.Count)
                {
                    errores.Add($"el paso «{paso.Id}» declara {paso.Lee.Count} lectura(s) y «{paso.Tipo}» lee {tipo.Lecturas}.");
                }
                if (tipo.Escrituras != paso.Escribe.Count)
                {
                    errores.Add($"el paso «{paso.Id}» declara {paso.Escribe.Count} escritura(s) y «{paso.Tipo}» produce {tipo.Escrituras}.");
                }

                var llave = tipo.Llave switch
                {
                    LlaveRequerida.Ninguna when paso.Llave is not null || paso.LlaveBase is not null
                        => $"el paso «{paso.Id}» declara una llave y «{paso.Tipo}» no usa ninguna: quien la lea creerá "
                           + "que ese paso no se duplica.",
                    LlaveRequerida.Fija when paso.Llave is null
                        => $"el paso «{paso.Id}» es de tipo «{paso.Tipo}», que llama con «llave», y no la declara.",
                    LlaveRequerida.PorItem when paso.LlaveBase is null
                        => $"el paso «{paso.Id}» es de tipo «{paso.Tipo}», que llama con «llave_base» y el ítem, y no la declara.",
                    _ => null,
                };
                if (llave is not null) errores.Add(llave);
            }

            if (paso.Llave is not null && paso.LlaveBase is not null)
            {
                errores.Add($"el paso «{paso.Id}» declara «llave» y «llave_base»: es una o la otra.");
            }
        }
    }

    /// <summary>
    /// Recorre las fases en orden: cada lectura tiene que tener quién la escriba antes EN SU FASE.
    /// </summary>
    /// <remarks>
    /// El contexto es transitorio: lo que escribió <c>abrir</c> no llega a <c>cerrar</c>. La que
    /// abre empieza con la entrada; cada una que continúa, con lo que el binding reconstruye de la
    /// saga guardada y nada más. Con un solo conjunto para todas las fases, un paso de <c>cerrar</c>
    /// que leyera la cotización pasaba el arranque y lanzaba después de capturar.
    /// </remarks>
    /// <returns>Dónde aparece cada paso: en qué fase y, si se repite, en qué bloque.</returns>
    private static Dictionary<string, (int Fase, ParaCadaDef? Bloque)> RevisarOrden(
        FlujoDef flujo, ContratoDelFlujo contrato, List<string> errores)
    {
        var donde = new Dictionary<string, (int Fase, ParaCadaDef? Bloque)>(StringComparer.Ordinal);

        for (var f = 0; f < flujo.Fases.Count; f++)
        {
            var fase = flujo.Fases[f];
            // Lo efímero de ESTA fase se suma a lo que tiene al empezar; el de otra no está.
            var alEmpezar = (f == 0 ? flujo.Entrada : contrato.Reconstruye)
                .Concat(flujo.EfimeraDe(fase.Nombre))
                .ToList();
            var escritos = new HashSet<string>(alEmpezar, StringComparer.Ordinal);

            foreach (var elemento in fase.Pasos)
            {
                if (elemento.ParaCada is { } bloque)
                {
                    RevisarBloque(flujo, contrato, f, alEmpezar, bloque, escritos, donde, errores);
                    continue;
                }

                if (Declarado(flujo, fase.Nombre, elemento.Paso ?? string.Empty, errores) is not { } paso) continue;
                Ubicar(paso.Id, f, null, donde, errores);

                foreach (var nombre in paso.Lee.Where(n => !escritos.Contains(n)))
                {
                    errores.Add(NadieLoEscribe(paso, nombre, f, fase));
                }
                foreach (var nombre in paso.Escribe)
                {
                    if (nombre.Contains('.', StringComparison.Ordinal))
                    {
                        errores.Add($"«{paso.Id}» escribe «{nombre}» fuera de un bloque: no hay ítem al que ponérselo.");
                    }
                    else
                    {
                        escritos.Add(nombre);
                    }
                }
            }
        }

        return donde;
    }

    private static string NadieLoEscribe(PasoDef paso, string nombre, int f, FaseDef fase)
        => f == 0
            ? $"«{paso.Id}» lee «{nombre}» y nadie lo escribe antes (ni es una entrada)."
            : $"«{paso.Id}» lee «{nombre}» y nadie lo escribe antes en la fase «{fase.Nombre}» (ni lo reconstruye "
              + "la saga al continuarla: lo que escribió una fase anterior no se guarda).";

    private static void RevisarBloque(
        FlujoDef flujo, ContratoDelFlujo contrato, int f, IReadOnlyCollection<string> alEmpezar, ParaCadaDef bloque,
        HashSet<string> escritos, Dictionary<string, (int Fase, ParaCadaDef? Bloque)> donde, List<string> errores)
    {
        // Los campos que el ítem trae de origen: los de una reserva son los de HoldLeg, y los de una
        // lista de la entrada los declara el código que la arma. Nada se acepta sin declarar.
        var deOrigen = new HashSet<string>(StringComparer.Ordinal);

        if (bloque.Fuente.StartsWith(Reservas, StringComparison.Ordinal))
        {
            var reservante = bloque.Fuente[Reservas.Length..];
            if (!donde.TryGetValue(reservante, out var u) || u.Bloque is null || u.Fase >= f
                || flujo.Pasos[reservante].Reserva is null)
            {
                errores.Add($"un bloque repite sobre «{bloque.Fuente}» y «{reservante}» no es un paso que "
                    + "reserve por ítem en una fase anterior.");
            }
            deOrigen.Add(HoldLeg.CampoHold);
            deOrigen.Add(HoldLeg.CampoCierre);
        }
        else if (!alEmpezar.Contains(bloque.Fuente, StringComparer.Ordinal))
        {
            errores.Add(f == 0
                ? $"un bloque repite sobre «{bloque.Fuente}», que no es una entrada ni «{Reservas}<paso>»."
                : $"un bloque repite sobre «{bloque.Fuente}», que la saga no reconstruye al continuarla ni es «{Reservas}<paso>».");
        }
        else if (contrato.CamposDeItem.TryGetValue(bloque.Fuente, out var campos))
        {
            deOrigen.UnionWith(campos);
        }
        else
        {
            errores.Add($"un bloque repite sobre «{bloque.Fuente}» y el código no declara qué campos trae cada ítem "
                + $"({nameof(ContratoDelFlujo.CamposDeItem)}).");
        }

        if (escritos.Contains(bloque.Como))
        {
            errores.Add($"el bloque sobre «{bloque.Fuente}» llama al ítem «{bloque.Como}», que ya es un nombre del contexto.");
        }

        var yaEscritos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in bloque.Pasos)
        {
            if (Declarado(flujo, $"para_cada {bloque.Fuente}", id, errores) is not { } paso) continue;
            Ubicar(paso.Id, f, bloque, donde, errores);

            foreach (var nombre in paso.Lee)
            {
                if (string.Equals(nombre, bloque.Como, StringComparison.Ordinal)) continue;

                if (Ambito.Campo(nombre, bloque.Como) is { } campo)
                {
                    if (!yaEscritos.Contains(campo) && !deOrigen.Contains(campo))
                    {
                        errores.Add($"«{paso.Id}» lee «{nombre}» y en ese punto del bloque nadie lo escribió (ni lo trae el ítem).");
                    }
                    continue;
                }

                if (!escritos.Contains(nombre)) errores.Add(NadieLoEscribe(paso, nombre, f, flujo.Fases[f]));
            }

            foreach (var nombre in paso.Escribe)
            {
                if (Ambito.Campo(nombre, bloque.Como) is { } campo) yaEscritos.Add(campo);
                else if (nombre.Contains('.', StringComparison.Ordinal))
                {
                    errores.Add($"«{paso.Id}» escribe «{nombre}», que no es un campo de «{bloque.Como}».");
                }
                else escritos.Add(nombre);
            }
        }
    }

    private static void RevisarReservas(
        FlujoDef flujo, Dictionary<string, (int Fase, ParaCadaDef? Bloque)> donde, ContratoDelFlujo contrato,
        List<string> errores)
    {
        var algunaReserva = false;

        foreach (var paso in flujo.Pasos.Values)
        {
            if (paso.Reserva is { } reserva)
            {
                if (string.IsNullOrWhiteSpace(reserva.Antes))
                {
                    errores.Add($"la reserva de «{paso.Id}» no dice cómo se deshace antes de consumirla («antes»).");
                }
                if (reserva.Despues is not null && string.IsNullOrWhiteSpace(reserva.Despues))
                {
                    errores.Add($"la reserva de «{paso.Id}» declara «despues» vacío.");
                }

                if (!donde.TryGetValue(paso.Id, out var u)) continue;
                algunaReserva = true;

                // Lo reservado se guarda en la saga por el nombre del paso: si la saga no sabe
                // guardarlo, la capacidad ya reservó y nadie lo anota para deshacerlo.
                var forma = u.Bloque is null ? FormaDeReserva.Unica : FormaDeReserva.PorItem;
                if (!contrato.Reservas.TryGetValue(paso.Id, out var guardada))
                {
                    errores.Add($"«{paso.Id}» reserva y la saga no sabe guardar lo que reserva: el binding no lo "
                        + $"declara en {nameof(ContratoDelFlujo.Reservas)}.");
                }
                else if (guardada != forma)
                {
                    errores.Add($"«{paso.Id}» reserva {Forma(forma)} y el binding la guarda {Forma(guardada)}.");
                }

                if (!flujo.Pasos.TryGetValue(reserva.ConsumadoPor, out var consumidor))
                {
                    errores.Add($"la reserva de «{paso.Id}» se consuma con «{reserva.ConsumadoPor}», que no está declarado. "
                        + "Sin quien la consuma, su compensación queda armada para siempre.");
                    continue;
                }

                if (!string.Equals(consumidor.CierraReserva, paso.Id, StringComparison.Ordinal))
                {
                    errores.Add($"la reserva de «{paso.Id}» se consuma con «{consumidor.Id}», y «{consumidor.Id}» "
                        + $"no lo declara con «cierra_reserva: {paso.Id}».");
                }

                if (!donde.TryGetValue(consumidor.Id, out var c)) continue;

                if (c.Fase <= u.Fase)
                {
                    errores.Add($"la reserva de «{paso.Id}» se consuma con «{consumidor.Id}», que no está en una fase "
                        + "POSTERIOR: reservar y consumir en la misma llamada no es una reserva en dos tiempos.");
                }

                var esperado = u.Bloque is null ? null : Reservas + paso.Id;
                if (!string.Equals(c.Bloque?.Fuente, esperado, StringComparison.Ordinal))
                {
                    errores.Add(u.Bloque is null
                        ? $"la reserva de «{paso.Id}» es única y «{consumidor.Id}» la consuma dentro de un bloque."
                        : $"la reserva de «{paso.Id}» es por ítem y «{consumidor.Id}» tiene que repetirse sobre «{esperado}».");
                }
            }

            if (paso.CierraReserva is { } cerrada)
            {
                // El cierre es UNO, y es el que la reserva nombra: sólo a ése se le comprueban la fase
                // posterior y el bloque. Un segundo paso con «cierra_reserva» se saltaría las dos y
                // podría capturar en la misma llamada que autoriza.
                if (!flujo.Pasos.TryGetValue(cerrada, out var reservante) || reservante.Reserva is null)
                {
                    errores.Add($"«{paso.Id}» cierra la reserva de «{cerrada}», que no reserva nada.");
                }
                else if (!string.Equals(reservante.Reserva.ConsumadoPor, paso.Id, StringComparison.Ordinal))
                {
                    errores.Add($"«{paso.Id}» cierra la reserva de «{cerrada}», y esa reserva se consuma con "
                        + $"«{reservante.Reserva.ConsumadoPor}»: cada reserva tiene un solo cierre.");
                }
            }
        }

        if (algunaReserva && !typeof(IHoldLedger).IsAssignableFrom(contrato.Saga))
        {
            errores.Add($"reserva y {contrato.Saga.Name} no implementa {nameof(IHoldLedger)}: el intérprete no tendría "
                + "de dónde leer lo reservado para cerrarlo.");
        }
    }

    private static string Forma(FormaDeReserva forma)
        => forma == FormaDeReserva.Unica ? "una vez por saga" : "una vez por ítem";

    private static PasoDef? Declarado(FlujoDef flujo, string donde, string id, List<string> errores)
    {
        if (flujo.Pasos.TryGetValue(id, out var paso)) return paso;
        errores.Add($"«{donde}» nombra el paso «{id}», que no está declarado en «pasos».");
        return null;
    }

    private static void Ubicar(
        string id, int fase, ParaCadaDef? bloque,
        Dictionary<string, (int Fase, ParaCadaDef? Bloque)> donde, List<string> errores)
    {
        if (!donde.TryAdd(id, (fase, bloque)))
        {
            errores.Add($"el paso «{id}» aparece dos veces: su reserva y su llave serían ambiguas.");
        }
    }
}

/// <summary>Un flujo que el orquestador declara, con lo que su código da por hecho de él.</summary>
public sealed record FlujoRegistrado(FlujoDef Flujo, ContratoDelFlujo Contrato);

/// <summary>Los flujos que un orquestador declara, como opciones que se validan al arrancar.</summary>
public sealed class FlowCatalog
{
    private readonly Dictionary<string, FlujoRegistrado> _flujos = new(StringComparer.Ordinal);

    public IReadOnlyCollection<FlujoRegistrado> Flujos => _flujos.Values;

    /// <summary>Añade (o reemplaza, por clave) un flujo.</summary>
    public FlowCatalog Registrar(FlujoDef flujo, ContratoDelFlujo contrato)
    {
        ArgumentNullException.ThrowIfNull(flujo);
        ArgumentNullException.ThrowIfNull(contrato);
        _flujos[flujo.Clave] = new FlujoRegistrado(flujo, contrato);
        return this;
    }
}

/// <summary>
/// Valida el catálogo contra los pasos registrados. Con <c>ValidateOnStart</c>, el proceso NO arranca.
/// </summary>
/// <remarks>
/// Es la misma forma que el validador de la configuración de negocio (ADR 0137): una opción que
/// no sirve se dice al arrancar, no la primera vez que alguien la usa.
/// </remarks>
public sealed class FlowDefinitionValidator : IValidateOptions<FlowCatalog>
{
    private readonly IRegistroDePasos _registro;

    public FlowDefinitionValidator(IRegistroDePasos registro) => _registro = registro;

    public ValidateOptionsResult Validate(string? name, FlowCatalog options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Vacío no es «nada que validar»: es un orquestador que se olvidó de declarar su flujo, y
        // que arrancaría sano para fallar en la primera llamada.
        if (options.Flujos.Count == 0)
        {
            return ValidateOptionsResult.Fail("El catálogo de flujos está vacío: el orquestador no declaró ninguno.");
        }

        var errores = options.Flujos.SelectMany(f => FlowValidator.Validar(f.Flujo, _registro, f.Contrato)).ToList();
        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }
}

/// <summary>El registro de los flujos de un orquestador, uno por llamada.</summary>
public static class FlowRegistration
{
    /// <summary>
    /// Registra <paramref name="flujo"/> y su validación AL ARRANCAR, contra lo que su código da por
    /// hecho. El orquestador registra además su <see cref="IRegistroDePasos"/>, que es contra lo que
    /// se validan los tipos de paso.
    /// </summary>
    /// <param name="services">El contenedor.</param>
    /// <param name="flujo">La definición.</param>
    /// <param name="binding">Cómo la guarda su saga: lo que reconstruye, los campos de sus ítems y
    /// las reservas que sabe guardar.</param>
    /// <param name="fases">Las fases que la fachada invoca, en orden: las mismas con las que arma su
    /// <see cref="FlowRunner{TSaga}"/>.</param>
    public static IServiceCollection AddFlow<TSaga>(
        this IServiceCollection services, FlujoDef flujo, IFlowBinding<TSaga> binding, IReadOnlyList<string> fases)
        where TSaga : class, ISaga<TSaga>
    {
        var contrato = ContratoDelFlujo.De(binding, fases);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FlowCatalog>, FlowDefinitionValidator>());
        services.AddOptions<FlowCatalog>()
            .Configure(catalogo => catalogo.Registrar(flujo, contrato))
            .ValidateOnStart();
        return services;
    }
}
