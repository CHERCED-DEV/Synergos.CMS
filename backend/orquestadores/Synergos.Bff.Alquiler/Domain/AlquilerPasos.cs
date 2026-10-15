using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>
/// Los pasos propios de la renta declarada (<c>flujos/alquiler.renta.json</c>, línea 005): la regla del alquiler
/// que no es de nadie más (el tramo por duración).
/// </summary>
public static class AlquilerPasos
{
    /// <summary>Cuántas unidades admite un alquiler de una vez: el tope del flujo imperativo.</summary>
    public const int MaxUnidades = RentalFlow.MaxUnits;

    /// <summary>Los pasos propios, para sumarlos a los comunes en el registro del orquestador.</summary>
    public static IPaso[] Propios(OfertaDeAlquiler ofertas, int maxDiasDelDespliegue)
        => [new PasoTramo(ofertas, maxDiasDelDespliegue)];
}

/// <summary>
/// <c>alquiler.tramo</c>: lee el equipo, las unidades y la ventana; revisa los límites contra la oferta publicada y
/// escribe lo que se cotiza —el tramo por días por unidades, y la garantía por unidades— y la agenda del equipo.
/// </summary>
/// <remarks>
/// <para><b>Es la regla del catálogo</b> (<c>ReglasDeAlquiler</c> en el CMS) escrita del lado que cobra: el
/// valor del día es el del tramo MÁS ALTO que cubre la duración, o la tarifa base. Los IMPORTES no salen de
/// aquí: salen de <c>Api.Pricing</c>, que los tiene porque la oferta los publicó. De la oferta registrada se
/// leen sólo los límites y desde cuántos días aplica cada tramo.</para>
///
/// <para><b>Todo rechazo va antes de tocar ninguna capacidad</b>, con un código por causa (las mismas de la ruta
/// directa): equipo sin oferta, ventana mal puesta, más días que el tope del despliegue, fuera de los límites del
/// equipo, y unidades fuera de rango.</para>
///
/// <para><b>Las unidades llegan como lista</b> (<c>[{ "unidad": 1 }, …]</c>) porque un bloque <c>para_cada</c>
/// sólo repite sobre una entrada; la lista tiene que ser 1..N, sin repetidos, y ése es el número de unidades.</para>
/// </remarks>
public sealed class PasoTramo(OfertaDeAlquiler ofertas, int maxDiasDelDespliegue) : IPaso
{
    public string Tipo => "alquiler.tramo";
    public int Lecturas => 3;
    public int Escrituras => 3;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura =>
        [typeof(IReadOnlyList<(Ref Sujeto, int Cantidad)>), typeof(IReadOnlyList<(Ref Sujeto, int Cantidad)>), typeof(Ref)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var equipo = entrada.Lee<string>(0).Trim();
        var unidades = entrada.Lee<IReadOnlyList<FlowContext>>(1);
        var ventana = entrada.Lee<TimeWindow>(2);

        return Task.FromResult(Revisar(equipo, unidades, ventana) is { } no
            ? SalidaDePaso.Rechaza(no)
            : Escribir(equipo, unidades.Count, Dias(ventana)));
    }

    /// <summary>Cuántos días cubre la ventana: del día 1 al día 2 es un día, como en el catálogo.</summary>
    public static int Dias(TimeWindow ventana)
        => (int)(ventana.End.UtcDateTime.Date - ventana.Start.UtcDateTime.Date).TotalDays;

    /// <summary>Desde cuántos días aplica el tramo que le toca a esa duración: el más alto que la cubre, o 1.</summary>
    public static int TramoPara(OfertaDeAlquilerRegistrada oferta, int dias)
        => oferta.Tramos.Where(t => t.DesdeDias <= dias).Select(t => t.DesdeDias).DefaultIfEmpty(1).Max();

    private SalidaDePaso Escribir(string equipo, int unidades, int dias)
    {
        var oferta = ofertas.Registrada(equipo)!;
        IReadOnlyList<(Ref Sujeto, int Cantidad)> alquiler = [(RentalSubject.TarifaOf(equipo, TramoPara(oferta, dias)), dias * unidades)];
        IReadOnlyList<(Ref Sujeto, int Cantidad)> garantia = [(RentalSubject.GarantiaUnidadOf(equipo), unidades)];
        return SalidaDePaso.Sigue(alquiler, garantia, RentalSubject.For(equipo));
    }

    private Rejection? Revisar(string equipo, IReadOnlyList<FlowContext> unidades, TimeWindow ventana)
    {
        if (ofertas.Registrada(equipo) is not { Retirada: false } oferta)
        {
            return Rejection.NotFound("alquiler.equipment_not_offered",
                $"El equipo {equipo} no tiene oferta publicada para alquilarse por aquí.");
        }

        var dias = Dias(ventana);
        if (dias <= 0)
        {
            return Rejection.Invalid("alquiler.bad_window", "La devolución tiene que ser posterior al retiro: del 1 al 2 es un día.");
        }
        if (maxDiasDelDespliegue > 0 && dias > maxDiasDelDespliegue)
        {
            return Rejection.Invalid("alquiler.window_too_long",
                $"Este despliegue no alquila más de {maxDiasDelDespliegue} días: una autorización de garantía no dura más.");
        }
        if (dias < oferta.MinDias || dias > oferta.MaxDias)
        {
            return Rejection.Invalid("alquiler.window_out_of_bounds",
                $"El equipo {equipo} se alquila entre {oferta.MinDias} y {oferta.MaxDias} días.");
        }

        var numeros = unidades.Select(u => u.Get<int>("unidad")).ToList();
        if (numeros.Count < 1 || numeros.Count > AlquilerPasos.MaxUnidades || numeros.Count > oferta.Unidades
            || !numeros.Order().SequenceEqual(Enumerable.Range(1, numeros.Count)))
        {
            return Rejection.Invalid("alquiler.bad_quantity",
                $"Hay que alquilar entre 1 y {Math.Min(AlquilerPasos.MaxUnidades, oferta.Unidades)} unidades, numeradas 1..N sin repetir.");
        }

        return null;
    }
}
