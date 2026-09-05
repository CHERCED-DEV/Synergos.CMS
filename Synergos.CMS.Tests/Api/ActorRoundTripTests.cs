using System.Text.Json;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Que un <see cref="Actor"/> guardado se pueda volver a leer (defecto #82).
/// </summary>
/// <remarks>
/// <para><b>La clave de estos tests es que fuerzan la lectura DESDE DISCO.</b>
/// <see cref="JsonCollectionStore{T}"/> cachea en memoria, así que escribir y leer contra la misma
/// instancia nunca deserializa: da en el caché y pasa en verde con el defecto puesto. Es
/// exactamente por lo que esto vivió sin que nadie lo viera. Se abre un almacén <b>nuevo</b> sobre
/// el mismo fichero, que es lo que hace un reinicio.</para>
/// </remarks>
public sealed class ActorRoundTripTests : IDisposable
{
    private readonly string _raiz = Path.Combine(
        Path.GetTempPath(), "syn-actor-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* el temporal no es el sujeto del test */ }
    }

    private sealed record Asiento(string Id, Actor Actor, string Action);

    private JsonCollectionStore<Asiento> Almacen() => new(_raiz, "asientos", a => a.Id);

    // ── Lo que reventaba ────────────────────────────────────────────────────

    /// <summary>
    /// Un asiento escrito se lee después de «reiniciar».
    /// </summary>
    /// <remarks>
    /// Sin el conversor esto lanza <c>NotSupportedException</c> —«the collection type
    /// IReadOnlySet&lt;string&gt; is abstract... and could not be instantiated»— y en la capacidad
    /// eso sale por HTTP como un 500 en toda lectura. Los datos estaban en disco, íntegros, y la
    /// única puerta para consultarlos estaba cerrada.
    /// </remarks>
    [Fact]
    public void Un_actor_guardado_se_lee_despues_de_reiniciar()
    {
        var actor = Actor.Of(Ref.Create("cms.actor", "abc"), "funcionario", "revisor");
        Almacen().Put(new Asiento("e1", actor, "gov.case.decide"));

        // Almacén NUEVO sobre el mismo fichero: esto es lo que hace un reinicio. Leer del MISMO
        // da en el caché y pasa en verde con el defecto puesto — comprobado mutando.
        var leido = Almacen().All().Single();

        Assert.Equal("cms.actor", leido.Actor.Principal.Kind);
        Assert.Equal("abc", leido.Actor.Principal.Id);
        Assert.Equal(new[] { "funcionario", "revisor" }, leido.Actor.Roles.OrderBy(r => r, StringComparer.Ordinal));
    }

    /// <summary>
    /// Y los roles siguen siendo insensibles a mayúsculas.
    /// </summary>
    /// <remarks>
    /// <para>Es el defecto de detrás, y el peligroso. <see cref="Actor.Of"/> arma el conjunto con
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>; un arreglo que sólo cambiara el tipo por
    /// algo deserializable devolvería un conjunto con el comparador por defecto, y
    /// <c>HasAnyRole("Funcionario")</c> pasaría a ser <c>false</c> después de reiniciar.</para>
    ///
    /// <para>Eso cambia un 500 ruidoso por una decisión de permisos equivocada y <b>callada</b>,
    /// que es peor que el defecto que se venía a arreglar.</para>
    /// </remarks>
    [Fact]
    public void Los_roles_siguen_sin_distinguir_mayusculas_tras_reiniciar()
    {
        Almacen().Put(new Asiento("e1", Actor.Of(Ref.Create("cms.actor", "abc"), "funcionario"), "x"));

        var leido = Almacen().All().Single();

        Assert.True(leido.Actor.HasAnyRole("Funcionario"));
        Assert.True(leido.Actor.HasAnyRole("FUNCIONARIO"));
        Assert.False(leido.Actor.HasAnyRole("otro"));
    }

    /// <summary>Un actor sin roles vuelve sin roles, no roto.</summary>
    [Fact]
    public void Un_actor_sin_roles_vuelve_entero()
    {
        Almacen().Put(new Asiento("e1", Actor.Of(Ref.Create("system", "cron")), "x"));

        var leido = Almacen().All().Single();

        Assert.Empty(leido.Actor.Roles);
        Assert.Equal("cron", leido.Actor.Principal.Id);
    }

    /// <summary>
    /// <c>isAnonymous</c> se DERIVA al leer, no se cree.
    /// </summary>
    /// <remarks>
    /// Es una propiedad calculada que el serializador escribía. Leerla del fichero dejaría que uno
    /// editado a mano dijera que un actor con nombre y apellido es anónimo — y en una bitácora eso
    /// es justo lo que alguien querría escribir.
    /// </remarks>
    [Fact]
    public void Lo_anonimo_se_deriva_del_principal_y_no_del_fichero()
    {
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new ActorJsonConverter() },
        };

        var mentira = """{"principal":{"kind":"cms.actor","id":"abc"},"roles":[],"isAnonymous":true}""";
        var leido = JsonSerializer.Deserialize<Actor>(mentira, opciones);

        Assert.NotNull(leido);
        Assert.False(leido!.IsAnonymous);
    }

    /// <summary>Un principal a medias no se guarda como actor a medias: se rechaza.</summary>
    /// <remarks>
    /// Un actor sin identificador no contesta «¿quién hizo esto?», que es la única pregunta que se
    /// le hace a una bitácora. Devolverlo vacío lo escondería.
    /// </remarks>
    [Fact]
    public void Un_principal_a_medias_no_se_lee_en_silencio()
    {
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new ActorJsonConverter() },
        };

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Actor>("""{"principal":{"kind":"cms.actor"},"roles":[]}""", opciones));
    }
}
