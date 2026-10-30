using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Synergos.Bff.Core;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La compra deja su envío para lo posterior (013-seller-operaciones, T013; R2): <c>tienda.compra</c> declara
/// <c>"para_lo_posterior": ["cobroId", "envio"]</c>, y un flujo del personal posterior a ella lee el envío con
/// <c>flow.leer-original</c>: el de una compra de ahora y el de una de ANTES, guardada con la definición vieja.
/// </summary>
/// <remarks>
/// <para><b>El posterior es de prueba</b> y llega por la carpeta de flujos del orquestador (<c>tienda:Flujos:Carpeta</c>):
/// sólo lee el envío, así que lo que se mira es lo que la compra deja y no lo que hacen los flujos del despacho (T015).
/// Si la compra no lo dejara, el orquestador no arrancaría: el validador no deja leer de la original lo que ella no deja
/// para lo posterior, aunque lo publique en su salida.</para>
///
/// <para><b>Las compras de antes se despachan sin migrar nada.</b> El envío ya se guardaba, porque es de la salida. Se
/// mira con <c>EnvioParaLoPosterior/compra-de-antes.json</c>, el fichero tal como lo dejó el almacén de sagas, en su
/// sitio: capturado de una compra real por la puerta (<see cref="CompraDeTiendaReal"/>) con la definición de antes, el
/// 2026-10-30.</para>
/// </remarks>
public sealed class EnvioParaLoPosteriorTests : IDisposable
{
    private const string Posterior = "prueba.lee-el-envio";

    /// <summary>La compra de antes: su id, su envío y dónde la dejó el almacén, bajo la raíz del orquestador.</summary>
    private const string CompraDeAntes = "compra-de-antes";

    private const string EnvioDeAntes = "a2c52e8288ec46c8a19cfe485b96c841";

    private const string DondeSeGuardo = "flujos/sagas/7bb5d431d4c333bef33a64502acb619ec1851cc7d6d5f830ea1bc041c95c267e.json";

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "envio-posterior-" + Guid.NewGuid().ToString("N"));

    public EnvioParaLoPosteriorTests()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(Path.Combine(_carpeta, Posterior + ".json"), $$"""
            {
              "clave": "{{Posterior}}",
              "posterior_a": "tienda.compra",
              "acceso": "personal",
              "entrada": ["personal", "compra"],
              "tipos": {
                "personal": { "de": "sujeto" },
                "compra": "texto",
                "envio": { "tipo": "texto", "de": "original" }
              },
              "salida": ["compra", "envio"],
              "fases": { "abrir": ["original"] },
              "pasos": {
                "original": { "tipo": "flow.leer-original", "lee": ["compra"], "escribe": ["envio"] }
              }
            }
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, recursive: true); }
        catch (IOException) { /* un temporal */ }
    }

    private CompraDeTiendaReal Tienda() => new(ajustes: new Dictionary<string, string> { ["tienda:Flujos:Carpeta"] = _carpeta });

    private static async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> LeerElEnvio(CompraDeTiendaReal t, string compra, string llave)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/{Posterior}") { Content = JsonContent.Create(new { compra }) };
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, "tienda.personal:staff-1");
        peticion.Headers.Add("Idempotency-Key", llave);
        using var r = await t.Orquestador.SendAsync(peticion);
        return (r.StatusCode, await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    [Fact]
    public async Task El_personal_lee_el_envio_de_una_compra_completada()
    {
        using var t = Tienda();
        var (_, abierta) = await t.Abrir(llave: "compra-1");
        var compra = abierta.GetProperty("id").GetString()!;
        var (cerrada, _) = await t.Cerrar(compra);
        Assert.Equal(HttpStatusCode.OK, cerrada);

        var (estado, leido) = await LeerElEnvio(t, compra, "leer-1");

        Assert.True(estado == HttpStatusCode.Created, leido.ToString());
        Assert.Equal(t.Dato(compra, "envio"), leido.GetProperty("datos").GetProperty("envio").GetString());
    }

    [Fact]
    public async Task Una_compra_guardada_con_la_definicion_de_antes_deja_leer_su_envio()
    {
        using var t = Tienda();
        var fichero = Path.Combine(t.Raiz, DondeSeGuardo);
        Directory.CreateDirectory(Path.GetDirectoryName(fichero)!);
        File.Copy(Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "EnvioParaLoPosterior", "compra-de-antes.json"), fichero);

        var (estado, leido) = await LeerElEnvio(t, CompraDeAntes, "leer-antes");

        Assert.True(estado == HttpStatusCode.Created, leido.ToString());
        Assert.Equal(EnvioDeAntes, leido.GetProperty("datos").GetProperty("envio").GetString());
    }
}
