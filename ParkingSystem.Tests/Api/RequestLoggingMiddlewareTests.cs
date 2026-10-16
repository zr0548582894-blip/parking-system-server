using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ParkingSystem.API.Middleware;
using ParkingSystem.Tests.Helpers;

namespace ParkingSystem.Tests.Api;

public class RequestLoggingMiddlewareTests
{
    private readonly CapturingLogger<RequestLoggingMiddleware> _logger = new();

    private static DefaultHttpContext NewContext(string method = "GET", string path = "/api/parking-spots", string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    [Fact]
    public async Task LogsExactlyOneInformationLineWithMethodPathAndStatus()
    {
        var context = NewContext();
        var middleware = new RequestLoggingMiddleware(next => { next.Response.StatusCode = 200; return Task.CompletedTask; }, _logger);

        await middleware.InvokeAsync(context);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("GET /api/parking-spots -> 200", entry.Message);
        Assert.Contains(" ms", entry.Message);
    }

    [Fact]
    public async Task LogsTheStatusCodeTheRequestEndedWith()
    {
        var context = NewContext("POST", "/api/reservations");
        var middleware = new RequestLoggingMiddleware(next => { next.Response.StatusCode = 409; return Task.CompletedTask; }, _logger);

        await middleware.InvokeAsync(context);

        Assert.Contains("POST /api/reservations -> 409", Assert.Single(_logger.Entries).Message);
    }

    [Fact]
    public async Task DoesNotLogTheQueryStringOrAnySecretFromHeadersAndBody()
    {
        var context = NewContext(query: "?password=hunter2&token=abc123");
        context.Request.Headers.Authorization = "Bearer very-secret-token";
        var middleware = new RequestLoggingMiddleware(_ => Task.CompletedTask, _logger);

        await middleware.InvokeAsync(context);

        var message = Assert.Single(_logger.Entries).Message;
        Assert.DoesNotContain("hunter2", message);
        Assert.DoesNotContain("abc123", message);
        Assert.DoesNotContain("very-secret-token", message);
        Assert.DoesNotContain("?", message);
    }

    [Fact]
    public async Task AnEncodedNewlineInThePath_CannotCreateAFakeLogLine()
    {
        var context = NewContext(path: "/api/x\n2030-01-01|ERROR|fake line");
        var middleware = new RequestLoggingMiddleware(_ => Task.CompletedTask, _logger);

        await middleware.InvokeAsync(context);

        var message = Assert.Single(_logger.Entries).Message;
        Assert.DoesNotContain("\n", message);
        Assert.DoesNotContain("\r", message);
    }

    [Fact]
    public async Task WhenTheRequestThrows_LogsStatus500AndRethrowsForTheExceptionHandler()
    {
        var context = NewContext();
        var middleware = new RequestLoggingMiddleware(_ => throw new InvalidOperationException("boom"), _logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Contains("-> 500", Assert.Single(_logger.Entries).Message);
    }

    [Fact]
    public async Task WhenTheClientDisconnects_LogsStatus499AndRethrows()
    {
        using var cts = new CancellationTokenSource();
        var context = NewContext();
        context.RequestAborted = cts.Token;
        cts.Cancel();
        var middleware = new RequestLoggingMiddleware(_ => throw new OperationCanceledException(), _logger);

        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        Assert.Contains("-> 499", Assert.Single(_logger.Entries).Message);
    }
}
