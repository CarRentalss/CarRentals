using System.Diagnostics;
using CarRental.Api.Auth;

namespace CarRental.Api.Middleware;

/// <summary>
/// Audyt wywołań API: kto, co, z jakim wynikiem i jak długo.
/// Nie logujemy nagłówków (klucz API, token) ani query stringa (mogą być dane osobowe).
/// W KM3 te logi pójdą do Application Insights.
/// </summary>
public sealed class AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var caller = context.User.FindFirst(AuthConstants.BrokerClaim)?.Value
                ?? context.User.Identity?.Name
                ?? "anonymous";

            logger.LogInformation(
                "Audit {Method} {Path} -> {StatusCode} in {ElapsedMs} ms, caller: {Caller}, traceId: {TraceId}",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                caller,
                Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
        }
    }
}
