using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Broker.Api.Auth;

/// <summary>
/// Ochrona przed CSRF dla API z ciasteczkiem: POST/PUT/PATCH/DELETE muszą mieć nagłówek X-CSRF: 1.
/// Nasz front zawsze go dodaje, a formularz albo link z innej strony nie może ustawić własnego nagłówka
/// (a fetch z innej domeny z takim nagłówkiem wymaga CORS, którego Broker nie włącza).
/// </summary>
public sealed class CsrfHeaderFilter : IResourceFilter
{
    public const string HeaderName = "X-CSRF";
    public const string HeaderValue = "1";

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            return;
        }

        if (request.Headers[HeaderName] != HeaderValue)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = $"Brak nagłówka {HeaderName}: {HeaderValue}."
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" }
            };
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
