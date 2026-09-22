using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Una variable <c>Umbraco__CMS__…</c> de un compose es configuración de Umbraco, y también tiene
/// que estar donde el schema del vendedor la declara (#159).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> El #138 encontró que <c>Umbraco:CMS:Global:UmbracoApplicationUrl</c> vive
/// en <c>WebRouting</c> —<c>Global</c> tiene 28 propiedades y ninguna es ésa— y arregló los dos
/// <c>appsettings</c>. Su gate lee <c>appsettings*.json</c> y nada más, así que los <b>dos
/// compose</b> siguieron escribiendo la clave en la sección equivocada: en producción la URL
/// pública que <c>compose.prod.yml</c> pasaba se descartaba en silencio y quedaba la del perfil,
/// <c>http://localhost:8080/</c>, con el <c>KeepAliveJob</c> diciendo «No umbracoApplicationUrl for
/// service (yet), skip» cada sesenta segundos — el mismo síntoma que el #138 documentó, vivo en el
/// artefacto que su gate no podía leer.</para>
///
/// <para><b>Y en <c>docker-compose.yml</c> el comentario de arriba de la línea explicaba el daño
/// que la línea no evitaba</b>: «sin esto, el backoffice y los links de notificación apuntan a
/// localhost, que desde la tablet es la tablet misma y no resuelve nada». La clave estaba, con su
/// valor correcto, en la sección que nadie lee.</para>
///
/// <para><b>El criterio NO se reescribe:</b> el cruce por ruta vive una vez, en
/// <c>ClavesDeUmbracoTests.PorQueNoLaDeclara</c>, y lo consumen los dos gates. Una clave de un
/// bloque <c>environment:</c> es la misma configuración por otro transporte
/// (<c>feedback_the_same_algorithm_is_not_the_same_thing</c>), y con dos implementaciones el día
/// que una se afine la otra miente.</para>
///
/// <para><b>Y que tiene que ser por RUTA está medido ACÁ, no sólo citado del #138.</b> Con el
/// defecto puesto y el cruce hecho por <i>pertenencia del nombre</i> —recolectar los nombres que el
/// schema conoce y comprobar que el de la hoja esté— este gate pasa en <b>VERDE</b>:
/// <c>UmbracoApplicationUrl</c> sí es una propiedad que el schema declara, sólo que de otro padre.
/// El primer intento de esa mutación salió rojo <i>por la razón equivocada</i> (cruzar sólo la hoja
/// contra la raíz del schema falla también con la sección correcta), y un rojo que no prueba lo que
/// uno cree es peor que no mutar: hubo que rehacerla.</para>
/// </remarks>
public sealed class ClavesDeUmbracoEnComposeTests
{
    private static IReadOnlyList<string> Composes()
        => new[] { "compose.prod.yml", "docker-compose.yml" }
            .Select(f => Proyectos.Ruta(f))
            .ToList();

    [Fact]
    public void Toda_clave_de_Umbraco_de_un_compose_esta_donde_el_schema_la_declara()
    {
        var malas = new List<string>();
        var cruzadas = 0;

        foreach (var compose in Composes())
        {
            Assert.True(File.Exists(compose), $"No existe {compose}: revisar este gate.");
            var nombre = Path.GetFileName(compose);

            foreach (var clave in ClavesDeUmbraco(compose))
            {
                cruzadas++;
                var problema = ClavesDeUmbracoTests.PorQueNoLaDeclara(clave.Split("__"));
                if (problema is not null) malas.Add($"{nombre} → {clave}   ✗ {problema}");
            }
        }

        // Red de seguridad: si el parseo deja de ver, «ninguna está mal» y el gate pasa en verde
        // sobre el vacío — el fallo silencioso que este repo ya tiene escrito cinco veces.
        //
        // El umbral va DEBAJO de lo medido (hoy 6: la URL pública y las dos de la credencial
        // desatendida, en los dos compose) y no encima. Puesto en 8 «por si crecen», la red salta
        // antes que la comprobación y la mutación no llega nunca al cruce que se quiere probar —
        // que es exactamente lo que le pasó a este gate en su primera corrida.
        Assert.True(cruzadas >= 4,
            $"Sólo se vieron {cruzadas} claves `Umbraco__*` en los compose. El parseo no está "
            + "leyendo los bloques `environment:`, así que este gate no mira nada.");

        Assert.True(malas.Count == 0,
            "Estas variables de un compose ponen configuración de Umbraco en una sección que el "
            + "schema del vendedor NO declara:\n  " + string.Join("\n  ", malas)
            + "\n\nEl binder de .NET descarta en SILENCIO lo que no mapea, así que el valor no "
            + "llega y el servidor se comporta como uno sin configurar: un servidor bien "
            + "configurado con el síntoma de uno mal. Es el #138 en el artefacto que su gate no "
            + "leía (#159).");
    }

    /// <summary>
    /// Las claves <c>Umbraco__*</c> de los bloques <c>environment:</c> de un compose.
    /// </summary>
    /// <remarks>
    /// <para>Los comentarios se descartan antes de parsear, y <b>hoy no cambia el resultado</b> —
    /// medido quitando esa línea: el gate sigue verde—. No es la protección que parece: el regex
    /// está <b>anclado</b> al principio de la línea recortada, y un comentario de YAML empieza por
    /// <c>#</c>, así que no puede casar. Se queda como seguro barato para el día que alguien
    /// afloje el ancla.</para>
    /// <para>Donde ese descarte SÍ es load-bearing es en un gate que busque una <b>subcadena en
    /// cualquier parte</b> del fichero —<c>TransitoriedadTests</c>, o el gate de cableado del
    /// <c>.editorconfig</c> del #151—: ahí la explicación del defecto nombra lo prohibido y el gate
    /// se dispara con su propia prosa
    /// (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>). Decirlo al revés sería
    /// documentación que afirma de más, que es peor que no decir nada.</para>
    /// </remarks>
    private static IEnumerable<string> ClavesDeUmbraco(string compose)
    {
        var sangriaDelBloque = -1;

        foreach (var cruda in File.ReadAllLines(compose))
        {
            var linea = cruda.TrimStart();
            if (linea.Length == 0 || linea.StartsWith('#')) continue;

            var sangria = cruda.Length - linea.Length;

            if (linea.StartsWith("environment:", StringComparison.Ordinal))
            {
                sangriaDelBloque = sangria;
                continue;
            }

            if (sangriaDelBloque < 0 || sangria <= sangriaDelBloque)
            {
                sangriaDelBloque = -1;
                continue;
            }

            var m = Regex.Match(linea, @"^(Umbraco(?:__[A-Za-z0-9_]+)+):");
            if (m.Success) yield return m.Groups[1].Value;
        }
    }
}
