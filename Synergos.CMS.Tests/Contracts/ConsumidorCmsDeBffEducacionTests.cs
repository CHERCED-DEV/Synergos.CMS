using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Tests.Services;
using Synergos.CMS.Web.Services.Academy;

namespace Synergos.CMS.Tests.Contracts;

/// <summary>
/// Lo que el CMS le pide a <c>Bff.Educacion</c> y lo que lee de su respuesta cabe en el contrato que el orquestador
/// publica (<c>docs/contracts/openapi/Synergos.Bff.Educacion.json</c>, 010-educacion-matricula): el gate de consumidor
/// CMS → orquestador (principio III), contra el documento COMITEADO que la deriva mantiene fiel al código.
/// </summary>
public sealed class ConsumidorCmsDeBffEducacionTests
{
    private static readonly Lazy<JsonDocument> Documento = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Educacion.json"))));

    private static JsonElement Raiz => Documento.Value.RootElement;

    private static RegistroDeMatriculasDeLaPuerta Registro(RegistroDeMatriculasDeLaPuertaTests.OrquestadorFalso orquestador)
    {
        var puerta = new PuertaSettings();
        puerta.Flujos["educacion.matricula"] = new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "educacion.alumno" };
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(puerta);
        var libro = new StubEnrollmentService(new StubCourseCatalogProvider(new StubContentStream(new StubSocialGraphService(), new StubReactionService(),
            () => new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc))), new StubPaymentProvider());
        return new RegistroDeMatriculasDeLaPuerta(orquestador, libro, libro, monitor, NullLogger<RegistroDeMatriculasDeLaPuerta>.Instance, true);
    }

    [Fact]
    public async Task Leer_una_matricula_es_consultar_y_lo_que_lee_esta_en_su_respuesta()
    {
        var orquestador = new RegistroDeMatriculasDeLaPuertaTests.OrquestadorFalso();
        await Registro(orquestador).AnotarAsync("m-1", Guid.NewGuid(), "Ana", "ana@synergos.co", default);

        Assert.Equal("/v1/flujos/educacion.matricula/m-1", Assert.Single(orquestador.Rutas));
        var op = Raiz.GetProperty("paths").GetProperty("/v1/flujos/educacion.matricula/{id}").GetProperty("get");
        Assert.Equal("consultar", op.GetProperty("x-synergos-flujo").GetProperty("operacion").GetString());
        LleveElSujeto(op);
        LoQueLeeEstaEn("EducacionMatriculaRespuesta");
    }

    [Fact]
    public async Task Reconciliar_es_listar_con_su_consulta_y_lo_que_lee_esta_en_la_pagina()
    {
        var orquestador = new RegistroDeMatriculasDeLaPuertaTests.OrquestadorFalso();
        await Registro(orquestador).ReconciliarAsync(Guid.NewGuid(), "Ana", "ana@synergos.co", default);

        var ruta = Assert.Single(orquestador.Rutas);
        Assert.StartsWith("/v1/flujos/educacion.matricula?", ruta, StringComparison.Ordinal);
        var op = Raiz.GetProperty("paths").GetProperty("/v1/flujos/educacion.matricula").GetProperty("get");
        Assert.Equal("listar", op.GetProperty("x-synergos-flujo").GetProperty("operacion").GetString());
        LleveElSujeto(op);
        var consulta = op.GetProperty("parameters").EnumerateArray()
            .Where(p => p.GetProperty("in").GetString() == "query").Select(p => p.GetProperty("name").GetString()).ToHashSet();
        foreach (var p in ruta.Split('?')[1].Split('&').Select(x => x.Split('=')[0]))
        {
            Assert.True(consulta.Contains(p), $"«listar» no publica el parámetro «{p}».");
        }
        Assert.True(Esquema("EducacionMatriculaPagina").GetProperty("properties").TryGetProperty("items", out _));
        LoQueLeeEstaEn("EducacionMatriculaRespuesta");
    }

    /// <summary>
    /// Lo que manda el publicador de la oferta (al publicar un curso o al republicar) cabe en <c>PublishCourseOffer</c>:
    /// un campo renombrado —<c>cursoId</c> por <c>curso</c>— lo rechazaría el orquestador y el curso no se matricularía.
    /// </summary>
    [Fact]
    public async Task Lo_que_manda_publicar_la_oferta_de_un_curso_cabe_en_PublishCourseOffer()
    {
        var orquestador = new PublicadorDeOfertasDeCursosTests.OrquestadorFalso();

        await PublicadorDeOfertasDeCursosTests.Publicador(orquestador).PublicarAsync(PublicadorDeOfertasDeCursosTests.Curso());

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        var op = Raiz.GetProperty("paths").GetProperty(ruta).GetProperty("post");
        Assert.Equal("PublishCourseOffer", op.GetProperty("operationId").GetString());
        AdmiteLaLlave(op, llave);
        using var enviado = JsonDocument.Parse(cuerpo);
        var oferta = Esquema("PublicarOfertaDeCursoRequest").GetProperty("properties");
        foreach (var campo in enviado.RootElement.EnumerateObject())
        {
            Assert.True(oferta.TryGetProperty(campo.Name, out _), $"PublicarOfertaDeCursoRequest no declara «{campo.Name}».");
        }
        var planes = enviado.RootElement.GetProperty("planes");
        Assert.NotEqual(0, planes.GetArrayLength());
        var plan = Esquema("PlanDeLaOfertaDto").GetProperty("properties");
        foreach (var campo in planes.EnumerateArray().SelectMany(p => p.EnumerateObject()))
        {
            Assert.True(plan.TryGetProperty(campo.Name, out _), $"PlanDeLaOfertaDto no declara «{campo.Name}».");
        }
    }

    /// <summary>El retiro de la oferta (al despublicar o borrar un curso) existe con la ruta y la llave que manda el CMS.</summary>
    [Fact]
    public async Task Lo_que_manda_retirar_la_oferta_existe_en_RetireCourseOffer()
    {
        var orquestador = new PublicadorDeOfertasDeCursosTests.OrquestadorFalso();

        await PublicadorDeOfertasDeCursosTests.Publicador(orquestador).RetirarAsync("gestion-agil");

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.Equal("/v1/ofertas/gestion-agil/retirar", ruta);
        var op = Raiz.GetProperty("paths").GetProperty("/v1/ofertas/{curso}/retirar").GetProperty("post");
        Assert.Equal("RetireCourseOffer", op.GetProperty("operationId").GetString());
        AdmiteLaLlave(op, llave);
        Assert.False(op.TryGetProperty("requestBody", out _));
        Assert.Equal("", cuerpo);
    }

    private static void AdmiteLaLlave(JsonElement op, string? llave)
    {
        var cabecera = op.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.NotNull(llave);
        Assert.True(llave!.Length <= cabecera.GetProperty("schema").GetProperty("maxLength").GetInt32());
    }

    private static void LleveElSujeto(JsonElement op)
        => Assert.Contains(op.GetProperty("parameters").EnumerateArray(),
            p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "X-Synergos-Sujeto");

    /// <summary>Cada campo que lee el registro (<c>MatriculaLeida</c>) lo publica la respuesta del flujo.</summary>
    private static void LoQueLeeEstaEn(string nombre)
    {
        var respuesta = Esquema(nombre).GetProperty("properties");
        foreach (var campo in new[] { "id", "numero", "estado", "datos", "error" })
        {
            Assert.True(respuesta.TryGetProperty(campo, out _), $"{nombre} no publica «{campo}».");
        }
        var datos = respuesta.GetProperty("datos").GetProperty("properties");
        Assert.True(datos.TryGetProperty("curso", out _), $"{nombre}.datos no publica «curso».");
        Assert.True(datos.TryGetProperty("total", out _), $"{nombre}.datos no publica «total».");
    }

    private static JsonElement Esquema(string nombre) => Raiz.GetProperty("components").GetProperty("schemas").GetProperty(nombre);

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
}
