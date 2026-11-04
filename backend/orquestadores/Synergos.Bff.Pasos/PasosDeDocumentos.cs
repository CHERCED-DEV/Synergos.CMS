using System.Text.Json;
using System.Text.Json.Nodes;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Pasos;

// ── Lo que se firma y lo que se guarda (núcleo, NN-24 y NN-25 de 003) ──────────
// La nota firmada, la receta y el resultado del laboratorio con su PDF: sellar un contenido con la llave del consultorio
// y guardar un documento de su dueño. Ninguno sabe de Salud.

/// <summary>
/// <c>signing.firmar</c>: sella lo que lee con la llave que lee —la del consultorio, que declara el negocio (003-Q14)— y
/// escribe el sello y, si la definición lo nombra, la llave que lo produjo (NN-24 de 003).
/// </summary>
/// <remarks>
/// <para><b>Sella (<c>/v1/seals</c>) y no firma un token (<c>/v1/signatures</c>)</b>, por lo mismo que el diploma (#45) y el
/// contrato de Alquiler: aquel token vence, no es determinista y publica su contenido sin llave. Una nota o una receta firmada
/// se consulta años después, y el mismo contenido tiene que dar el mismo sello. Comprobarlo es <c>/v1/seals/verify</c> con el
/// contenido al lado.</para>
///
/// <para><b>No pide llave de idempotencia</b>: sellar es una función de la llave y el contenido, y no escribe nada en la
/// capacidad. Sin llave vigente para ese consultorio, el no de la capacidad tal cual (<c>signing.no_active_key</c>).</para>
/// </remarks>
public sealed class PasoFirmaSellar(ISigningPort firmas) : IPaso
{
    public string Tipo => "signing.firmar";
    public int Lecturas => 2;
    public int Escrituras => 2;
    public int EscriturasOpcionales => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string), typeof(string)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var sellado = await firmas.SellarAsync(entrada.Lee<string>(0), entrada.Lee<string>(1), ct);
        return sellado.IsOk ? SalidaDePaso.Sigue(sellado.Value.Sello, sellado.Value.LlaveId) : SalidaDePaso.Rechaza(sellado.Rejection!);
    }
}

/// <summary>
/// <c>documents.guardar</c>: guarda el <c>documento</c> que lee en <c>Api.Documents</c>, como de su dueño, y escribe su id
/// (NN-25 de 003). Reserva el documento: una definición que lo declare con <c>antes: "documents.borrar"</c> lo borra si la
/// saga se deshace antes de consumirlo.
/// </summary>
/// <remarks>
/// <para><b>De quién es el documento.</b> En un flujo del dueño, del dueño de la saga: nunca de alguien que se lee, que
/// dejaría guardar papeles a nombre de otro con un cuerpo armado a mano. En un flujo del personal —el consultorio que carga
/// el resultado de un paciente—, de quien la definición lee en segundo lugar, porque la puerta ya comprobó el rol de quien
/// actúa. Cualquier otra combinación se rechaza (<c>documents.titular_requerido</c>).</para>
///
/// <para><b>El contenido no vive en la saga.</b> Llega con el tipo <c>documento</c>, que no se puede guardar: el validador no
/// deja arrancar un flujo que lo saque, lo deje para lo posterior o lo lea en otra fase. El PDF con datos clínicos va de la
/// petición a la capacidad y en ningún otro sitio se escribe.</para>
/// </remarks>
public sealed class PasoDocumentoGuardar(IDocumentsPort documentos) : IPaso
{
    public const string SinTitular = "documents.titular_requerido";

    public string Tipo => "documents.guardar";
    public int Lecturas => 2;
    public int LecturasOpcionales => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (Dueno(entrada) is not { } dueno)
        {
            return SalidaDePaso.Rechaza(Rejection.Forbidden(SinTitular,
                $"«{Tipo}» guarda el documento del dueño de la saga o, en un flujo del personal, del que nombra la definición; "
                + "aquí no hay uno que valga."));
        }

        var guardado = await documentos.GuardarAsync(dueno, entrada.Lee<DocumentoAdjunto>(0), entrada.Llave(), ct);
        return guardado.IsOk ? SalidaDePaso.Reserva(new Reservado(guardado.Value), guardado.Value) : SalidaDePaso.Rechaza(guardado.Rejection!);
    }

    private static Ref? Dueno(EntradaDePaso entrada)
    {
        var nombraOtro = entrada.Paso.Lee.Count > 1;
        return entrada.Flujo is { EsDePersonal: true }
            ? nombraOtro ? entrada.Lee<Ref>(1) : null
            : nombraOtro ? null : entrada.Dueno;
    }
}

/// <summary>Deshace <c>documents.guardar</c>: borra el documento que guardó (NN-25 de 003).</summary>
public sealed class DeshacerDocumento(IDeshacerDocumentos documentos) : IDeshaceUnTipo
{
    public string Tipo => TiposDeDeshacer.BorrarDocumento;

    public async Task<Rejection?> DeshacerAsync(SagaDeFlujo saga, Compensation pendiente, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pendiente);
        var borrado = await documentos.BorrarAsync(pendiente.TargetId, ct);
        return borrado.IsOk ? null : borrado.Rejection;
    }
}

/// <summary>
/// <c>documento</c>: un fichero que llega a la fase que lo guarda, <c>{ nombre, tipo, contenidoBase64 }</c>. NO se guarda
/// en la saga (NN-25 de 003): puede llevar datos clínicos.
/// </summary>
public sealed class TipoDocumento : ITipoDeValor
{
    private static readonly string[] Campos = ["nombre", "tipo", "contenidoBase64"];

    public string Nombre => "documento";
    public Type Clr => typeof(DocumentoAdjunto);
    public bool Guardable => false;

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{donde}: tiene que ser un objeto {{ {string.Join(", ", Campos)} }}.");
        }
        foreach (var p in valor.EnumerateObject().Where(p => !Campos.Contains(p.Name, StringComparer.Ordinal)))
        {
            throw new FormatException($"{donde}: trae «{p.Name}», y sólo admite {string.Join(", ", Campos)}.");
        }

        var nombre = Texto(valor, "nombre");
        var tipo = Texto(valor, "tipo");
        var contenido = Texto(valor, "contenidoBase64");
        var bytes = new byte[contenido.Length];
        if (!Convert.TryFromBase64String(contenido, bytes, out var leidos) || leidos == 0)
        {
            throw new FormatException($"{donde}.contenidoBase64: no es base64 de un contenido.");
        }
        return new DocumentoAdjunto(nombre, tipo, contenido);

        string Texto(JsonElement o, string campo)
            => o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()!.Trim()
                : throw new FormatException($"{donde}.{campo}: hace falta, y es un texto.");
    }

    public JsonNode Escribir(object valor)
    {
        var d = (DocumentoAdjunto)valor;
        return new JsonObject { ["nombre"] = d.Nombre, ["tipo"] = d.Tipo, ["contenidoBase64"] = d.ContenidoBase64 };
    }

    public JsonObject Esquema() => new()
    {
        ["type"] = "object",
        ["required"] = new JsonArray(Campos.Select(c => (JsonNode?)c).ToArray()),
        ["properties"] = new JsonObject(Campos.Select(c => KeyValuePair.Create(c, (JsonNode?)new JsonObject { ["type"] = "string" }))),
    };
}
