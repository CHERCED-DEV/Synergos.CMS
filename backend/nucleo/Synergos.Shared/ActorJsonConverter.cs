using System.Text.Json;
using System.Text.Json.Serialization;
using Synergos.Core;

namespace Synergos.Shared;

/// <summary>
/// Cómo va y vuelve un <see cref="Actor"/> del disco (defecto #82).
/// </summary>
/// <remarks>
/// <para><b>Sin esto, lo que se guarda no se puede leer.</b> <c>Actor.Roles</c> es un
/// <c>IReadOnlySet&lt;string&gt;</c>, y <c>System.Text.Json</c> lo <i>escribe</i> sin problema pero
/// no sabe leerlo: no puede instanciar una interfaz de conjunto. La bitácora guardaba sus asientos
/// perfectamente y devolvía 500 en toda lectura en cuanto el proceso se reiniciaba — blindada
/// contra reescribir el pasado, y sin poder leerlo.</para>
///
/// <para><b>Y reconstruye por <see cref="Actor.Of"/>, no por el constructor.</b> Ése es el punto
/// fino: <c>Of</c> arma el conjunto con <see cref="StringComparer.OrdinalIgnoreCase"/>. Un
/// conversor que se limitara a devolver un <c>HashSet</c> daría uno con el comparador por defecto,
/// y <c>HasAnyRole("Funcionario")</c> pasaría a ser <c>false</c> después de reiniciar sin que nada
/// fallara. Eso cambia un 500 ruidoso por una decisión de permisos equivocada y callada, que es
/// peor que el defecto que se venía a arreglar.</para>
///
/// <para><b>Vive acá y no en <c>Synergos.Core</c></b>: Core es el vocabulario y no sabe qué es un
/// disco (<c>CLAUDE.md</c> §2). Cómo se escribe ese vocabulario es fontanería, y
/// <see cref="JsonCollectionStore{T}"/> es el embudo por el que persisten las veinte — arreglarlo
/// dentro de una capacidad dejaría la misma mina puesta para la siguiente.</para>
/// </remarks>
public sealed class ActorJsonConverter : JsonConverter<Actor>
{
    public override Actor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Un actor se lee de un objeto.");
        }

        string? kind = null;
        string? id = null;
        var roles = new List<string>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var propiedad = reader.GetString();
            reader.Read();

            // Se comparan sin distinguir mayúsculas porque las opciones del almacén son `Web`
            // (camelCase) y un fichero escrito con otras no debería volverse ilegible.
            if (string.Equals(propiedad, "principal", StringComparison.OrdinalIgnoreCase))
            {
                LeerPrincipal(ref reader, out kind, out id);
            }
            else if (string.Equals(propiedad, "roles", StringComparison.OrdinalIgnoreCase))
            {
                LeerRoles(ref reader, roles);
            }
            else
            {
                // `isAnonymous` es una propiedad CALCULADA: se escribe y no se lee, porque se
                // deriva del principal. Guardarla y creerla dejaría que un fichero editado a mano
                // dijera que un actor con nombre es anónimo.
                reader.Skip();
            }
        }

        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id))
        {
            throw new JsonException("Un actor necesita principal con tipo e identificador.");
        }

        return Actor.Of(Ref.Create(kind!, id!), roles.ToArray());
    }

    private static void LeerPrincipal(ref Utf8JsonReader reader, out string? kind, out string? id)
    {
        kind = null;
        id = null;
        if (reader.TokenType != JsonTokenType.StartObject) { reader.Skip(); return; }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var propiedad = reader.GetString();
            reader.Read();

            if (string.Equals(propiedad, "kind", StringComparison.OrdinalIgnoreCase)) kind = reader.GetString();
            else if (string.Equals(propiedad, "id", StringComparison.OrdinalIgnoreCase)) id = reader.GetString();
            else reader.Skip();
        }
    }

    private static void LeerRoles(ref Utf8JsonReader reader, List<string> roles)
    {
        if (reader.TokenType != JsonTokenType.StartArray) { reader.Skip(); return; }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var rol = reader.GetString();
                if (!string.IsNullOrWhiteSpace(rol)) roles.Add(rol!);
            }
        }
    }

    public override void Write(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();

        writer.WritePropertyName("principal");
        writer.WriteStartObject();
        writer.WriteString("kind", value.Principal.Kind);
        writer.WriteString("id", value.Principal.Id);
        writer.WriteEndObject();

        writer.WritePropertyName("roles");
        writer.WriteStartArray();
        foreach (var rol in value.Roles)
        {
            writer.WriteStringValue(rol);
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }
}
