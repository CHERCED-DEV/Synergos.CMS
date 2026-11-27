using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El catálogo de muestra de Educación promete lo que tiene (016, D-016-C26): cada descarga de una lección existe en
/// disco, y el curso de 57 minutos cuesta lo que su contenido sostiene y no promete lo que no enseña.
/// </summary>
public sealed class EducacionDeEjemploTests
{
    private static string Wwwroot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) { dir = dir.Parent; }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Synergos.CMS.Web", "wwwroot");
    }

    [Fact]
    public void Cada_descarga_de_una_leccion_existe_en_disco()
    {
        var descargas = AcademyDemoSeed.Courses
            .SelectMany(c => c.Modules).SelectMany(m => m.Lessons).SelectMany(l => l.Resources)
            .ToList();

        foreach (var d in descargas)
        {
            var ruta = Path.Combine(Wwwroot(), d.Url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(ruta), $"«{d.Title}» apunta a {d.Url}, que no existe.");
        }
    }

    [Fact]
    public void El_curso_de_arquitectura_limpia_cuesta_lo_que_sostiene_y_no_promete_produccion()
    {
        var curso = AcademyDemoSeed.Courses.Single(c => c.Id == "dev-clean-architecture");

        Assert.Equal(57, curso.DurationMinutes);
        Assert.Equal(89_000m, curso.Price);
        Assert.DoesNotContain("producción", curso.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SOLID", curso.Summary + " " + string.Join(" ", curso.Outcomes), StringComparison.Ordinal);
    }

    [Fact] // D-016-Q4: ningún curso de pago cuesta más de $ 120.000 por hora de clase (antes, 75 minutos a $ 480.000).
    public void Ningun_curso_de_pago_cuesta_mas_de_lo_que_dura()
    {
        foreach (var curso in AcademyDemoSeed.Courses.Where(c => c.Price > 0))
        {
            var tope = 120_000m * curso.DurationMinutes / 60m;
            Assert.True(curso.Price <= tope, $"«{curso.Title}»: {curso.DurationMinutes} min a {curso.Price:N0}, tope {tope:N0}.");
        }
    }
}
