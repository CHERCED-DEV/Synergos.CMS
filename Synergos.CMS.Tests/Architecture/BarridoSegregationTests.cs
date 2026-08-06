using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Clients;
using Synergos.Bff.Tienda.Domain;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Dónde vive el barrido, como invariante ejecutable (HU #29).
/// </summary>
/// <remarks>
/// <para><b>La línea que esto defiende:</b> la capacidad sabe QUÉ está colgado y CÓMO se reintenta
/// un envío; el orquestador decide CUÁNDO se vuelve a intentar y CUÁNTAS veces antes de rendirse.
/// Un barrido periódico <i>es</i> la máquina de reintentar y rendirse, y esa máquina ya vive en
/// <c>Bff.Core</c> para las compensaciones.</para>
///
/// <para><b>Y por qué hace falta un gate y no basta con haberlo escrito bien.</b> Meter el lazo
/// dentro de <c>Api.Notifications</c> es la decisión <i>cómoda</i>: la capacidad ya tiene los
/// datos delante, así que quien venga después va a proponerlo con buenos argumentos. El resultado
/// serían dos techos, dos cadencias y dos ideas de cuándo rendirse — y el día que difieran, nadie
/// sabría cuál manda. Eso no rompe nada en el arranque: rompe el día que un aviso importante se
/// reintenta para siempre en un sitio y se abandona en el otro.</para>
///
/// <para>El gate mira el <b>código fuente</b> y no los ensamblados porque lo que se prohíbe es una
/// forma de escribirlo, no un tipo concreto: un <c>Timer</c>, un <c>while</c> con <c>Task.Delay</c>
/// y un <c>BackgroundService</c> son el mismo error con tres caras.</para>
/// </remarks>
public sealed class BarridoSegregationTests
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

    private static IReadOnlyList<(string Ruta, string Texto)> Fuentes(string proyecto)
        => Directory.EnumerateFiles(Path.Combine(RepoRoot(), proyecto), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(RepoRoot(), f), File.ReadAllText(f)))
            .ToList();

    [Fact]
    public void La_capacidad_de_avisos_NO_tiene_un_lazo_de_fondo()
    {
        // Las tres caras del mismo error. Se nombran las tres porque prohibir solo
        // `BackgroundService` invita a escribirlo con un `Timer` y sentir que se cumplió la regla.
        var prohibido = new[] { "BackgroundService", "IHostedService", "AddHostedService", "PeriodicTimer" };

        var culpables = Fuentes("Synergos.Api.Notifications")
            .SelectMany(f => prohibido.Where(p => f.Texto.Contains(p, StringComparison.Ordinal))
                                      .Select(p => $"{f.Ruta}: {p}"))
            .ToList();

        Assert.True(culpables.Count == 0,
            "El barrido NO va dentro de la capacidad: la máquina de reintentar y rendirse vive en "
            + "Bff.Core y duplicarla daría dos techos y dos cadencias. Encontrado en:\n  "
            + string.Join("\n  ", culpables));
    }

    [Fact]
    public void La_capacidad_de_avisos_NO_decide_cuantas_veces_se_insiste()
    {
        // El techo es la mitad de la decisión que este reparto le da al orquestador. Que la
        // capacidad no tenga lazo pero sí un «máximo de intentos» sería el mismo problema con un
        // disfraz más difícil de ver: dos números en dos sitios, y el día que difieran nadie sabe
        // cuál manda.
        //
        // `Attempts` sí es de la capacidad —es un HECHO de su registro, no una política—, así que
        // lo que se busca es el TECHO, no el contador.
        var sospechosos = new[] { "MaxAttempts", "MaxRetries", "RetryCeiling", "MaxIntentos" };

        var culpables = Fuentes("Synergos.Api.Notifications")
            .SelectMany(f => sospechosos.Where(p => f.Texto.Contains(p, StringComparison.Ordinal))
                                        .Select(p => $"{f.Ruta}: {p}"))
            .ToList();

        Assert.True(culpables.Count == 0,
            "Cuántas veces se insiste lo decide quien barre, no quien entrega. Encontrado en:\n  "
            + string.Join("\n  ", culpables));
    }

    [Fact]
    public void Todo_orquestador_levanta_el_barrido_de_avisos()
    {
        // Que exista no alcanza: un barrido que nadie registra es un fichero. Y va en el registro
        // COMPARTIDO —no en el Program.cs de un orquestador elegido a dedo— porque elegir uno
        // obligaría a nombrarlo en algún sitio («el de la tienda barre los avisos de salud») y el
        // día que ese host esté caído nadie barrería.
        //
        // ⚠️ Esto se comprobaba buscando el TEXTO `AddHostedService<DeliverySweeper>` en
        // SagaMachinery.cs, y no vigilaba nada: al mutarlo poniéndole `//` delante, el gate siguió
        // en VERDE — la línea comentada contiene el texto igual. Se cambió por lo único que no
        // admite esa lectura: montar el registro de verdad y preguntarle al contenedor.
        var builder = WebApplication.CreateBuilder();
        builder.AddSagaMachinery<PurchaseSaga, TiendaCompensationExecutor>(
            new SagaVocabulary("tienda", "la compra"), TiendaCapabilities.Cart);

        var registrado = builder.Services.Any(d =>
            d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(DeliverySweeper));

        Assert.True(registrado,
            "AddSagaMachinery no registró DeliverySweeper: los orquestadores arrancarían sanos, "
            + "pasarían su /health y nadie volvería por un aviso colgado.");
    }

    [Fact]
    public void El_barrido_habla_con_la_capacidad_por_HTTP_y_no_de_otra_forma()
    {
        // Es la regla de CLAUDE.md §11 aplicada al caso concreto: si el barrido pudiera llamar a
        // la capacidad en proceso, la capacidad sería una carpeta con ínfulas. La comprobación va
        // sobre el csproj porque una referencia de ensamblado es lo único que lo permitiría, y es
        // exactamente lo que alguien añadiría «para no tener que serializar».
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.Bff.Core", "Synergos.Bff.Core.csproj"));

        Assert.DoesNotContain("Synergos.Api.", csproj, StringComparison.Ordinal);
    }
}
