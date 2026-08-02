using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Filters;

/// <summary>
/// Cubre el antiforgery del dashboard de admin (auditoría F5, backlog Alto #2) — las dos mitades
/// del mismo mecanismo: el atributo en el controller y el token en cada form.
/// </summary>
/// <remarks>
/// <para><b>Por qué las dos mitades.</b> Antiforgery solo funciona si servidor y vista están de
/// acuerdo. El atributo solo, sin tokens en las formas, rompe las 16 acciones POST del dashboard
/// con un 400; los tokens solos, sin el atributo, no validan nada. Cada mitad por separado pasa
/// un build verde, así que ninguna se sostiene en "se ve bien en el diff".</para>
///
/// <para><b>El duplicado silencioso.</b> El <c>FormTagHelper</c> emite un token implícito
/// <i>solo</i> cuando el <c>&lt;form&gt;</c> no trae atributo <c>action</c> — se verificó contra
/// la app real, no de memoria. Si además la vista pone <c>@Html.AntiForgeryToken()</c>, quedan
/// dos hidden con el mismo nombre; <c>StringValues</c> los une con coma y la validación FALLA.
/// De ahí que las tres formas sin <c>action</c> lleven <c>asp-antiforgery="false"</c>, y de ahí
/// que <see cref="Cada_form_POST_de_admin_declara_exactamente_UN_token"/> cuente ocurrencias en
/// vez de limitarse a comprobar que hay al menos una.</para>
/// </remarks>
public sealed class AdminAntiforgeryTests
{
    [Fact]
    public void AdminController_valida_antiforgery_en_TODOS_los_verbos_inseguros()
    {
        // AutoValidate y no ValidateAntiForgeryToken action-por-action: el default pasa a
        // cerrado, igual que con RequireRoles. Un POST nuevo nace validado.
        var attribute = typeof(AdminController)
            .GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), inherit: false);

        Assert.Single(attribute);
    }

    [Fact]
    public void Cada_form_POST_de_admin_declara_exactamente_UN_token()
    {
        var offenders = new List<string>();
        var checkedForms = 0;

        foreach (var file in Directory.GetFiles(AdminViewsDirectory(), "*.cshtml"))
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            foreach (var form in PostForms(source))
            {
                checkedForms++;
                var tokens = Regex.Matches(form.Body, @"@Html\.AntiForgeryToken\(\)").Count;
                var suppressed = form.OpeningTag.Contains(@"asp-antiforgery=""false""", StringComparison.Ordinal);
                var hasAction = form.OpeningTag.Contains("action=", StringComparison.Ordinal);

                if (tokens != 1)
                {
                    offenders.Add($"{name}: form con {tokens} @Html.AntiForgeryToken() (debe ser 1) → {Excerpt(form.OpeningTag)}");
                }

                // Sin action el tag helper añadiría el suyo encima del de la vista.
                if (!hasAction && !suppressed)
                {
                    offenders.Add($"{name}: form sin action= y sin asp-antiforgery=\"false\" → token DUPLICADO → {Excerpt(form.OpeningTag)}");
                }

                // Al revés también es un bug: suprimir el implícito en un form que nunca lo tuvo
                // es ruido que induce a error sobre cómo funciona el tag helper.
                if (hasAction && suppressed)
                {
                    offenders.Add($"{name}: form con action= no necesita asp-antiforgery=\"false\" → {Excerpt(form.OpeningTag)}");
                }
            }
        }

        Assert.Empty(offenders);
        Assert.Equal(16, checkedForms); // si aparece o desaparece un form POST, que se note aquí
    }

    [Fact]
    public void Solo_los_forms_POST_llevan_token()
    {
        // La invariante en su forma corta: en Views/Admin hay token si y solo si el form es POST.
        //
        // Los GET son los filtros del dashboard —idempotentes, un token ahí ensucia el
        // querystring y sugiere que mutan algo—. Los method="dialog" cierran el <dialog> en el
        // cliente y NUNCA viajan al servidor. Ambos necesitan asp-antiforgery="false" explícito:
        // se comprobó contra la app corriendo que el tag helper le ponía un token implícito al
        // form del diálogo de confirmación de la cola de moderación, porque su regla mira si hay
        // atributo action, no si el método es inseguro.
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(AdminViewsDirectory(), "*.cshtml"))
        {
            var name = Path.GetFileName(file);
            foreach (var form in Forms(File.ReadAllText(file)))
            {
                if (form.OpeningTag.Contains(@"method=""post""", StringComparison.Ordinal))
                {
                    continue; // los cubre el test de arriba
                }

                if (form.Body.Contains("@Html.AntiForgeryToken()", StringComparison.Ordinal))
                {
                    offenders.Add($"{name}: form no-POST con token explícito → {Excerpt(form.OpeningTag)}");
                }

                var isGet = form.OpeningTag.Contains(@"method=""get""", StringComparison.Ordinal);
                var suppressed = form.OpeningTag.Contains(@"asp-antiforgery=""false""", StringComparison.Ordinal);
                if (!isGet && !suppressed)
                {
                    offenders.Add($"{name}: form no-POST sin asp-antiforgery=\"false\" → token implícito inútil → {Excerpt(form.OpeningTag)}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    // ── Lectura de las vistas ─────────────────────────────────────────────────

    private readonly record struct RazorForm(string OpeningTag, string Body);

    private static IEnumerable<RazorForm> PostForms(string source) =>
        Forms(source).Where(f => f.OpeningTag.Contains(@"method=""post""", StringComparison.Ordinal));

    /// <summary>
    /// Parte el .cshtml en formas. No hay <c>&lt;form&gt;</c> anidados en Views/Admin (HTML no lo
    /// permite), así que emparejar cada apertura con el siguiente cierre es suficiente.
    /// </summary>
    private static IEnumerable<RazorForm> Forms(string source)
    {
        foreach (Match open in Regex.Matches(source, @"<form\b[^>]*>", RegexOptions.Singleline))
        {
            var close = source.IndexOf("</form>", open.Index, StringComparison.Ordinal);
            var end = close < 0 ? source.Length : close;
            yield return new RazorForm(open.Value, source[open.Index..end]);
        }
    }

    private static string Excerpt(string tag) =>
        tag.Length <= 90 ? tag : tag[..90] + "…";

    private static string AdminViewsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Synergos.CMS.Web", "Views", "Admin");
    }
}
