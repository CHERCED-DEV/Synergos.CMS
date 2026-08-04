namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La tienda del CMS compra contra el ORQUESTADOR, nunca contra las capacidades sueltas (HU #24).
/// </summary>
/// <remarks>
/// <para><b>Por qué esto necesita un gate y no basta con haberlo hecho bien una vez.</b> Cablear
/// el CMS a <c>Api.Inventory</c> + <c>Api.Payments</c> + <c>Api.Orders</c> por separado es el
/// atajo natural: son tres llamadas obvias y cada una funciona. Lo que no se ve al escribirlas es
/// que el checkout entero <b>puede fallar a la mitad</b> — si el cobro falla hay que soltar el
/// stock apartado— y que el CMS <b>no tiene dónde anotar una compensación pendiente</b>. Lo que
/// saldría de ese atajo no es «acoplamiento feo»: es stock apartado que nadie suelta y plata
/// cobrada sin pedido.</para>
///
/// <para>Y hay un detalle que solo se ve habiéndolo sufrido: <b>la compensación cambia de
/// carácter al capturar</b>. Antes de capturar, deshacer el pago es «liberar»; después, es
/// «devolver». Eso ya está resuelto en <c>Bff.Core</c> y reimplementarlo saldría mal.</para>
///
/// <para><b>Tosco a propósito</b>, como los demás gates del repo: mira los nombres de las
/// capacidades en las URL del CMS. No atrapa a un adversario, atrapa el atajo de un martes.</para>
/// </remarks>
public sealed class ShopWiringTests
{
    /// <summary>
    /// Las capacidades que el checkout usa POR DEBAJO y que el CMS no puede llamar de frente.
    /// </summary>
    /// <remarks>
    /// <c>Api.Cart</c> NO está: abrir una canasta y ponerle líneas es lo que el BFF exige recibir
    /// —<c>POST /v1/purchases</c> toma un <c>cartId</c>—, y nada de eso hay que deshacerlo si
    /// falla: una canasta abierta y nunca comprada vence sola. No hay saga que reimplementar,
    /// así que no hay nada que prohibir.
    /// </remarks>
    private static readonly string[] Prohibidas =
    {
        "v1/payments", "v1/orders", "v1/items", "v1/holds", "v1/shipments", "v1/quotes",
    };

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

    private static IEnumerable<string> FuentesDelCms()
        => new[] { "Synergos.CMS.Web", "Synergos.CMS.Application", "Synergos.CMS.Interfaces" }
            .Select(p => Path.Combine(RepoRoot(), p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>Quita comentarios de línea: acá la prosa NOMBRA las rutas prohibidas para explicarlas.</summary>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta)
            .Select(l =>
            {
                var t = l.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("///", StringComparison.Ordinal)
                    || t.StartsWith("*", StringComparison.Ordinal))
                {
                    return string.Empty;
                }
                var i = l.IndexOf("//", StringComparison.Ordinal);
                return i >= 0 ? l[..i] : l;
            }));

    [Fact]
    public void El_CMS_no_llama_a_las_capacidades_del_checkout_de_frente()
    {
        // Lo que tiene que poner esto en rojo: alguien resuelve «me falta el stock acá» con un
        // GET a Api.Inventory. Funciona, y deja el CMS a un paso de estar orquestando una saga
        // que no sabe deshacer.
        var infracciones = new List<string>();

        foreach (var f in FuentesDelCms())
        {
            var codigo = SinComentarios(f);
            foreach (var ruta in Prohibidas)
            {
                if (codigo.Contains($"\"{ruta}", StringComparison.OrdinalIgnoreCase)
                    || codigo.Contains($"/{ruta}", StringComparison.OrdinalIgnoreCase))
                {
                    infracciones.Add($"{Path.GetFileName(f)} → {ruta}");
                }
            }
        }

        Assert.True(infracciones.Count == 0,
            "El CMS está llamando a una capacidad del checkout de frente: "
            + string.Join(", ", infracciones.Distinct(StringComparer.Ordinal))
            + ". Comprar va contra Bff.Tienda (POST /v1/purchases): reservar + cobrar + crear el "
            + "pedido pueden fallar a la mitad, y el CMS no tiene dónde anotar una compensación "
            + "pendiente.");
    }

    [Fact]
    public void El_cliente_de_la_tienda_apunta_al_orquestador()
    {
        // El complemento del anterior: que la ruta que SÍ se usa sea la del BFF. Sin esto, borrar
        // la llamada entera también pasaría el gate de arriba.
        var cliente = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services", "HttpShopOrderService.cs"));

        Assert.Contains("v1/purchases", cliente, StringComparison.Ordinal);
    }

    [Fact]
    public void El_stub_sigue_siendo_el_default()
    {
        // Un clon limpio tiene que arrancar y vender sin levantar seis servicios. Si el default
        // se moviera a Bff, `git clone && dotnet run` dejaría de tener tienda — y el síntoma
        // sería un checkout que falla, no un error de arranque que alguien lea.
        var composer = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.Shop.cs"));

        Assert.Contains("StubShopOrderService", composer, StringComparison.Ordinal);

        // El modo Bff es OPT-IN: se entra por una comparación explícita contra "Bff", así que
        // ausencia de config = stub.
        Assert.Contains("\"Bff\"", composer, StringComparison.Ordinal);

        var settings = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.CMS.Application", "Configuration", "TiendaSettings.cs"));
        Assert.Contains("Mode { get; init; } = \"Stub\"", settings, StringComparison.Ordinal);
    }
}
