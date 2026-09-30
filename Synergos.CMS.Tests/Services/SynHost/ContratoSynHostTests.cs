using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// El contrato de lo que viaja a cada elemento migrado (ADR 0135): los records
/// <c>[ElementoSynHost]</c> y <c>docs/contracts/elementos-synhost.json</c> son lo mismo, campo a
/// campo — y el fichero lleva, por elemento, el <c>config</c> EXACTO que emite su vista.
/// </summary>
/// <remarks>
/// <para><b>Es la mitad C# de la cadena que cierra D1.</b> El record declara lo que viaja; este
/// gate lo proyecta al JSON de la superficie de acople; del lado del UI,
/// <c>tools/contrato-synhost.mjs --check</c> exige que el tipo TS generado de ese JSON esté al
/// día, el sanitizador de cada elemento se tipa con él (leer una clave que no viaja no compila) y
/// un spec lo EJECUTA con el <c>ejemplo</c> de acá (una clave que viaja y nadie lee se pone roja).
/// El CI del CMS no clona al hermano, así que el cruce entre repos va por un fichero versionado,
/// como <c>mortgage-vectors.json</c> (#167).</para>
///
/// <para><b>El ejemplo no se escribe: se EMITE.</b> Por cada elemento, una muestra de lo que un
/// editor autoraría pasa por el resolver registrado, <see cref="SolicitudSynHost"/> y el
/// <see cref="DefaultSynHostEmitter"/> reales, y se lee el <c>config='…'</c> del tag. Es lo que
/// el navegador recibe, <c>culture</c> incluida — la prueba que encontró D1 era exactamente
/// ejecutar el sanitizador con eso.</para>
///
/// <para><b>Para regenerar</b> tras cambiar un record o una muestra:
/// <c>SYNERGOS_ACTUALIZAR_CONTRATOS=1 dotnet test Synergos.CMS.Tests --filter ContratoSynHost</c>,
/// y el diff va en el commit que lo causó; después, en el UI,
/// <c>node tools/contrato-synhost.mjs</c>.</para>
/// </remarks>
public sealed class ContratoSynHostTests
{
    private const string VariableParaActualizar = "SYNERGOS_ACTUALIZAR_CONTRATOS";

    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");

    /// <summary>
    /// Lo que un editor autoraría en cada bloque, por alias del ElementType. Una por record, y hay
    /// gate: un record sin muestra no tiene ejemplo, y sin ejemplo el UI no puede ejecutar su
    /// sanitizador contra lo que la vista emite.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Alias, object? Valor)[]> Muestras =
        new Dictionary<string, (string, object?)[]>(StringComparer.Ordinal)
        {
            ["carousel"] = new (string, object?)[]
            {
                ("slidesJson", """[{"imageUrl":"/media/sala.jpg","alt":"Sala con ventanal","caption":"La sala"},{"imageUrl":"/media/cocina.jpg","alt":"Cocina integral","caption":"La cocina"}]"""),
                ("autoplayInterval", "4000"),
            },
            ["dropdown"] = new (string, object?)[]
            {
                ("triggerLabel", "País"),
                ("optionsJson", """[{"value":"co","label":"Colombia"},{"value":"mx","label":"México","href":"/mx"}]"""),
                ("selectedValue", "co"),
                ("searchable", true),
            },
            ["kpi-card"] = new (string, object?)[]
            {
                ("kpiLabel", "Ventas del mes"),
                ("kpiValue", "1.234"),
                ("kpiTrend", "up"),
                ("kpiDelta", "+12 %"),
                ("kpiPeriod", "vs. agosto"),
            },
            ["rating-stars"] = new (string, object?)[]
            {
                ("valueNow", "4"),
                ("maxStars", "5"),
                ("ariaLabel", "Valoración de los huéspedes"),
            },
            ["tag"] = new (string, object?)[]
            {
                ("tagLabel", "Oferta"),
                ("tagColor", "success"),
            },
        };

    private static readonly JsonSerializerOptions Fichero = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
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

    private static string RutaDelContrato()
        => Path.Combine(RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "elementos-synhost.json");

    /// <summary>Los records que viajan: todo tipo de Interfaces con <see cref="ElementoSynHostAttribute"/>.</summary>
    private static IReadOnlyList<Type> Records()
        => typeof(ElementoSynHostAttribute).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<ElementoSynHostAttribute>() is not null)
            .OrderBy(t => t.GetCustomAttribute<ElementoSynHostAttribute>()!.Nombre, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Cada_record_tiene_muestra_y_cada_muestra_tiene_record()
    {
        var records = Records().Select(r => SolicitudSynHost.Elemento(r).Nombre).ToList();

        // Red de seguridad por el vacío: si el descubrimiento se rompe y devuelve cero, el
        // contrato se regeneraría vacío y todo lo de abajo compararía nada contra nada.
        Assert.NotEmpty(records);
        Assert.Equal(Muestras.Keys.OrderBy(k => k, StringComparer.Ordinal), records);
    }

    /// <summary>
    /// La muestra de cada elemento hace viajar TODOS los campos del record, los de sus listas
    /// incluidos.
    /// </summary>
    /// <remarks>
    /// El UI ejecuta el sanitizador con el ejemplo y exige que cada clave mueva la salida; una
    /// clave que la muestra no hace viajar no la mira nadie, y un sanitizador podría tirarla en
    /// verde. Por eso la cobertura se exige acá, que es donde la muestra se escribe.
    /// </remarks>
    [Fact]
    public async Task La_muestra_de_cada_elemento_hace_viajar_todos_los_campos_del_record()
    {
        var sinViajar = new List<string>();

        foreach (var record in Records())
        {
            var elemento = SolicitudSynHost.Elemento(record);
            var ejemplo = await Ejemplo(record, Muestras[elemento.Nombre]);

            foreach (var propiedad in SolicitudSynHost.Cable.GetTypeInfo(record).Properties)
            {
                if (!ejemplo.TryGetProperty(propiedad.Name, out var valor))
                {
                    sinViajar.Add($"{elemento.Nombre}.{propiedad.Name}");
                    continue;
                }

                var item = ElementoDeLista(propiedad.PropertyType);
                if (item is null || valor.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                sinViajar.AddRange(SolicitudSynHost.Cable.GetTypeInfo(item).Properties
                    .Where(p => !valor.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p.Name, out _)))
                    .Select(p => $"{elemento.Nombre}.{propiedad.Name}[].{p.Name}"));
            }
        }

        Assert.True(sinViajar.Count == 0,
            "La muestra no hace viajar estos campos, así que ningún gate mira si el elemento los lee: "
            + string.Join(", ", sinViajar));
    }

    /// <summary>El record de los ítems de una lista del record, o <c>null</c> si no es una lista de records.</summary>
    private static Type? ElementoDeLista(Type tipo)
    {
        var item = tipo.IsGenericType && tipo.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            ? tipo.GenericTypeArguments[0]
            : null;
        return item is { IsClass: true } && item != typeof(string) ? item : null;
    }

    [Fact]
    public void Cada_record_tiene_exactamente_un_resolver_registrado()
    {
        var servicios = new ServiceCollection().AddResolutoresSynHost();

        foreach (var record in Records())
        {
            var servicio = typeof(IResolutorSynHost<>).MakeGenericType(record);
            var cuantos = servicios.Count(d => d.ServiceType == servicio);
            Assert.True(cuantos == 1,
                $"{record.Name} tiene {cuantos} resolver(es) registrado(s). La vista lo pide con @inject: "
                + "con cero, la página contesta 500 la primera vez que alguien la pide —ningún build "
                + "compila las vistas—; con dos, gana el último en silencio.");
        }
    }

    [Fact]
    public void Todo_campo_declara_si_es_contenido_o_decision()
    {
        var sinOrigen = Records()
            .SelectMany(r => SolicitudSynHost.Cable.GetTypeInfo(r).Properties
                .Where(p => (p.AttributeProvider as PropertyInfo)?.GetCustomAttribute<CampoSynHostAttribute>() is null)
                .Select(p => $"{r.Name}.{p.Name}"))
            .ToList();

        Assert.True(sinOrigen.Count == 0,
            "Estos campos no declaran [CampoSynHost(Contenido|Decision)]: " + string.Join(", ", sinOrigen)
            + ". Es la clasificación que la fábrica lee para saber qué dato pide un elemento.");
    }

    [Fact]
    public void Las_secciones_de_diccionario_que_declara_un_record_existen_en_uSync()
    {
        var alias = Directory.EnumerateFiles(
                Path.Combine(RepoRoot(), "Synergos.CMS.Web", "uSync", "v9", "Dictionary"), "*.config")
            .Select(f => Regex.Match(File.ReadAllText(f), "<Dictionary [^>]*Alias=\"([^\"]+)\"").Groups[1].Value)
            .Where(a => a.Length > 0)
            .ToList();

        Assert.True(alias.Count > 100, $"Se leyeron {alias.Count} claves de diccionario: el descubrimiento está roto.");

        var huerfanas = Records()
            .SelectMany(r => SolicitudSynHost.Elemento(r).Diccionario.Select(s => (Record: r.Name, Seccion: s)))
            .Where(x => !alias.Any(a => a.StartsWith(x.Seccion + ".", StringComparison.Ordinal)))
            .Select(x => $"{x.Record} → {x.Seccion}")
            .ToList();

        Assert.True(huerfanas.Count == 0,
            "Secciones declaradas sin ninguna clave en uSync/v9/Dictionary: " + string.Join(", ", huerfanas)
            + ". Una sección que no existe sale siempre por el texto de respaldo y parece traducida.");
    }

    /// <summary>
    /// La vista de un elemento migrado pide su resolver, y ninguna otra lo emite con un
    /// diccionario libre.
    /// </summary>
    /// <remarks>
    /// Es lo que hace que «una vista que emite una clave que su record no declara» no pueda
    /// existir: la vista ya no escribe claves. Si alguien vuelve a armar un
    /// <c>SynHostEmitRequest</c> a mano con el mismo <c>BlockAlias</c>, el elemento tendría dos
    /// caminos de emisión y uno sin contrato — que es D1 esperando.
    /// </remarks>
    [Fact]
    public void La_vista_de_cada_elemento_migrado_resuelve_y_emite_sin_diccionario_libre()
    {
        var vistas = Directory.EnumerateFiles(
                Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Select(f => (Ruta: Path.GetRelativePath(RepoRoot(), f), Texto: File.ReadAllText(f)))
            .ToList();

        Assert.True(vistas.Count > 100, $"Se leyeron {vistas.Count} vistas: el descubrimiento está roto.");

        var malas = new List<string>();
        foreach (var record in Records())
        {
            var nombre = SolicitudSynHost.Elemento(record).Nombre;
            var conResolver = vistas
                .Where(v => Regex.IsMatch(v.Texto, @"@inject\s+[\w.]*IResolutorSynHost<[\w.]*\b" + record.Name + ">"))
                .ToList();
            var libres = vistas
                .Where(v => Regex.IsMatch(v.Texto, "BlockAlias:\\s*\"" + Regex.Escape(nombre) + "\""))
                .ToList();

            if (conResolver.Count != 1)
            {
                malas.Add($"{nombre}: {conResolver.Count} vista(s) inyectan IResolutorSynHost<{record.Name}> (debe ser 1).");
            }

            malas.AddRange(libres.Select(v => $"{nombre}: {v.Ruta} arma a mano un SynHostEmitRequest con su BlockAlias."));
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public async Task El_contrato_de_docs_contracts_es_el_que_sale_de_los_records()
    {
        var esperado = (await Derivar()).ReplaceLineEndings("\n");
        var ruta = RutaDelContrato();

        if (Environment.GetEnvironmentVariable(VariableParaActualizar) == "1")
        {
            await File.WriteAllTextAsync(ruta, esperado, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        Assert.True(File.Exists(ruta),
            $"No existe {ruta}. Generalo con {VariableParaActualizar}=1 dotnet test --filter ContratoSynHost.");

        var enDisco = (await File.ReadAllTextAsync(ruta)).ReplaceLineEndings("\n");
        if (string.Equals(enDisco, esperado, StringComparison.Ordinal))
        {
            return;
        }

        var lineasEsperadas = esperado.Split('\n');
        var lineasEnDisco = enDisco.Split('\n');
        var primera = Enumerable.Range(0, Math.Max(lineasEsperadas.Length, lineasEnDisco.Length))
            .First(i => i >= lineasEsperadas.Length || i >= lineasEnDisco.Length
                     || !string.Equals(lineasEsperadas[i], lineasEnDisco[i], StringComparison.Ordinal));

        Assert.Fail(
            "docs/contracts/elementos-synhost.json no es lo que declaran los records. Un record cambió "
            + "(o una muestra) y el contrato que lee el UI se quedó atrás: el tipo TS que se genera de "
            + "él seguiría describiendo el cable viejo."
            + Environment.NewLine
            + $"Primera diferencia, línea {primera + 1}:"
            + Environment.NewLine
            + $"  esperado: {(primera < lineasEsperadas.Length ? lineasEsperadas[primera] : "(fin)")}"
            + Environment.NewLine
            + $"  en disco: {(primera < lineasEnDisco.Length ? lineasEnDisco[primera] : "(fin)")}"
            + Environment.NewLine
            + $"Regeneralo con {VariableParaActualizar}=1 dotnet test Synergos.CMS.Tests --filter ContratoSynHost "
            + "y corré después `node tools/contrato-synhost.mjs` en el UI.");
    }

    // ── La derivación ────────────────────────────────────────────────────────────────────────

    private static async Task<string> Derivar()
    {
        var tipos = new SortedDictionary<string, IReadOnlyList<CampoDelContrato>>(StringComparer.Ordinal);
        var elementos = new List<ElementoDelContrato>();

        foreach (var record in Records())
        {
            var elemento = SolicitudSynHost.Elemento(record);
            elementos.Add(new ElementoDelContrato(
                Nombre: elemento.Nombre,
                Tipo: elemento.Tipo == TipoDeColocable.Pieza ? "pieza" : "funcionalidad",
                Record: record.Name,
                Diccionario: elemento.Diccionario,
                Campos: Campos(record, tipos, conOrigen: true),
                Ejemplo: await Ejemplo(record, Muestras[elemento.Nombre])));
        }

        var contrato = new ContratoDelFichero(
            Comentario: "GENERADO desde los records [ElementoSynHost] de Synergos.CMS.Interfaces por "
                + "Synergos.CMS.Tests/Services/SynHost/ContratoSynHostTests.cs — no se edita a mano (ADR 0135). "
                + "`ejemplo` es el config EXACTO que emite la vista para una muestra autorada.",
            Elementos: elementos,
            Tipos: tipos.Select(t => new TipoDelContrato(t.Key, t.Value)).ToList());

        return JsonSerializer.Serialize(contrato, Fichero) + "\n";
    }

    private static IReadOnlyList<CampoDelContrato> Campos(
        Type record,
        SortedDictionary<string, IReadOnlyList<CampoDelContrato>> tipos,
        bool conOrigen)
    {
        var nulabilidad = new NullabilityInfoContext();
        var campos = new List<CampoDelContrato>();

        foreach (var propiedad in SolicitudSynHost.Cable.GetTypeInfo(record).Properties)
        {
            var info = propiedad.AttributeProvider as PropertyInfo
                ?? throw new InvalidOperationException($"{record.Name}.{propiedad.Name} no es una propiedad.");

            var opcional = Nullable.GetUnderlyingType(info.PropertyType) is not null
                || nulabilidad.Create(info).ReadState == NullabilityState.Nullable;

            var origen = conOrigen
                ? info.GetCustomAttribute<CampoSynHostAttribute>()?.Origen switch
                {
                    OrigenDelCampo.Contenido => "contenido",
                    OrigenDelCampo.Decision => "decision",
                    _ => "SIN-ORIGEN",
                }
                : null;

            campos.Add(new CampoDelContrato(propiedad.Name, TipoTs(info.PropertyType, tipos), opcional, origen));
        }

        return campos;
    }

    /// <summary>El tipo del cable, con la gramática que el generador TS del UI entiende.</summary>
    private static string TipoTs(Type tipo, SortedDictionary<string, IReadOnlyList<CampoDelContrato>> tipos)
    {
        tipo = Nullable.GetUnderlyingType(tipo) ?? tipo;

        if (tipo == typeof(string)) return "string";
        if (tipo == typeof(bool)) return "boolean";
        if (tipo == typeof(int) || tipo == typeof(long) || tipo == typeof(decimal) || tipo == typeof(double)) return "number";

        var elementoDeLista = tipo.IsArray
            ? tipo.GetElementType()
            : tipo.IsGenericType && tipo.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                ? tipo.GenericTypeArguments[0]
                : null;
        if (elementoDeLista is not null)
        {
            return TipoTs(elementoDeLista, tipos) + "[]";
        }

        if (tipo.IsClass && tipo.Namespace == typeof(ElementoSynHostAttribute).Namespace)
        {
            if (!tipos.ContainsKey(tipo.Name))
            {
                tipos[tipo.Name] = Array.Empty<CampoDelContrato>();
                tipos[tipo.Name] = Campos(tipo, tipos, conOrigen: false);
            }

            return tipo.Name;
        }

        throw new InvalidOperationException(
            $"{tipo.Name} no tiene traducción al contrato: sólo string, boolean, number, listas y records "
            + "del mismo espacio de nombres. Añadirla acá Y en el generador del UI.");
    }

    /// <summary>
    /// El <c>config</c> que emite la vista para <paramref name="muestra"/>: resolver registrado →
    /// <see cref="SolicitudSynHost"/> → <see cref="DefaultSynHostEmitter"/>, leído del tag.
    /// </summary>
    private static async Task<JsonElement> Ejemplo(Type record, (string Alias, object? Valor)[] muestra)
    {
        using var proveedor = new ServiceCollection()
            .AddSingleton(ElementoFalso.Fallback)
            .AddSingleton(ElementoFalso.Diccionario())
            .AddSingleton(ElementoFalso.Urls())
            .AddLogging()
            .AddResolutoresSynHost()
            .BuildServiceProvider();

        var resolutor = proveedor.GetRequiredService(typeof(IResolutorSynHost<>).MakeGenericType(record));
        var resuelto = resolutor.GetType().GetMethod(nameof(IResolutorSynHost<object>.Resolver))!
            .Invoke(resolutor, new object[] { ElementoFalso.Con(muestra) })!;

        var solicitud = (SynHostEmitRequest)typeof(SolicitudSynHost).GetMethod(nameof(SolicitudSynHost.Para))!
            .MakeGenericMethod(record)
            .Invoke(null, new object?[] { resuelto, null, EsCo })!;

        var emitido = await new DefaultSynHostEmitter(Substitute.For<IBundleRegistryClient>()).EmitAsync(solicitud);
        return SolicitudSynHostTests.ConfigEmitido(emitido.ElementHtml);
    }

    private sealed record ContratoDelFichero(
        string Comentario,
        IReadOnlyList<ElementoDelContrato> Elementos,
        IReadOnlyList<TipoDelContrato> Tipos);

    private sealed record ElementoDelContrato(
        string Nombre,
        string Tipo,
        string Record,
        IReadOnlyList<string> Diccionario,
        IReadOnlyList<CampoDelContrato> Campos,
        JsonElement Ejemplo);

    private sealed record CampoDelContrato(string Nombre, string Tipo, bool Opcional, string? Origen);

    private sealed record TipoDelContrato(string Record, IReadOnlyList<CampoDelContrato> Campos);
}
