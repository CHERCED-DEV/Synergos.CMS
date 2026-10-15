using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Tienda.Clients;
using Synergos.Bff.Tienda.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Un orquestador con los dos motores (núcleo N2, NN-15 de Salud): la máquina de su saga tipada y la de la
/// genérica, como pide el matiz 1 del lote 2.
/// </summary>
/// <remarks>
/// <para><b>Chocaban en dos sitios y ninguno fallaba al arrancar.</b> Las dos sagas acababan en la misma
/// carpeta, y cada barrido leía las de la otra. Y la llave compartida salía dos veces en los clientes de las
/// capacidades comunes («k,k»), que la capacidad rechaza con 401.</para>
///
/// <para>Se arma como lo haría Salud con su cita: la saga de Tienda hace aquí de saga tipada, porque es la que
/// el árbol ya registra con su ejecutor.</para>
/// </remarks>
public sealed class DosMaquinasTests : IDisposable
{
    private const string Llave = "llave-compartida";
    private static readonly string[] DeLaTienda = ["cart", "pricing", "inventory", "orders", "payments", "fulfillment"];
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "dos-maquinas-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal */ }
    }

    private WebApplicationBuilder Orquestador()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["prueba:Storage:Root"] = _raiz;
        foreach (var cap in DeLaTienda.Concat(Synergos.Bff.Pasos.Http.PuertosHttp.Capacidades).Distinct(StringComparer.Ordinal))
        {
            builder.Configuration[$"prueba:Capabilities:{cap}:ApiKey"] = Llave;
        }

        var vocabulario = new SagaVocabulary("prueba", "la compra");
        builder.AddCorrelation();
        builder.AddSagaMachinery<PurchaseSaga, TiendaCompensationExecutor>(vocabulario, DeLaTienda);
        builder.Services.AddSingleton<TiendaCapabilities>();
        builder.AddOrquestadorDeclarado(vocabulario, typeof(SagaEngine<>).Assembly);
        return builder;
    }

    [Fact]
    public void Cada_saga_guarda_en_su_sitio_y_ningun_barrido_lee_las_de_la_otra()
    {
        using var proveedor = Orquestador().Services.BuildServiceProvider();
        var tipadas = proveedor.GetRequiredService<ISagaStore<PurchaseSaga>>();
        var genericas = proveedor.GetRequiredService<ISagaStore<SagaDeFlujo>>();
        var hace = DateTimeOffset.UtcNow.AddHours(-2);
        var comprador = Ref.Create("prueba.comprador", "u-1");

        tipadas.Put(new PurchaseSaga("compra-1", comprador, "ca-1", SagaStatus.Running, [], null, null, null,
            Money.Of(1000m, "COP"), [], null, hace));
        genericas.Put(new SagaDeFlujo("flujo-1", "prueba.flujo", comprador, SagaStatus.Running,
            new Dictionary<string, ValorDeFlujo>(), new Dictionary<string, IReadOnlyList<ReservaDeFlujo>>(), [], null, hace));

        // Lo que ve el barrido de abandono de cada una: las suyas y ninguna más.
        Assert.Equal(new[] { "compra-1" }, tipadas.StartedBefore(DateTimeOffset.UtcNow).Select(s => s.Id));
        Assert.Equal(new[] { "flujo-1" }, genericas.StartedBefore(DateTimeOffset.UtcNow).Select(s => s.Id));

        // La tipada sigue en la raíz, donde están sus datos de producción; la genérica, en flujos/.
        var opciones = Options.Create(new SagaStorageOptions { Root = _raiz });
        Assert.NotNull(new FileSystemSagaStore<PurchaseSaga>(opciones).Find("compra-1"));
        Assert.NotNull(new FileSystemSagaStore<SagaDeFlujo>(opciones, new CarpetaDeSagas<SagaDeFlujo>(OrquestadorDeclarado.CarpetaDeLosFlujos))
            .Find("flujo-1"));
    }

    [Fact]
    public void La_llave_compartida_sale_una_sola_vez_en_los_clientes_que_las_dos_comparten()
    {
        using var proveedor = Orquestador().Services.BuildServiceProvider();
        var fabrica = proveedor.GetRequiredService<IHttpClientFactory>();

        foreach (var cap in new[] { "pricing", "inventory", "payments", "notifications", "booking", "cart" })
        {
            using var http = fabrica.CreateClient(cap);
            Assert.True(http.DefaultRequestHeaders.GetValues(SharedKeyAuth.HeaderName).SequenceEqual(new[] { Llave }),
                $"«{cap}» manda {SharedKeyAuth.HeaderName}: {string.Join(" | ", http.DefaultRequestHeaders.GetValues(SharedKeyAuth.HeaderName))}");
        }
    }
}
