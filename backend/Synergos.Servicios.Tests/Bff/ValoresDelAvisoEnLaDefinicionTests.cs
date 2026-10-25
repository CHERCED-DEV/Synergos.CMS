using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Pasos.Http;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Lo que el validador exige de los <c>valores</c> que declara un aviso (núcleo, NM-2 de 010; NN-6 de 003; NR-3 de 008): que
/// alguien los escriba antes —y que la saga los guarde si se leen en otra fase—, que el paso los admita, que no pisen uno de
/// los suyos, que no se repitan y que sean de un tipo que se lee en un correo.
/// </summary>
public sealed class ValoresDelAvisoEnLaDefinicionTests
{
    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Validar no llama a nadie.");
    }

    /// <summary>La matrícula: se autoriza al abrir y, al cerrar, se captura y se avisa con el curso y el plan.</summary>
    private static readonly string Matricula = Q("""
        {
          'clave': 'muestra.matricula',
          'entrada': ['alumno', 'curso', 'plan', 'total'],
          'tipos': { 'alumno': { 'de': 'sujeto' }, 'curso': 'texto', 'plan': 'texto?', 'total': 'dinero', 'contacto': { 'de': 'contacto' } },
          'salida': ['cobroId'],
          'efimera': { 'cerrar': ['contacto'] },
          'fases': { 'abrir': ['autorizar'], 'cerrar': ['capturar', 'avisar'] },
          'pasos': {
            'autorizar': {
              'tipo': 'payments.autorizar', 'lee': ['alumno', 'total'], 'escribe': ['cobroId'], 'llave': 'authorize',
              'reserva': { 'consumado_por': 'capturar', 'antes': 'payments.anular', 'despues': 'payments.devolver' }
            },
            'capturar': { 'tipo': 'payments.capturar', 'lee': ['cobroId'], 'llave': 'capture', 'cierra_reserva': 'autorizar' },
            'avisar': {
              'tipo': 'notifications.avisar', 'lee': ['alumno', 'contacto', 'total'], 'llave': 'notify',
              'plantilla': 'muestra.matricula.confirmada', 'al_fallar': 'seguir', 'valores': ['curso', 'plan']
            }
          }
        }
        """);

    private static string Q(string conSimples) => conSimples.Replace('\'', '"');

    private static IReadOnlyList<string> Errores(string json)
    {
        var flujo = FlujoDef.Leer(json);
        var puertos = new PuertosHttp(new SinRed());
        var registro = PasosComunes.Registro(puertos);
        var binding = new BindingDeFlujo(flujo, TiposDeValor.Con(new TipoContacto(), new TipoDireccion(), new TipoFranjas()),
            registro, new DeshacerComun(puertos, puertos, puertos, puertos, []).Tipos);
        return FlowValidator.Validar(flujo, registro, ContratoDelFlujo.De(binding, flujo.Fases.Select(f => f.Nombre).ToList()));
    }

    private static string Mutar(string antes, string despues)
    {
        var a = Q(antes);
        Assert.Contains(a, Matricula, StringComparison.Ordinal);
        return Matricula.Replace(a, Q(despues), StringComparison.Ordinal);
    }

    private static void Dice(string json, string mensaje)
        => Assert.Contains(Errores(json), e => e.Contains(mensaje, StringComparison.Ordinal));

    private static void Arranca(string json)
    {
        var errores = Errores(json);
        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }

    [Fact]
    public void Lo_que_entra_al_abrir_y_lee_el_aviso_al_cerrar_arranca_porque_la_saga_lo_guarda()
        => Arranca(Matricula);

    [Fact]
    public void Un_aviso_sin_total_arranca()
        => Arranca(Mutar("'lee': ['alumno', 'contacto', 'total'], 'llave': 'notify'", "'lee': ['alumno', 'contacto'], 'llave': 'notify'"));

    [Fact]
    public void Un_valor_que_nadie_escribe_antes_no_arranca()
    {
        // Leído en la fase que cierra, la saga tendría que guardarlo desde la que abre, y nadie lo escribe ni lo declara.
        Dice(Mutar("'valores': ['curso', 'plan']", "'valores': ['curso', 'profesor']"),
            "«profesor» se guarda en la saga (lo lee una fase que continúa, sale o queda para lo posterior) y no se sabe su tipo");

        // En una sola fase, es una lectura más que nadie escribe antes.
        var unaFase = Mutar("'fases': { 'abrir': ['autorizar'], 'cerrar': ['capturar', 'avisar'] }",
                "'fases': { 'abrir': ['autorizar', 'avisar'], 'cerrar': ['capturar'] }")
            .Replace(Q("'efimera': { 'cerrar': ['contacto'] }"), Q("'efimera': { 'abrir': ['contacto'] }"), StringComparison.Ordinal)
            .Replace(Q("'valores': ['curso', 'plan']"), Q("'valores': ['curso', 'profesor']"), StringComparison.Ordinal);
        Dice(unaFase, "«avisar» lee «profesor» y nadie lo escribe antes (ni es una entrada).");
    }

    [Fact]
    public void Un_valor_que_pisa_uno_fijo_o_que_se_repite_no_arranca()
    {
        Dice(Mutar("'valores': ['curso', 'plan']", "'valores': ['curso', 'total']"),
            "el paso «avisar» declara el valor «total», que «notifications.avisar» ya pone");
        Dice(Mutar("'valores': ['curso', 'plan']", "'valores': ['curso', 'curso']"),
            "el paso «avisar» nombra «curso» dos veces en «valores».");
    }

    [Fact]
    public void Un_valor_que_no_se_escribe_en_un_correo_no_arranca()
    {
        var conRef = Mutar("'curso': 'texto',", "'curso': 'texto', 'cursoRef': { 'tipo': 'ref', 'kind': 'muestra.curso' },")
            .Replace(Q("'entrada': ['alumno', 'curso', 'plan', 'total']"), Q("'entrada': ['alumno', 'curso', 'plan', 'total', 'cursoRef']"), StringComparison.Ordinal)
            .Replace(Q("'valores': ['curso', 'plan']"), Q("'valores': ['curso', 'cursoRef']"), StringComparison.Ordinal);
        Dice(conRef, "el paso «avisar» declara el valor «cursoRef», que es «ref»: no se escribe en un correo");

        var conLista = Mutar("'curso': 'texto',", "'curso': 'texto', 'temas': { 'lista': { 'nombre': 'texto' } },")
            .Replace(Q("'entrada': ['alumno', 'curso', 'plan', 'total']"), Q("'entrada': ['alumno', 'curso', 'plan', 'total', 'temas']"), StringComparison.Ordinal)
            .Replace(Q("'valores': ['curso', 'plan']"), Q("'valores': ['curso', 'temas']"), StringComparison.Ordinal);
        Dice(conLista, "el paso «avisar» declara el valor «temas», que es una lista");
    }

    [Fact]
    public void Un_paso_que_no_avisa_con_valores_no_los_declara()
        => Dice(Mutar("'cierra_reserva': 'autorizar' }", "'cierra_reserva': 'autorizar', 'valores': ['curso'] }"),
            "el paso «capturar» declara «valores» y «payments.capturar» no avisa con valores");
}
