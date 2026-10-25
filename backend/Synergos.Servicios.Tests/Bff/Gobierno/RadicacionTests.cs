using System.Net;
using System.Text.Json;
using Synergos.Bff.Core;
using Synergos.CMS.Tests.Bff.Reservas;

namespace Synergos.CMS.Tests.Bff.Gobierno;

/// <summary>
/// Historia 3 de 009-gobierno-tramite (T024): radicar un trámite por la puerta —la tasa se autoriza al abrir, se captura
/// y nace el expediente en la v2 al cerrar, y sale un aviso— contra procesos, precios, pagos y avisos reales; lo que
/// ninguna capacidad sabe hacer a pedido se inyecta en el cable.
/// </summary>
public sealed class RadicacionTests
{
    private const string Gratuito = "tram-paz-y-salvo";

    private static decimal Total(JsonElement r) => r.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal();

    private static string Id(JsonElement r) => r.GetProperty("id").GetString()!;

    [Fact]
    public async Task Abrir_autoriza_la_tasa_y_cerrar_la_captura_arranca_el_expediente_en_la_v2_y_avisa_una_vez()
    {
        using var r = await new RadicacionReal().ConOferta();

        var (abrio, abierta) = await r.Abrir();

        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());
        var id = Id(abierta);
        Assert.Equal(RadicacionReal.Tasa, Total(abierta));
        var autorizado = await r.Cobro(id);
        Assert.Equal("Authorized", autorizado.GetProperty("status").GetString());
        Assert.Equal(RadicacionReal.Tasa, autorizado.GetProperty("amount").GetProperty("amount").GetDecimal());
        Assert.Equal(0, await r.Expedientes(id));

        var (cerro, cerrada) = await r.Cerrar(id);

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal("Captured", (await r.Cobro(id)).GetProperty("status").GetString());
        var expediente = await r.Leer("workflow", $"v1/instances/{cerrada.GetProperty("datos").GetProperty("expediente").GetString()}");
        Assert.Equal("gobierno.radicacion", expediente.GetProperty("subjectKind").GetString());
        Assert.Equal(id, expediente.GetProperty("subjectId").GetString());
        Assert.Equal("gov.tramite.v2", expediente.GetProperty("definitionKey").GetString());
        Assert.Equal("submitted", expediente.GetProperty("state").GetString());
        Assert.Equal(1, await r.Expedientes(id));
        var avisos = await r.Avisos();
        Assert.Equal(1, avisos.GetArrayLength());
        Assert.Equal("Tu solicitud quedó radicada", avisos[0].GetProperty("subject").GetString());
    }

    /// <summary>
    /// La aceptación de la notificación electrónica y la versión del texto aceptado entran al abrir y salen al cerrar
    /// (el CMS las guarda con las respuestas, T027); de las capacidades sale el expediente y su estado, y ningún
    /// identificador de pagos.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task La_salida_lleva_la_aceptacion_y_su_version_el_expediente_y_ningun_identificador_de_pagos(bool acepta)
    {
        using var r = await new RadicacionReal().ConOferta();
        var (_, abierta) = await r.Abrir(cuerpo: RadicacionReal.Cuerpo(acepta: acepta));

        var (_, cerrada) = await r.Cerrar(Id(abierta));

        var datos = cerrada.GetProperty("datos");
        Assert.Equal(["estado", "expediente", "notificacionElectronica", "total", "tramite", "versionDelTexto"],
            datos.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(acepta, datos.GetProperty("notificacionElectronica").GetBoolean());
        Assert.Equal(RadicacionReal.VersionDelTexto, datos.GetProperty("versionDelTexto").GetString());
        Assert.Equal("gobierno.tramite", datos.GetProperty("tramite").GetProperty("kind").GetString());
        Assert.Equal(RadicacionReal.Tramite, datos.GetProperty("tramite").GetProperty("id").GetString());
        Assert.Equal("submitted", datos.GetProperty("estado").GetString());
        Assert.DoesNotContain("cobroId", cerrada.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_tramite_gratuito_se_radica_sin_llamar_a_pagos_y_avisa()
    {
        using var r = await new RadicacionReal().ConOferta(RadicacionReal.Oferta(Gratuito, 0m));

        var (_, abierta) = await r.Abrir(llave: "radicacion-gratis", cuerpo: RadicacionReal.Cuerpo(Gratuito));
        var id = Id(abierta);
        var (cerro, cerrada) = await r.Cerrar(id);

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal(0m, Total(cerrada));
        Assert.False(r.Tiene(id, "cobroId"));
        Assert.Equal(0, r.Cable.Veces("POST", "^/v1/payments"));
        Assert.Equal(1, await r.Expedientes(id));
        Assert.Equal(1, (await r.Avisos()).GetArrayLength());
    }

    /// <summary>
    /// SC-006: el doble clic y 20 reintentos de abrir y de cerrar con la misma llave caen en la MISMA radicación: un
    /// cobro y un expediente. Se afirma lo que quedó en las capacidades, no cuántas llamadas hubo —contar llamadas pasa
    /// sólo en fila—.
    /// </summary>
    [Fact]
    public async Task El_doble_clic_y_20_reintentos_con_la_misma_llave_cobran_una_vez_y_nace_un_expediente()
    {
        using var r = await new RadicacionReal().ConOferta();

        var dobles = await Task.WhenAll(r.Abrir(), r.Abrir());
        var id = Assert.Single(dobles.Where(d => d.Estado is HttpStatusCode.Created or HttpStatusCode.OK).Select(d => Id(d.Cuerpo)).Distinct());
        for (var i = 0; i < 20; i++)
        {
            var (estado, otra) = await r.Abrir();
            Assert.True(estado is HttpStatusCode.Created or HttpStatusCode.OK, otra.ToString());
            Assert.Equal(id, Id(otra));
        }

        await Task.WhenAll(r.Cerrar(id), r.Cerrar(id));
        for (var i = 0; i < 20; i++)
        {
            var (estado, cerrada) = await r.Cerrar(id);
            Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
            Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        }

        var (_, lista) = await r.Listar();
        Assert.Equal(1, lista.GetProperty("total").GetInt32());
        Assert.Equal(id, Id(lista.GetProperty("items")[0]));
        Assert.Equal(1, await r.Cobros(id));
        Assert.Equal("Captured", (await r.Cobro(id)).GetProperty("status").GetString());
        Assert.Equal(1, await r.Expedientes(id));
        Assert.Equal(1, (await r.Avisos()).GetArrayLength());
    }

    /// <summary>FR-019: el aviso que no sale no deshace la radicación (<c>al_fallar: seguir</c>): el expediente existe y la tasa está cobrada.</summary>
    [Fact]
    public async Task Si_el_aviso_falla_la_radicacion_queda_igual()
    {
        using var r = await new RadicacionReal(falla: p => RadicacionReal.Es(p, "POST", "^/v1/deliveries$") ? "notifications.unavailable" : null)
            .ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);

        var (cerro, cerrada) = await r.Cerrar(id);

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal("Captured", (await r.Cobro(id)).GetProperty("status").GetString());
        Assert.Equal(1, await r.Expedientes(id));
        Assert.Equal(0, r.Cable.Veces("POST", "/refund$"));
    }

    [Fact]
    public async Task La_radicacion_de_otro_ciudadano_no_existe_para_quien_la_pide()
    {
        using var r = await new RadicacionReal().ConOferta();
        var (_, abierta) = await r.Abrir(llave: "radicacion-ana", ciudadano: "ana");
        var id = Id(abierta);

        var (consulta, cuerpo) = await r.Consultar(id, ciudadano: "beto");
        var (cierre, _) = await r.Cerrar(id, ciudadano: "beto");

        Assert.Equal(HttpStatusCode.NotFound, consulta);
        Assert.Equal("gobierno.saga_not_found", cuerpo.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, cierre);
        Assert.Equal("Authorized", (await r.Cobro(id)).GetProperty("status").GetString());
        Assert.Equal(0, await r.Expedientes(id));
    }

    [Fact]
    public async Task Sin_oferta_se_rechaza_con_price_not_found_y_no_se_cobra_ni_queda_gratis()
    {
        using var r = new RadicacionReal();

        var (estado, cuerpo) = await r.Abrir(llave: "radicacion-sin-oferta");

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("pricing.price_not_found", cuerpo.GetProperty("code").GetString());
        Assert.NotEqual(SagaStatus.Completed, r.Sagas.Find("radicacion-sin-oferta")?.Status);
        Assert.False(r.Tiene("radicacion-sin-oferta", "cobroId"));
        Assert.Equal(0, r.Cable.Veces("POST", "^/v1/payments"));
    }

    [Fact]
    public async Task Un_cobro_rechazado_no_deja_expediente_ni_nada_retenido()
    {
        using var r = await new RadicacionReal(falla: p => RadicacionReal.Es(p, "POST", "^/v1/payments$") ? "payments.payment_declined" : null)
            .ConOferta();

        var (estado, cuerpo) = await r.Abrir();

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("payments.payment_declined", cuerpo.GetProperty("code").GetString());
        Assert.NotEqual(SagaStatus.Completed, r.Sagas.Find("radicacion-1")?.Status);
        Assert.False(r.Tiene("radicacion-1", "cobroId"));
        Assert.Equal(0, await r.Expedientes("radicacion-1"));
    }

    [Fact]
    public async Task Una_captura_negada_anula_la_autorizacion_y_no_nace_el_expediente()
    {
        using var r = await new RadicacionReal(falla: p => RadicacionReal.Es(p, "POST", "^/v1/payments/[^/]+/capture$") ? "payments.capture_failed" : null)
            .ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);

        var (estado, cuerpo) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("payments.capture_failed", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        Assert.Equal("Voided", (await r.Cobro(id)).GetProperty("status").GetString());
        Assert.Equal(0, await r.Expedientes(id));
        Assert.Equal(0, (await r.Avisos()).GetArrayLength());
    }

    /// <summary>
    /// Se cobró y el expediente no nació: con procesos caído DESPUÉS de capturar, la radicación se deshace y la tasa se
    /// devuelve entera (<c>despues: payments.devolver</c>); no sale aviso ni la pantalla puede decir «no se te cobró».
    /// </summary>
    [Fact]
    public async Task Con_procesos_caido_tras_capturar_se_devuelve_la_tasa_entera()
    {
        using var r = await new RadicacionReal(falla: p => RadicacionReal.Es(p, "POST", "^/v1/instances$") ? "caida" : null)
            .ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);

        var (estado, _) = await r.Cerrar(id);

        Assert.True((int)estado >= 500, $"Una caída de procesos no es un rechazo del negocio: {(int)estado}.");
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        var cobro = await r.Cobro(id);
        Assert.Equal(RadicacionReal.Tasa, cobro.GetProperty("refunded").GetProperty("amount").GetDecimal());
        Assert.Equal(0m, cobro.GetProperty("refundable").GetProperty("amount").GetDecimal());
        Assert.Equal(1, r.Cable.Veces("POST", "/refund$"));
        Assert.Equal(0, await r.Expedientes(id));
        Assert.Equal(0, (await r.Avisos()).GetArrayLength());
    }

    /// <summary>SC-006: una radicación abierta y no cerrada se deshace a los 30 minutos —0 pesos retenidos— y no antes.</summary>
    [Fact]
    public async Task Una_radicacion_abandonada_se_anula_a_los_30_minutos_y_no_antes()
    {
        var reloj = new RelojFalso(DateTimeOffset.UtcNow);
        using var r = await new RadicacionReal(reloj: reloj).ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);

        reloj.Avanzar(TimeSpan.FromMinutes(29));
        await r.Barrer();
        Assert.Equal("Running", (await r.Consultar(id)).Cuerpo.GetProperty("estado").GetString());
        Assert.Equal("Authorized", (await r.Cobro(id)).GetProperty("status").GetString());

        reloj.Avanzar(TimeSpan.FromMinutes(2));
        await r.Barrer();

        Assert.Equal("Compensated", (await r.Consultar(id)).Cuerpo.GetProperty("estado").GetString());
        Assert.Equal("Voided", (await r.Cobro(id)).GetProperty("status").GetString());
        Assert.Equal(0, await r.Expedientes(id));
    }

    /// <summary>FR-035: cada petición se sigue por UNA correlación —la de la puerta— hasta cada capacidad que toca.</summary>
    [Fact]
    public async Task Cada_peticion_se_sigue_por_su_correlacion_hasta_cada_capacidad()
    {
        using var r = await new RadicacionReal().ConOferta();

        var antes = r.Cable.Llamadas.Count;
        var (_, abierta) = await r.Abrir(correlacion: "radicacionabrir01");
        var entre = r.Cable.Llamadas.Count;
        var (cerro, _) = await r.Cerrar(Id(abierta), correlacion: "radicacioncerrar01");

        Assert.Equal(HttpStatusCode.OK, cerro);
        var deAbrir = r.Cable.Llamadas.Skip(antes).Take(entre - antes).ToList();
        var deCerrar = r.Cable.Llamadas.Skip(entre).ToList();
        Assert.Equal(["payments", "pricing"], deAbrir.Select(l => l.Capacidad).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(["notifications", "payments", "workflow"], deCerrar.Select(l => l.Capacidad).Distinct().Order(StringComparer.Ordinal));
        Assert.All(deAbrir, l => Assert.Equal("radicacionabrir01", l.Correlacion));
        Assert.All(deCerrar, l => Assert.Equal("radicacioncerrar01", l.Correlacion));
    }

    /// <summary>
    /// FR-035: cada llamada de cada paso queda en el registro con la radicación y su número —el que lleva el aviso—, y
    /// ninguna línea lleva el correo ni el nombre del contacto. El radicado lo acuña el CMS y va en SU registro (T027).
    /// </summary>
    [Fact]
    public async Task Cada_llamada_de_cada_paso_lleva_la_radicacion_y_su_numero_sin_datos_personales()
    {
        var registro = new RegistroConAmbitos();
        using var r = await new RadicacionReal(registro: registro).ConOferta();
        lock (registro.Lineas) registro.Lineas.Clear();

        var (_, abierta) = await r.Abrir();
        var id = Id(abierta);
        await r.Cerrar(id);

        var numero = abierta.GetProperty("numero").GetString()!;
        var llamadas = registro.Lineas
            .Where(l => l.Categoria == RadicacionReal.LlamadasAlArnes && !l.Mensaje.Contains("/v1/deliveries/", StringComparison.Ordinal))
            .ToList();
        foreach (var ruta in new[] { "/v1/quotes", "POST /v1/payments", "/capture", "POST /v1/instances", "POST /v1/deliveries" })
        {
            Assert.Contains(llamadas, l => l.Mensaje.Contains(ruta, StringComparison.Ordinal));
        }
        Assert.All(llamadas, l =>
        {
            Assert.True(numero.Equals(l.Valores.GetValueOrDefault("NumeroDeSaga")), $"«{l.Mensaje}» no lleva el número {numero}.");
            Assert.Equal(id, l.Valores.GetValueOrDefault("Saga"));
        });
        var todo = string.Join('\n', registro.Lineas.SelectMany(l => l.Textos.Append(l.Mensaje)));
        Assert.DoesNotContain(RadicacionReal.Correo, todo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RadicacionReal.Nombre, todo, StringComparison.Ordinal);
    }

    private sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;
        public override DateTimeOffset GetUtcNow() => _ahora;
        public void Avanzar(TimeSpan d) => _ahora += d;
    }
}
