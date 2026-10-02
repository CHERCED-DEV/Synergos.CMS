using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;

namespace Synergos.CMS.Web.Services.Listados;

/// <summary>
/// Los cursos del catálogo de Educación: el mismo proveedor que lee la app de la academia, así que
/// el listado y la app no pueden contar cursos distintos.
/// </summary>
public sealed class CursosDelCatalogo : IFuenteDeListado
{
    private readonly ICourseCatalogProvider _catalogo;

    public CursosDelCatalogo(ICourseCatalogProvider catalogo) => _catalogo = catalogo;

    public string Clave => "cursos";

    public IReadOnlyList<FilaDelListado> Filas(PeticionDelListado peticion)
    {
        ArgumentNullException.ThrowIfNull(peticion);
        // El proveedor es asíncrono por contrato y los de este CMS responden en memoria; el
        // resolver de un elemento es síncrono (se llama desde la vista).
        var resultado = _catalogo.SearchAsync(new CourseQuery(Text: peticion.Consulta)).GetAwaiter().GetResult();

        return resultado.Courses
            .Where(c => FormatoDelListado.Responde(peticion.Consulta, c.Title, c.Category, c.Level, c.InstructorName))
            .Select(c => new FilaDelListado(
                Id: c.Id,
                Title: c.Title,
                Image: c.CoverImageUrl,
                ImageAlt: c.Title,
                Badge: c.Category,
                Specs:
                [
                    new(peticion.Rotulo("DataGrid.Level", "Nivel"), c.Level),
                    new(peticion.Rotulo("DataGrid.Duration", "Duración"), FormatoDelListado.Duracion(c.DurationMinutes)),
                    new(peticion.Rotulo("DataGrid.Price", "Precio"), c.IsFree
                        ? peticion.Rotulo("DataGrid.Free", "Gratis")
                        : FormatoDelListado.Precio(c.Price, c.Currency, peticion.Cultura)),
                ]))
            .ToList();
    }
}

/// <summary>Los eventos del catálogo de Eventos: el mismo proveedor que lee la app de eventos.</summary>
public sealed class EventosDelCatalogo : IFuenteDeListado
{
    private readonly IEventCatalogProvider _catalogo;

    public EventosDelCatalogo(IEventCatalogProvider catalogo) => _catalogo = catalogo;

    public string Clave => "eventos";

    public IReadOnlyList<FilaDelListado> Filas(PeticionDelListado peticion)
    {
        ArgumentNullException.ThrowIfNull(peticion);
        var eventos = _catalogo.SearchAsync(peticion.Consulta).GetAwaiter().GetResult();

        return eventos
            .Where(e => FormatoDelListado.Responde(peticion.Consulta, e.Title, e.Category, e.City, e.Venue))
            .OrderBy(e => e.StartUtc)
            .Select(e => new FilaDelListado(
                Id: e.Id,
                Title: e.Title,
                Image: string.IsNullOrWhiteSpace(e.ImageUrl) ? null : e.ImageUrl,
                ImageAlt: e.Title,
                Badge: e.Category,
                Specs:
                [
                    new(peticion.Rotulo("DataGrid.Date", "Fecha"), FormatoDelListado.Fecha(e.StartUtc, peticion.Cultura, peticion.Zona)),
                    new(peticion.Rotulo("DataGrid.Place", "Lugar"), string.IsNullOrWhiteSpace(e.Venue) ? e.City : $"{e.Venue}, {e.City}"),
                    new(peticion.Rotulo("DataGrid.PriceFrom", "Desde"), FormatoDelListado.Precio(e.PriceFrom, e.Currency, peticion.Cultura)),
                ]))
            .ToList();
    }
}

/// <summary>Los inmuebles del catálogo de Propiedades: el mismo proveedor que lee la app del portal.</summary>
public sealed class InmueblesDelCatalogo : IFuenteDeListado
{
    private readonly IPropertyCatalogProvider _catalogo;

    public InmueblesDelCatalogo(IPropertyCatalogProvider catalogo) => _catalogo = catalogo;

    public string Clave => "inmuebles";

    public IReadOnlyList<FilaDelListado> Filas(PeticionDelListado peticion)
    {
        ArgumentNullException.ThrowIfNull(peticion);
        var resultado = _catalogo.SearchAsync(new PropertyQuery(Text: peticion.Consulta)).GetAwaiter().GetResult();

        return resultado.Listings
            .Where(l => FormatoDelListado.Responde(peticion.Consulta, l.Title, l.Type, l.City, l.Neighborhood))
            .Select(l => new FilaDelListado(
                Id: l.Id,
                Title: l.Title,
                Image: string.IsNullOrWhiteSpace(l.ImageUrl) ? null : l.ImageUrl,
                ImageAlt: l.Title,
                Badge: FormatoDelListado.Etiqueta(l.Type, peticion.Cultura),
                Specs:
                [
                    new(peticion.Rotulo("DataGrid.Location", "Ubicación"), string.IsNullOrWhiteSpace(l.Neighborhood) ? l.City : $"{l.Neighborhood}, {l.City}"),
                    new(peticion.Rotulo("DataGrid.Price", "Precio"), FormatoDelListado.Precio(l.Price, l.Currency, peticion.Cultura)),
                ]))
            .ToList();
    }
}
