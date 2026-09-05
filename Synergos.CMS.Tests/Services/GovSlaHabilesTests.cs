using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El término del expediente se cuenta en días HÁBILES (defecto #77).
/// </summary>
/// <remarks>
/// <para><b>Esto no existía, y por eso el defecto vivió.</b> <c>SlaDaysLeft</c> aparecía tres veces
/// en la suite y las tres eran un literal pasado a un <c>CaseDetail</c> de fixture, o el criterio
/// de ordenación reproducido a mano dentro del propio assert — un test que recalcula el orden con
/// la misma expresión que el código confirmaría el orden aunque el cálculo estuviera al revés.
/// Nadie ejercitaba la FUNCIÓN, sólo el campo ya calculado. Es la forma de #42, #48 y #72: la
/// regla se prueba y la fuente del dato no.</para>
/// </remarks>
public sealed class GovSlaHabilesTests
{
    private static readonly GovCitizen Ciudadano =
        new("Ana Torres", "ana.torres@correo.co", "1030567890", "+57 300 555 1234");

    /// <summary>Reloj movible: sin esto no se puede mirar el expediente «más adelante».</summary>
    private sealed class Reloj
    {
        public DateTimeOffset Ahora { get; set; }

        /// <summary>Las 09:00 de Bogotá de ese día, en UTC.</summary>
        public void EnBogota(int año, int mes, int dia)
            => Ahora = new DateTimeOffset(año, mes, dia, 9, 0, 0, TimeSpan.FromHours(-5));
    }

    private static (StubApplicationService Svc, Reloj Reloj) Nuevo()
    {
        var reloj = new Reloj();
        return (new StubApplicationService(
            new StubTramiteCatalogProvider(),
            new StubGovFeeCalculator(),
            new StubPaymentProvider(),
            null,
            () => reloj.Ahora,
            null), reloj);
    }

    /// <summary>Renovación de cédula: 15 días hábiles autorados.</summary>
    private static Dictionary<string, string> Cedula() => new()
    {
        ["nombreCompleto"] = "Ana Torres",
        ["cedula"] = "1030567890",
        ["correo"] = "ana.torres@correo.co",
        ["telefono"] = "+57 300 555 1234",
        ["motivo"] = "Deterioro",
    };

    /// <summary>Licencia de conducción: 5 días hábiles autorados.</summary>
    private static Dictionary<string, string> Licencia() => new()
    {
        ["nombreCompleto"] = "Ana Torres",
        ["cedula"] = "1030567890",
        ["correo"] = "ana.torres@correo.co",
        ["categoria"] = "B1",
        ["examenMedico"] = "true",
    };

    // ── El ejemplo del ticket, de punta a punta ─────────────────────────────

    /// <summary>
    /// Un trámite de 15 días hábiles radicado el lunes vence el lunes de tres semanas.
    /// </summary>
    /// <remarks>
    /// <para>El día 16 de calendario —miércoles 23— el sistema decía <c>0</c> o negativo, o sea
    /// «vencido», sobre un expediente que estaba en plazo. Hoy dice que quedan tres días de
    /// trabajo, que es lo que quedan.</para>
    ///
    /// <para>El sesgo era conservador —el sistema se creía con menos tiempo del que la ley da—
    /// pero eso no lo volvía inofensivo: disparaba «vencido» antes de tiempo, y en PQRSD el plazo
    /// <b>es</b> el producto.</para>
    /// </remarks>
    [Fact]
    public async Task Quince_dias_habiles_no_vencen_el_dia_dieciseis_de_calendario()
    {
        var (svc, reloj) = Nuevo();
        reloj.EnBogota(2026, 9, 7);                      // lunes

        var radicado = await svc.RadicarAsync("trm-cedula", Cedula(), Ciudadano);
        Assert.Equal(15, radicado.Case.SlaDaysLeft);     // el día de radicación, quedan los 15

        reloj.EnBogota(2026, 9, 23);                     // miércoles, día 16 de CALENDARIO
        var alDia16 = svc.FindCase(radicado.Case.CaseId);

        Assert.NotNull(alDia16);
        Assert.Equal(3, alDia16!.SlaDaysLeft);           // jue 24, vie 25, lun 28
    }

    /// <summary>
    /// El día del vencimiento queda 0, y a partir de ahí el número es negativo.
    /// </summary>
    /// <remarks>
    /// Es la convención que ya tenía el cálculo anterior. Lo que este arreglo cambia es la UNIDAD,
    /// no el punto de corte: quien pintaba «vence hoy» con un 0 sigue pintando lo mismo.
    /// </remarks>
    [Fact]
    public async Task El_dia_del_vencimiento_queda_cero_y_despues_negativo()
    {
        var (svc, reloj) = Nuevo();
        reloj.EnBogota(2026, 9, 7);
        var caso = (await svc.RadicarAsync("trm-cedula", Cedula(), Ciudadano)).Case.CaseId;

        reloj.EnBogota(2026, 9, 28);                     // el lunes que vence
        Assert.Equal(0, svc.FindCase(caso)!.SlaDaysLeft);

        reloj.EnBogota(2026, 9, 29);                     // martes: un hábil de mora
        Assert.Equal(-1, svc.FindCase(caso)!.SlaDaysLeft);
    }

    // ── Lo que de verdad dolía: la cola ─────────────────────────────────────

    /// <summary>
    /// Dos trámites con plazos distintos se ordenaban mal ENTRE SÍ.
    /// </summary>
    /// <remarks>
    /// <para>El error no era un desplazamiento uniforme que se cancelara al comparar: crece con la
    /// longitud del término (≈2 días por cada 5 hábiles), así que un trámite largo se veía más
    /// urgente que uno corto que vencía antes. <b>La cola decía que urgía primero el que no
    /// urgía.</b></para>
    ///
    /// <para>Con las fechas de abajo el orden se INVIERTE entre las dos formas de contar, que es
    /// lo que hace a este test valer: no comprueba un número, comprueba una decisión.</para>
    /// <list type="bullet">
    ///   <item>hábiles — cédula: quedan 5; licencia: quedan 4 → primero la licencia (correcto:
    ///   vence el 25, antes que el 28);</item>
    ///   <item>calendario — cédula: quedaba 1; licencia: quedaban 2 → primero la cédula.</item>
    /// </list>
    /// </remarks>
    [Fact]
    public async Task La_cola_pone_primero_al_que_de_verdad_vence_antes()
    {
        var (svc, reloj) = Nuevo();

        reloj.EnBogota(2026, 9, 7);                      // lunes — cédula, 15 hábiles → vence 28
        var cedula = (await svc.RadicarAsync("trm-cedula", Cedula(), Ciudadano)).Case.CaseId;

        reloj.EnBogota(2026, 9, 18);                     // viernes — licencia, 5 hábiles → vence 25
        var licencia = (await svc.RadicarAsync("trm-licencia-conduccion", Licencia(), Ciudadano)).Case.CaseId;

        reloj.EnBogota(2026, 9, 21);                     // lunes: se mira la cola

        var quedanCedula = svc.FindCase(cedula)!.SlaDaysLeft;
        var quedanLicencia = svc.FindCase(licencia)!.SlaDaysLeft;

        Assert.Equal(5, quedanCedula);
        Assert.Equal(4, quedanLicencia);

        // Contando calendario habría dado 1 y 2: la cédula parecía la urgente aunque vence tres
        // días DESPUÉS que la licencia.
        Assert.True(quedanLicencia < quedanCedula,
            "La cola pone primero al que vence antes: si esto cae, se está contando calendario.");
    }

    // ── Los casos que la ficha declara ──────────────────────────────────────

    /// <summary>Un expediente ya resuelto no tiene plazo que contar.</summary>
    [Fact]
    public async Task Un_expediente_terminal_no_cuenta_plazo()
    {
        var (svc, reloj) = Nuevo();
        reloj.EnBogota(2026, 9, 7);
        var caso = (await svc.RadicarAsync("trm-cedula", Cedula(), Ciudadano)).Case.CaseId;

        reloj.EnBogota(2026, 10, 20);                    // muy vencido si contara
        var resuelto = svc.ApplyDecision(
            caso, CaseStatus.Resuelto, "funcionario@entidad.gov.co", "Aprobado.", reloj.Ahora, null);

        Assert.Equal(0, resuelto.SlaDaysLeft);
    }

    /// <summary>
    /// Radicar de noche no regala un día de plazo.
    /// </summary>
    /// <remarks>
    /// <para>Las 20:00 de Bogotá ya son el día siguiente en UTC. Contando allá, quien radica un
    /// lunes por la noche recibe el término de quien radicó el martes — <b>un día hábil de
    /// regalo</b>, y sólo por la hora a la que le dio a enviar.</para>
    ///
    /// <para>No es simétrico ni se compensa: el mismo desfase le QUITA un día a quien mira su
    /// expediente de noche. Y no salta a la vista porque de viernes a sábado las dos formas de
    /// contar coinciden —ninguno de los dos es hábil—, así que hace falta un lunes para verlo.</para>
    /// </remarks>
    [Fact]
    public async Task Radicar_de_noche_no_regala_un_dia_de_plazo()
    {
        var (svc, reloj) = Nuevo();

        // Lunes 7-sep-2026, 20:00 en Bogotá = martes 8 a las 01:00 UTC.
        reloj.Ahora = new DateTimeOffset(2026, 9, 7, 20, 0, 0, TimeSpan.FromHours(-5));
        Assert.Equal(new DateOnly(2026, 9, 8), DateOnly.FromDateTime(reloj.Ahora.UtcDateTime));

        var caso = (await svc.RadicarAsync("trm-licencia-conduccion", Licencia(), Ciudadano)).Case.CaseId;

        // 5 hábiles desde el LUNES 7: mar 8, mié 9, jue 10, vie 11, lun 14 → vence el lunes 14.
        // Desde el martes 8 vencería el martes 15: un día hábil que nadie autorizó.
        reloj.EnBogota(2026, 9, 14);
        Assert.Equal(0, svc.FindCase(caso)!.SlaDaysLeft);
    }

    /// <summary>
    /// Y mirarlo de noche no descuenta un día que todavía queda.
    /// </summary>
    /// <remarks>
    /// El mismo desfase, del otro lado: a las 20:00 del lunes en Bogotá el expediente aún tiene el
    /// martes por delante, pero contando en UTC ya es martes y ese día deja de contarse.
    /// </remarks>
    [Fact]
    public async Task Mirar_el_expediente_de_noche_no_descuenta_un_dia()
    {
        var (svc, reloj) = Nuevo();
        reloj.EnBogota(2026, 9, 7);
        var caso = (await svc.RadicarAsync("trm-licencia-conduccion", Licencia(), Ciudadano)).Case.CaseId;

        // Vence el lunes 14. El lunes 7 a las 20:00 de Bogotá quedan cinco hábiles: 8, 9, 10, 11, 14.
        // Contando en UTC ya sería martes y el propio martes dejaría de contarse: diría cuatro.
        reloj.Ahora = new DateTimeOffset(2026, 9, 7, 20, 0, 0, TimeSpan.FromHours(-5));

        Assert.Equal(5, svc.FindCase(caso)!.SlaDaysLeft);
    }
}
