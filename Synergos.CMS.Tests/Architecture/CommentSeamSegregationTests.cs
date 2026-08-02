using System.Reflection;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El corte de <c>ICommentRepository</c> en tres caras (auditoría, backlog Medio #5) como
/// invariante ejecutable: cada consumidor pide la cara que usa, y ninguna más.
/// </summary>
/// <remarks>
/// <para><b>Por qué se partió.</b> Eran 12 métodos en una sola interfaz y se midió quién usaba
/// qué. De los cinco consumidores, <b>ninguno</b> usaba las tres caras: el blog solo lee
/// aprobados, el hilo público solo comenta y reacciona, y las dos consolas solo moderan. El
/// costo se veía en los dobles de test — <c>BlogsControllerOla6Tests</c> tenía que implementar
/// los doce métodos para usar uno, y cada método nuevo del repositorio rompía un test de blogs
/// que no tiene nada que ver con moderación.</para>
///
/// <para><b>Por qué un test y no confiar en el diff.</b> Nada impide que mañana alguien cambie
/// <c>ICommentReader</c> por <c>ICommentModeration</c> en el ctor de <c>BlogsController</c>
/// "porque ya que estamos" — compila, pasa todo, y el renderizador público queda con la
/// capacidad de leer y borrar la cola de moderación. Eso es justo lo que el corte iba a
/// impedir, así que se fija aquí.</para>
///
/// <para>Se mira el <b>constructor</b> y no los usings: es el único punto donde una clase
/// declara qué capacidades recibe, y es lo que el contenedor de DI resuelve.</para>
/// </remarks>
public sealed class CommentSeamSegregationTests
{
    private static readonly Type Reader = typeof(ICommentReader);
    private static readonly Type Writer = typeof(ICommentWriter);
    private static readonly Type Moderation = typeof(ICommentModeration);

    private static readonly Type[] AllFaces = { Reader, Writer, Moderation };

    /// <summary>Las caras del repositorio de comentarios que el ctor del tipo recibe.</summary>
    private static IReadOnlyList<Type> FacesTakenBy(Type consumer)
    {
        var ctor = consumer
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        return ctor.GetParameters()
            .Select(p => p.ParameterType)
            .Where(AllFaces.Contains)
            .ToList();
    }

    [Fact]
    public void El_render_publico_del_blog_solo_puede_LEER_aprobados()
    {
        // La propiedad de verdad: por construcción, BlogsController no puede ver un comentario
        // sin aprobar ni borrar nada. No es una convención, es que el tipo no lo ofrece.
        Assert.Equal(new[] { Reader }, FacesTakenBy(typeof(BlogsController)));
    }

    [Fact]
    public void El_hilo_publico_solo_puede_ESCRIBIR()
    {
        Assert.Equal(new[] { Writer }, FacesTakenBy(typeof(CommentsController)));
    }

    [Theory]
    [InlineData(typeof(AdminController))]
    [InlineData(typeof(CommentsModerationController))]
    public void Las_dos_consolas_solo_reciben_la_cara_de_MODERACION(Type console)
    {
        // Moderan, y por eso ven pendientes. Lo que NO reciben es la cara de escritura: una
        // consola de moderación no crea comentarios ni reparte "me gusta".
        Assert.Equal(new[] { Moderation }, FacesTakenBy(console));
    }

    [Fact]
    public void El_tooling_de_demo_siembra_pero_no_modera()
    {
        // Único consumidor con dos caras — y aun así no la tercera: sembrar un hilo de demo no
        // es aprobar nada.
        var faces = FacesTakenBy(typeof(DevContentFiller));

        Assert.Equal(2, faces.Count);
        Assert.Contains(Reader, faces);
        Assert.Contains(Writer, faces);
        Assert.DoesNotContain(Moderation, faces);
    }

    [Fact]
    public void Ningun_consumidor_recibe_las_TRES_caras()
    {
        // Si algún día uno las pide todas, el corte dejó de pagar y hay que rehacerlo o
        // revertirlo — pero conscientemente, no por goteo.
        var consumers = new[]
        {
            typeof(BlogsController), typeof(CommentsController),
            typeof(CommentsModerationController), typeof(AdminController),
            typeof(DevContentFiller),
        };

        var greedy = consumers.Where(c => FacesTakenBy(c).Count == AllFaces.Length).ToList();

        Assert.Empty(greedy);
    }

    [Fact]
    public void No_quedo_una_interfaz_compuesta_por_la_puerta_de_atras()
    {
        // Un `ICommentRepository : ICommentReader, ICommentWriter, ICommentModeration` volvería
        // a permitir pedirlo todo con un solo parámetro, y el corte sería decorativo.
        var composite = typeof(ICommentReader).Assembly
            .GetTypes()
            .Where(t => t.IsInterface)
            .Where(t => AllFaces.Count(f => f.IsAssignableFrom(t) && f != t) > 1)
            .ToList();

        Assert.Empty(composite);
    }

    [Fact]
    public void Una_sola_instancia_sirve_las_tres_caras()
    {
        // Tres AddSingleton<I, Impl>() darían TRES repositorios sobre el mismo directorio, y el
        // lock de escritura de cada uno no sabría de los otros dos. Se fija que la impl es una
        // sola clase, que es lo que permite registrarla por su tipo concreto y reenviar.
        var impl = typeof(FileSystemCommentRepository);

        Assert.True(Reader.IsAssignableFrom(impl));
        Assert.True(Writer.IsAssignableFrom(impl));
        Assert.True(Moderation.IsAssignableFrom(impl));
    }
}
