using Microsoft.Extensions.DependencyInjection;
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
/// <para><b>Lo que NO comprueba, dicho:</b> los campos que un ítem de ENTRADA trae de origen
/// (<c>linea.cantidad</c>) no están declarados en ningún sitio, así que se aceptan; sí se exige que
/// un campo que algún paso del bloque escribe no se lea antes de escribirlo.</para>
/// </remarks>
public static class FlowValidator
{
    /// <summary>La fuente de un bloque que repite sobre los apartados de un paso: <c>reservas:apartar</c>.</summary>
    public const string Reservas = "reservas:";

    /// <summary>Los problemas de <paramref name="flujo"/>, vacío si no tiene ninguno.</summary>
    /// <param name="flujo">La definición.</param>
    /// <param name="registro">Los pasos que el orquestador tiene registrados.</param>
    /// <param name="saga">Si se conoce, el tipo de saga: tiene que tener las ranuras que el flujo usa.</param>
    public static IReadOnlyList<string> Validar(FlujoDef flujo, IRegistroDePasos registro, Type? saga = null)
    {
        ArgumentNullException.ThrowIfNull(flujo);
        ArgumentNullException.ThrowIfNull(registro);

        var errores = new List<string>();
        if (flujo.Fases.Count == 0) errores.Add("no declara ninguna fase.");

        RevisarTipos(flujo, registro, errores);
        var donde = RevisarOrden(flujo, errores);

        foreach (var id in flujo.Pasos.Keys.Where(id => !donde.ContainsKey(id)).Order(StringComparer.Ordinal))
        {
            errores.Add($"declara el paso «{id}» y ninguna fase lo nombra: suele ser un nombre mal escrito en la fase.");
        }

        RevisarReservas(flujo, donde, saga, errores);

        return errores.Select(e => $"El flujo «{flujo.Clave}»: {e}").ToList();
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
            }

            if (paso.Llave is not null && paso.LlaveBase is not null)
            {
                errores.Add($"el paso «{paso.Id}» declara «llave» y «llave_base»: es una o la otra.");
            }
        }
    }

    /// <summary>Recorre las fases en orden: cada lectura tiene que tener quién la escriba antes.</summary>
    /// <returns>Dónde aparece cada paso: en qué fase y, si se repite, en qué bloque.</returns>
    private static Dictionary<string, (int Fase, ParaCadaDef? Bloque)> RevisarOrden(FlujoDef flujo, List<string> errores)
    {
        var donde = new Dictionary<string, (int Fase, ParaCadaDef? Bloque)>(StringComparer.Ordinal);
        var escritos = new HashSet<string>(flujo.Entrada, StringComparer.Ordinal);

        for (var f = 0; f < flujo.Fases.Count; f++)
        {
            var fase = flujo.Fases[f];
            foreach (var elemento in fase.Pasos)
            {
                if (elemento.ParaCada is { } bloque)
                {
                    RevisarBloque(flujo, f, bloque, escritos, donde, errores);
                    continue;
                }

                if (Declarado(flujo, fase.Nombre, elemento.Paso ?? string.Empty, errores) is not { } paso) continue;
                Ubicar(paso.Id, f, null, donde, errores);

                foreach (var nombre in paso.Lee.Where(n => !escritos.Contains(n)))
                {
                    errores.Add($"«{paso.Id}» lee «{nombre}» y nadie lo escribe antes (ni es una entrada).");
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

    private static void RevisarBloque(
        FlujoDef flujo, int f, ParaCadaDef bloque, HashSet<string> escritos,
        Dictionary<string, (int Fase, ParaCadaDef? Bloque)> donde, List<string> errores)
    {
        // Los campos que el ítem trae de origen. De los apartados de un paso se saben —son los de
        // HoldLeg—; los de una entrada no se declaran, y por eso se aceptan los que nadie escribe.
        var deOrigen = new HashSet<string>(StringComparer.Ordinal);
        var opaco = false;

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
        else if (flujo.Entrada.Contains(bloque.Fuente, StringComparer.Ordinal))
        {
            opaco = true;
        }
        else
        {
            errores.Add($"un bloque repite sobre «{bloque.Fuente}», que no es una entrada ni «{Reservas}<paso>».");
            opaco = true;
        }

        if (escritos.Contains(bloque.Como))
        {
            errores.Add($"el bloque sobre «{bloque.Fuente}» llama al ítem «{bloque.Como}», que ya es un nombre del contexto.");
        }

        var pasos = bloque.Pasos
            .Select(id => Declarado(flujo, $"para_cada {bloque.Fuente}", id, errores))
            .OfType<PasoDef>()
            .ToList();

        var losEscribe = pasos
            .SelectMany(p => p.Escribe)
            .Select(n => Ambito.Campo(n, bloque.Como))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var yaEscritos = new HashSet<string>(StringComparer.Ordinal);

        foreach (var paso in pasos)
        {
            Ubicar(paso.Id, f, bloque, donde, errores);

            foreach (var nombre in paso.Lee)
            {
                if (string.Equals(nombre, bloque.Como, StringComparison.Ordinal)) continue;

                if (Ambito.Campo(nombre, bloque.Como) is { } campo)
                {
                    var disponible = yaEscritos.Contains(campo)
                                     || deOrigen.Contains(campo)
                                     || (opaco && !losEscribe.Contains(campo));
                    if (!disponible)
                    {
                        errores.Add($"«{paso.Id}» lee «{nombre}» y en ese punto del bloque nadie lo escribió.");
                    }
                    continue;
                }

                if (!escritos.Contains(nombre))
                {
                    errores.Add($"«{paso.Id}» lee «{nombre}» y nadie lo escribe antes (ni es una entrada).");
                }
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
        FlujoDef flujo, Dictionary<string, (int Fase, ParaCadaDef? Bloque)> donde, Type? saga, List<string> errores)
    {
        var porItem = 0;
        var unicas = 0;

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
                if (u.Bloque is null) unicas++;
                else porItem++;

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

            if (paso.CierraReserva is { } cerrada
                && (!flujo.Pasos.TryGetValue(cerrada, out var reservante) || reservante.Reserva is null))
            {
                errores.Add($"«{paso.Id}» cierra la reserva de «{cerrada}», que no reserva nada.");
            }
        }

        // Una ranura de cada, y es un límite de F1 dicho en voz alta: Tienda tiene dos reservas
        // únicas (el pedido y el cobro) y portarla obliga a decidir cómo se nombran.
        if (porItem > 1 || unicas > 1)
        {
            errores.Add($"declara {porItem} reserva(s) por ítem y {unicas} única(s), y el intérprete lleva "
                + $"una ranura de cada ({nameof(IHoldLedger)}, {nameof(IChargeLedger)}).");
        }

        if (saga is null) return;
        if (porItem > 0 && !typeof(IHoldLedger).IsAssignableFrom(saga))
        {
            errores.Add($"reserva por ítem y {saga.Name} no implementa {nameof(IHoldLedger)}.");
        }
        if (unicas > 0 && !typeof(IChargeLedger).IsAssignableFrom(saga))
        {
            errores.Add($"tiene una reserva única y {saga.Name} no implementa {nameof(IChargeLedger)}.");
        }
    }

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

/// <summary>Los flujos que un orquestador declara, como opciones que se validan al arrancar.</summary>
public sealed class FlowCatalog
{
    private readonly Dictionary<string, FlujoDef> _flujos = new(StringComparer.Ordinal);

    public IReadOnlyCollection<FlujoDef> Flujos => _flujos.Values;

    /// <summary>Añade (o reemplaza, por clave) un flujo.</summary>
    public FlowCatalog Registrar(FlujoDef flujo)
    {
        ArgumentNullException.ThrowIfNull(flujo);
        _flujos[flujo.Clave] = flujo;
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

        var errores = options.Flujos.SelectMany(f => FlowValidator.Validar(f, _registro)).ToList();
        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }
}

/// <summary>El registro de los flujos de un orquestador, en una llamada.</summary>
public static class FlowRegistration
{
    /// <summary>
    /// Registra <paramref name="flujos"/> y su validación AL ARRANCAR. El orquestador registra
    /// además su <see cref="IRegistroDePasos"/>, que es contra lo que se validan.
    /// </summary>
    public static IServiceCollection AddFlows(this IServiceCollection services, params FlujoDef[] flujos)
    {
        services.AddSingleton<IValidateOptions<FlowCatalog>, FlowDefinitionValidator>();
        services.AddOptions<FlowCatalog>()
            .Configure(catalogo =>
            {
                foreach (var flujo in flujos) catalogo.Registrar(flujo);
            })
            .ValidateOnStart();
        return services;
    }
}
