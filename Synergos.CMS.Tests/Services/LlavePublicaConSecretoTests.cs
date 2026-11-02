using Microsoft.AspNetCore.DataProtection;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Tests.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La llave pública con el secreto del despliegue (012-blogs-social, I0.b): quien conozca un correo no puede calcular
/// qué llave ni qué alias tiene esa persona.
/// </summary>
/// <remarks>
/// El mutante de estas pruebas es el código de I0: la llave por <see cref="SeudonimoDePersona.De"/>, la huella sin
/// secreto que cualquiera calcula. Con él dan rojo las dos primeras y la del reinicio.
/// </remarks>
public sealed class LlavePublicaConSecretoTests : IDisposable
{
    private const string Ana = "ana.perez@correo.test";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "syn-012-i0b-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void Sin_el_secreto_la_llave_de_un_correo_conocido_no_se_calcula()
    {
        var cara = IdentidadDePrueba.Con((Ana, "Ana Pérez")).De(Ana);

        // Lo que calcula cualquiera que conozca el correo: la huella sin secreto, la de las capacidades.
        Assert.NotEqual(SeudonimoDePersona.De(Ana), cara.Llave);
        Assert.NotEqual(CaraPublica.Alias("Ana Pérez", SeudonimoDePersona.De(Ana)), cara.Alias);
        // Ni con otro secreto.
        Assert.NotEqual(SeudonimoDePersona.Publico(Ana, "otro-secreto"u8.ToArray()), cara.Llave);
        Assert.True(CaraPublica.EsLlave(cara.Llave), cara.Llave);
    }

    [Fact]
    public void Con_el_mismo_secreto_la_llave_sale_estable_y_con_otro_sale_otra()
    {
        var store = new InMemoryJsonEntityStore();
        var a = IdentidadDePrueba.Con(store, IdentidadDePrueba.SecretoConfigurado(store, "secreto-a"), (Ana, "Ana Pérez")).De(Ana);
        var otraVez = IdentidadDePrueba.Con(store, IdentidadDePrueba.SecretoConfigurado(store, "secreto-a"), (Ana, "Ana Pérez")).De(Ana);
        var b = IdentidadDePrueba.Con(store, IdentidadDePrueba.SecretoConfigurado(store, "secreto-b"), (Ana, "Ana Pérez")).De(Ana);

        Assert.Equal(a, otraVez);
        Assert.NotEqual(a.Llave, b.Llave);
    }

    [Fact]
    public async Task Sin_secreto_configurado_se_genera_uno_se_guarda_cifrado_y_sobrevive_un_reinicio()
    {
        var store = new InMemoryJsonEntityStore();
        var llavero = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_raiz, "dpkeys")));

        var antes = IdentidadDePrueba.Con(store, IdentidadDePrueba.SecretoGenerado(store, llavero), (Ana, "Ana Pérez")).De(Ana);
        // «Reinicio»: otra custodia y otra cara sobre el mismo almacén y el mismo llavero.
        var despues = IdentidadDePrueba.Con(store, IdentidadDePrueba.SecretoGenerado(store, llavero), (Ana, "Ana Pérez")).De(Ana);

        Assert.Equal(antes, despues);
        Assert.NotEqual(SeudonimoDePersona.De(Ana), antes.Llave);

        var secreto = await IdentidadDePrueba.SecretoGenerado(store, llavero).GetKeyAsync();
        var guardado = await store.ReadAsync("keys", "identidad-publica-v1");
        Assert.Equal(32, secreto.Length);
        Assert.False(string.IsNullOrWhiteSpace(guardado));
        Assert.DoesNotContain(Convert.ToBase64String(secreto), guardado, StringComparison.Ordinal);
        Assert.Equal(SeudonimoDePersona.Publico(Ana, secreto), antes.Llave);
    }

    [Fact]
    public async Task Lo_anotado_con_la_llave_de_antes_sigue_llevando_al_correo_y_la_nueva_se_anota_al_volver_a_actuar()
    {
        var store = new InMemoryJsonEntityStore();
        var identidad = IdentidadDePrueba.Con(store, (Ana, "Ana Pérez"));

        // Lo que anotó I0, con la huella sin secreto.
        var llaveDeAntes = SeudonimoDePersona.De(Ana);
        var aliasDeAntes = CaraPublica.Alias("Ana Pérez", llaveDeAntes);
        await store.WriteAsync(IdentidadPublicaDeMiembros.PorLlave, llaveDeAntes,
            $$"""{"llave":"{{llaveDeAntes}}","correo":"{{Ana}}","alias":"{{aliasDeAntes}}"}""");
        await store.WriteAsync(IdentidadPublicaDeMiembros.PorAlias, aliasDeAntes,
            $$"""{"alias":"{{aliasDeAntes}}","llave":"{{llaveDeAntes}}"}""");

        var nueva = identidad.De(Ana);
        Assert.Null(await identidad.CorreoDeAsync(nueva.Alias));
        Assert.Equal(Ana, await identidad.CorreoDeAsync(aliasDeAntes));

        await identidad.RecordarAsync(Ana);

        Assert.Equal(Ana, await identidad.CorreoDeAsync(nueva.Llave));
        Assert.Equal(Ana, await identidad.CorreoDeAsync(nueva.Alias));
        Assert.Equal(Ana, await identidad.CorreoDeAsync(llaveDeAntes));
        Assert.Equal(Ana, await identidad.CorreoDeAsync(aliasDeAntes));
    }

    [Fact]
    public void Un_secreto_vacio_no_se_acepta_porque_ese_HMAC_lo_calcula_cualquiera()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeudonimoDePersona.Publico(Ana, []));
    }

    [Fact]
    public void El_composer_lee_la_seccion_y_registra_la_custodia_del_secreto()
    {
        var fuente = File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.Platform.cs"));

        Assert.Contains(@"Configure<IdentidadPublicaSettings>(builder.Config.GetSection(""Synergos:IdentidadPublica""))", fuente, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<SecretoDeLaCaraPublica>()", fuente, StringComparison.Ordinal);
    }

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
}
