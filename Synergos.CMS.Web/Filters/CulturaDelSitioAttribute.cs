using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Filters;

/// <summary>
/// La página que pinta un controlador MVC se pinta en la cultura de SU sitio
/// (<see cref="SitioDeLaPeticion"/>): la del dominio, o la por defecto.
/// </summary>
/// <remarks>
/// <para><b>Lo que arregla</b> (#190): <c>/blog/tag/*</c> publicaba el bridge con
/// <c>culture: "en-US"</c>, <c>&lt;html lang="en"&gt;</c> y las fechas en inglés («12 Jun 2026»),
/// en un sitio <c>es-CO</c>. Umbraco fija la cultura al rutear contenido; una ruta MVC propia no
/// pasa por ahí y se queda con la de ASP.NET.</para>
///
/// <para><b>Por qué un atributo y no la cultura de TODAS las peticiones no ruteadas</b>: por ahí
/// pasan también las APIs, y cambiarles la cultura cambia cómo formatean y leen números —en es-CO
/// el punto es de miles—. Lo que se arregla es la PÁGINA, y la página la declara su controlador.</para>
///
/// <para><b>Es filtro de RECURSO y asíncrono a propósito</b>: la cultura vive en el contexto de
/// ejecución, y fijarla dentro de la acción no llega a la vista —que se pinta después, fuera de
/// ella—. Fijada aquí, antes de <c>next()</c>, la ven la acción, la vista, el layout y el bridge. Y
/// al volver se restaura sola.</para>
///
/// <para>Sólo toca la cultura del hilo —lo que leen el bridge, el diccionario y los formatos—. El
/// contenido publicado se sigue leyendo en la variante que Umbraco pone por defecto: hoy no hay
/// contenido en otra cultura, y una página de un dominio <c>en-US</c> sin traducir saldría vacía en
/// vez de en español.</para>
/// </remarks>
public sealed class CulturaDelSitioAttribute : TypeFilterAttribute
{
    public CulturaDelSitioAttribute() : base(typeof(CulturaDelSitioFilter))
    {
    }

    private sealed class CulturaDelSitioFilter : IAsyncResourceFilter
    {
        private readonly SitioDeLaPeticion _sitio;

        public CulturaDelSitioFilter(SitioDeLaPeticion sitio) => _sitio = sitio;

        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            var cultura = Cultura(_sitio.Resolver().Cultura);
            if (cultura is not null)
            {
                CultureInfo.CurrentCulture = cultura;
                CultureInfo.CurrentUICulture = cultura;
            }

            await next().ConfigureAwait(false);
        }

        private static CultureInfo? Cultura(string? nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return null;
            }

            try
            {
                return CultureInfo.GetCultureInfo(nombre);
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }
    }
}
