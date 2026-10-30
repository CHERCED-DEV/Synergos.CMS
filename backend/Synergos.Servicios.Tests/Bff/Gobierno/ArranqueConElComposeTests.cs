using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff.Gobierno;

/// <summary>
/// El orquestador de Gobierno arranca con EXACTAMENTE el entorno que le da el compose de producción, y no arranca sin su
/// catálogo de procesos (009-gobierno-tramite, NP-1).
/// </summary>
/// <remarks>
/// <para><b>Por qué se lanza el ensamblado y no el host de pruebas.</b> <c>RadicacionReal</c> pone la configuración a mano,
/// así que estaba en verde con un compose que no declaraba el catálogo. Lo vio el director al ensayar la integración en
/// master: <c>bff-gobierno</c> caía al arrancar y <c>deploy-remoto.sh</c> esperaba seis minutos a que estuviera sano para
/// ponerse rojo.</para>
///
/// <para>Esto lee el bloque de <c>bff-gobierno</c> de <c>compose.prod.yml</c>, resuelve sus <c>${…}</c> como compose con el
/// <c>.env</c> que deja el bootstrap, traduce cada ruta del contenedor por su montaje —el volumen de datos a un directorio
/// temporal, el catálogo al fichero que <c>deploy.yml</c> deja en <c>/opt/synergos</c>— y lanza el dll compilado con ese
/// entorno, lo que pone la imagen y nada más que lo que el sistema necesita para lanzar un proceso. Sin Docker: lo que se
/// prueba es la costura entre el compose, el despliegue y el arranque, no la imagen.</para>
/// </remarks>
public sealed class ArranqueConElComposeTests
{
    private const string Servicio = "bff-gobierno";
    private const string Catalogo = "Gobierno__Procesos__Archivo";

    /// <summary>Holgado a propósito: arrancar tarda segundos, y un plazo justo daría rojos con la máquina cargada.</summary>
    private static readonly TimeSpan Plazo = TimeSpan.FromSeconds(90);

    /// <summary>Lo que <c>.env</c> le da a este bloque: la única obligatoria (<c>:?</c>) que lee.</summary>
    private static readonly Dictionary<string, string> ElEnv = new(StringComparer.Ordinal)
    {
        ["SYNERGOS_API_KEY"] = "llave-de-la-prueba",
    };

    /// <summary>Lo que el sistema operativo necesita para lanzar un proceso; todo lo demás del entorno de la suite se quita.</summary>
    private static readonly HashSet<string> DelSistema = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "PATHEXT", "HOME", "TMPDIR", "TEMP", "TMP", "DOTNET_ROOT", "DOTNET_ROOT_X64", "SystemRoot", "SystemDrive",
        "windir", "ComSpec", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "LOCALAPPDATA", "APPDATA", "ProgramData",
        "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS",
    };

    [Fact]
    public async Task Arranca_con_exactamente_el_entorno_del_compose_y_contesta_su_health()
    {
        using var datos = new DirectorioTemporal();
        using var orquestador = Lanzar(EntornoDelCompose(datos.Ruta, traducirElCatalogo: true));

        var sano = await SaludHasta(orquestador);

        Assert.True(sano,
            $"{Servicio} no contestó /health 200 con el entorno de compose.prod.yml ({orquestador.Estado()}). Es lo que "
            + "deploy-remoto.sh espera de cada contenedor durante seis minutos antes de ponerse rojo."
            + Environment.NewLine + orquestador.Salida());
    }

    [Theory]
    [InlineData("sin la variable", "falta")]
    [InlineData("sin el montaje", "no existe")]
    public async Task Sin_su_catalogo_no_arranca_y_lo_dice(string caso, string loQueDice)
    {
        using var datos = new DirectorioTemporal();
        var entorno = EntornoDelCompose(datos.Ruta, traducirElCatalogo: caso != "sin el montaje");
        if (caso == "sin la variable")
        {
            Assert.True(entorno.Remove(Catalogo), $"El compose no le declara {Catalogo} a {Servicio}.");
        }

        using var orquestador = Lanzar(entorno);
        var salio = await SalidaHasta(orquestador);

        Assert.True(salio, $"{Servicio} arrancó {caso} del catálogo de procesos: el flujo se validaría contra nada."
            + Environment.NewLine + orquestador.Salida());
        Assert.NotEqual(0, orquestador.Proceso.ExitCode);
        Assert.Contains("gobierno:Procesos:Archivo", orquestador.Salida(), StringComparison.Ordinal);
        Assert.Contains(loQueDice, orquestador.Salida(), StringComparison.Ordinal);
    }

    // ── El compose, leído como lo genera compose-gen ─────────────────────────

    private sealed record Montaje(string Origen, string Destino);

    /// <summary>
    /// El entorno del bloque de <see cref="Servicio"/>, resuelto y con las rutas del contenedor traducidas por sus montajes.
    /// </summary>
    /// <param name="datos">Dónde cae el volumen con nombre (<c>/app/data</c>).</param>
    /// <param name="traducirElCatalogo">Sin esto, el catálogo queda en su ruta del contenedor, que en esta máquina no
    /// existe: es lo que vería un contenedor con la variable y sin el montaje.</param>
    private static Dictionary<string, string> EntornoDelCompose(string datos, bool traducirElCatalogo)
    {
        var (entorno, montajes) = Bloque(Servicio);

        var traducciones = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in montajes)
        {
            if (m.Origen.StartsWith("./", StringComparison.Ordinal))
            {
                if (traducirElCatalogo) traducciones[m.Destino] = DelServidor(m.Origen[2..]);
            }
            else if (!m.Origen.StartsWith('/'))
            {
                traducciones[m.Destino] = datos;
            }
        }
        Assert.Contains("/app/data", traducciones.Keys);

        return entorno.ToDictionary(
            e => e.Key,
            e => Traducir(Resolver(e.Value), traducciones),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// El entorno y los montajes de un servicio: las líneas de <c>environment:</c> y los elementos de <c>volumes:</c>,
    /// cortos (<c>- nombre:/ruta</c>) y largos (<c>- type: bind</c> con sus claves debajo).
    /// </summary>
    private static (Dictionary<string, string> Entorno, List<Montaje> Montajes) Bloque(string servicio)
    {
        var lineas = File.ReadAllLines(Path.Combine(Proyectos.Raiz(), "compose.prod.yml"));
        var inicio = Array.IndexOf(lineas, $"  {servicio}:");
        Assert.True(inicio >= 0, $"compose.prod.yml no tiene el servicio {servicio}.");

        var entorno = new Dictionary<string, string>(StringComparer.Ordinal);
        var montajes = new List<Montaje>();
        string? seccion = null;
        Dictionary<string, string>? largo = null;

        void CerrarLargo()
        {
            if (largo is null) return;
            montajes.Add(new(largo.GetValueOrDefault("source", ""), largo.GetValueOrDefault("target", "")));
            largo = null;
        }

        foreach (var linea in lineas.Skip(inicio + 1))
        {
            var texto = linea.Trim();
            if (texto.Length == 0 || texto.StartsWith('#')) continue;

            var sangria = linea.Length - linea.TrimStart().Length;
            if (sangria <= 2) break;
            if (sangria == 4)
            {
                CerrarLargo();
                seccion = texto.Split(':')[0];
                continue;
            }

            if (seccion == "environment" && sangria == 6)
            {
                var corte = texto.IndexOf(':', StringComparison.Ordinal);
                entorno[texto[..corte]] = texto[(corte + 1)..].Trim();
            }
            else if (seccion == "volumes" && texto.StartsWith("- ", StringComparison.Ordinal))
            {
                CerrarLargo();
                var elemento = texto[2..].Trim();
                if (elemento.StartsWith("type:", StringComparison.Ordinal))
                {
                    largo = new(StringComparer.Ordinal);
                }
                else
                {
                    var partes = elemento.Split(':');
                    montajes.Add(new(partes[0], partes[1]));
                }
            }
            else if (seccion == "volumes" && largo is not null && texto.IndexOf(':', StringComparison.Ordinal) is > 0 and var clave)
            {
                largo[texto[..clave]] = texto[(clave + 1)..].Trim();
            }
        }

        CerrarLargo();
        return (entorno, montajes);
    }

    /// <summary>Un valor como lo resuelve compose: sin comillas, y cada <c>${X}</c>, <c>${X:-def}</c> o <c>${X:?msg}</c>.</summary>
    private static string Resolver(string valor)
    {
        var crudo = valor.Length >= 2 && valor[0] == '"' && valor[^1] == '"' ? valor[1..^1] : valor;
        return Regex.Replace(crudo, @"\$\{(?<n>[A-Za-z0-9_]+)(?:(?<op>:[-?])(?<resto>[^}]*))?\}", m =>
        {
            if (ElEnv.TryGetValue(m.Groups["n"].Value, out var puesto) && puesto.Length > 0) return puesto;
            return m.Groups["op"].Value switch
            {
                ":-" => m.Groups["resto"].Value,
                ":?" => throw new Xunit.Sdk.XunitException(
                    $"El compose exige {m.Groups["n"].Value} a {Servicio} y el .env de la prueba no la trae."),
                _ => "",
            };
        });
    }

    private static string Traducir(string valor, IReadOnlyDictionary<string, string> traducciones)
    {
        foreach (var (destino, origen) in traducciones.OrderByDescending(t => t.Key.Length))
        {
            if (valor == destino) return origen;
            if (valor.StartsWith(destino + "/", StringComparison.Ordinal)) return Path.Combine(origen, valor[(destino.Length + 1)..]);
        }
        return valor;
    }

    /// <summary>
    /// De dónde sale en el repo el fichero que <c>deploy.yml</c> deja en <c>/opt/synergos</c> con ese nombre: la lista del
    /// <c>scp</c>, que copia aplanando las carpetas.
    /// </summary>
    private static string DelServidor(string nombre)
    {
        var deploy = File.ReadAllText(Path.Combine(Proyectos.Raiz(), ".github", "workflows", "deploy.yml"));
        var lista = Regex.Match(deploy, @"scp -i ~/\.ssh/id_deploy[^\n]*\n(?<lista>.*?)""\$USUARIO@\$HOST:/opt/synergos/""",
            RegexOptions.Singleline);
        Assert.True(lista.Success, "No se encontró la lista del scp de .github/workflows/deploy.yml.");

        var copiado = lista.Groups["lista"].Value.Replace('\\', ' ')
            .Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(f => Path.GetFileName(f) == nombre);
        Assert.True(copiado is not null,
            $"El compose monta ./{nombre} de /opt/synergos y deploy.yml no lo copia: en el servidor no existiría.");
        return Path.Combine(Proyectos.Raiz(), copiado!);
    }

    // ── El proceso ───────────────────────────────────────────────────────────

    /// <summary>El dll que compila la solución, de la misma configuración que esta suite.</summary>
    private static string Dll()
    {
        var partes = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var bin = Array.LastIndexOf(partes, "bin");
        Assert.True(bin >= 0 && bin + 2 < partes.Length, $"No se reconoce la salida de esta suite: {AppContext.BaseDirectory}");

        var dll = Proyectos.Dir("Synergos.Bff.Gobierno", "bin", partes[bin + 1], partes[bin + 2], "Synergos.Bff.Gobierno.dll");
        Assert.True(File.Exists(dll), $"No existe {dll}: esta suite referencia el proyecto, así que compilarla lo compila.");
        return dll;
    }

    private static Orquestador Lanzar(IReadOnlyDictionary<string, string> entorno)
    {
        var dll = Dll();
        var puerto = PuertoLibre();
        var psi = new ProcessStartInfo("dotnet")
        {
            // El WORKDIR de la imagen es la carpeta publicada: ahí busca el host su appsettings.json.
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add(dll);

        var sistema = psi.Environment.Where(e => DelSistema.Contains(e.Key)).ToList();
        psi.Environment.Clear();
        foreach (var (clave, valor) in sistema) psi.Environment[clave] = valor;

        // Lo que pone Dockerfile.service, salvo la dirección: en el contenedor es el 8080 de su red; acá, un puerto libre.
        psi.Environment["DOTNET_gcServer"] = "0";
        psi.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{puerto}";
        foreach (var (clave, valor) in entorno) psi.Environment[clave] = valor;

        var proceso = Process.Start(psi)!;
        var orquestador = new Orquestador(proceso, puerto);
        proceso.OutputDataReceived += (_, e) => orquestador.Anotar(e.Data);
        proceso.ErrorDataReceived += (_, e) => orquestador.Anotar(e.Data);
        proceso.BeginOutputReadLine();
        proceso.BeginErrorReadLine();
        return orquestador;
    }

    private static async Task<bool> SaludHasta(Orquestador orquestador)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var reloj = Stopwatch.StartNew();
        while (reloj.Elapsed < Plazo && !orquestador.Proceso.HasExited)
        {
            try
            {
                using var respuesta = await http.GetAsync(new Uri($"http://127.0.0.1:{orquestador.Puerto}/health"));
                if (respuesta.StatusCode == HttpStatusCode.OK) return true;
            }
            catch (HttpRequestException)
            {
                // Todavía no escucha.
            }
            catch (TaskCanceledException)
            {
                // Escucha y no contesta a tiempo: se vuelve a preguntar.
            }
            await Task.Delay(250);
        }
        return false;
    }

    private static async Task<bool> SalidaHasta(Orquestador orquestador)
    {
        using var plazo = new CancellationTokenSource(Plazo);
        try
        {
            await orquestador.Proceso.WaitForExitAsync(plazo.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static int PuertoLibre()
    {
        using var oyente = new TcpListener(IPAddress.Loopback, 0);
        oyente.Start();
        return ((IPEndPoint)oyente.LocalEndpoint).Port;
    }

    private sealed class Orquestador(Process proceso, int puerto) : IDisposable
    {
        private readonly StringBuilder _salida = new();

        public Process Proceso { get; } = proceso;

        public int Puerto { get; } = puerto;

        public void Anotar(string? linea)
        {
            if (linea is null) return;
            lock (_salida) _salida.AppendLine(linea);
        }

        public string Salida()
        {
            lock (_salida) return _salida.ToString();
        }

        public string Estado() => Proceso.HasExited ? $"salió con {Proceso.ExitCode}" : "sigue corriendo";

        public void Dispose()
        {
            if (!Proceso.HasExited)
            {
                Proceso.Kill(entireProcessTree: true);
                Proceso.WaitForExit(TimeSpan.FromSeconds(15));
            }
            Proceso.Dispose();
        }
    }

    private sealed class DirectorioTemporal : IDisposable
    {
        public string Ruta { get; } = Directory.CreateTempSubdirectory("arranque-gobierno-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Ruta, recursive: true);
            }
            catch (IOException)
            {
                // Un temporal que el proceso recién parado todavía retiene: lo limpia el sistema.
            }
            catch (UnauthorizedAccessException)
            {
                // Ídem.
            }
        }
    }
}
