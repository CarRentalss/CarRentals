using System.Diagnostics;
using Broker.Api.Auth;

namespace Broker.Api.Middleware;

/// <summary>
/// Audyt wywołań Brokera: kto (oid albo anonim), co, z jakim wynikiem i jak długo.
/// Nie logujemy nagłówków (ciasteczko, X-CSRF), query stringa ani treści (dane osobowe z profilu i wyceny).
/// Pliki statyczne Reacta nie przechodzą przez ten middleware.
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
            // oid to identyfikator konta, a nie imię czy email, więc można go logować
            var caller = context.User.Identity?.IsAuthenticated == true
                ? $"user:{context.User.GetObjectId() ?? "unknown"}"
                : "anonymous";

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
