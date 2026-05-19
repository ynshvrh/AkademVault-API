// TODO(xsrf-reenable): This filter is currently disabled (not registered in Program.cs).
// XSRF was turned off because the cross-origin SPA (web on Cloudflare Pages, API on Render)
// cannot read a cookie set by this API origin via document.cookie, so Angular has no way to
// echo the token back as the X-XSRF-TOKEN header. Restore this filter once the SPA can obtain
// the token (e.g., via a response header populated in AuthController + an Angular interceptor).
// See related TODO markers in Program.cs and Controllers/AuthController.cs.

/*
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AkademVault_API.Services;


// Authorization filter that runs CSRF validation on unsafe methods and emits a structured JSON 400 on failure.
public class JsonAntiforgeryFilter : IAsyncAuthorizationFilter
{
    private readonly IAntiforgery _antiforgery;

    public JsonAntiforgeryFilter(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    // Validates the antiforgery token for unsafe verbs unless the action opts out via [IgnoreAntiforgeryToken].
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)) return;

        var hasIgnore = context.ActionDescriptor.EndpointMetadata
            .OfType<IgnoreAntiforgeryTokenAttribute>()
            .Any();
        if (hasIgnore) return;

        try
        {
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new
            {
                message = "CSRF-токен відсутній або недійсний. Оновіть сторінку.",
                code = "antiforgery_failed"
            })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }
    }
}
*/
