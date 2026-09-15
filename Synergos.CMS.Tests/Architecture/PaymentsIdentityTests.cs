namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Quién paga se COMPRUEBA, no se cree — en el único cobro que no pasa por un orquestador
/// (HU #14, lo que quedaba; #27).
/// </summary>
/// <remarks>
/// <para><b>Por qué justo aquí y no en los otros siete consumidores.</b> La decisión de la
/// propagación (<c>PropagacionDeIdentidadTests</c>) dice que un orquestador no reenvía la
/// credencial de quien llamó, así que a <c>Api.Payments</c> no se le puede presentar identidad
/// por la vía de Tienda, Salud, Eventos o Viajes. **La tasa de un trámite es la excepción**: va
/// directo del CMS a la capacidad (#27), sin saga en medio, así que es el único sitio donde hoy
/// hay un llamador real que puede firmar. Y conviene que el primero sea real y no un fake — es
/// lo que hizo que #72 encontrara lo que encontró.</para>
///
/// <para><b>Lo que estaba mal.</b> Con la llave compartida sola, cualquier servicio que pudiera
/// hablar con <c>Api.Payments</c> escribía un cobro a nombre de quien quisiera, y quedaba. Es el
/// defecto #42/#48/#72 sobre el registro de quién movió plata — de los que alguien cita el día
/// que hay una disputa, y por tanto de los que no pueden ser falsificables.</para>
///
/// <para><b>Y el punto más fino, que es lo que vuelve el token prueba y no adorno:</b> la
/// capacidad rechaza un token que nombre a otro (<c>token_subject_mismatch</c>), así que el
/// sujeto firmado tiene que ser EXACTAMENTE el mismo valor que viaja como <c>payerId</c>. Por eso
/// el <c>payerId</c> pasó a ser el <c>MemberKey</c> cuando hay sesión: presentar el token del
/// miembro mientras viaja el seudónimo de su correo no daría un cobro peor firmado — daría un
/// rechazo.</para>
///
/// <para><b>Este gate mide que la pieza esté ENCHUFADA, no que exista</b>
/// (<c>feedback_an_exemption_needs_a_signature_behind_it</c>). Es la lección de
/// <c>Api.Notifications</c>, que quitó la llamada de su lambda y no falló ni un test porque los
/// suyos probaban el verificador. Acá eso se traduce en mirar los DOS lados —el emisor y la
/// capacidad— y además el COMPOSER: el emisor recibe el issuer por un parámetro con valor por
/// defecto <c>null</c>, así que un composer que no lo inyecte deja el cableado entero
/// compilando, pasando los tests y sin presentar nada nunca.</para>
/// </remarks>
public sealed class PaymentsIdentityTests
{
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

    private static string SinComentarios(string ruta)
    {
        Assert.True(File.Exists(ruta), $"No existe {ruta}: revisar este gate.");

        var limpias = new List<string>();
        foreach (var linea in File.ReadAllLines(ruta))
        {
            var t = linea.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)) continue;
            var i = linea.IndexOf("//", StringComparison.Ordinal);
            limpias.Add(i >= 0 ? linea[..i] : linea);
        }
        return string.Join('\n', limpias);
    }

    private static string Fuente(params string[] partes)
        => SinComentarios(Path.Combine(new[] { RepoRoot() }.Concat(partes).ToArray()));

    private static string Emisor() => Fuente("Synergos.CMS.Web", "Services", "HttpPaymentProvider.cs");

    // ── El lado del CMS ─────────────────────────────────────────────────────

    /// <summary>El cobro presenta la identidad de quien paga cuando la hay.</summary>
    [Fact]
    public void El_cobro_presenta_la_identidad()
    {
        var emisor = Emisor();

        Assert.Contains("_identidad.IssueAsync", emisor, StringComparison.Ordinal);
        Assert.Contains("X-Synergos-Identity", emisor, StringComparison.Ordinal);
    }

    /// <summary>
    /// Y sólo con sesión detrás: un correo tecleado no es una identidad comprobada.
    /// </summary>
    /// <remarks>
    /// <b>Es la mitad que se cae sola si nadie la vigila</b>, porque quitarla parece una
    /// simplificación («¿por qué no pedimos token siempre?»). Sin esta guarda, el pago de
    /// invitado haría que la capacidad anotara <c>IdentityToken</c> sobre un correo que nadie
    /// comprobó — el defecto #42 con la firma tapándolo mejor, que es peor que no firmar.
    /// </remarks>
    [Fact]
    public void Sin_sesion_no_se_pide_token()
    {
        var emisor = Emisor();

        Assert.Contains("PayerMemberKey is not Guid", emisor, StringComparison.Ordinal);
    }

    /// <summary>
    /// El sujeto que se firma es el MISMO que viaja como pagador.
    /// </summary>
    /// <remarks>
    /// Sin esto el token no es prueba: la capacidad lo rechazaría con
    /// <c>token_subject_mismatch</c> y el cobro se caería, o —peor, si algún día se relajara esa
    /// comprobación— quedaría un cobro firmado a nombre de alguien distinto del que aparece como
    /// pagador.
    /// </remarks>
    [Fact]
    public void El_sujeto_firmado_es_el_pagador()
    {
        var emisor = Emisor();

        Assert.Contains("var pagador = PayerId(request)", emisor, StringComparison.Ordinal);
        Assert.Contains("payerId = pagador", emisor, StringComparison.Ordinal);
        Assert.Contains("new IdentitySubject(nombres.PayerKind, pagador", emisor, StringComparison.Ordinal);
    }

    /// <summary>
    /// Se declara el SUELO y nunca más que eso.
    /// </summary>
    /// <remarks>
    /// La fuerza de una afirmación es la de lo que alguien <b>verificó</b>. Que este despliegue
    /// sepa emitir un token no autoriza a escribir <c>IdentityToken</c> — quien lo sube es la
    /// capacidad, tras comprobarlo. Escribirlo acá «porque es lo que mandamos» es el defecto #42
    /// exacto.
    /// </remarks>
    [Fact]
    public void Se_declara_el_suelo_y_nunca_mas()
    {
        var emisor = Emisor();

        Assert.Contains("assertion = IdentityAssertions.CmsSession", emisor, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityAssertions.IdentityToken", emisor, StringComparison.Ordinal);
    }

    /// <summary>
    /// Y los DOS composers lo inyectan de verdad.
    /// </summary>
    /// <remarks>
    /// <para><b>El diente que de verdad hacía falta.</b> El emisor recibe el issuer por un
    /// parámetro con valor por defecto <c>null</c> —tiene que serlo, o los tests que construyen
    /// el proveedor a mano dejarían de compilar—, así que un composer que no lo inyecte deja
    /// TODO lo de arriba en verde y sin presentar nada. Eso no es una hipótesis: es lo que
    /// <c>Api.Notifications</c> descubrió quitando la llamada de su lambda sin que fallara un
    /// solo test.</para>
    ///
    /// <para><b>Y son los dos sitios y no el que interesa.</b> Quien decide si se presenta es el
    /// proveedor —sólo con <c>MemberKey</c>—, no el composer; cablear uno sí y otro no dejaría el
    /// mismo tipo comportándose distinto según por dónde se construyó, que es exactamente cómo se
    /// esconde un defecto detrás de una seam
    /// (<c>feedback_property_injection_breaks_at_two_impls</c>).</para>
    /// </remarks>
    [Fact]
    public void Los_dos_composers_inyectan_el_emisor()
    {
        var sitios = new (string Fichero, string Cual)[]
        {
            (Path.Combine("Synergos.CMS.Web", "Composers", "SeamComposer.EventsPropertiesGov.cs"), "la tasa de un trámite"),
            (Path.Combine("Synergos.CMS.Web", "Composers", "SeamComposer.PaymentEngine.cs"), "el seam completo"),
        };

        foreach (var (fichero, cual) in sitios)
        {
            var texto = Fuente(fichero.Split(Path.DirectorySeparatorChar));

            var construye = texto.IndexOf("new HttpPaymentProvider(", StringComparison.Ordinal);
            Assert.True(construye >= 0, $"{fichero} ya no construye HttpPaymentProvider: revisar este gate.");

            var cierra = texto.IndexOf(");", construye, StringComparison.Ordinal);
            Assert.True(cierra > construye, $"No se pudo leer la construcción en {fichero}: revisar este gate.");

            Assert.True(
                texto[construye..cierra].Contains("IIdentityTokenIssuer", StringComparison.Ordinal),
                $"El composer de {cual} construye HttpPaymentProvider SIN inyectarle el emisor de "
                + "identidad.\n\nEso no rompe nada y no falla ningún test: el parámetro tiene valor "
                + "por defecto null, así que el cobro sale declarando CmsSession para siempre y "
                + "todo lo demás sigue en verde. Es la forma exacta que Api.Notifications "
                + "descubrió quitando la llamada de su lambda — un gate que mide que la pieza "
                + "EXISTA y no que esté ENCHUFADA no vigila nada.");
        }
    }

    // ── El lado de la capacidad ─────────────────────────────────────────────

    /// <summary>
    /// <c>Api.Payments</c> comprueba el token y guarda con qué se afirmó.
    /// </summary>
    /// <remarks>
    /// Las dos mitades hacen falta y se rompen por separado: sin verificar, la capacidad sigue
    /// creyendo el <c>payerId</c> que le mandan y el token es decoración; sin guardar, el arreglo
    /// es invisible — los cobros nuevos valdrían más que los viejos y nada lo diría. Es lo que
    /// #72 tuvo que aprender sobre la bitácora.
    /// </remarks>
    [Fact]
    public void La_capacidad_comprueba_y_guarda_con_que_se_afirmo()
    {
        var programa = Fuente("Synergos.Api.Payments", "Program.cs");
        Assert.Contains("AddIdentityTokens", programa, StringComparison.Ordinal);

        // Se mira el CUERPO del endpoint que autoriza, no el fichero entero.
        //
        // Esta distinción no es teórica: la primera versión de este gate afirmaba que el fichero
        // dijera «IdentityAssertions.Resolve» en alguna parte, y al mutarlo —reemplazando la
        // llamada del lambda por «créele al llamador»— pasó en VERDE, porque el helper
        // Afirmacion() seguía ahí abajo con esa cadena dentro. Es exactamente lo que
        // Api.Notifications descubrió quitando la llamada de su lambda sin que fallara un test
        // (feedback_an_exemption_needs_a_signature_behind_it): un gate que mide que la pieza
        // EXISTA y no que esté ENCHUFADA no vigila nada.
        var autoriza = CuerpoDelEndpointQueAutoriza();

        Assert.Contains("Afirmacion(identidad, http, payer", autoriza, StringComparison.Ordinal);

        // Y que lo resuelto DECIDA: sin esto se podría resolver y seguir igual.
        Assert.Contains("if (assertion is null) return", autoriza, StringComparison.Ordinal);
        Assert.Contains("assertion.Value", autoriza, StringComparison.Ordinal);

        var dominio = Fuente("Synergos.Api.Payments", "Domain", "Payment.cs");
        Assert.Contains("PaidWith", dominio, StringComparison.Ordinal);

        var servicio = Fuente("Synergos.Api.Payments", "Domain", "PaymentService.cs");
        Assert.Contains("PaidWith: assertion", servicio, StringComparison.Ordinal);
    }

    /// <summary>El cuerpo del <c>MapPost("/v1/payments")</c>, hasta el siguiente endpoint.</summary>
    /// <remarks>
    /// Recortar hace falta porque el fichero entero incluye el helper que resuelve la afirmación,
    /// así que buscar ahí confunde «lo llama» con «lo tiene escrito». El corte va hasta el
    /// siguiente <c>app.Map</c>, que es donde empieza otro endpoint.
    /// </remarks>
    private static string CuerpoDelEndpointQueAutoriza()
    {
        var endpoints = Fuente("Synergos.Api.Payments", "Endpoints", "PaymentEndpoints.cs");

        var abre = endpoints.IndexOf("app.MapPost(\"/v1/payments\"", StringComparison.Ordinal);
        Assert.True(abre >= 0, "No se encontró el MapPost de /v1/payments: revisar este gate.");

        var cierra = endpoints.IndexOf("app.Map", abre + 1, StringComparison.Ordinal);
        Assert.True(cierra > abre, "No se encontró el final del endpoint: revisar este gate.");

        return endpoints[abre..cierra];
    }

    /// <summary>
    /// Y lo guardado se puede LEER.
    /// </summary>
    /// <remarks>
    /// <b>El espejo de <c>feedback_no_read_without_a_write_path</c></b>, que a este repo ya le
    /// costó una vez: el expediente de Gobierno guardaba el estado del cobro desde la ADR 0116 y
    /// <c>CaseDetail</c> no lo declaraba, así que se escribía a disco y no salía por ningún
    /// endpoint. Un campo que ninguna pantalla enseña no está guardado: está enterrado. La
    /// pregunta que lo caza es «¿qué lectura devuelve este campo?», y acá la respuesta tiene que
    /// ser el <c>PaymentResponse</c>, que es el único que sale de la capacidad.
    /// </remarks>
    [Fact]
    public void Lo_guardado_se_puede_leer()
    {
        var contratos = Fuente("Synergos.Api.Payments", "Contracts", "PaymentContracts.cs");

        Assert.Contains("PaidWith", contratos, StringComparison.Ordinal);
        Assert.Contains("p.PaidWith?.ToString()", contratos, StringComparison.Ordinal);
    }
}
