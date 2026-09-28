using Microsoft.AspNetCore.Mvc.Testing;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Levanta las capacidades DE VERDAD, en proceso, y se las sirve a un orquestador por el mismo
/// <see cref="IHttpClientFactory"/> que usa en producción (#162).
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> Había <b>seis</b> copias de un <c>CapacidadesFalsas</c>
/// —un <c>HttpMessageHandler</c> que contesta 200 a lo que sea— y ninguna aplicaba una sola regla
/// de la capacidad que doblaba. <b>Un doble no rechaza por una regla que no conoce</b>, así que la
/// cobertura que daban era sobre el FLUJO y nunca sobre el CONTRATO. Eso dejó vivo cinco HU el
/// defecto #168: desde la HU #14 <c>POST /v1/payments</c> rechazaba a quien no declarara
/// afirmación, ningún orquestador la mandaba, y las tres suites estaban en verde porque los seis
/// dobles decían que sí.</para>
///
/// <para><b>Por qué hosts reales y no un doble «más listo».</b> Un doble derivado del contrato
/// sería un SEGUNDO modelo de las reglas, escrito a mano, que se desincroniza en silencio — dos
/// verdades sobre las mismas reglas y gana la que nadie mira. Y un gate por propiedad (la salida
/// del #147) funciona por propiedad y no por regla: cada propiedad nueva sería un gate nuevo. Un
/// host real es fiel <b>por construcción</b>, porque ejecuta las reglas.</para>
///
/// <para><b>El marcador del ensamblado NO es <c>Program</c>.</b> Las veinte capacidades lo declaran
/// en el namespace GLOBAL, así que <c>WebApplicationFactory&lt;Program&gt;</c> es un CS0433 ambiguo
/// entre veinte ensamblados — medido, no supuesto. Se usa cualquier tipo público del ensamblado
/// (un <c>record</c> de sus <c>Contracts/</c>), que es lo único que la fábrica necesita para
/// localizar el entry point.</para>
///
/// <para><b>Lo que este arnés NO es.</b> No sustituye a verificar con procesos vivos (§10.6): acá
/// todo corre en un proceso, así que no ve lo que sólo se ve con dos réplicas sobre el mismo
/// volumen ni lo que se ve matando un servicio a media saga. Los dos defectos más caros del repo
/// los encontró un proceso vivo. Lo que esto cierra es el hueco de en medio: que un orquestador
/// hable con el CONTRATO de verdad sin levantar seis contenedores.</para>
///
/// <para><b>Y NO reemplaza a los seis dobles, que es lo que el #162 daba por hecho.</b> Al ir a
/// convertirlos se midió para qué existen: <b>38 inyecciones de fallo</b>
/// (<c>.Falla("POST /v1/items/i-3/holds", Conflict, "inventory.out_of_stock")</c>) repartidas en
/// los seis. Eso es justo lo que un host real <b>no</b> sabe hacer — no se le puede pedir que
/// conteste sin existencias a la tercera línea— y es el sujeto entero de los tests de
/// compensación. Convertirlos habría cambiado una cobertura por otra, no sumado.</para>
///
/// <para><b>El reparto, entonces, y es lo que el ticket no tenía:</b> <i>host real</i> contesta
/// «¿la llamada satisface el CONTRATO?», que es lo que ningún doble puede decir; <i>doble</i>
/// contesta «¿qué pasa cuando el tercer paso FALLA?», que es lo que ningún host real puede montar.
/// Ninguna de las dos respuestas del #162 —fundirlos, o un gate por propiedad— servía para las dos
/// preguntas, porque son dos preguntas.</para>
/// </remarks>
public sealed class ArnesDeCapacidades : IHttpClientFactory, IDisposable
{
    /// <summary>La llave compartida que el arnés configura en todas y manda en cada petición.</summary>
    private const string Llave = "llave-del-arnes";

    // El valor es una FUNCIÓN y no la fábrica: `WebApplicationFactory<T>` es genérica en su
    // marcador, así que no cabe en un diccionario sin envolverla — y envolverla con una jerarquía
    // propia era más código para lo mismo.
    private readonly Dictionary<string, Func<HttpClient>> _hosts = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _aDisponer = new();
    private readonly List<string> _raices = new();

    /// <summary>
    /// Levanta la capacidad <paramref name="capacidad"/>, cuyo ensamblado se localiza por
    /// <typeparamref name="TMarcador"/>. El nombre tiene que ser el que el orquestador le pide al
    /// <see cref="IHttpClientFactory"/> — que es el nombre de la capacidad.
    /// </summary>
    public ArnesDeCapacidades Levanta<TMarcador>(string capacidad)
        where TMarcador : class
    {
        var raiz = Path.Combine(Path.GetTempPath(), "arnes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(raiz);
        _raices.Add(raiz);

        var fabrica = new WebApplicationFactory<TMarcador>()
            .WithWebHostBuilder(b =>
            {
                // Cada capacidad con su propio almacén, como en el compose: compartirlo haría que
                // dos capacidades se vieran los ficheros y taparía justo lo que §0.B.11 vigila.
                b.UseSetting("Storage:Root", raiz);
                b.UseSetting("SharedKey", Llave);
            });

        // `CreateClient()` es lo que de verdad arranca el host; sin esto el fallo aparecería más
        // tarde y hablando de otra cosa.
        var sonda = fabrica.CreateClient();
        _aDisponer.Add(sonda);
        _aDisponer.Add(fabrica);

        _hosts[capacidad] = fabrica.CreateClient;
        return this;
    }

    /// <summary>
    /// El cliente de una capacidad levantada, con la llave compartida puesta.
    /// </summary>
    /// <remarks>
    /// <b>Una capacidad que nadie levantó RECHAZA en vez de devolver un cliente que da 404.</b> Un
    /// 404 se lee como «la capacidad contestó que no», así que el test seguiría corriendo contra
    /// nada y su verde no diría nada — el fallo silencioso que este repo ya pagó cuatro veces.
    /// </remarks>
    public HttpClient CreateClient(string name)
    {
        if (!_hosts.TryGetValue(name, out var host))
        {
            throw new InvalidOperationException(
                $"El orquestador pidió el cliente de «{name}» y este arnés no la levantó. "
                + $"Levantadas: {(_hosts.Count == 0 ? "ninguna" : string.Join(", ", _hosts.Keys.Order(StringComparer.Ordinal)))}. "
                + "Añadila con `.Levanta<Marcador>(\"" + name + "\")` — devolver un cliente que da "
                + "404 dejaría el test corriendo contra nada y su verde no diría nada.");
        }

        var cliente = host();
        cliente.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
        _aDisponer.Add(cliente);
        return cliente;
    }

    public void Dispose()
    {
        foreach (var d in _aDisponer) d.Dispose();
        foreach (var r in _raices)
        {
            try { if (Directory.Exists(r)) Directory.Delete(r, recursive: true); }
            catch (IOException) { /* el host puede tener un fichero aún abierto; es un temporal */ }
        }
    }

}
