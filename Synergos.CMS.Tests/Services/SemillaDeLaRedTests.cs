using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La semilla de la red sólo con DevSeed (012-blogs-social, I0, T001, P2 A) y los artículos del CMS en la app (T002).
/// </summary>
/// <remarks>
/// Antes la semilla se servía SIEMPRE: «un documento ausente cae al seed» valía en producción, y el inicio mostraba 8
/// publicaciones de 4 personas que no existen, con sus seguidores y sus reacciones.
/// </remarks>
public sealed class SemillaDeLaRedTests
{
    private static (StubContentStream Stream, StubSocialGraphService Grafo, StubReactionService Reacciones) Red(SemillaDeLaRed semilla)
    {
        var grafo = new StubSocialGraphService(null, null, semilla);
        var reacciones = new StubReactionService(null, null, semilla);
        return (new StubContentStream(grafo, reacciones, null, null, null, semilla), grafo, reacciones);
    }

    [Fact]
    public async Task Apagada_el_inicio_no_trae_a_nadie_de_la_demo()
    {
        var (stream, _, _) = Red(SemillaDeLaRed.Apagada);

        var pagina = await stream.GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou, PageSize: 100));

        Assert.Empty(pagina.Items);
        Assert.Null(await stream.GetItemAsync("post-001"));
    }

    [Fact]
    public async Task Apagada_lo_que_publica_un_miembro_si_sale()
    {
        var (stream, _, _) = Red(SemillaDeLaRed.Apagada);

        var creado = await stream.CreateAsync(new NewContentItem("ana@correo.test", "Hola, red."));
        var pagina = await stream.GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou));

        Assert.Equal(creado.Id, Assert.Single(pagina.Items).Id);
    }

    [Fact]
    public async Task Apagada_no_hay_seguidos_ni_reacciones_ni_perfiles_de_la_demo()
    {
        var (_, grafo, reacciones) = Red(SemillaDeLaRed.Apagada);
        var perfiles = new StubSocialProfileProjection(SemillaDeLaRed.Apagada);

        Assert.Empty(await grafo.GetFollowingAsync("act-elena"));
        Assert.Equal(0, (await reacciones.GetStateAsync("post-001")).Total);
        Assert.Empty(await reacciones.CountsForAllAsync());
        Assert.Null(await perfiles.GetByHandleAsync("elena.dev"));
        Assert.Null(await perfiles.GetByActorIdAsync("act-elena"));
    }

    [Fact]
    public async Task Apagada_las_notificaciones_no_derivan_de_la_demo()
    {
        var (_, grafo, reacciones) = Red(SemillaDeLaRed.Apagada);
        var notificaciones = new StubNotificationFeed(grafo, reacciones, null, SemillaDeLaRed.Apagada);

        Assert.Empty(await notificaciones.GetForAsync("act-elena"));
    }

    [Fact]
    public async Task Encendida_la_demo_sale_como_siempre()
    {
        var (stream, grafo, reacciones) = Red(SemillaDeLaRed.Encendida);

        var pagina = await stream.GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou, PageSize: 100));

        Assert.Equal(8, pagina.Items.Count);
        Assert.NotEmpty(await grafo.GetFollowingAsync("act-elena"));
        Assert.Equal(3, (await reacciones.GetStateAsync("post-001")).Total);
        Assert.NotNull(await new StubSocialProfileProjection(SemillaDeLaRed.Encendida).GetByHandleAsync("elena.dev"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void La_semilla_es_la_de_DevSeed(bool devSeed)
    {
        Assert.Equal(devSeed, SemillaDeLaRed.Segun(devSeed).EstaEncendida);
    }

    [Fact]
    public async Task Los_constructores_de_siempre_la_sirven_encendida()
    {
        // Es lo que hacían, y lo que esperan las pruebas que los usan; quien la apaga es el composer.
        var pagina = await new StubContentStream(new StubSocialGraphService(), new StubReactionService())
            .GetFeedAsync(new FeedQuery());

        Assert.Contains(pagina.Items, i => i.Id == "post-001");
    }

    /// <summary>
    /// El composer registra la semilla según <c>Synergos:DevSeed:Enabled</c> y se la pasa a los cinco stubs. Se lee la
    /// fuente porque el composer necesita un <c>IUmbracoBuilder</c>: es como lo mide <c>SocialWiringTests</c>.
    /// </summary>
    [Fact]
    public void El_composer_le_pasa_la_semilla_de_DevSeed_a_los_cinco_stubs()
    {
        var fuente = File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.Social.cs"));

        Assert.Matches(
            new Regex(@"SemillaDeLaRed\.Segun\(\s*sp\.GetRequiredService<IOptions<DevSeedSettings>>\(\)\.Value\.Enabled\)"),
            fuente);
        foreach (var stub in new[] { "StubReactionService", "StubSocialGraphService", "StubContentStream", "StubSocialProfileProjection", "StubNotificationFeed" })
        {
            var construccion = new Regex($@"new {stub}\((?<args>[^;]*?)\)\);", RegexOptions.Singleline).Match(fuente);
            Assert.True(construccion.Success, $"No encuentro la construcción de {stub} en el composer.");
            Assert.Contains("GetRequiredService<SemillaDeLaRed>()", construccion.Groups["args"].Value, StringComparison.Ordinal);
        }
    }

    // ── T002: los artículos del CMS llegan a la app ───────────────────────

    [Theory]
    [InlineData("appsettings.Development.json")]
    [InlineData("appsettings.Docker.json")]
    public void El_feed_de_la_red_sale_del_contenido_del_CMS(string fichero)
    {
        var catalogo = Catalogo(fichero);

        // Sin la clave, la fuente es «demo» y la app no veía ninguno de los artículos del blog.
        Assert.Equal("cms", catalogo.Sources["Social"]);
        // Y sin el alcance, la fuente falla cerrada y no siembra nada.
        Assert.Equal("blogs", catalogo.Scopes["Social"]);
    }

    [Fact]
    public void El_alcance_de_la_red_es_el_brandKey_del_siteRoot_de_Blogs()
    {
        var raiz = File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "uSync", "v9", "Content", "blogs.config"));

        var brandKey = Regex.Match(raiz, @"<brandKey>\s*<Value><!\[CDATA\[(?<valor>[^\]]*)\]\]></Value>").Groups["valor"].Value;

        Assert.Equal(Catalogo("appsettings.Docker.json").Scopes["Social"], brandKey);
    }

    private static CatalogSettings Catalogo(string fichero)
        => new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "Synergos.CMS.Web", fichero), optional: false)
            .Build()
            .GetSection("Synergos:Catalog")
            .Get<CatalogSettings>()!;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
