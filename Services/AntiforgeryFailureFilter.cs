using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AkademVault_API.Services;


public class JsonAntiforgeryFilter : IAsyncAuthorizationFilter
{
    private readonly IAntiforgery _antiforgery;

    public JsonAntiforgeryFilter(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

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
