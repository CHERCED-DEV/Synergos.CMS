using System.Text.Json;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Quién es personal de la tienda se declara UNA vez (013-seller-operaciones, T003; lote 12, P1 = A): cada entrada de la
/// puerta de un flujo del personal de la tienda declara los mismos roles que <c>Synergos:Tienda:RolesDelPersonal</c>.
/// </summary>
/// <remarks>
/// <para>Las guardas del CMS (la RMA, la oferta, la consola del vendedor) leen <c>RolesDelPersonal</c>; la puerta lee los
/// <c>Roles</c> de cada flujo. Con dos listas distintas, alguien llevaría una devolución hasta «reembolsada» por la ruta
/// del CMS y la plata no saldría por la puerta, o al revés. Se compara el TEXTO de <c>appsettings.json</c>, que es lo que
/// arranca el CMS: un flujo nuevo del personal (<c>tienda.*</c> con <c>Acceso: Personal</c>) entra solo en la cuenta.</para>
///
/// <para>Sin <c>RolesDelPersonal</c> en el fichero, el personal es el de por defecto (<see cref="TiendaSettings.RolPorDefecto"/>):
/// el gate lo calcula con la misma función que las guardas.</para>
/// </remarks>
public sealed class RolesDelPersonalDeLaTiendaTests
{
    private static readonly JsonDocumentOptions Json = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static JsonDocument Ajustes()
        => JsonDocument.Parse(File.ReadAllText(Proyectos.Dir("Synergos.CMS.Web", "appsettings.json")), Json);

    private static IReadOnlyList<string> Lista(JsonElement arreglo)
        => arreglo.EnumerateArray().Select(r => r.GetString() ?? string.Empty).ToList();

    private static IReadOnlyList<string> ElPersonal(JsonElement synergos)
    {
        var declarados = synergos.TryGetProperty("Tienda", out var tienda) && tienda.TryGetProperty("RolesDelPersonal", out var roles)
            ? Lista(roles).ToList()
            : [];
        return new TiendaSettings { RolesDelPersonal = declarados }.Personal();
    }

    private static string Comparable(IEnumerable<string> roles)
        => string.Join(",", roles.Select(r => r.Trim().ToLowerInvariant()).Order(StringComparer.Ordinal));

    [Fact]
    public void Cada_flujo_del_personal_de_la_tienda_declara_en_la_puerta_los_roles_del_personal()
    {
        using var ajustes = Ajustes();
        var synergos = ajustes.RootElement.GetProperty("Synergos");
        var personal = ElPersonal(synergos);

        var delPersonal = synergos.GetProperty("Puerta").GetProperty("Flujos").EnumerateObject()
            .Where(f => f.Name.StartsWith("tienda.", StringComparison.Ordinal)
                && f.Value.TryGetProperty("Acceso", out var acceso)
                && string.Equals(acceso.GetString(), "Personal", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Por lo menos tienda.devolucion (004). Si no encuentra ninguno, el gate no estaría mirando nada.
        Assert.Contains(delPersonal, f => f.Name == "tienda.devolucion");
        foreach (var flujo in delPersonal)
        {
            var roles = flujo.Value.TryGetProperty("Roles", out var r) ? Lista(r) : [];
            Assert.True(
                Comparable(roles) == Comparable(personal),
                $"Synergos:Puerta:Flujos:{flujo.Name} declara [{string.Join(", ", roles)}] y el personal de la tienda es "
                + $"[{string.Join(", ", personal)}] (Synergos:Tienda:RolesDelPersonal). Son la misma lista.");
        }
    }

    [Fact]
    public void Un_rol_del_personal_en_blanco_no_deja_arrancar_el_CMS()
    {
        // El validador existe y se registra con ValidateOnStart: sin lo segundo, el rol en blanco se descubre en la
        // primera devolución que alguien intenta atender.
        var composer = File.ReadAllLines(Proyectos.Dir("Synergos.CMS.Web", "Composers", "SeamComposer.Shop.cs"));

        Assert.Contains(composer, l => l.Contains("AddOptions<TiendaSettings>()", StringComparison.Ordinal)
            && l.TrimEnd().EndsWith(".ValidateOnStart();", StringComparison.Ordinal));
        Assert.Contains(composer, l => l.Contains("IValidateOptions<TiendaSettings>, ValidadorDeLaTienda", StringComparison.Ordinal));
    }
}
