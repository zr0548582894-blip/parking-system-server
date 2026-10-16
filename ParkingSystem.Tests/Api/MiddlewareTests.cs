using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using ParkingSystem.API.Middleware;

namespace ParkingSystem.Tests.Api;

public class MiddlewareTests
{
    private readonly Mock<ILogger<ExceptionHandlingMiddleware>> _exceptionLogger = new();

    private static DefaultHttpContext NewContext(string? correlationHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/test";
        if (correlationHeader is not null)
        {
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = correlationHeader;
        }

        return context;
    }

    private static CorrelationIdMiddleware CorrelationMiddleware(RequestDelegate next) =>
        new(next, Mock.Of<ILogger<CorrelationIdMiddleware>>());

    private ExceptionHandlingMiddleware ExceptionMiddleware(RequestDelegate next) =>
        new(next, _exceptionLogger.Object);

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }

    private void VerifyLogged(LogLevel level, Times times) =>
        _exceptionLogger.Verify(l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => true),
            It.IsAny<Exception?>(),
            It.Is<Func<It.IsAnyType, Exception?, string>>((formatter, _) => true)), times);

    // ---------- CorrelationId ----------

    [Fact]
    public async Task CorrelationId_NoIncomingHeader_GeneratesOneAndReturnsItInTheResponse()
    {
        var context = NewContext();

        await CorrelationMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        var id = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal(id, CorrelationIdMiddleware.Get(context));
    }

    [Fact]
    public async Task CorrelationId_SafeIncomingValue_IsKept()
    {
        var context = NewContext("client-id_123");

        await CorrelationMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal("client-id_123", CorrelationIdMiddleware.Get(context));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("line\nbreak")]
    [InlineData("<script>")]
    public async Task CorrelationId_UnsafeIncomingValue_IsReplacedWithAGeneratedOne(string unsafeValue)
    {
        var context = NewContext(unsafeValue);

        await CorrelationMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.NotEqual(unsafeValue, CorrelationIdMiddleware.Get(context));
    }

    [Fact]
    public async Task CorrelationId_TooLongIncomingValue_IsReplacedWithAGeneratedOne()
    {
        var tooLong = new string('a', 65);
        var context = NewContext(tooLong);

        await CorrelationMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.NotEqual(tooLong, CorrelationIdMiddleware.Get(context));
    }

    // ---------- טיפול בשגיאות ----------

    [Fact]
    public async Task ExceptionHandling_NoException_PassesTheRequestThrough()
    {
        var context = NewContext();
        context.Response.StatusCode = StatusCodes.Status200OK;

        await ExceptionMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        VerifyLogged(LogLevel.Error, Times.Never());
    }

    [Fact]
    public async Task ExceptionHandling_UnexpectedException_Returns500ProblemJsonWithoutInternalDetailsAndLogsError()
    {
        var context = NewContext();

        await ExceptionMiddleware(_ => throw new InvalidOperationException("secret internal detail")).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        var body = await ReadBodyAsync(context);
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
        Assert.DoesNotContain("secret internal detail", body.ToString());

        VerifyLogged(LogLevel.Error, Times.Once());
    }

    [Fact]
    public async Task ExceptionHandling_AfterCorrelationMiddleware_ResponseBodyAndHeaderCarryTheSameId()
    {
        var context = NewContext("trace-42");

        // אותו סדר כמו ב-Program.cs: טיפול בשגיאות מבחוץ, ו-CorrelationId בתוכו
        var pipeline = ExceptionMiddleware(
            CorrelationMiddleware(_ => throw new InvalidOperationException("boom")).InvokeAsync);

        await pipeline.InvokeAsync(context);

        var body = await ReadBodyAsync(context);
        Assert.Equal("trace-42", body.GetProperty("correlationId").GetString());
        Assert.Equal("trace-42", context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task ExceptionHandling_BadHttpRequest_ReturnsItsStatusAndLogsWarningNotError()
    {
        var context = NewContext();

        await ExceptionMiddleware(_ => throw new BadHttpRequestException("too big", StatusCodes.Status413PayloadTooLarge))
            .InvokeAsync(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        VerifyLogged(LogLevel.Warning, Times.Once());
        VerifyLogged(LogLevel.Error, Times.Never());
    }

    [Fact]
    public async Task ExceptionHandling_ClientDisconnected_WritesNothingAndLogsNoError()
    {
        using var cts = new CancellationTokenSource();
        var context = NewContext();
        context.RequestAborted = cts.Token;
        cts.Cancel();

        await ExceptionMiddleware(_ => throw new OperationCanceledException()).InvokeAsync(context);

        Assert.Equal(0, context.Response.Body.Length);
        VerifyLogged(LogLevel.Error, Times.Never());
    }
}
