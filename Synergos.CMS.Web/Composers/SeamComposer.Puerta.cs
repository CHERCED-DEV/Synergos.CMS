using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Web.Composers;

public sealed partial class SeamComposer
{
    /// <summary>
    /// La puerta de los flujos (ADR 0140 F3): la tabla de los contratos incrustados, un cliente por
    /// orquestador con destino, y su configuración validada al arrancar.
    /// </summary>
    /// <remarks>
    /// <para><b>No nombra ningún orquestador ni ningún flujo</b>: los que hay salen de los contratos que
    /// el CMS lleva incrustados, y el destino de cada uno, por convención, de <c>Synergos:X</c> —la misma
    /// sección que ya lee el resto del CMS para hablar con él—. Un orquestador nuevo entra con su contrato,
    /// sin tocar este fichero.</para>
    ///
    /// <para><b>La tabla se arma acá, al componer</b>, y no al primer uso: un contrato que expone algo que la
    /// puerta no sabe pasar tiene que impedir el arranque, no la primera compra.</para>
    /// </remarks>
    private static void ComposePuerta(IUmbracoBuilder builder)
    {
        var services = builder.Services;
        var tabla = TablaDeLaPuerta.Incrustada();
        services.AddSingleton(tabla);

        var conDestino = new List<string>();
        foreach (var orquestador in tabla.Orquestadores)
        {
            var seccion = builder.Config.GetSection("Synergos").GetSection(orquestador);
            if (string.IsNullOrWhiteSpace(seccion["BaseUrl"])) continue;

            // El techo de la puerta, aunque la sección diga más: comprar espera treinta segundos al
            // orquestador, y la puerta tiene que contestar antes de que el TimeoutMiddleware corte.
            var destino = DestinoDelArbol.De(seccion, seccion["BaseUrl"]!, (int)ReenvioDeLaPuerta.Techo.TotalSeconds);
            services.AddClienteDelArbolDeServicios(
                ReenvioDeLaPuerta.Cliente(orquestador),
                destino with { Timeout = destino.Timeout < ReenvioDeLaPuerta.Techo ? destino.Timeout : ReenvioDeLaPuerta.Techo });
            conDestino.Add(orquestador);
        }

        services.AddSingleton(new DestinosDeLaPuerta(conDestino));
        services.AddSingleton<ReenvioDeLaPuerta>();
        services.AddOptions<PuertaSettings>().Bind(builder.Config.GetSection(PuertaSettings.Seccion)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<PuertaSettings>, ValidadorDeLaPuerta>();
    }
}
