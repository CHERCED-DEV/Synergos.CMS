using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services;

/// <summary>
/// Qué franjas de visita EXISTEN para un listado (HU #33a).
/// </summary>
/// <remarks>
/// <para><b>Esto se queda del lado del CMS, y no es un descuido.</b> Cuáles son los horarios de
/// visita de un inmueble es una decisión del negocio inmobiliario —la agenda del agente—, no del
/// cupo. <c>Api.Booking</c> sabe si una ventana concreta está libre; no sabe, ni tiene por qué
/// saber, que este vertical enseña a las 9 y a las 11 durante los tres días siguientes.</para>
///
/// <para><b>Es exactamente el reparto que la HU #33 tenía sin resolver:</b> el dato descriptivo se
/// queda donde vive el contenido, el cupo vive en la capacidad. No son «dos verdades que
/// sincronizar» — son dos cosas distintas, y por eso ninguna de las dos duplica a la otra.</para>
///
/// <para><b>Existe como tipo aparte porque tiene DOS implementaciones que lo necesitan</b> (el
/// stub en proceso y el cliente HTTP), que es el listón de <c>CLAUDE.md</c> §6 para sacar algo a
/// su propio sitio. Antes de que hubiera dos, vivía dentro del stub y estaba bien ahí.</para>
/// </remarks>
public static class VisitAgenda
{
    /// <summary>A partir de mañana: hoy ya no da tiempo a coordinar una visita.</summary>
    private const int PrimerDia = 1;

    /// <summary>Cuántos días de agenda se ofrecen.</summary>
    private const int Dias = 3;

    /// <summary>Las horas de visita, en UTC.</summary>
    private static readonly int[] Horas = { 9, 11 };

    /// <summary>Cuánto dura una visita. Es lo que se aparta como ventana en la capacidad.</summary>
    public static readonly TimeSpan Duracion = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Las franjas del listado, todas marcadas como disponibles.
    /// </summary>
    /// <remarks>
    /// Devuelve la agenda <b>sin consultar cupo</b>: quién dice si una franja sigue libre es el
    /// almacén (en el stub) o <c>Api.Booking</c> (cableado). Mezclar las dos cosas acá dejaría a
    /// este tipo necesitando un almacén, y entonces ya no sería una función del listado.
    /// </remarks>
    public static IReadOnlyList<VisitSlot> For(string listingId, DateTimeOffset ahora)
    {
        var baseDay = ahora.UtcDateTime.Date.AddDays(PrimerDia);
        var slots = new List<VisitSlot>(Dias * Horas.Length);

        for (var day = 0; day < Dias; day++)
        {
            foreach (var hour in Horas)
            {
                var start = new DateTimeOffset(baseDay.AddDays(day).AddHours(hour), TimeSpan.Zero);
                slots.Add(new VisitSlot($"{listingId}-{start:yyyyMMddHHmm}", start));
            }
        }

        return slots;
    }

    /// <summary>La franja concreta, o <c>null</c> si ese id no es de este listado.</summary>
    public static VisitSlot? Find(string listingId, string slotId, DateTimeOffset ahora)
        => For(listingId, ahora).FirstOrDefault(s =>
            string.Equals(s.Id, slotId, StringComparison.OrdinalIgnoreCase));
}
