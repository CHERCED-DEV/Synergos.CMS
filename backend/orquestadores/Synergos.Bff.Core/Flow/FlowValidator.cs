using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Synergos.Core;

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

        var enLecturas = flujo.LecturasDeclaradas.Values.SelectMany(l => l.Pasos).ToHashSet(StringComparer.Ordinal);
        foreach (var id in flujo.Pasos.Keys.Where(id => !donde.ContainsKey(id) && !enLecturas.Contains(id)).Order(StringComparer.Ordinal))
        {
            errores.Add($"declara el paso «{id}» y ninguna fase lo nombra: suele ser un nombre mal escrito en la fase.");
        }

        RevisarReservas(flujo, donde, contrato, errores);
        RevisarAlFallar(flujo, donde, errores);
        RevisarConservadas(flujo, registro, errores);
        RevisarPosterior(flujo, registro, donde, errores);
        RevisarParaLoPosterior(flujo, contrato, errores);
        RevisarOmitirSiCero(flujo, errores);
        RevisarOmitirSiFalta(flujo, registro, contrato, errores);
        RevisarModoGenerico(flujo, registro, contrato, errores);
        RevisarLecturas(flujo, registro, contrato, errores);

        return errores.Select(e => $"El flujo «{flujo.Clave}»: {e}").ToList();
    }

    /// <summary>
    /// Los nombres de fase que chocarían con las rutas que el host mapea por convención, o con el nombre que
    /// esas rutas llevan en la puerta (<c>listar</c>, NB-6, y las lecturas, NB-3: dos operaciones con un nombre).
    /// </summary>
    public static readonly IReadOnlyList<string> FasesReservadas =
        ["cotizar", "cancelar", "consultar", "reintentar", "compensaciones", "listar", "disponibilidad"];

    /// <summary>
    /// Los nombres que puede tener una lectura sin saga (núcleo, NB-3 de 011): son operaciones de la puerta, así que
    /// la lista es cerrada y la comparten el orquestador, la puerta del CMS y el generador del UI.
    /// </summary>
    public static readonly IReadOnlyList<string> LecturasDeLaPuerta = ["disponibilidad"];

    /// <summary>
    /// Las lecturas sin saga (NB-3 de 011): lo que se ve antes de abrir, sin apartar ni cobrar nada, y sin dueño.
    /// </summary>
    /// <remarks>
    /// <para><b>No escribe en ninguna capacidad</b>, y es lo que la deja abrirse a quien todavía no inició sesión:
    /// ningún paso de una lectura reserva, cierra, pide llave, avisa ni sigue si falla. Uno con llave es uno que
    /// escribe, como en <c>FlowRunner.AnticiparAsync</c>, y también uno que actúa sin ella (<see cref="IPaso.ActuaSinLlave"/>,
    /// NR-8 de 008): anular o cancelar es escribir aunque no se pida llave.</para>
    ///
    /// <para><b>Sin dueño</b>: su entrada viene del cuerpo o del negocio del sitio, nunca del sujeto ni del contacto
    /// de la sesión. La pide un invitado; con un sujeto sería una operación de alguien.</para>
    ///
    /// <para><b>Lee lo que tiene</b>: cada paso lee lo que entra en la lectura o lo que escribió un paso anterior de
    /// ELLA, no lo que una fase escribiría en una saga que no existe.</para>
    /// </remarks>
    private static void RevisarLecturas(FlujoDef flujo, IRegistroDePasos registro, ContratoDelFlujo contrato, List<string> errores)
    {
        foreach (var lectura in flujo.LecturasDeclaradas.Values.OrderBy(l => l.Nombre, StringComparer.Ordinal))
        {
            var aqui = $"la lectura «{lectura.Nombre}»";
            if (!LecturasDeLaPuerta.Contains(lectura.Nombre, StringComparer.Ordinal))
            {
                errores.Add($"{aqui} no es una lectura de la puerta ({string.Join(", ", LecturasDeLaPuerta)}): nadie podría pedirla.");
            }
            if (contrato.Tipos is not { } tipos)
            {
                errores.Add($"{aqui} necesita «tipos»: el host lee su entrada con ellos, y un flujo tipado no los declara.");
                continue;
            }
            if (lectura.Pasos.Count == 0) errores.Add($"{aqui} no tiene pasos: no leería nada.");

            var disponibles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var nombre in lectura.Entrada)
            {
                var tipo = flujo.Tipos?.GetValueOrDefault(nombre);
                if (tipo is null)
                {
                    errores.Add($"{aqui} recibe «{nombre}» y no declara su tipo en «tipos».");
                }
                else if (tipo.Origen is OrigenDelValor.Sujeto or OrigenDelValor.Contacto)
                {
                    errores.Add($"{aqui} recibe «{nombre}» del {tipo.Origen.ToString().ToLowerInvariant()}, y una lectura no tiene dueño "
                        + "ni avisa: la pide quien todavía no inició sesión.");
                }
                disponibles.Add(nombre);
            }

            foreach (var id in lectura.Pasos)
            {
                if (!flujo.Pasos.TryGetValue(id, out var paso))
                {
                    errores.Add($"{aqui} nombra el paso «{id}», que no está declarado.");
                    continue;
                }
                // Un tipo que pide llave obliga a declararla (RevisarTipos), así que mirar la llave declarada basta.
                if (paso.Reserva is not null || paso.CierraReserva is not null || paso.Llave is not null || paso.LlaveBase is not null
                    || paso.Plantilla is not null || paso.AlFallar is not null || paso.OmitirSiCero is not null
                    || paso.OmitirSiFalta is not null
                    || registro.Para(paso.Tipo) is { ActuaSinLlave: true })
                {
                    errores.Add($"{aqui} da el paso «{id}», que reserva, cierra, pide llave, avisa u omite: una lectura no escribe "
                        + "en ninguna capacidad.");
                }
                foreach (var leido in paso.Lee.Where(n => !disponibles.Contains(n)))
                {
                    errores.Add($"{aqui}: «{id}» lee «{leido}», que ni entra en la lectura ni lo escribe un paso anterior de ella.");
                }
                foreach (var escrito in paso.Escribe) disponibles.Add(escrito);
            }

            if (lectura.Salida.Count == 0) errores.Add($"{aqui} no devuelve nada.");
            foreach (var nombre in lectura.Salida)
            {
                if (!disponibles.Contains(nombre))
                {
                    errores.Add($"{aqui} devuelve «{nombre}», que ni entra ni lo escribe un paso de ella.");
                }
                else if (BindingDeFlujo.TipoDe(flujo, tipos, registro, nombre) is null)
                {
                    errores.Add($"{aqui} devuelve «{nombre}» y no se sabe su tipo: ni entra con tipo ni lo escribe un paso que declare el suyo.");
                }
            }
        }
    }

    /// <summary>
    /// Lo que no se guarda en una saga genérica por su NOMBRE: el mismo olfato que
    /// <c>PropagacionDeIdentidadTests.Ninguna_saga_guarda_una_credencial</c> aplica a los parámetros de un record de saga.
    /// </summary>
    /// <remarks>
    /// En la saga genérica, «añadir un campo al record» es añadir una palabra a la definición: el gate de la
    /// fuente no la ve, así que la regla vive aquí y corre al arrancar.
    /// </remarks>
    public static readonly IReadOnlyList<string> HuelenACredencial = ["Token", "Bearer", "Credential", "Credencial", "Jwt"];

    /// <summary>
    /// Las reglas de un flujo GENÉRICO (núcleo N1): el host lee su entrada y guarda su saga con los tipos
    /// que declara, así que lo que falte o no cuadre se dice al arrancar.
    /// </summary>
    /// <remarks>
    /// Sólo corre si el contrato trae los tipos de valor del orquestador (<see cref="ContratoDelFlujo.Tipos"/>),
    /// que es lo que pone el binding derivado. Un flujo tipado (Eventos) no las cumple ni las necesita.
    /// </remarks>
    private static void RevisarModoGenerico(FlujoDef flujo, IRegistroDePasos registro, ContratoDelFlujo contrato, List<string> errores)
    {
        if (contrato.Tipos is not { } tipos) return;

        var declarados = flujo.Tipos ?? new Dictionary<string, TipoDeclarado>(StringComparer.Ordinal);
        var efimeros = (flujo.Efimera ?? new Dictionary<string, IReadOnlyList<string>>())
            .SelectMany(e => e.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var nombre in flujo.Entrada.Concat(efimeros).Where(n => !declarados.ContainsKey(n)).Distinct(StringComparer.Ordinal))
        {
            errores.Add($"«{nombre}» entra y no declara su tipo en «tipos»: el host no sabría de dónde leerlo ni cómo.");
        }

        // Lo que entra en una lectura (NB-3) también se declara en «tipos»; sus reglas propias, en RevisarLecturas.
        var deLecturas = flujo.LecturasDeclaradas.Values.SelectMany(l => l.Entrada).ToHashSet(StringComparer.Ordinal);

        foreach (var (nombre, tipo) in declarados.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            var esEntrada = flujo.Entrada.Contains(nombre, StringComparer.Ordinal);
            if (!esEntrada && !efimeros.Contains(nombre) && !deLecturas.Contains(nombre) && tipo.Origen != OrigenDelValor.Original)
            {
                errores.Add($"declara el tipo de «{nombre}», que no es una entrada, ni algo efímero, ni lo que entra en una lectura.");
            }

            var valores = tipo.EsLista
                ? tipo.Campos!.Select(c => (Donde: $"{nombre}.{c.Key}", Declarado: c.Value))
                : [(Donde: nombre, Declarado: new TipoDeCampo(tipo.Tipo!, tipo.Clase))];
            foreach (var (donde, declarado) in valores)
            {
                if (tipos.Resolver(declarado.Tipo) is not { } resuelto)
                {
                    errores.Add($"el tipo de «{nombre}» es «{declarado.Tipo}», y ese tipo de valor no está registrado.");
                    continue;
                }

                var esReferencia = resuelto.Tipo.Clr == typeof(Ref);
                if (declarado.Clase is not null && !esReferencia)
                {
                    errores.Add($"«{donde}» declara la clase «{declarado.Clase}» y es «{declarado.Tipo}»: sólo una referencia tiene clase.");
                }
                else if (esReferencia && declarado.Clase is null && tipo.Origen == OrigenDelValor.Cuerpo)
                {
                    errores.Add($"«{donde}» es una referencia que viene del cuerpo y no declara su clase "
                        + "({ \"tipo\": \"ref\", \"kind\": \"…\" }): el host sólo lee del cuerpo la clase que la definición fija.");
                }
            }

            switch (tipo.Origen)
            {
                case OrigenDelValor.Contacto when !efimeros.Contains(nombre) || esEntrada:
                    errores.Add($"«{nombre}» es el contacto del aviso y no es sólo efímero: un dato personal acabaría en la saga.");
                    break;
                case OrigenDelValor.Negocio when !esEntrada && deLecturas.Contains(nombre):
                    break;
                case OrigenDelValor.Original when esEntrada || efimeros.Contains(nombre) || deLecturas.Contains(nombre):
                    errores.Add($"«{nombre}» viene de la original y además entra: lo escribe quien la lee, al abrir.");
                    break;
                case OrigenDelValor.Sujeto or OrigenDelValor.Negocio when !esEntrada && !deLecturas.Contains(nombre):
                    errores.Add($"«{nombre}» viene de la puerta ({tipo.Origen}) y no es una entrada: sólo la fase que abre lo recibe.");
                    break;
            }
        }

        var sujetos = flujo.Entrada.Count(n => declarados.GetValueOrDefault(n)?.Origen == OrigenDelValor.Sujeto);
        if (sujetos != 1)
        {
            errores.Add($"la entrada tiene que traer exactamente un sujeto ({{ \"de\": \"sujeto\" }}), que es el dueño de la saga, y trae {sujetos}.");
        }

        foreach (var fase in flujo.Fases.Where(f => FasesReservadas.Contains(f.Nombre, StringComparer.Ordinal)))
        {
            errores.Add($"la fase «{fase.Nombre}» se llama como una operación del host ({string.Join(", ", FasesReservadas)}): su ruta chocaría.");
        }

        var escritos = flujo.Pasos.Values.SelectMany(p => p.Escribe).ToHashSet(StringComparer.Ordinal);
        foreach (var nombre in flujo.LoQueSale.Where(n => !escritos.Contains(n) && !flujo.Entrada.Contains(n, StringComparer.Ordinal)))
        {
            errores.Add($"la salida nombra «{nombre}», que ningún paso escribe y no entra: saldría siempre vacía.");
        }

        var escalaresParaLoPosterior = flujo.LoQueDejaParaLoPosterior.Where(n => !n.StartsWith(Reservas, StringComparison.Ordinal)).ToList();
        foreach (var nombre in escalaresParaLoPosterior.Where(n => !escritos.Contains(n) && !flujo.Entrada.Contains(n, StringComparer.Ordinal)))
        {
            errores.Add($"deja para lo posterior «{nombre}», que ningún paso escribe y no entra: lo posterior lo leería siempre vacío.");
        }

        foreach (var nombre in contrato.Reconstruye.Union(flujo.LoQueSale, StringComparer.Ordinal).Union(escalaresParaLoPosterior, StringComparer.Ordinal))
        {
            if (HuelenACredencial.FirstOrDefault(o => nombre.Contains(o, StringComparison.OrdinalIgnoreCase)) is { } olor)
            {
                errores.Add($"«{nombre}» se guarda en la saga y se llama como una credencial («{olor}»): el almacén de "
                    + "sagas se respalda, y un bearer ahí convierte la copia de datos en un llavero. Si de verdad hace "
                    + "falta, que sea efímero de la fase que lo usa.");
            }

            var tipo = BindingDeFlujo.TipoDe(flujo, tipos, registro, nombre);
            if (tipo is null)
            {
                errores.Add($"«{nombre}» se guarda en la saga (lo lee una fase que continúa, sale o queda para lo posterior) y no se sabe su tipo: "
                    + "ni entra con tipo ni lo escribe un paso que declare el suyo, o lo escriben dos con tipos distintos.");
            }
            else if (!tipo.Tipo.Guardable)
            {
                errores.Add($"«{nombre}» se guarda en la saga y su tipo, «{tipo.Declarado}», no se puede guardar.");
            }
        }

        if (contrato.Deshacer is { } conocidos)
        {
            foreach (var paso in flujo.Pasos.Values.Where(p => p.Reserva is not null).OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                foreach (var tipo in new[] { paso.Reserva!.Antes, paso.Reserva.Despues }.OfType<string>()
                             .Where(t => !conocidos.Contains(t, StringComparer.Ordinal)))
                {
                    errores.Add($"la reserva de «{paso.Id}» se deshace con «{tipo}», y nadie sabe deshacer ese tipo "
                        + $"(se conocen: {string.Join(", ", conocidos.Order(StringComparer.Ordinal))}).");
                }
            }
        }
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
    /// <para>Por eso además no reserva, no va en un bloque y no escribe: lo que escribiera un paso que
    /// puede fallar sin parar la fase no tendría con qué contar quien lo lea.</para>
    ///
    /// <para><b>Cerrar sí, con una condición</b> (núcleo N2, N-3 de Tienda): el cierre de una reserva SIN
    /// <c>despues</c> puede seguir si falla. Cumplir el pedido tras despachar es eso: con la plata cobrada
    /// y el envío hecho, un pedido sin cumplir se concilia, no se anula. Completar marca hecha su
    /// compensación, así que nadie lo anula. Con <c>despues</c>, cerrar es lo que mueve la plata o
    /// consume, y completar sin cerrar dejaría la saga cobrada sin cobro. Y detrás de ese cierre sólo
    /// pueden ir pasos que también siguen: uno que abortara deshaciendo lo demás anularía un pedido que
    /// quizá sí se cumplió.</para>
    /// </remarks>
    private static void RevisarAlFallar(
        FlujoDef flujo, Dictionary<string, (int Fase, ParaCadaDef? Bloque)> donde, List<string> errores)
    {
        foreach (var paso in flujo.Pasos.Values.Where(p => p.AlFallar is not null))
        {
            var aqui = $"«{paso.Id}» declara «al_fallar»";

            // soltar (NV-2): sólo en el cierre por ítem de un bloque sobre las reservas que cierra. Ahí hay un ítem
            // que soltar con su «antes»; en cualquier otro sitio no habría qué soltar ni a quién preguntar.
            if (paso.SueltaSiFalla)
            {
                if (paso.CierraReserva is not { } cierra)
                {
                    errores.Add($"«{paso.Id}» declara «al_fallar: soltar» y no cierra ninguna reserva: no hay ítem que soltar.");
                }
                else if (!donde.TryGetValue(paso.Id, out var dondeSuelta)
                         || !string.Equals(dondeSuelta.Bloque?.Fuente, Reservas + cierra, StringComparison.Ordinal))
                {
                    errores.Add($"«{paso.Id}» declara «al_fallar: soltar» fuera de un bloque sobre «{Reservas}{cierra}»: "
                        + "sólo ahí hay un ítem de esa reserva que soltar y seguir con los demás.");
                }
                continue;
            }
            if (paso.Reserva is not null)
            {
                errores.Add($"{aqui} y reserva: seguir dejaría la saga a medias.");
            }
            if (paso.CierraReserva is { } cerrada
                && flujo.Pasos.TryGetValue(cerrada, out var reservante) && reservante.Reserva?.Despues is { } despues)
            {
                errores.Add($"{aqui} y cierra la reserva de «{cerrada}», que se deshace con «{despues}» después de "
                    + "cerrarla: completar sin cerrarla dejaría la saga como si se hubiera consumido.");
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

            if (paso.CierraReserva is not null)
            {
                var detras = elementos.Skip(posicion + 1)
                    .SelectMany(e => e.ParaCada?.Pasos ?? new[] { e.Paso! })
                    .Where(id => !flujo.Pasos.TryGetValue(id, out var p) || p.AlFallar is null)
                    .ToList();
                if (detras.Count > 0)
                {
                    errores.Add($"{aqui} y cierra una reserva, y detrás van pasos que no siguen si fallan "
                        + $"({string.Join(", ", detras.Select(d => $"«{d}»"))}): uno que abortara deshaciendo la saga "
                        + "anularía lo que este cierre quizá sí cerró.");
                }
                continue;
            }

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

    /// <summary>
    /// Lo que se cierra sin consumir (P7 = A, <c>payments.conservar</c>): queda vivo fuera de la saga, así que tiene
    /// que verse.
    /// </summary>
    /// <remarks>
    /// <para><b>Sin <c>despues</c></b>: lo conservado no se consumió. Un <c>despues</c> diría que, una vez cerrado, se
    /// deshace distinto, y es justo lo que un cierre que conserva no hace.</para>
    ///
    /// <para><b>Con su id a la vista</b>: una garantía retenida que nadie sabe cuál es no la liquida nadie. El id es
    /// lo primero que escribe el paso que reserva, y sale en la salida o la original lo deja para lo posterior
    /// (<c>para_lo_posterior</c>, D-011-NB10), por su nombre o con <c>reservas:&lt;paso&gt;</c>.</para>
    /// </remarks>
    private static void RevisarConservadas(FlujoDef flujo, IRegistroDePasos registro, List<string> errores)
    {
        foreach (var paso in flujo.Pasos.Values.Where(p => p.CierraReserva is not null).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (registro.Para(paso.Tipo) is not { Conserva: true }
                || !flujo.Pasos.TryGetValue(paso.CierraReserva!, out var reservante) || reservante.Reserva is not { } reserva)
            {
                continue;
            }

            var aqui = $"«{paso.Id}» conserva la reserva de «{reservante.Id}»";
            if (reserva.Despues is { } despues)
            {
                errores.Add($"{aqui}, que declara «despues: {despues}»: lo conservado no se consume, y después de "
                    + "completar lo resuelve la operación posterior.");
            }
            if (reservante.Escribe.Count == 0
                || !(flujo.LoQueSale.Contains(reservante.Escribe[0], StringComparer.Ordinal)
                     || flujo.LoQueDejaParaLoPosterior.Contains(reservante.Escribe[0], StringComparer.Ordinal)
                     || flujo.LoQueDejaParaLoPosterior.Contains(Reservas + reservante.Id, StringComparer.Ordinal)))
            {
                errores.Add($"{aqui} y su id no sale en la salida ni lo deja para lo posterior: lo reservado queda vivo "
                    + "fuera de la saga, y nadie sabría cuál liquidar.");
            }
        }
    }

    /// <summary>
    /// Una operación posterior al cierre (P6 = A): un flujo aparte que lee su original con UN paso, al abrir, y declara
    /// el tipo de lo que lee de ella.
    /// </summary>
    /// <remarks>
    /// <para><b>Un solo lector, en la fase que abre y fuera de un bloque</b>: la original se lee una vez, antes de mover
    /// nada. Leerla después sería decidir con un dato que pudo cambiar entre fases, y dos lectores, dos verdades.</para>
    ///
    /// <para><b>Lo que escribe es exactamente lo declarado <c>de: original</c></b>, y nadie más lo escribe: así el tipo de
    /// cada dato está dicho en la definición, y ningún paso posterior lo pisa con otra cosa. Que la original lo tenga en
    /// su salida, con ese tipo, se cruza al arrancar entre los flujos del orquestador
    /// (<see cref="ValidarPosteriores"/>).</para>
    /// </remarks>
    private static void RevisarPosterior(
        FlujoDef flujo, IRegistroDePasos registro, Dictionary<string, (int Fase, ParaCadaDef? Bloque)> donde, List<string> errores)
    {
        var deLaOriginal = (flujo.Tipos ?? new Dictionary<string, TipoDeclarado>(StringComparer.Ordinal))
            .Where(t => t.Value.Origen == OrigenDelValor.Original).Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        var lectores = flujo.Pasos.Values.Where(p => registro.Para(p.Tipo) is { LeeLaOriginal: true })
            .OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

        if (flujo.PosteriorA is not { } original)
        {
            foreach (var nombre in deLaOriginal.Order(StringComparer.Ordinal))
            {
                errores.Add($"«{nombre}» viene de la original y el flujo no declara «posterior_a»: no hay de dónde leerlo.");
            }
            foreach (var lector in lectores)
            {
                errores.Add($"«{lector.Id}» lee la original y el flujo no declara «posterior_a»: no sabría de qué flujo es.");
            }
            return;
        }

        if (string.Equals(original, flujo.Clave, StringComparison.Ordinal))
        {
            errores.Add($"es «posterior_a» sí mismo: una operación posterior lee la saga de OTRO flujo.");
        }
        if (lectores.Count != 1)
        {
            errores.Add($"es «posterior_a: {original}» y {lectores.Count} pasos leen la original: tiene que ser uno, en la fase que abre.");
        }

        foreach (var lector in lectores)
        {
            if (!donde.TryGetValue(lector.Id, out var u) || u.Fase != 0 || u.Bloque is not null)
            {
                errores.Add($"«{lector.Id}» lee la original fuera de la fase que abre, o dentro de un bloque: se lee una vez, "
                    + "al abrir, antes de mover nada.");
            }
            foreach (var nombre in deLaOriginal.Except(lector.Escribe, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                errores.Add($"«{nombre}» viene de la original y «{lector.Id}» no lo escribe.");
            }
            foreach (var nombre in lector.Escribe.Where(n => !deLaOriginal.Contains(n)))
            {
                errores.Add($"«{lector.Id}» escribe «{nombre}», que no se declara {{ \"de\": \"original\" }} en «tipos»: no se sabría su tipo.");
            }
        }

        foreach (var otro in flujo.Pasos.Values.Where(p => !lectores.Contains(p)).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            foreach (var nombre in otro.Escribe.Where(deLaOriginal.Contains))
            {
                errores.Add($"«{otro.Id}» escribe «{nombre}», que viene de la original: lo pisaría.");
            }
        }
    }

    /// <summary>
    /// Lo que la saga deja para una operación posterior (<c>para_lo_posterior</c>, D-011-NB10 y NN-18 de 005): se guarda
    /// con ella y el contrato no lo publica.
    /// </summary>
    /// <remarks>
    /// <para><b>Sólo una saga genérica lo guarda</b>: con tipos, que es lo que deja leerlo después con el suyo.</para>
    ///
    /// <para><b>Un dato por su nombre</b> lo escribe un paso o entra, con un tipo que se guarda (lo revisa
    /// <see cref="RevisarModoGenerico"/>, como la salida). Nunca algo efímero: el contacto del aviso no acaba en la
    /// saga por esta puerta.</para>
    ///
    /// <para><b>Lo reservado, con <c>reservas:&lt;paso&gt;</c></b>, de un paso que reserva: cada reserva guarda sobre qué
    /// quedó al cerrarse (<see cref="ReservaDeFlujo.Cerrada"/>), y lo posterior lo recorre por ítem.</para>
    /// </remarks>
    private static void RevisarParaLoPosterior(FlujoDef flujo, ContratoDelFlujo contrato, List<string> errores)
    {
        if (flujo.LoQueDejaParaLoPosterior.Count == 0) return;
        if (contrato.Tipos is null)
        {
            errores.Add("deja algo para lo posterior y su saga no es genérica: no lo guarda con tipos, y nadie podría leerlo.");
            return;
        }

        var efimeros = (flujo.Efimera ?? new Dictionary<string, IReadOnlyList<string>>())
            .SelectMany(e => e.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var nombre in flujo.LoQueDejaParaLoPosterior)
        {
            if (nombre.StartsWith(Reservas, StringComparison.Ordinal))
            {
                var paso = nombre[Reservas.Length..];
                if (!flujo.Pasos.TryGetValue(paso, out var reservante) || reservante.Reserva is null)
                {
                    errores.Add($"deja para lo posterior «{nombre}», y «{paso}» no es un paso que reserve.");
                }
            }
            else if (efimeros.Contains(nombre))
            {
                errores.Add($"deja para lo posterior «{nombre}», que es efímero: no se guarda, y menos para después.");
            }
        }
    }

    /// <summary>
    /// Lo que cada flujo posterior (P6) le pide a su original, entre los flujos de un orquestador: que exista, que sea
    /// genérica y que DEJE para lo posterior (<c>para_lo_posterior</c>, D-011-NB10), con el mismo tipo, cada dato que lee,
    /// y lo que reservó con el paso que recorre (<c>reservas:&lt;paso&gt;</c>, NN-18 de 005).
    /// </summary>
    /// <remarks>
    /// Va aparte de <see cref="Validar"/> porque mira dos definiciones a la vez; lo llama el validador del catálogo al
    /// arrancar. Anulable o no da igual aquí: lo que la original escribió con un paso se guarda anulable, y el posterior
    /// decide si lo exige (<c>flow.original_sin_dato</c>) o lo acepta nulo.
    /// </remarks>
    public static IReadOnlyList<string> ValidarPosteriores(IReadOnlyCollection<FlujoRegistrado> flujos, IRegistroDePasos registro)
    {
        ArgumentNullException.ThrowIfNull(flujos);
        ArgumentNullException.ThrowIfNull(registro);

        var porClave = flujos.ToDictionary(f => f.Flujo.Clave, StringComparer.Ordinal);
        var errores = new List<string>();
        foreach (var posterior in flujos.Where(f => f.Flujo.PosteriorA is not null).OrderBy(f => f.Flujo.Clave, StringComparer.Ordinal))
        {
            var aqui = $"El flujo «{posterior.Flujo.Clave}»";
            var deCual = posterior.Flujo.PosteriorA!;
            if (!porClave.TryGetValue(deCual, out var original))
            {
                errores.Add($"{aqui}: es posterior a «{deCual}», y este orquestador no tiene ese flujo.");
                continue;
            }
            if (original.Contrato.Tipos is not { } tipos)
            {
                errores.Add($"{aqui}: es posterior a «{deCual}», que no es genérico: su saga no guarda su salida con tipos.");
                continue;
            }

            var deja = original.Flujo.LoQueDejaParaLoPosterior;
            foreach (var (nombre, declarado) in (posterior.Flujo.Tipos ?? new Dictionary<string, TipoDeclarado>(StringComparer.Ordinal))
                         .Where(t => t.Value.Origen == OrigenDelValor.Original).OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                if (declarado.ReservasDe is { } paso)
                {
                    if (!deja.Contains(Reservas + paso, StringComparer.Ordinal))
                    {
                        errores.Add($"{aqui}: lee en «{nombre}» lo que «{deCual}» reservó con «{paso}», y «{deCual}» no lo deja "
                            + $"para lo posterior (\"para_lo_posterior\": [\"{Reservas}{paso}\"]).");
                    }
                    continue;
                }
                if (!deja.Contains(nombre, StringComparer.Ordinal))
                {
                    errores.Add($"{aqui}: lee «{nombre}» de «{deCual}», que no lo deja para lo posterior (\"para_lo_posterior\"): "
                        + "lo que la original publica en su salida no basta.");
                    continue;
                }
                var alla = BindingDeFlujo.TipoDe(original.Flujo, tipos, registro, nombre);
                var aca = tipos.Resolver(declarado.Tipo!);
                if (alla is null || aca is null || !string.Equals(alla.Tipo.Nombre, aca.Tipo.Nombre, StringComparison.Ordinal))
                {
                    errores.Add($"{aqui}: lee «{nombre}» de «{deCual}» como «{declarado.Tipo}», y allí es «{alla?.Declarado ?? "sin tipo"}».");
                }
            }
        }
        return errores;
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
                // Las últimas opcionales se pueden no nombrar (NB-5): un paso que ahora dice algo más no deja sin
                // arrancar a las definiciones que ya lo usaban. Más nombres de los que produce, nunca.
                if (paso.Escribe.Count > tipo.Escrituras || paso.Escribe.Count < tipo.Escrituras - tipo.EscriturasOpcionales)
                {
                    errores.Add(tipo.EscriturasOpcionales == 0
                        ? $"el paso «{paso.Id}» declara {paso.Escribe.Count} escritura(s) y «{paso.Tipo}» produce {tipo.Escrituras}."
                        : $"el paso «{paso.Id}» declara {paso.Escribe.Count} escritura(s) y «{paso.Tipo}» produce {tipo.Escrituras}, "
                          + $"de las que las últimas {tipo.EscriturasOpcionales} se pueden no nombrar.");
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

            if (registro.Para(paso.Tipo) is { } avisa && avisa.UsaPlantilla != paso.Plantilla is not null)
            {
                errores.Add(avisa.UsaPlantilla
                    ? $"el paso «{paso.Id}» es de tipo «{paso.Tipo}», que avisa con «plantilla», y no la declara."
                    : $"el paso «{paso.Id}» declara una plantilla y «{paso.Tipo}» no avisa: quien la lea creerá que ese "
                      + "paso manda algo.");
            }

            if (registro.Para(paso.Tipo) is { } pide && pide.UsaProposito != paso.Proposito is not null)
            {
                errores.Add(pide.UsaProposito
                    ? $"el paso «{paso.Id}» es de tipo «{paso.Tipo}», que comprueba un permiso con «proposito», y no lo declara."
                    : $"el paso «{paso.Id}» declara un propósito y «{paso.Tipo}» no comprueba permisos: quien lo lea creerá "
                      + "que ese paso pide permiso.");
            }
        }

        // Dos pasos con la misma llave son, para la capacidad, UNA llamada: la segunda devuelve lo que hizo la
        // primera, que es la idempotencia funcionando. Una renta con la garantía y el alquiler bajo la misma
        // llave apartaría el equipo y cobraría una sola vez (NN-2 de 005).
        foreach (var repetida in flujo.Pasos.Values
                     .Where(p => (p.Llave ?? p.LlaveBase) is not null)
                     .GroupBy(p => (p.Llave ?? p.LlaveBase)!, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var pasos = string.Join(" y ", repetida.Select(p => $"«{p.Id}»").Order(StringComparer.Ordinal));
            errores.Add($"los pasos {pasos} declaran la misma llave «{repetida.Key}»: para la capacidad son una sola "
                + "llamada, y la segunda devolvería lo que hizo la primera.");
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
        else if (flujo.Tipos?.GetValueOrDefault(bloque.Fuente) is { ReservasDe: not null }
                 && escritos.Contains(bloque.Fuente) && !alEmpezar.Contains(bloque.Fuente, StringComparer.Ordinal))
        {
            // Lo que la original reservó, por ítem (NN-18 de 005): lo escribe flow.leer-original en esta fase, y cada
            // ítem trae lo reservado, sobre qué actuar ahora y cuánto.
            deOrigen.UnionWith(TipoDeclarado.CamposDeLoReservado.Keys);
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

    /// <summary>
    /// <c>omitir_si_cero</c> sólo en un paso que reserva, y sobre algo que el paso lee.
    /// </summary>
    /// <remarks>
    /// Que reserve es lo que hace que saltárselo no deje nada a medias: su cierre ve que no hay
    /// reserva y no corre. Que lo lea es lo que garantiza que el monto esté ahí en ese punto — el
    /// orden de las lecturas ya lo comprueba <see cref="RevisarOrden"/>.
    /// </remarks>
    private static void RevisarOmitirSiCero(FlujoDef flujo, List<string> errores)
    {
        foreach (var paso in flujo.Pasos.Values.Where(p => p.OmitirSiCero is not null))
        {
            if (paso.Reserva is null)
            {
                errores.Add($"«{paso.Id}» declara «omitir_si_cero» y no reserva nada: no hay cierre que se salte solo.");
            }
            if (!paso.Lee.Contains(paso.OmitirSiCero!, StringComparer.Ordinal))
            {
                errores.Add($"«{paso.Id}» se omite si «{paso.OmitirSiCero}» es cero y no lo lee.");
            }
        }
    }

    /// <summary>
    /// <c>omitir_si_falta</c> (NN-19): en un paso que no reserva, sobre algo que el paso lee y que puede faltar.
    /// </summary>
    /// <remarks>
    /// <para><b>Que no reserve</b>: el que reserva ya tiene su forma de saltarse, <c>omitir_si_cero</c>, y su cierre lo
    /// sigue solo. Éste es el paso que actúa sobre algo que la saga ya tiene —devolver el cobro, anular la garantía—, y
    /// cuando no lo tiene no hay nada que hacer.</para>
    ///
    /// <para><b>Que pueda faltar</b>: en una saga genérica, el tipo de ese dato es anulable. Uno obligatorio nunca falta,
    /// y omitir sobre él es una regla que nadie cumple.</para>
    /// </remarks>
    private static void RevisarOmitirSiFalta(FlujoDef flujo, IRegistroDePasos registro, ContratoDelFlujo contrato, List<string> errores)
    {
        foreach (var paso in flujo.Pasos.Values.Where(p => p.OmitirSiFalta is not null).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var dato = paso.OmitirSiFalta!;
            if (paso.Reserva is not null)
            {
                errores.Add($"«{paso.Id}» declara «omitir_si_falta» y reserva: lo que reserva se salta con «omitir_si_cero», y su cierre lo sigue solo.");
            }
            if (!paso.Lee.Contains(dato, StringComparer.Ordinal))
            {
                errores.Add($"«{paso.Id}» se omite si falta «{dato}» y no lo lee.");
                continue;
            }
            if (contrato.Tipos is { } tipos && !dato.Contains('.', StringComparison.Ordinal)
                && BindingDeFlujo.TipoDe(flujo, tipos, registro, dato) is { Anulable: false } tipo)
            {
                errores.Add($"«{paso.Id}» se omite si falta «{dato}», y «{dato}» es «{tipo.Declarado}»: no es anulable, así que nunca falta.");
            }
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
        errores.AddRange(FlowValidator.ValidarPosteriores(options.Flujos, _registro));
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
