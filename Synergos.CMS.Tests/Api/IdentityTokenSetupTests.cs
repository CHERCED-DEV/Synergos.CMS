using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Un servicio que exige llave de firma <b>no arranca sin ella</b> (HU #14, rebanada 3).
/// </summary>
/// <remarks>
/// <para><b>Estos tests existen porque el arranque mentía.</b> La llave se leía dentro de una
/// fábrica de singleton, así que el fallo era perezoso: en una API mínima nadie resuelve el
/// servicio hasta la primera petición que lo inyecta. <c>Api.Identity</c> sin llave arrancaba
/// <b>verde</b>, contestaba <c>/health</c> y solo reventaba cuando una persona intentaba entrar —
/// es decir, pasaba la prueba de humo de un despliegue mal configurado.</para>
///
/// <para><b>Lo destapó levantar el proceso, no un test</b> (<c>CLAUDE.md</c> §10.6), y por eso el
/// gate comprueba <i>cuándo</i> falla y no solo <i>si</i> falla: la versión perezosa también
/// terminaba lanzando, un rato más tarde y con el despliegue ya dado por bueno.</para>
/// </remarks>
public sealed class IdentityTokenSetupTests
{
    private static IHostApplicationBuilder Host(params (string Clave, string Valor)[] ajustes)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            ajustes.Select(a => new KeyValuePair<string, string?>(a.Clave, a.Valor)));
        return builder;
    }

    private static (string, string)[] ConLlave => new[]
    {
        ($"{IdentityTokenSetup.Section}:Keys:k1", "llave-de-firma-para-el-test"),
        ($"{IdentityTokenSetup.Section}:ActiveKeyId", "k1"),
    };

    /// <summary>
    /// Sin llave, quien la exige revienta <b>al cablear</b> — no en la primera petición.
    /// </summary>
    /// <remarks>
    /// Es EL test de esta rebanada. Si algún día vuelve a fallar de forma perezosa, éste se pone
    /// rojo aunque el mensaje de error siga siendo idéntico.
    /// </remarks>
    [Fact]
    public void Quien_exige_llave_falla_al_CABLEAR_y_no_en_la_primera_peticion()
    {
        var builder = Host();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddIdentityTokens(required: true));

        Assert.Contains(IdentityTokenSetup.Section, ex.Message, StringComparison.Ordinal);
    }

    [Fact] // happy: con llave, quien la exige la inyecta sin comprobar nulos.
    public void Con_llave_el_emisor_recibe_el_emisor_a_secas()
    {
        var builder = Host(ConLlave);
        builder.AddIdentityTokens(required: true);

        using var sp = builder.Services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IdentityTokens>());
        Assert.True(sp.GetRequiredService<IdentityTokenGate>().Configured);
    }

    /// <summary>
    /// Quien solo verifica arranca sin configurar nada: es el camino del clon limpio.
    /// </summary>
    /// <remarks>
    /// Y arranca <b>sin</b> poder verificar, que es distinto de arrancar verificando mal: un token
    /// presentado acá se rechaza con <c>identity.token_not_verifiable</c>, no se ignora.
    /// </remarks>
    [Fact]
    public void Quien_solo_verifica_arranca_sin_llave_y_lo_dice()
    {
        var builder = Host();
        builder.AddIdentityTokens(required: false);

        using var sp = builder.Services.BuildServiceProvider();
        var gate = sp.GetRequiredService<IdentityTokenGate>();

        Assert.False(gate.Configured);
        Assert.Null(gate.Tokens);
        // Y NO se registra el emisor a secas: pedirlo tiene que fallar, no devolver algo inútil.
        Assert.Null(sp.GetService<IdentityTokens>());
    }

    [Fact] // una llave en blanco es «no hay llave», no una llave vacía que firmaría cualquier cosa.
    public void Una_llave_en_blanco_cuenta_como_no_haberla_puesto()
    {
        var builder = Host(($"{IdentityTokenSetup.Section}:Keys:k1", "   "));

        Assert.Throws<InvalidOperationException>(() => builder.AddIdentityTokens(required: true));
    }

    [Fact] // el verificador también se construye al cablear: un ActiveKeyId inválido no espera.
    public void Una_llave_activa_inexistente_falla_al_cablear()
    {
        var builder = Host(
            ($"{IdentityTokenSetup.Section}:Keys:k1", "llave-de-firma-para-el-test"),
            ($"{IdentityTokenSetup.Section}:ActiveKeyId", "k-que-no-existe"));

        Assert.Throws<ArgumentException>(() => builder.AddIdentityTokens(required: false));
    }
}
