using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Synergos.Bff.Core;

/// <summary>
/// Quién está deshaciendo una saga ahora mismo, y hasta cuándo.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta (#34).</b> El barrido toma lo que está pendiente y lo ejecuta, y
/// nada marcaba que alguien ya lo estuviera ejecutando. Dos barridos sobre el mismo almacén ven la
/// misma saga pendiente en la misma vuelta y los dos la compensan — y <b>una compensación no es
/// idempotente por naturaleza</b>: «devolver 2 unidades» ejecutado dos veces devuelve 4, que es
/// justo por lo que el ajuste relativo existe (#30). Un reembolso doble es lo mismo con plata.</para>
///
/// <para><b>Por qué en el almacén y no un <c>lock</c>.</b> Un <c>lock</c> es de proceso, y el caso
/// son varios procesos —dos réplicas del mismo orquestador— contra el mismo
/// <c>Storage:Root</c>. Es la diferencia con los avisos (HU #29): allá reintentan varios procesos
/// contra <b>una</b> capacidad, así que a <c>Api.Notifications</c> le alcanza una marca en
/// memoria (<c>retry_in_flight</c>); acá cada proceso tiene <b>su propio</b> almacén, y la marca
/// tiene que estar donde los dos la ven.</para>
///
/// <para><b>Y tiene que VENCER.</b> Un arriendo sin vencimiento convierte un proceso muerto a
/// media compensación en una compensación que no ejecuta nadie nunca — cambia «se hace dos veces»
/// por «no se hace», que es peor: la primera se nota y la segunda no.</para>
/// </remarks>
public interface ISagaLease
{
    /// <summary>
    /// Toma el arriendo de una saga, o <c>null</c> si lo tiene otro y sigue vigente.
    /// </summary>
    /// <remarks>
    /// <b>No espera.</b> Quien no lo consigue no se queda parado: vuelve en la siguiente vuelta
    /// del barrido, que es el mismo trato que <c>retry_in_flight</c> — «el otro intento está en
    /// curso», no «esto no se puede hacer».
    /// </remarks>
    IDisposable? TryAcquire(string sagaId);
}

/// <summary>
/// El arriendo por defecto: un fichero por saga, junto al almacén.
/// </summary>
/// <remarks>
/// <para><b>Va al disco y no por <c>JsonCollectionStore</c>, y eso no es una comodidad.</b> Ese
/// almacén cachea la colección entera en memoria mientras el proceso vive —es lo que escondió el
/// defecto #82 durante meses— así que un arriendo escrito ahí <b>no lo vería la otra réplica</b>:
/// cada proceso leería su propia copia y los dos se creerían dueños. Un arriendo que sólo ve
/// quien lo tomó no es un arriendo.</para>
///
/// <para><b>La toma es un <c>rename</c>, que es lo único atómico que hay acá.</b> Se escribe el
/// contenido en un temporal y se mueve al sitio SIN sobrescribir: el que gana la carrera crea el
/// fichero y el que pierde recibe un <c>IOException</c>. Crear el fichero vacío y escribirlo
/// después dejaría una ventana en la que otro lo lee a medias.</para>
/// </remarks>
public sealed class FileSystemSagaLease : ISagaLease
{
    /// <summary>Lo mínimo que puede durar un arriendo, aunque la configuración diga menos.</summary>
    /// <remarks>
    /// Un arriendo más corto que una vuelta de compensación se lo roba el de al lado <i>mientras
    /// la primera todavía está hablando con las capacidades</i> — o sea, exactamente el defecto
    /// que esto viene a cerrar, con un fichero de por medio para disimularlo.
    /// </remarks>
    public const int MinimoSegundos = 30;

    private readonly string _dir;
    private readonly IOptionsMonitor<SweepOptions> _opciones;
    private readonly TimeProvider _reloj;

    public FileSystemSagaLease(
        IOptions<SagaStorageOptions> almacen, IOptionsMonitor<SweepOptions> opciones, TimeProvider reloj)
    {
        // Junto a las sagas y no dentro del JSON: ver arriba. Que compartan raíz es a propósito —
        // un despliegue que monte el volumen del almacén se lleva los arriendos con él.
        _dir = Path.Combine(almacen.Value.Root, "leases");
        _opciones = opciones;
        _reloj = reloj;
    }

    private TimeSpan Duracion => TimeSpan.FromSeconds(
        Math.Max(MinimoSegundos, _opciones.CurrentValue.CompensationLeaseSeconds));

    public IDisposable? TryAcquire(string sagaId)
    {
        Directory.CreateDirectory(_dir);

        var ahora = _reloj.GetUtcNow();
        var ruta = Ruta(sagaId);
        var duenio = Guid.NewGuid().ToString("n");
        var contenido = $"{duenio}|{(ahora + Duracion).UtcTicks}|{sagaId}";

        if (Poner(ruta, contenido)) return new Arriendo(ruta, duenio);

        // Lo tiene otro. Si sigue vigente, no hay nada que discutir.
        if (!Vencido(ruta, ahora)) return null;

        // Está vencido: quien lo tomó se murió a media compensación. Robarlo es lo correcto —lo
        // que NO se puede es que dos lo roben a la vez, así que el robo es también un `rename`:
        // sólo uno consigue mover el fichero vencido fuera del sitio.
        if (!Robar(ruta)) return null;

        return Poner(ruta, contenido) ? new Arriendo(ruta, duenio) : null;
    }

    /// <summary>
    /// El fichero de una saga. <b>Por hash, y no por su identificador.</b>
    /// </summary>
    /// <remarks>
    /// El identificador de una saga ES la llave de idempotencia que manda el llamador: hasta 128
    /// caracteres cualesquiera, barras y puntos incluidos. Metido en una ruta sería una travesía de
    /// directorios servida en bandeja, y además cambiaría de significado entre sistemas de
    /// ficheros. El identificador de verdad va DENTRO del fichero, para que se pueda mirar.
    /// </remarks>
    private string Ruta(string sagaId)
        => Path.Combine(_dir, Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sagaId))).ToLowerInvariant() + ".lease");

    /// <summary>Escribe el arriendo completo o no escribe nada. <c>false</c> si ya había uno.</summary>
    private bool Poner(string ruta, string contenido)
    {
        var tmp = Path.Combine(_dir, Guid.NewGuid().ToString("n") + ".tmp");
        try
        {
            File.WriteAllText(tmp, contenido);
            File.Move(tmp, ruta);   // sin overwrite: falla si ya existe, que es lo que se quiere
            return true;
        }
        catch (IOException)
        {
            Borrar(tmp);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            Borrar(tmp);
            return false;
        }
    }

    /// <summary>
    /// Si el arriendo que hay en el sitio ya no vale.
    /// </summary>
    /// <remarks>
    /// <b>Un fichero que no se puede leer o no se entiende cuenta como VENCIDO</b>, y es
    /// deliberado: al revés, un arriendo corrupto —de una versión anterior, de un disco lleno—
    /// dejaría esa saga sin compensar para siempre y en silencio. Entre repetir una compensación
    /// una vez y no hacerla nunca, la que se nota es la primera.
    /// </remarks>
    private static bool Vencido(string ruta, DateTimeOffset ahora)
    {
        try
        {
            var partes = File.ReadAllText(ruta).Split('|');
            if (partes.Length < 2 || !long.TryParse(partes[1], out var ticks)) return true;
            return new DateTimeOffset(ticks, TimeSpan.Zero) <= ahora;
        }
        catch (FileNotFoundException) { return true; }   // lo soltaron entre medias
        catch (DirectoryNotFoundException) { return true; }
        catch (IOException) { return true; }
    }

    /// <summary>Saca de en medio un arriendo vencido. <c>false</c> si otro llegó primero.</summary>
    private static bool Robar(string ruta)
    {
        var muerto = ruta + "." + Guid.NewGuid().ToString("n") + ".vencido";
        try
        {
            File.Move(ruta, muerto);
            Borrar(muerto);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void Borrar(string ruta)
    {
        try { File.Delete(ruta); } catch (IOException) { /* best-effort */ }
    }

    /// <summary>
    /// Soltar el arriendo. <b>Sólo si todavía es nuestro.</b>
    /// </summary>
    /// <remarks>
    /// Comprobar el dueño antes de borrar es lo que impide el caso feo: una compensación que tardó
    /// más que su arriendo, otro se lo robó, y al terminar la primera borra el arriendo <i>del
    /// segundo</i> — dejando a un tercero entrar sobre una compensación en curso.
    /// </remarks>
    private sealed class Arriendo : IDisposable
    {
        private readonly string _ruta;
        private readonly string _duenio;
        private bool _soltado;

        public Arriendo(string ruta, string duenio)
        {
            _ruta = ruta;
            _duenio = duenio;
        }

        public void Dispose()
        {
            if (_soltado) return;
            _soltado = true;
            try
            {
                if (File.ReadAllText(_ruta).StartsWith(_duenio, StringComparison.Ordinal)) File.Delete(_ruta);
            }
            catch (IOException) { /* ya no está, o no se puede: vence solo */ }
            catch (UnauthorizedAccessException) { /* idem */ }
        }
    }
}
