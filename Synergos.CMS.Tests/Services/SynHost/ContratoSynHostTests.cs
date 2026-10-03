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
using Synergos.CMS.Web.Services.Listados;
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
/// <para><b>Y trae lo que el editor puede ELEGIR</b> (#181): por cada selector del ElementType
/// (desplegable, radios, casillas), dónde cae en el <c>config</c> y qué viaja de cada prevalor de
/// uSync después del resolver REAL. El UI lo cruza contra lo que su sanitizador acepta, en las dos
/// direcciones (<c>contrato-synhost.spec.ts</c>, el gate de vocabulario). Por eso agregar un
/// prevalor a un DataType también deja este fichero desactualizado.</para>
///
/// <para><b>Para regenerar</b> tras cambiar un record, una muestra o un selector:
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
            // La configuración de negocio no es del editor: sale de NegocioBase (ADR 0137).
            ["eventos"] = new (string, object?)[]
            {
                ("heading", "Vive los mejores eventos"),
                ("subheading", "Conciertos, teatro y festivales"),
                ("role", "organizer"),
            },
            ["realty"] = new (string, object?)[]
            {
                ("heading", "Encuentra el lugar que estás buscando"),
                ("subheading", "Compra y arriendo, en lista y en mapa"),
            },
            ["academy"] = new (string, object?)[]
            {
                ("heading", "Aprende lo que el mercado pide"),
                ("subheading", "Catálogo, lecciones e instructores a tu ritmo"),
            },
            ["blogs"] = new (string, object?)[]
            {
                ("heading", "Conecta, publica y crece tu audiencia"),
                ("subheading", "Sigue autores, publica historias y reacciona en tiempo real"),
            },
            ["booking-wizard"] = new (string, object?)[]
            {
                ("destinationLabel", "Hoteles SynergosLabs"),
            },
            // La identidad no es del editor: el resolver pone el paciente de demo (#197).
            ["ehr"] = Array.Empty<(string, object?)>(),
            ["gov"] = new (string, object?)[]
            {
                ("heading", "Tus trámites, sin filas"),
                ("subheading", "Radica, paga la tasa y sigue tu expediente"),
            },
            ["seller"] = new (string, object?)[]
            {
                ("heading", "Tu negocio, en un solo panel"),
                ("subheading", "Ventas, publicaciones, mensajes y devoluciones"),
            },
            ["storefront"] = new (string, object?)[]
            {
                ("heading", "Compra en nuestra tienda online"),
                ("subheading", "Catálogo, carrito y checkout"),
            },
            ["travel-shell"] = new (string, object?)[]
            {
                ("heading", "Reserva tu próximo viaje"),
                ("subheading", "Vuelos, hoteles y paquetes"),
            },
            // Un listado cuyas filas arma el servidor (#196, tanda D): la fuente se elige, las filas
            // salen de ella (en el test, FuentesDeMuestra).
            ["data-grid"] = new (string, object?)[]
            {
                ("fuente", "fichas"),
            },
            ["search-box"] = new (string, object?)[]
            {
                ("searchPlaceholder", "Buscar cursos por tema o nivel…"),
                ("submitToPage", true),
            },
            // Un formulario del modelo de Forms por pasos (#196, tanda D): cada campo de un paso
            // lleva todo su vocabulario, para que el UI vea viajar cada clave a cualquier profundidad.
            ["form-stepper"] = new (string, object?)[]
            {
                ("formInternalKey", "reserva-cita"),
                ("steps", ElementoFalso.Lista(
                    ElementoFalso.Con(
                        ("stepTitle", "Tu reserva"),
                        ("stepDescription", "Elige el servicio y la fecha."),
                        ("fields", ElementoFalso.Lista(
                            ElementoFalso.Con(
                                ("fieldName", "servicio"),
                                ("fieldLabel", "Servicio"),
                                ("fieldType", "select"),
                                ("fieldRequired", true),
                                ("fieldPlaceholder", "Elige uno"),
                                ("fieldHelpText", "Puedes cambiarlo después."),
                                ("fieldOptions", new[] { "Asesoría express", "Auditorio" }))))))),
                ("allowSkip", true),
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
            ["scroll-top"] = new (string, object?)[]
            {
                ("scrollThreshold", "400"),
                ("position", "bottom-left"),
                ("ariaLabel", "Subir al inicio"),
            },
            ["range-slider"] = new (string, object?)[]
            {
                ("label", "Precio por noche"),
                ("minValue", "50000"),
                ("maxValue", "500000"),
                ("step", "10000"),
                ("initialValue", "250000"),
            },
            ["select-multi"] = new (string, object?)[]
            {
                ("label", "Amenidades"),
                ("optionsJson", """[{"value":"piscina","label":"Piscina"},{"value":"gym","label":"Gimnasio"},{"value":"bbq","label":"Zona BBQ"}]"""),
                ("maxSelections", "2"),
            },
            ["stepper"] = new (string, object?)[]
            {
                ("stepsJson", """[{"label":"Datos"},{"label":"Pago"},{"label":"Confirmación"}]"""),
                ("currentStep", "1"),
            },
            ["tabs"] = new (string, object?)[]
            {
                ("tabsJson", """[{"id":"resumen","label":"Resumen","content":"Lo esencial de la estadía."},{"id":"precios","label":"Precios","content":"Desde $120.000 por noche."}]"""),
                ("initialTab", "precios"),
            },
            ["timeline"] = new (string, object?)[]
            {
                ("eventsJson", """[{"date":"2019-03-01","title":"Fundación","description":"Abrimos la primera sede en Medellín."},{"date":"2024","title":"Segunda sede","description":"Llegamos a Bogotá."}]"""),
            },
            ["tour-guide"] = new (string, object?)[]
            {
                ("stepsJson", """[{"selector":".site-header","title":"Bienvenido","content":"Este es el menú principal."},{"selector":"#buscar","title":"Buscá","content":"Encontrá cualquier cosa desde acá."}]"""),
                ("autoStart", true),
            },
            ["tree-view"] = new (string, object?)[]
            {
                ("treeJson", """[{"label":"Productos","children":[{"label":"Hogar","children":[{"label":"Cocina"}]},{"label":"Jardín"}]},{"label":"Servicios"}]"""),
                ("expandAll", true),
                ("ariaLabel", "Catálogo de la tienda"),
            },
            ["accordion"] = new (string, object?)[]
            {
                ("itemsJson", """[{"title":"¿Cuánto tarda el envío?","content":"Entre 2 y 5 días hábiles en ciudades principales."},{"title":"¿Puedo devolver un producto?","content":"Sí, dentro de los 30 días siguientes a la entrega."}]"""),
                ("allowMultiple", true),
            },
            ["badge-group"] = new (string, object?)[]
            {
                ("badgesJson", """[{"label":"Envío gratis","color":"success","iconKey":"truck"},{"label":"Nuevo","color":"brand","iconKey":"sparkles"}]"""),
                ("layout", "stack"),
            },
            ["breadcrumb"] = new (string, object?)[]
            {
                ("itemsJson", """[{"label":"Inicio","url":"/"},{"label":"Tienda","url":"/tienda"},{"label":"Zapatos"}]"""),
                ("includeStructuredData", true),
            },
            ["color-swatches"] = new (string, object?)[]
            {
                ("swatchesJson", """[{"hex":"#1e3a8a","name":"Azul noche"},{"hex":"#f97316","name":"Naranja"}]"""),
                ("shape", "circle"),
            },
            ["icon-label"] = new (string, object?)[]
            {
                ("iconKey", "check"),
                ("labelText", "Envío gratis a todo el país"),
            },
            ["notification-toast"] = new (string, object?)[]
            {
                ("message", "Tu pedido quedó confirmado."),
                ("type", "success"),
                ("durationMs", "8000"),
            },
            ["progress-bar"] = new (string, object?)[]
            {
                ("valueNow", "3"),
                ("valueMax", "5"),
                ("ariaLabel", "Pasos completados del registro"),
            },
            ["audio-player"] = new (string, object?)[]
            {
                ("audioFile", ElementoFalso.Medio("/media/podcast/episodio-12.mp3")),
                ("trackTitle", "Episodio 12: la ciudad que camina"),
                ("artistName", "Radio Synergos"),
            },
            ["avatar"] = new (string, object?)[]
            {
                ("avatarImage", ElementoFalso.Medio("/media/equipo/ana-gomez.jpg", "Ana Gómez, directora de producto")),
            },
            ["video-player"] = new (string, object?)[]
            {
                ("videoFile", ElementoFalso.Medio("/media/propiedades/recorrido-casa-lago.mp4")),
                ("posterImage", ElementoFalso.Medio("/media/propiedades/casa-lago-fachada.jpg", "Fachada de la casa del lago")),
                ("chaptersJson", """[{"startSeconds":0,"title":"Llegada"},{"startSeconds":42,"title":"La sala"}]"""),
                ("enableAnalytics", true),
            },
            ["hero-banner"] = new (string, object?)[]
            {
                ("title", "Viví el Caribe colombiano"),
                ("subtitle", "Temporada 2026: vuelos y hoteles con el 20 % de descuento"),
                ("media", ElementoFalso.Medio("/media/hero/playa-palomino.jpg", "Playa de Palomino al atardecer")),
                ("ctaLabel", "Reservar ahora"),
                ("ctaLink", ElementoFalso.Enlace("/reservas", "Reservas")),
            },
            ["fab"] = new (string, object?)[]
            {
                ("iconKey", "whatsapp"),
                ("actionLink", ElementoFalso.Enlace("https://wa.me/573001234567", "WhatsApp", "_blank")),
                ("position", "bottom-left"),
                ("ariaLabel", "Escribinos por WhatsApp"),
            },
            ["cookie-consent"] = new (string, object?)[]
            {
                ("bannerText", "Usamos cookies propias y de terceros para medir el uso del sitio."),
                ("acceptLabel", "Acepto todas"),
                ("rejectLabel", "Sólo las necesarias"),
                ("settingsLabel", "Elegir cuáles"),
                ("policyLink", ElementoFalso.Enlace("/privacidad", "Política de privacidad")),
            },
            ["share-bar"] = new (string, object?)[]
            {
                ("platforms", new[] { "whatsapp", "twitter", "linkedin" }),
                ("shareLink", ElementoFalso.Enlace("https://synergos.local/eventos/feria-del-libro-2026")),
                ("shareTitle", "Feria del libro 2026: programa completo"),
            },
            ["rich-tooltip"] = new (string, object?)[]
            {
                ("triggerText", "Cuota de manejo"),
                ("tooltipContent", new Umbraco.Cms.Core.Strings.HtmlEncodedString(
                    "<p>Cobro <strong>mensual</strong> por administrar la tarjeta.</p><p>Se exonera con compras desde $&nbsp;300.000.</p>")),
                ("placement", "bottom-start"),
            },
            ["countdown-clock"] = new (string, object?)[]
            {
                ("endDateTime", "2030-12-31T23:59:59-05:00"),
                ("labelFormat", "Quedan {days} días y {hours} horas"),
            },
            ["countdown-digital"] = new (string, object?)[]
            {
                ("endDateTime", "2030-12-31T23:59:59-05:00"),
                ("hideLabels", true),
                ("style", "digits"),
            },
            ["avatar-group"] = new (string, object?)[]
            {
                ("avatarsJson", """[{"url":"/media/equipo/ana-gomez.jpg","name":"Ana Gómez","role":"Directora"},{"url":"/media/equipo/luis-pardo.jpg","name":"Luis Pardo","role":"CTO"},{"name":"Marta Ruiz"}]"""),
                ("maxVisible", "2"),
                ("ariaLabel", "Equipo directivo"),
            },
            ["lightbox-gallery"] = new (string, object?)[]
            {
                ("imagesJson", """[{"thumbUrl":"/media/casa/sala-t.jpg","fullUrl":"/media/casa/sala.jpg","alt":"Sala con ventanal","caption":"La sala"},{"thumbUrl":"/media/casa/cocina-t.jpg","fullUrl":"/media/casa/cocina.jpg","alt":"Cocina integral","caption":"La cocina"}]"""),
                ("columns", "2"),
            },
            ["chart-bar"] = new (string, object?)[]
            {
                ("chartTitle", "Afiliados nuevos por trimestre"),
                ("dataJson", """[{"label":"T1","value":1200},{"label":"T2","value":"1.845.300"},{"label":"T3","value":"950,5"}]"""),
                ("orientation", "horizontal"),
            },
            ["map-pin"] = new (string, object?)[]
            {
                ("centerLat", "4,7110"),
                ("centerLng", "-74.0721"),
                ("zoomLevel", "12"),
                ("pinsJson", """[{"lat":4.6097,"lng":-74.0817,"title":"Oficina Bogotá","description":"Carrera 7 # 71-21, piso 12"},{"lat":"6,2518","lng":"-75.5636","title":"Oficina Medellín","description":"El Poblado"}]"""),
            },
            ["color-picker"] = new (string, object?)[]
            {
                ("label", "Color de acento de tu tienda"),
                ("initialColor", "#0f766e"),
                ("paletteJson", """["#0f766e","#b45309","#7c3aed","#be123c"]"""),
            },
            ["app-launcher"] = new (string, object?)[]
            {
                ("heading", "Explora las apps"),
                ("subheading", "Un motor, muchos productos"),
                ("apps", """[{"id":"tienda","name":"Tienda","tagline":"Catálogo, carrito y checkout.","icon":"bag","status":"live","industry":"Retail","persona":"Comprador","capabilities":["Catálogo","Pagos"],"url":"/tienda","demoMode":"deeplink"},{"id":"gobierno","name":"Gobierno","status":"soon","industry":"Sector público","persona":"Ciudadano","capabilities":"Trámites, Citas","url":"/gobierno","demoMode":"embed"}]"""),
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
            "Estos campos no declaran [CampoSynHost(Contenido|Decision|Negocio|Sesion)]: " + string.Join(", ", sinOrigen)
            + ". Es la clasificación que la fábrica lee para saber qué dato pide un elemento.");
    }

    /// <summary>
    /// Sólo una funcionalidad recibe configuración de negocio (ADR 0134, ADR 0137): una pieza recibe
    /// decisiones del editor.
    /// </summary>
    /// <remarks>
    /// Una pieza con un campo de negocio sería la comisión de un sitio entrando por un botón: la
    /// regla de la funcionalidad repartida por la página, sin que nadie la pueda cambiar en un sitio.
    /// </remarks>
    [Fact]
    public void Un_campo_de_negocio_solo_lo_lleva_una_funcionalidad()
    {
        var deNegocio = Records()
            .SelectMany(r => SolicitudSynHost.Cable.GetTypeInfo(r).Properties
                .Where(p => (p.AttributeProvider as PropertyInfo)?.GetCustomAttribute<CampoSynHostAttribute>()?.Origen
                    == OrigenDelCampo.Negocio)
                .Select(p => (Record: r, Campo: p.Name)))
            .ToList();

        // Sin piso, un descubrimiento roto pasaría en verde: eventos es el piloto y los lleva.
        Assert.Contains(deNegocio, c => c.Record == typeof(EventosProps));

        var enPiezas = deNegocio
            .Where(c => SolicitudSynHost.Elemento(c.Record).Tipo != TipoDeColocable.Funcionalidad)
            .Select(c => $"{c.Record.Name}.{c.Campo}")
            .ToList();

        Assert.True(enPiezas.Count == 0,
            "Estas piezas llevan configuración de negocio, que es sólo de las funcionalidades: "
            + string.Join(", ", enPiezas) + ". Si es una decisión del editor, es Decision.");
    }

    /// <summary>
    /// Lo que el editor podía escribir antes en una funcionalidad —la base de la API, el JSON libre,
    /// la moneda— sigue guardado en el contenido de las bases que ya existen, y no viaja: lo de
    /// negocio lo fija el sitio (ADR 0137, escala #196).
    /// </summary>
    /// <remarks>
    /// Cada resolver corre con su muestra y otra vez con esas claves VIEJAS encima, con valores que
    /// no son los del sitio: si una vista o un resolver volviera a leer alguna, el <c>config</c>
    /// emitido cambiaría. Las funcionalidades se descubren, no se listan.
    /// </remarks>
    [Fact]
    public async Task Lo_que_el_editor_escribia_antes_en_una_funcionalidad_no_viaja()
    {
        var viejas = new (string Alias, object? Valor)[]
        {
            ("apiBase", "/otra/api"),
            ("currency", "USD"),
            ("config", """{"apiBase":"/otra/api","currency":"USD","feePercent":99}"""),
        };
        var funcionalidades = Records()
            .Where(r => SolicitudSynHost.Elemento(r).Tipo == TipoDeColocable.Funcionalidad)
            .ToList();

        // Sin piso, un descubrimiento roto pasaría en verde: eventos es el piloto.
        Assert.Contains(typeof(EventosProps), funcionalidades);

        var leen = new List<string>();
        foreach (var record in funcionalidades)
        {
            var nombre = SolicitudSynHost.Elemento(record).Nombre;
            var muestra = Muestras[nombre];
            var conViejas = muestra.Concat(viejas.Where(v => muestra.All(m => m.Alias != v.Alias))).ToArray();

            var antes = (await Ejemplo(record, muestra)).GetRawText();
            var despues = (await Ejemplo(record, conViejas)).GetRawText();
            if (antes != despues)
            {
                leen.Add($"{nombre}: {despues}");
            }
        }

        Assert.True(leen.Count == 0,
            "Estas funcionalidades hacen viajar lo que el editor escribía antes en el bloque, que ya no es "
            + "suyo: " + string.Join("; ", leen));
    }

    /// <summary>
    /// Ninguna funcionalidad le ofrece al editor <c>compIntegration</c>: el JSON libre y el modo de
    /// integración no viajan a una funcionalidad (ADR 0135 §6, <c>SolicitudSynHost.Para</c>), y lo
    /// de negocio lo fija el sitio (ADR 0137, cambio 6).
    /// </summary>
    /// <remarks>
    /// Un campo que el servidor descarta y el editor sigue viendo es una mentira del backoffice: se
    /// escribe, se guarda y no hace nada. Las funcionalidades se descubren de los records y su
    /// ElementType se sigue desde la vista, como en el gate de vocabulario.
    /// </remarks>
    [Fact]
    public void Ninguna_funcionalidad_le_ofrece_al_editor_compIntegration()
    {
        var repo = RepoRoot();
        var funcionalidades = Records()
            .Where(r => SolicitudSynHost.Elemento(r).Tipo == TipoDeColocable.Funcionalidad)
            .ToList();

        // Sin piso, un descubrimiento roto pasaría en verde: eventos es el piloto.
        Assert.Contains(typeof(EventosProps), funcionalidades);

        var conIntegracion = funcionalidades
            .SelectMany(r => SelectoresDeUSync.ElementTypesDe(repo, r).Select(et => (Record: r, ElementType: et)))
            .Where(p => SelectoresDeUSync.Composiciones(repo, p.ElementType).Contains("compIntegration"))
            .Select(p => $"{SolicitudSynHost.Elemento(p.Record).Nombre} ({p.ElementType})")
            .ToList();

        Assert.True(conIntegracion.Count == 0,
            "Estas funcionalidades le siguen ofreciendo al editor compIntegration, que el servidor descarta: "
            + string.Join(", ", conIntegracion) + ". Quitar la composición del ElementType (medir antes el contenido).");
    }

    /// <summary>
    /// La mitad CMS del gate de vocabulario (#181) encuentra lo que tiene que cruzar: el
    /// ElementType de cada record, y selectores en ellos.
    /// </summary>
    /// <remarks>
    /// <para>Es la red de seguridad por el vacío. Los selectores del contrato se DERIVAN —de la vista
    /// que inyecta el resolver, de los componentes del Block Grid que la llaman y de los DataTypes
    /// de uSync—, y si cualquiera de esos pasos deja de encontrar algo el contrato se regeneraría
    /// sin selectores y el gate del UI cruzaría nada contra nada, en verde.</para>
    ///
    /// <para>El piso es «ninguno sin ElementType» y «más de cero selectores», no la cifra de hoy:
    /// una cifra exacta acá se pondría roja al quitar una opción del schema, que es justo lo que el
    /// gate pide que se pueda hacer. Lo que se pierde a medias lo ve el JSON versionado (deja de ser
    /// el que sale de los records) y, del otro lado, la línea base del UI.</para>
    /// </remarks>
    [Fact]
    public void Cada_record_encuentra_su_ElementType_y_sus_selectores_no_son_cero()
    {
        var repo = RepoRoot();
        var porRecord = Records().ToDictionary(r => r.Name, r => SelectoresDeUSync.ElementTypesDe(repo, r));

        var sinElementType = porRecord.Where(p => p.Value.Count == 0).Select(p => p.Key).ToList();
        Assert.True(sinElementType.Count == 0,
            "Ningún componente de blockgrid/Components llama a la vista SynHost de: " + string.Join(", ", sinElementType)
            + ". Sin ElementType no se sabe qué selectores ve el editor, y el gate de vocabulario no cruzaría nada.");

        var propios = porRecord.Values.SelectMany(ets => ets).SelectMany(et => SelectoresDeUSync.De(repo, et)).Count(s => s.Propia);
        Assert.True(propios > 0,
            "Los ElementTypes de los elementos migrados no tienen ningún selector propio: la lectura de uSync está rota "
            + $"(se buscan los editores {string.Join(", ", SelectoresDeUSync.EditoresDeSeleccion)}).");
    }

    /// <remarks>
    /// <para>Es el gate «un prefijo declarado que no casa ninguna clave → rojo» de la ADR 0136 §4.
    /// Casa con la MISMA regla con la que el bridge publica
    /// (<see cref="Synergos.CMS.Web.Services.Diccionario.DiccionarioDelBridge.EnAlgunaSeccion"/>) y
    /// sólo contra claves CON texto: una sección cuyos únicos ítems son contenedores no publicaría
    /// nada. Antes del piloto #186 la lista fija del bridge tenía tres así —<c>Comments.</c>,
    /// <c>Cart.</c>, <c>Account.</c>— y nada lo veía.</para>
    /// </remarks>
    [Fact]
    public void Las_secciones_de_diccionario_que_declara_un_record_existen_en_uSync()
    {
        var alias = ClavesDeUSync();

        Assert.True(alias.Count > 100, $"Se leyeron {alias.Count} claves de diccionario: el descubrimiento está roto.");

        var huerfanas = Records()
            .SelectMany(r => SolicitudSynHost.Elemento(r).Diccionario.Select(s => (Record: r.Name, Seccion: s)))
            .Where(x => !alias.Any(a => Synergos.CMS.Web.Services.Diccionario.DiccionarioDelBridge.EnAlgunaSeccion(a, [x.Seccion])))
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
            // Las dos formas de armar la solicitud a mano: con el nombre del parámetro
            // (`BlockAlias: "x"`) y POSICIONAL (`new SynHostEmitRequest("x", …)`). Sólo se miraba
            // la primera, y así estaban escritas countdown-digital y rich-tooltip: una segunda
            // vista que emitiera a mano un elemento migrado pasaba en verde (#180).
            var libres = vistas
                .Where(v => Regex.IsMatch(v.Texto, "BlockAlias:\\s*\"" + Regex.Escape(nombre) + "\"")
                         || Regex.IsMatch(v.Texto, "SynHostEmitRequest\\(\\s*\"" + Regex.Escape(nombre) + "\""))
                .ToList();

            if (conResolver.Count != 1)
            {
                malas.Add($"{nombre}: {conResolver.Count} vista(s) inyectan IResolutorSynHost<{record.Name}> (debe ser 1).");
            }

            malas.AddRange(libres.Select(v => $"{nombre}: {v.Ruta} arma a mano un SynHostEmitRequest con su nombre."));
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
            var selectores = await Selectores(record, Muestras[elemento.Nombre]);
            elementos.Add(new ElementoDelContrato(
                Nombre: elemento.Nombre,
                Tipo: elemento.Tipo == TipoDeColocable.Pieza ? "pieza" : "funcionalidad",
                Record: record.Name,
                Diccionario: elemento.Diccionario,
                Claves: ClavesDe(elemento.Diccionario),
                Campos: Campos(record, tipos, conOrigen: true),
                Selectores: selectores.Count > 0 ? selectores : null,
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

    /// <summary>
    /// Las claves de uSync que caen en <paramref name="secciones"/> —con la misma regla con la que
    /// el bridge las publica (<see cref="Synergos.CMS.Web.Services.Diccionario.DiccionarioDelBridge.EnAlgunaSeccion"/>)—,
    /// sin los contenedores, ordenadas; <c>null</c> si el elemento no declara secciones.
    /// </summary>
    /// <remarks>
    /// Van en el contrato para que el UI compruebe, sin el CMS al lado, que cada <c>t('clave')</c>
    /// de un elemento existe y cae en una sección que el elemento declara (ADR 0136 §4): una clave
    /// fuera de ellas no se publica en la página, y el elemento pintaría siempre su respaldo con
    /// cara de traducido.
    /// </remarks>
    private static IReadOnlyList<string>? ClavesDe(IReadOnlyList<string> secciones)
        => secciones.Count == 0
            ? null
            : ClavesDeUSync()
                .Where(a => Synergos.CMS.Web.Services.Diccionario.DiccionarioDelBridge.EnAlgunaSeccion(a, secciones))
                .Order(StringComparer.Ordinal)
                .ToList();

    /// <summary>Los alias de uSync/v9/Dictionary que tienen al menos una traducción (no los contenedores).</summary>
    private static IReadOnlyList<string> ClavesDeUSync()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "uSync", "v9", "Dictionary"), "*.config")
            .Select(File.ReadAllText)
            .Where(x => x.Contains("<Translation ", StringComparison.Ordinal))
            .Select(x => Regex.Match(x, "<Dictionary [^>]*Alias=\"([^\"]+)\"").Groups[1].Value)
            .Where(a => a.Length > 0)
            .ToList();

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
                    OrigenDelCampo.Negocio => "negocio",
                    OrigenDelCampo.Sesion => "sesion",
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
        using var proveedor = Proveedor();
        return await Emitido(proveedor, record, muestra);
    }

    private static ServiceProvider Proveedor()
        => FuentesDeMuestra(ConNegocioBase(new ServiceCollection()))
            .AddSingleton(ElementoFalso.Fallback)
            .AddSingleton(ElementoFalso.Diccionario())
            .AddSingleton(ElementoFalso.Urls())
            .AddLogging()
            .AddResolutoresSynHost()
            .BuildServiceProvider();

    /// <summary>
    /// Las fuentes de los listados (#196, tanda D), todas con la MISMA fila de muestra, que lleva
    /// cada campo de la fila. Iguales a propósito: elegir una fuente u otra elige DATOS, no
    /// vocabulario del elemento, así que el sondeo del selector <c>fuente</c> no debe ver cambios
    /// en el cable. Las fuentes reales se prueban aparte.
    /// </summary>
    private static IServiceCollection FuentesDeMuestra(IServiceCollection servicios)
    {
        foreach (var clave in new[] { "fichas", "cursos", "eventos", "inmuebles" })
        {
            servicios.AddSingleton<IFuenteDeListado>(new FuenteDeMuestra(clave));
        }

        return servicios;
    }

    private sealed class FuenteDeMuestra(string clave) : IFuenteDeListado
    {
        public string Clave => clave;

        public IReadOnlyList<FilaDelListado> Filas(PeticionDelListado peticion) =>
        [
            new FilaDelListado(
                Id: "ficha-1",
                Title: "Asesoría express",
                Href: "/booking/servicios/asesoria-express/",
                Image: "/media/asesoria.jpg",
                ImageAlt: "Asesoría express",
                Badge: "Consultoría",
                Specs: [new DatoDeLaFila(peticion.Rotulo("DataGrid.Price", "Precio"), "$ 180.000")]),
        ];
    }

    /// <summary>
    /// La configuración de negocio con los valores base de CADA sección de negocio que exista, que es
    /// lo que rige en un sitio sin override: el ejemplo del contrato muestra lo que llega por defecto.
    /// </summary>
    /// <remarks>
    /// Se descubren por la forma (<c>SeccionDeNegocio&lt;,&gt;</c>) y no se listan: una funcionalidad
    /// que gana su sección (#196) no tiene que tocar este test para que su ejemplo se emita.
    /// </remarks>
    private static IServiceCollection ConNegocioBase(IServiceCollection servicios)
    {
        var secciones = typeof(Synergos.CMS.Application.Configuration.SeccionDeNegocio<,>).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.BaseType is { IsGenericType: true } b
                && b.GetGenericTypeDefinition() == typeof(Synergos.CMS.Application.Configuration.SeccionDeNegocio<,>));

        foreach (var seccion in secciones)
        {
            var negocio = seccion.BaseType!.GenericTypeArguments[1];
            var valores = seccion.GetMethod("Para")!.Invoke(Activator.CreateInstance(seccion), new object?[] { null })!;
            servicios.AddSingleton(
                typeof(INegocioDelSitio<>).MakeGenericType(negocio),
                Activator.CreateInstance(typeof(NegocioFijo<>).MakeGenericType(negocio), valores)!);
        }

        return servicios;
    }

    private sealed class NegocioFijo<T>(T valores) : INegocioDelSitio<T>
        where T : class
    {
        public T Actual() => valores;
    }

    private static async Task<JsonElement> Emitido(IServiceProvider proveedor, Type record, (string Alias, object? Valor)[] muestra)
    {
        var resolutor = proveedor.GetRequiredService(typeof(IResolutorSynHost<>).MakeGenericType(record));
        var resuelto = resolutor.GetType().GetMethod(nameof(IResolutorSynHost<object>.Resolver))!
            .Invoke(resolutor, new object[] { ElementoFalso.Con(muestra) })!;

        var solicitud = (SynHostEmitRequest)typeof(SolicitudSynHost).GetMethod(nameof(SolicitudSynHost.Para))!
            .MakeGenericMethod(record)
            .Invoke(null, new object?[] { resuelto, null, EsCo })!;

        var emitido = await new DefaultSynHostEmitter(Substitute.For<IBundleRegistryClient>()).EmitAsync(solicitud);
        return SolicitudSynHostTests.ConfigEmitido(emitido.ElementHtml);
    }

    // ── Los selectores: lo que el editor elige, pasado por el resolver (#181) ──────────────────

    /// <summary>
    /// Por cada selector del ElementType de <paramref name="record"/>, dónde cae en el <c>config</c>
    /// y qué viaja de cada prevalor — con el resolver y el emitter REALES, no con una tabla.
    /// </summary>
    /// <remarks>
    /// <para><b>Por qué se ejecuta el resolver y no se lee.</b> El resolver traduce
    /// (<c>twitter</c> → <c>x</c>, <c>bottom-start</c> → <c>bottom</c>, <c>digits</c> →
    /// <c>plain</c>), filtra (<c>kpiTrend</c> sólo deja pasar tres) y a veces mete el valor DENTRO
    /// de una lista (el tipo del aviso cae en <c>toasts[].variant</c>). Lo que el elemento tiene que
    /// saber pintar es lo que sale de ahí, no lo que dice uSync: cruzar los prevalores crudos
    /// acusaría a <c>share-bar</c> de no pintar <c>twitter</c>.</para>
    ///
    /// <para><b>Cómo.</b> Sobre la muestra del elemento, la propiedad sin valor y después con cada
    /// prevalor, solo (como lista de uno si el selector es múltiple). La ruta del <c>config</c> que
    /// cambia es el <c>campo</c>; lo que hay ahí con cada prevalor, lo que <c>viaja</c>. Un selector
    /// que no mueve nada es uno que el editor ve y el elemento nunca recibe: se escribe con
    /// <c>campo: null</c> si es del ElementType, y se omite si es de una composición
    /// (<c>compDomVariant</c>, <c>compDomSpacing</c>… los lee el envoltorio, no el elemento).</para>
    ///
    /// <para>Un selector que mueve DOS rutas no tiene una lectura única, y el gate del UI no sabría
    /// qué cruzar: lanza en vez de escoger una.</para>
    /// </remarks>
    private static async Task<IReadOnlyList<SelectorDelContrato>> Selectores(Type record, (string Alias, object? Valor)[] muestra)
    {
        var repo = RepoRoot();
        using var proveedor = Proveedor();
        var resultado = new List<SelectorDelContrato>();
        var leidas = PropiedadesQueLee(proveedor, record, muestra);
        var deDatos = SolicitudSynHost.Elemento(record).SelectoresDeDatos.ToHashSet(StringComparer.Ordinal);
        var deDatosVistos = new HashSet<string>(StringComparer.Ordinal);

        foreach (var elementType in SelectoresDeUSync.ElementTypesDe(repo, record))
        {
            foreach (var selector in SelectoresDeUSync.De(repo, elementType))
            {
                // Un selector que elige DATOS (la fuente de un listado) no es vocabulario: cambiarlo
                // cambia las filas enteras. No se sondea y viaja sin campo (#196, tanda D).
                if (deDatos.Contains(selector.Propiedad))
                {
                    Assert.True(leidas.Contains(selector.Propiedad),
                        $"{record.Name}: declara «{selector.Propiedad}» como selector de datos y su resolver no lo lee.");
                    deDatosVistos.Add(selector.Propiedad);
                    resultado.Add(new SelectorDelContrato(
                        selector.Propiedad, selector.DataType, selector.Multiple, null,
                        selector.Prevalores.Select(p => new ValorDelSelector(p, null)).ToList(),
                        DeDatos: true));
                    continue;
                }

                // Lo que el resolver ni siquiera pide no puede viajar: no hace falta sondearlo. Es
                // también lo que hace barata la derivación —las composiciones traen cinco
                // selectores a cada elemento y ningún resolver los lee—.
                if (!leidas.Contains(selector.Propiedad))
                {
                    if (selector.Propia)
                    {
                        resultado.Add(new SelectorDelContrato(
                            selector.Propiedad, selector.DataType, selector.Multiple, null,
                            selector.Prevalores.Select(p => new ValorDelSelector(p, null)).ToList()));
                    }

                    continue;
                }

                var sinValor = Hojas(await Emitido(proveedor, record, Con(muestra, selector.Propiedad, null)));
                var sondas = new List<(string Editor, ILookup<string, string> Hojas)>();
                foreach (var prevalor in selector.Prevalores)
                {
                    object valor = selector.Multiple ? new[] { prevalor } : prevalor;
                    sondas.Add((prevalor, Hojas(await Emitido(proveedor, record, Con(muestra, selector.Propiedad, valor)))));
                }

                var rutas = sondas.SelectMany(s => Cambios(sinValor, s.Hojas)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                if (rutas.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"{record.Name}: el selector «{selector.Propiedad}» de {elementType} mueve {rutas.Count} rutas del config "
                        + $"({string.Join(", ", rutas)}). El gate de vocabulario (#181) cruza UNA; partilo o extendé el gate.");
                }

                if (rutas.Count == 0 && !selector.Propia)
                {
                    continue;
                }

                var campo = rutas.SingleOrDefault();
                resultado.Add(new SelectorDelContrato(
                    selector.Propiedad,
                    selector.DataType,
                    selector.Multiple,
                    campo,
                    sondas.Select(s => new ValorDelSelector(s.Editor, campo is null ? null : UnoSolo(s.Hojas[campo], record, selector.Propiedad))).ToList()));
            }
        }

        // Una declaración vieja (el ElementType ya no tiene ese selector) apagaría el gate para un
        // selector que ya no existe y nadie lo vería.
        var huerfanos = deDatos.Except(deDatosVistos).ToList();
        Assert.True(huerfanos.Count == 0,
            $"{record.Name}: declara como selectores de datos {string.Join(", ", huerfanos)}, que su ElementType no tiene.");

        return resultado;
    }

    /// <summary>
    /// Los alias que el resolver de <paramref name="record"/> le pide al bloque al resolver la
    /// muestra: las únicas propiedades que pueden llegar al elemento.
    /// </summary>
    private static IReadOnlySet<string> PropiedadesQueLee(IServiceProvider proveedor, Type record, (string Alias, object? Valor)[] muestra)
    {
        var elemento = ElementoFalso.Con(muestra);
        elemento.ClearReceivedCalls();

        var resolutor = proveedor.GetRequiredService(typeof(IResolutorSynHost<>).MakeGenericType(record));
        resolutor.GetType().GetMethod(nameof(IResolutorSynHost<object>.Resolver))!.Invoke(resolutor, new object[] { elemento });

        var leidas = elemento.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(Umbraco.Cms.Core.Models.PublishedContent.IPublishedElement.GetProperty))
            .Select(c => c.GetArguments()[0] as string)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        // Red de seguridad: un resolver que no pide NADA es uno que no se está observando (o un
        // NSubstitute que dejó de registrar llamadas), y todos sus selectores saldrían «no viaja».
        // Salvo que el record no reciba nada del editor (ehr: su API es de negocio y su paciente es
        // de la sesión, #196/#197); entonces no pedir nada es lo correcto.
        var delEditor = record.GetProperties().Any(p =>
            p.GetCustomAttribute<CampoSynHostAttribute>()?.Origen is OrigenDelCampo.Contenido or OrigenDelCampo.Decision);
        Assert.True(leidas.Count > 0 || !delEditor, $"El resolver de {record.Name} no le pidió ninguna propiedad al bloque de muestra.");
        return leidas;
    }

    /// <summary>La muestra con <paramref name="alias"/> puesto a <paramref name="valor"/> (<c>null</c>: sin valor).</summary>
    private static (string Alias, object? Valor)[] Con((string Alias, object? Valor)[] muestra, string alias, object? valor)
        => muestra.Where(m => !string.Equals(m.Alias, alias, StringComparison.Ordinal)).Append((alias, valor)).ToArray();

    /// <summary>
    /// Las hojas de un <c>config</c>: ruta → valores. Un objeto suma <c>.clave</c>, una lista
    /// <c>[]</c>; los nulos no son hojas (el emitter no los escribe).
    /// </summary>
    private static ILookup<string, string> Hojas(JsonElement config)
    {
        var hojas = new List<(string Ruta, string Valor)>();

        void Recorrer(JsonElement nodo, string ruta)
        {
            switch (nodo.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var propiedad in nodo.EnumerateObject())
                    {
                        Recorrer(propiedad.Value, ruta.Length == 0 ? propiedad.Name : $"{ruta}.{propiedad.Name}");
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in nodo.EnumerateArray())
                    {
                        Recorrer(item, $"{ruta}[]");
                    }
                    break;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    break;
                case JsonValueKind.String:
                    hojas.Add((ruta, nodo.GetString()!));
                    break;
                default:
                    hojas.Add((ruta, nodo.GetRawText()));
                    break;
            }
        }

        Recorrer(config, string.Empty);
        return hojas.ToLookup(h => h.Ruta, h => h.Valor, StringComparer.Ordinal);
    }

    /// <summary>Las rutas cuyos valores no son los mismos en <paramref name="antes"/> y <paramref name="despues"/>.</summary>
    private static IEnumerable<string> Cambios(ILookup<string, string> antes, ILookup<string, string> despues)
        => antes.Select(g => g.Key).Union(despues.Select(g => g.Key), StringComparer.Ordinal)
            .Where(ruta => !antes[ruta].Order(StringComparer.Ordinal).SequenceEqual(despues[ruta].Order(StringComparer.Ordinal), StringComparer.Ordinal));

    /// <summary>Lo que viaja de UN prevalor: un valor o ninguno; dos distintos son un resolver que el gate no sabe leer.</summary>
    private static string? UnoSolo(IEnumerable<string> valores, Type record, string propiedad)
    {
        var distintos = valores.Distinct(StringComparer.Ordinal).ToList();
        return distintos.Count switch
        {
            0 => null,
            1 => distintos[0],
            _ => throw new InvalidOperationException(
                $"{record.Name}: un solo prevalor de «{propiedad}» viaja como {distintos.Count} valores ({string.Join(", ", distintos)})."),
        };
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
        IReadOnlyList<string>? Claves,
        IReadOnlyList<CampoDelContrato> Campos,
        IReadOnlyList<SelectorDelContrato>? Selectores,
        JsonElement Ejemplo);

    private sealed record CampoDelContrato(string Nombre, string Tipo, bool Opcional, string? Origen);

    /// <summary>
    /// Un selector del ElementType y lo que viaja de cada prevalor. <c>campo</c> es la ruta del
    /// <c>config</c> donde cae (<c>position</c>, <c>platforms[]</c>, <c>toasts[].variant</c>);
    /// <c>null</c> si ningún prevalor llega al elemento. <c>deDatos</c> marca el selector que elige
    /// DATOS (la fuente de un listado, #196): no es vocabulario y el gate del UI no lo cruza.
    /// </summary>
    private sealed record SelectorDelContrato(
        string Propiedad,
        string DataType,
        bool Multiple,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Campo,
        IReadOnlyList<ValorDelSelector> Valores,
        bool? DeDatos = null);

    /// <summary>Lo que el editor elige y lo que de eso llega al elemento (<c>null</c>: nada).</summary>
    private sealed record ValorDelSelector(
        string Editor,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Viaja);

    private sealed record TipoDelContrato(string Record, IReadOnlyList<CampoDelContrato> Campos);
}
