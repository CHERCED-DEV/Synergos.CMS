using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff.Documentos;

/// <summary>
/// Lo que se firma y lo que se guarda (NN-24 y NN-25 de 003): <c>signing.firmar</c> y <c>documents.guardar</c> en un
/// orquestador declarado, contra <c>Api.Signing</c> y <c>Api.Documents</c> REALES. Lo que se afirma se lee en ellas: el sello
/// se comprueba con el contenido, y el documento es de quien tiene que ser, con su nombre, su tipo y su hash.
/// </summary>
/// <remarks>La sesión la pone la puerta del CMS como sujeto: aquí se simula con esa cabecera, como la manda la puerta.</remarks>
public sealed class DocumentosDeclaradosTests
{
    private const string Llave = "llave-de-los-documentos";
    private const string Consultorio = "muestra.consultorio.c-1";
    private const string Nota = "Paciente estable. Control en 30 días.";
    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.4\n% el hemograma de la muestra\n%%EOF");

    private static object Informe() => new { nombre = "hemograma.pdf", tipo = "application/pdf", contenidoBase64 = Convert.ToBase64String(Pdf) };

    // ── signing.firmar ─────────────────────────────────────────────────────

    [Fact]
    public async Task Firmar_sella_la_nota_con_la_llave_del_consultorio_y_el_sello_se_comprueba_con_la_nota()
    {
        await using var c = await Consultorio_.Arrancar();
        await c.CrearLlave(Consultorio);

        var (estado, firmada) = await c.Abrir("muestra.nota", "muestra.personal:dr-1", "n-1", new { consultorio = Consultorio, nota = Nota });
        var (_, otraVez) = await c.Abrir("muestra.nota", "muestra.personal:dr-2", "n-2", new { consultorio = Consultorio, nota = Nota });

        Assert.True(estado == HttpStatusCode.Created, firmada.ToString());
        var sello = firmada.GetProperty("datos").GetProperty("sello").GetString()!;
        var llaveId = firmada.GetProperty("datos").GetProperty("llaveId").GetString()!;
        Assert.Equal(sello, otraVez.GetProperty("datos").GetProperty("sello").GetString());
        var (cuadra, comprobado) = await c.Comprobar(Consultorio, Nota, sello);
        Assert.True(cuadra == HttpStatusCode.OK, comprobado.ToString());
        Assert.Equal(llaveId, comprobado.GetProperty("keyId").GetString());
        Assert.NotEqual(HttpStatusCode.OK, (await c.Comprobar(Consultorio, Nota + " Alta.", sello)).Estado);
    }

    [Fact]
    public async Task Sin_llave_del_consultorio_no_se_firma()
    {
        await using var c = await Consultorio_.Arrancar();

        var (estado, cuerpo) = await c.Abrir("muestra.nota", "muestra.personal:dr-1", "n-1", new { consultorio = "muestra.consultorio.c-9", nota = Nota });

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("signing.no_active_key", cuerpo.GetProperty("code").GetString());
    }

    // ── documents.guardar ──────────────────────────────────────────────────

    [Fact]
    public async Task El_consultorio_guarda_el_resultado_del_paciente_y_el_pdf_no_queda_en_ninguna_saga()
    {
        await using var c = await Consultorio_.Arrancar();

        var (estado, cargado) = await c.Abrir("muestra.resultado", "muestra.personal:lab-1", "r-1",
            new { paciente = new { kind = "muestra.paciente", id = "p-7" }, informe = Informe() });

        Assert.True(estado == HttpStatusCode.Created, cargado.ToString());
        var documento = await c.Documento(cargado.GetProperty("datos").GetProperty("documentoId").GetString()!);
        Assert.Equal(("muestra.paciente", "p-7"), (documento.GetProperty("ownerKind").GetString(), documento.GetProperty("ownerId").GetString()));
        Assert.Equal("hemograma.pdf", documento.GetProperty("name").GetString());
        Assert.Equal("application/pdf", documento.GetProperty("contentType").GetString());
        Assert.Equal(Pdf.Length, documento.GetProperty("size").GetInt64());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Pdf)), documento.GetProperty("sha256").GetString(), ignoreCase: true);
        Assert.Empty(c.FicherosQueContienen(Convert.ToBase64String(Pdf)));
    }

    /// <summary><c>documents.borrar</c> es la compensación de guardar: cancelar antes de liberar lo borra; liberar lo deja.</summary>
    [Fact]
    public async Task Cancelar_antes_de_liberar_borra_el_documento_y_liberar_lo_deja()
    {
        await using var c = await Consultorio_.Arrancar();
        var paciente = new { kind = "muestra.paciente", id = "p-7" };

        var (_, uno) = await c.Abrir("muestra.resultado", "muestra.personal:lab-1", "r-1", new { paciente, informe = Informe() });
        var (cancelo, cancelado) = await c.Operar("muestra.resultado", uno.GetProperty("id").GetString()!, "cancelar", "muestra.personal:lab-1");
        var (_, otro) = await c.Abrir("muestra.resultado", "muestra.personal:lab-1", "r-2", new { paciente, informe = Informe() });
        var (libero, liberado) = await c.Operar("muestra.resultado", otro.GetProperty("id").GetString()!, "liberar", "muestra.personal:lab-1");

        Assert.True(cancelo == HttpStatusCode.OK, cancelado.ToString());
        Assert.Equal("Compensated", cancelado.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.NotFound, await c.EstadoDelDocumento(uno.GetProperty("datos").GetProperty("documentoId").GetString()!));
        Assert.True(libero == HttpStatusCode.OK, liberado.ToString());
        Assert.Equal("Completed", liberado.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.OK, await c.EstadoDelDocumento(otro.GetProperty("datos").GetProperty("documentoId").GetString()!));
    }

    /// <summary>
    /// En un flujo del dueño el documento es del miembro, y no puede nombrar a otro; en uno del personal hay que nombrar de
    /// quién es. Lo que no vale no deja nada en <c>Api.Documents</c>.
    /// </summary>
    [Fact]
    public async Task El_documento_es_del_miembro_y_solo_el_personal_nombra_de_quien_es()
    {
        await using var c = await Consultorio_.Arrancar();

        var (propio, guardado) = await c.Abrir("muestra.documento-propio", "muestra.paciente:u-1", "d-1", new { archivo = Informe() });
        var (ajeno, rechazoAjeno) = await c.Abrir("muestra.documento-ajeno", "muestra.paciente:u-1", "d-2",
            new { archivo = Informe(), otro = new { kind = "muestra.paciente", id = "u-2" } });
        var (sinPaciente, rechazoSinPaciente) = await c.Abrir("muestra.resultado-sin-paciente", "muestra.personal:lab-1", "d-3", new { informe = Informe() });

        Assert.True(propio == HttpStatusCode.Created, guardado.ToString());
        var documento = await c.Documento(guardado.GetProperty("datos").GetProperty("documentoId").GetString()!);
        Assert.Equal(("muestra.paciente", "u-1"), (documento.GetProperty("ownerKind").GetString(), documento.GetProperty("ownerId").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, ajeno);
        Assert.Equal(PasoDocumentoGuardar.SinTitular, rechazoAjeno.GetProperty("code").GetString());
        Assert.Empty(await c.DocumentosDe("muestra.paciente", "u-2"));
        Assert.Equal(HttpStatusCode.Forbidden, sinPaciente);
        Assert.Equal(PasoDocumentoGuardar.SinTitular, rechazoSinPaciente.GetProperty("code").GetString());
        Assert.Empty(await c.DocumentosDe("muestra.personal", "lab-1"));
    }

    /// <summary>
    /// El documento no se guarda en la saga por construcción: un flujo que lo saque (o lo deje para lo posterior, o lo lea
    /// en otra fase) no arranca, y el mensaje nombra el valor y su tipo.
    /// </summary>
    [Fact]
    public async Task Un_flujo_que_sacaria_el_documento_de_la_fase_no_arranca()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Consultorio_.Arrancar("flujos-que-no-arrancan"));

        Assert.Contains("«archivo» se guarda en la saga y su tipo, «documento», no se puede guardar", ex.ToString(), StringComparison.Ordinal);
    }

    // ── El orquestador de la muestra ───────────────────────────────────────

    private sealed class Consultorio_ : IAsyncDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "documentos-declarados-" + Guid.NewGuid().ToString("N"));
        private WebApplication? _app;

        public ArnesDeCapacidades Capacidades { get; } = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Signing.Contracts.SealResponse>("signing")
            .Levanta<Synergos.Api.Documents.Contracts.DocumentResponse>("documents")
            .Levanta<Synergos.Api.Audit.Contracts.AuditEntryResponse>("audit");

        public HttpClient Cliente { get; private set; } = null!;

        public static async Task<Consultorio_> Arrancar(string carpeta = "flujos")
        {
            var c = new Consultorio_();
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["muestra:Flujos:Carpeta"] = Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "Documentos", carpeta),
                    ["muestra:Storage:Root"] = c._raiz,
                    ["Muestra:ApiKey"] = Llave,
                });
                builder.Services.AddSingleton<IHttpClientFactory>(c.Capacidades);
                builder.AddCorrelation();
                builder.AddOrquestadorDeclarado(ArranqueDeLaMuestra.Vocabulario, typeof(DocumentosDeclaradosTests).Assembly);

                c._app = builder.Build();
                c._app.UseCorrelation();
                c._app.UseSharedKeyAuth(c._app.Configuration["Muestra:ApiKey"]);
                c._app.MapFlujosDeclarados();
                await c._app.StartAsync();
                c.Cliente = c._app.GetTestClient();
                c.Cliente.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
                return c;
            }
            catch
            {
                await c.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Cliente?.Dispose();
            if (_app is not null) await _app.DisposeAsync();
            Capacidades.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }

        /// <summary>Crea la llave de firma del consultorio en <c>Api.Signing</c>, como al desplegar (003-Q14).</summary>
        public async Task CrearLlave(string proposito)
        {
            using var signing = Capacidades.CreateClient("signing");
            using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/keys") { Content = JsonContent.Create(new { purpose = proposito }) };
            peticion.Headers.Add("Idempotency-Key", "llave-" + proposito);
            using var r = await signing.SendAsync(peticion);
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        }

        public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Comprobar(string proposito, string contenido, string sello)
        {
            using var signing = Capacidades.CreateClient("signing");
            return await Leer(await signing.PostAsJsonAsync("v1/seals/verify", new { purpose = proposito, payload = contenido, seal = sello }));
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string flujo, string sujeto, string llave, object cuerpo)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/{flujo}") { Content = JsonContent.Create(cuerpo) };
            peticion.Headers.Add("Idempotency-Key", llave);
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto);
            return Enviar(peticion);
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Operar(string flujo, string saga, string operacion, string sujeto)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/{flujo}/{saga}/{operacion}") { Content = JsonContent.Create(new { }) };
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto);
            return Enviar(peticion);
        }

        public async Task<JsonElement> Documento(string id)
        {
            using var documents = Capacidades.CreateClient("documents");
            var (estado, cuerpo) = await Leer(await documents.GetAsync($"v1/documents/{id}"));
            Assert.True(estado == HttpStatusCode.OK, $"El documento {id} no está en Api.Documents: {(int)estado} {cuerpo}");
            return cuerpo;
        }

        public async Task<HttpStatusCode> EstadoDelDocumento(string id)
        {
            using var documents = Capacidades.CreateClient("documents");
            using var r = await documents.GetAsync($"v1/documents/{id}");
            return r.StatusCode;
        }

        public async Task<IReadOnlyList<JsonElement>> DocumentosDe(string clase, string id)
        {
            using var documents = Capacidades.CreateClient("documents");
            var pagina = await documents.GetFromJsonAsync<JsonElement>($"v1/documents?ownerKind={clase}&ownerId={id}");
            return pagina.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
        }

        /// <summary>Los ficheros del almacén del orquestador (sagas, arriendos, ofertas) que contienen <paramref name="texto"/>.</summary>
        public IReadOnlyList<string> FicherosQueContienen(string texto)
            => Directory.Exists(_raiz)
                ? Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories)
                    .Where(f => File.ReadAllText(f).Contains(texto, StringComparison.Ordinal)).ToList()
                : [];

        private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
        {
            using (peticion) return await Leer(await Cliente.SendAsync(peticion));
        }

        private static async Task<(HttpStatusCode, JsonElement)> Leer(HttpResponseMessage r)
        {
            using (r)
            {
                var texto = await r.Content.ReadAsStringAsync();
                return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
            }
        }
    }
}
