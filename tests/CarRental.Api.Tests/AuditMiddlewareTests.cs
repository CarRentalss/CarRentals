using System.Text;
using CarRental.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CarRental.Api.Tests;

/// <summary>Logger, który zbiera wpisy w pamięci, żeby sprawdzić treść audytu.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception));
}

public class AuditMiddlewareTests
{
    private static DefaultHttpContext JsonRequest(string json)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/rentals";
        context.Request.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        return context;
    }

    [Fact]
    public async Task InvokeAsync_RequestWithSecretHeaders_DoesNotLogSecrets()
    {
        var logger = new ListLogger<AuditMiddleware>();
        var middleware = new AuditMiddleware(_ => Task.CompletedTask, logger);
        var context = JsonRequest("{}");
        context.Request.Headers["x-api-key"] = "tajny-klucz-brokera-123";
        context.Request.Headers.Authorization = "Bearer tajny-token";
        context.Request.Headers.Cookie = "session=tajne-ciasteczko";
        context.Request.Headers.UserAgent = "OurBroker/1.0";

        await middleware.InvokeAsync(context);

        var message = Assert.Single(logger.Messages);
        Assert.DoesNotContain("tajny-klucz-brokera-123", message, StringComparison.Ordinal);
        Assert.DoesNotContain("tajny-token", message, StringComparison.Ordinal);
        Assert.DoesNotContain("tajne-ciasteczko", message, StringComparison.Ordinal);
        Assert.Contains("User-Agent=OurBroker/1.0", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_JsonBodyWithPersonalData_LogsMaskedBodyAndKeepsOriginalForController()
    {
        var logger = new ListLogger<AuditMiddleware>();
        string? bodySeenByController = null;
        var middleware = new AuditMiddleware(async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            bodySeenByController = await reader.ReadToEndAsync();
            context.Response.StatusCode = StatusCodes.Status202Accepted;
        }, logger);
        const string json = "{\"offerId\":\"abc\",\"firstName\":\"Jan\",\"lastName\":\"Kowalski\",\"email\":\"jan.kowalski@example.com\"}";

        await middleware.InvokeAsync(JsonRequest(json));

        Assert.Equal(json, bodySeenByController);
        var message = Assert.Single(logger.Messages);
        Assert.Contains("POST /api/v1/rentals -> 202", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Kowalski", message, StringComparison.Ordinal);
        Assert.DoesNotContain("jan.kowalski@", message, StringComparison.Ordinal);
        Assert.Contains("j***@example.com", message, StringComparison.Ordinal);
        Assert.Contains("\"offerId\":\"abc\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_MultipartRequest_DoesNotLogBody()
    {
        var logger = new ListLogger<AuditMiddleware>();
        var middleware = new AuditMiddleware(_ => Task.CompletedTask, logger);
        var context = JsonRequest("%PDF-1.4 plik");
        context.Request.ContentType = "multipart/form-data; boundary=x";

        await middleware.InvokeAsync(context);

        var message = Assert.Single(logger.Messages);
        Assert.DoesNotContain("%PDF", message, StringComparison.Ordinal);
        Assert.Contains("body: -", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatAllowedHeaders_NoAllowedHeaders_ReturnsDash()
    {
        var headers = new HeaderDictionary { ["x-api-key"] = "klucz", ["X-Custom"] = "cos" };

        var result = AuditMiddleware.FormatAllowedHeaders(headers);

        Assert.Equal("-", result);
    }

    [Fact]
    public void ForbiddenHeaders_ContainsApiKeyAuthorizationAndCookie()
    {
        Assert.Contains("x-api-key", AuditMiddleware.ForbiddenHeaders);
        Assert.Contains("Authorization", AuditMiddleware.ForbiddenHeaders);
        Assert.Contains("Cookie", AuditMiddleware.ForbiddenHeaders);
        Assert.DoesNotContain(AuditMiddleware.AllowedHeaders, h => AuditMiddleware.ForbiddenHeaders.Contains(h));
    }
}

public class SensitiveDataMaskerTests
{
    [Theory]
    [InlineData("jan.kowalski@example.com", "j***@example.com")]
    [InlineData("bez-malpy", "***")]
    [InlineData("", "***")]
    [InlineData(null, "***")]
    public void MaskEmail_VariousInputs_HidesLocalPart(string? email, string expected)
    {
        Assert.Equal(expected, SensitiveDataMasker.MaskEmail(email));
    }

    [Fact]
    public void MaskName_Surname_KeepsOnlyFirstLetter()
    {
        Assert.Equal("K***", SensitiveDataMasker.MaskName("Kowalski"));
    }

    [Fact]
    public void MaskJson_NestedPersonalFields_MasksAllLevelsCaseInsensitive()
    {
        var masked = SensitiveDataMasker.MaskJson("{\"customer\":{\"FirstName\":\"Anna\",\"EMAIL\":\"anna@x.pl\"},\"items\":[{\"lastName\":\"Nowak\"}],\"carId\":1}");

        Assert.NotNull(masked);
        Assert.DoesNotContain("Anna", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("Nowak", masked, StringComparison.Ordinal);
        Assert.Contains("a***@x.pl", masked, StringComparison.Ordinal);
        Assert.Contains("\"carId\":1", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void MaskJson_InvalidJson_ReturnsNull()
    {
        Assert.Null(SensitiveDataMasker.MaskJson("to nie jest json"));
    }
}
