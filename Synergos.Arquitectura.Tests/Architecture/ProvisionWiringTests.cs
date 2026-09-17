using System.Diagnostics;
using System.Text.RegularExpressions;
using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El estado que hay que sembrar en un servidor nuevo (<c>tools/provisionar.sh</c>), cruzado
/// contra lo que el CMS espera encontrar publicado.
/// </summary>
/// <remarks>
/// <para><b>Qué vigila, y por qué nada de esto falla al arrancar.</b> Las definiciones de proceso
/// y los recursos de <c>Api.Booking</c> son un paso de DESPLIEGUE: sin ellos los servicios
/// levantan sanos, contestan <c>/health</c> y pasan la prueba de humo — y la primera persona que
/// intenta avanzar un pedido se lleva un <c>definition_not_found</c>. Los cinco pasos vivían como
/// prosa en los comentarios de <c>.env.example</c>, marcados por el propio repo como «OJO, paso de
/// DESPLIEGUE que no es código», y no aparecían en el documento que dice qué hace el arquitecto a
/// mano (#114).</para>
///
/// <para><b>Los dos defectos que encontró correrlo contra las capacidades vivas</b>, y que ningún
/// test suelto habría visto porque los dos contestan <b>200</b>:</para>
///
/// <list type="number">
///   <item><b>El monto se publicaba en CERO.</b> El manifiesto se leía con
///   <c>IFS=$'\t'</c>, y bash cuenta el tabulador como <i>IFS whitespace</i>: una racha de
///   tabuladores es UN separador. Una entrada <c>precio</c> no lleva <c>capacity</c> ni
///   <c>timeZoneId</c>, así que sus campos se corrían dos puestos y el monto llegaba vacío —
///   publicado como <c>0</c> por un <c>${monto:-0}</c>. Cada oferta de viaje, gratis. Hoy el
///   separador es <c>0x1F</c> y un monto que no sea numérico se RECHAZA en vez de caer a cero.</item>
///
///   <item><b>Y una vez arreglado eso, el precio seguía en cero</b>: la llave de idempotencia
///   salía sólo del sujeto, y <c>SetPrice</c> mira el libro <b>antes</b> que nada y devuelve el
///   precio anterior. O sea que cambiar la tarifa en el manifiesto y volver a correr el script no
///   hacía nada, contestando 200 y diciendo «+ puesto precio». Hoy la llave lleva la HUELLA del
///   monto — <c>feedback_seeded_content_needs_fingerprint</c> aplicado a una tarifa.</item>
/// </list>
///
/// <para><b>El primero se prueba EJECUTANDO el script</b>, no leyéndolo: <c>--autoprueba</c> corre
/// el lector de verdad sobre un manifiesto de mentira y comprueba dónde caen los campos. No habla
/// con nadie, así que corre en CI sin levantar una capacidad.</para>
/// </remarks>
public sealed class ProvisionWiringTests
{
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

    private static string Guion()
        => string.Join('\n', File.ReadAllLines(Path.Combine(RepoRoot(), "tools", "provisionar.sh"))
            .Where(l => !l.TrimStart().StartsWith('#')));

    /// <summary>
    /// El lector del manifiesto alinea los campos — comprobado corriéndolo.
    /// </summary>
    [Fact]
    public void El_manifiesto_se_lee_con_los_campos_alineados()
    {
        var psi = new ProcessStartInfo("bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(Path.Combine("tools", "provisionar.sh"));
        psi.ArgumentList.Add("--autoprueba");

        using var p = Process.Start(psi)!;
        var salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.True(p.ExitCode == 0,
            "tools/provisionar.sh --autoprueba falló. Es el lector del manifiesto de despliegue: "
            + "si los campos se corren, una oferta se publica a CERO y la capacidad contesta 201."
            + Environment.NewLine + salida);
    }

    /// <summary>
    /// La llave de idempotencia de un precio lleva el monto dentro.
    /// </summary>
    /// <remarks>
    /// No se puede probar ejecutando sin una capacidad viva, así que se mira el cableado. La
    /// mutación que reproduce el defecto es quitar <c>:$monto</c> de la llave — que compila,
    /// contesta 200 y congela la tarifa para siempre.
    /// </remarks>
    [Fact]
    public void La_llave_de_un_precio_lleva_la_huella_del_monto()
    {
        var guion = Guion();
        var m = Regex.Match(guion, @"/v1/prices""[^\n]*""(provisionar:precio:[^""]*)""");

        Assert.True(m.Success,
            "No se encontró la llave de idempotencia con la que provisionar.sh publica un precio. "
            + "Si se reescribió esa línea, mové este gate con ella.");

        Assert.True(m.Groups[1].Value.Contains("$monto", StringComparison.Ordinal),
            $"La llave del precio es «{m.Groups[1].Value}» y no lleva el monto. `SetPrice` consulta "
            + "el libro de idempotencia ANTES de aplicar nada y devuelve el precio anterior, así "
            + "que con una llave derivada sólo del sujeto la tarifa queda CONGELADA en la primera "
            + "que se publicó: cambiarla en el manifiesto contesta 200 y no hace nada. Verificado "
            + "en vivo contra Api.Pricing (#114).");
    }

    /// <summary>
    /// Un recurso se BUSCA por sujeto antes de crearse.
    /// </summary>
    /// <remarks>
    /// Verificado en vivo contra <c>Api.Booking</c>: dos POST del mismo sujeto con llaves de
    /// idempotencia distintas dan <b>dos</b> recursos, los dos con 201, y el cupo del médico queda
    /// partido en dos sin que nada falle.
    /// </remarks>
    [Fact]
    public void Un_recurso_se_busca_por_sujeto_antes_de_crearse()
    {
        var guion = Guion();

        var buscar = guion.IndexOf("/v1/resources?subjectKind=", StringComparison.Ordinal);
        var crear = guion.IndexOf("POST \"$BOOKING_URL/v1/resources\"", StringComparison.Ordinal);

        Assert.True(buscar >= 0 && crear >= 0,
            "provisionar.sh ya no busca o ya no crea recursos de Api.Booking: mové este gate.");

        Assert.True(buscar < crear,
            "provisionar.sh crea el recurso sin buscarlo antes por sujeto. `RegisterResource` NO "
            + "reusa el id por sujeto: dos POST con llaves de idempotencia distintas dan DOS "
            + "recursos para el mismo médico —comprobado en vivo, los dos con 201— y el cupo queda "
            + "partido en dos sin que nada falle.");
    }

    /// <summary>
    /// Qué pipelines publica el script, en orden, tal como los escribe.
    /// </summary>
    private static Dictionary<string, string[]> PipelinesDelGuion()
        => Regex.Matches(Guion(), @"pipeline_json ""\$TRACKING_PREFIX\.(\w+)""\s+([^)""]+)\)")
            .ToDictionary(
                m => m.Groups[1].Value,
                m => m.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);

    /// <summary>
    /// Los cuatro pipelines que se publican son los cuatro que el CMS tiene en C#, etapa por
    /// etapa y en orden.
    /// </summary>
    /// <remarks>
    /// <para><b>Es la mitad que no se ve.</b> Con <c>Synergos:Tracking:Mode=Api</c> el CMS valida
    /// cada avance contra la definición publicada. Añadir «en aduana» al <c>ShopPipeline</c> de C#
    /// y no tocar el script deja un pedido que no puede avanzar — y no falla al desplegar: falla
    /// el día que a alguien le toca esa etapa, con un <c>transition_not_allowed</c> que no dice
    /// que el problema es el aprovisionamiento.</para>
    ///
    /// <para><b>Se cruza contra los pipelines por REFLEXIÓN y no contra una lista escrita acá</b>:
    /// una copia de las etapas dentro del gate sería la tercera, y las tres se desviarían por
    /// separado — que es el problema que la HU #46 vino a quitar cuando estaban copiadas cuatro
    /// veces en C#.</para>
    /// </remarks>
    [Theory]
    [InlineData("shop")]
    [InlineData("travel")]
    [InlineData("events")]
    [InlineData("academy")]
    public void Los_pipelines_publicados_son_los_que_el_CMS_recorre(string dominio)
    {
        var enCodigo = dominio switch
        {
            "shop" => StubOrderTrackingService.ShopPipeline,
            "travel" => TravelCartService.TravelPipeline,
            "events" => StubEventTicketingService.EventPipeline,
            "academy" => StubEnrollmentService.AcademyPipeline,
            _ => throw new ArgumentOutOfRangeException(nameof(dominio)),
        };

        var guion = PipelinesDelGuion();

        Assert.True(guion.ContainsKey(dominio),
            $"tools/provisionar.sh no publica la definición de seguimiento de «{dominio}». "
            + $"Se encontraron: {string.Join(", ", guion.Keys)}. Sin ella, con "
            + "Synergos:Tracking:Mode=Api ningún pedido de ese dominio avanza.");

        var esperadas = enCodigo.Select(e => e.Stage).ToArray();

        Assert.True(esperadas.SequenceEqual(guion[dominio], StringComparer.Ordinal),
            $"El pipeline «{dominio}» que se publica no es el que el CMS recorre." + Environment.NewLine
            + $"  en C#:              {string.Join(" → ", esperadas)}" + Environment.NewLine
            + $"  en provisionar.sh:  {string.Join(" → ", guion[dominio])}" + Environment.NewLine
            + "Con Tracking:Mode=Api el avance se valida contra la definición publicada: una etapa "
            + "que está en C# y no en la definición deja el pedido clavado ahí, y no falla al "
            + "desplegar — falla el día que a alguien le toca esa etapa.");
    }
}
