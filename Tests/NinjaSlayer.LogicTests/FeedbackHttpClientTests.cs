using System.Net;
using System.Net.Http.Headers;
using NinjaSlayer.Code.Feedback;

namespace NinjaSlayer.LogicTests;

public sealed class FeedbackHttpClientTests
{
    private static readonly Uri Endpoint = new("https://feedback.invalid/upload");

    [Fact]
    public void DefaultsMatchTheFeedbackBudgetContract()
    {
        var options = new FeedbackHttpClientOptions(Endpoint);

        Assert.Equal(3, options.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(10), options.AttemptTimeout);
        Assert.Equal(TimeSpan.FromSeconds(35), options.TotalBudget);
        Assert.Equal(TimeSpan.FromSeconds(5), options.MaxRetryDelay);
        Assert.Equal(4 * 1024, options.MaxErrorResponseBytes);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task RetriesOnlyRetryableHttpResponses(int statusCode)
    {
        var handler = new QueueHandler(
            Immediate((HttpStatusCode)statusCode),
            Immediate(HttpStatusCode.OK));
        var delays = new RecordingDelayStrategy();
        FeedbackHttpClient client = CreateClient(handler, delays: delays);

        FeedbackSendResult result = await client.SendAsync(CreateRequest);

        Assert.Equal(FeedbackSendOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, handler.CallCount);
        Assert.Single(delays.Delays);
    }

    [Fact]
    public async Task RejectsNonRetryableHttpResponsesImmediatelyAndBoundsTheBody()
    {
        var handler = new QueueHandler(Immediate(HttpStatusCode.BadRequest, new string('x', 5000)));
        var delays = new RecordingDelayStrategy();
        FeedbackHttpClient client = CreateClient(handler, delays: delays);

        FeedbackSendResult result = await client.SendAsync(CreateRequest);

        Assert.Equal(FeedbackSendOutcome.Rejected, result.Outcome);
        Assert.Equal(1, handler.CallCount);
        Assert.Empty(delays.Delays);
        Assert.Equal(4096, Assert.Single(result.Attempts).Detail?.Length);
    }

    [Fact]
    public async Task RetriesNetworkErrors()
    {
        var handler = new QueueHandler(
            _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")),
            Immediate(HttpStatusCode.OK));
        FeedbackHttpClient client = CreateClient(handler, delays: new RecordingDelayStrategy());

        FeedbackSendResult result = await client.SendAsync(CreateRequest);

        Assert.True(result.IsSuccess);
        Assert.Equal(FeedbackAttemptFailure.Network, result.Attempts[0].Failure);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CallerCancellationNeverRetries()
    {
        var handler = new QueueHandler(Immediate(HttpStatusCode.OK));
        FeedbackHttpClient client = CreateClient(handler, delays: new RecordingDelayStrategy());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync(CreateRequest, cancellation.Token));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task RetriesAttemptTimeouts()
    {
        var handler = new QueueHandler(
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Response(HttpStatusCode.OK);
            },
            Immediate(HttpStatusCode.OK));
        var options = new FeedbackHttpClientOptions(
            Endpoint,
            attemptTimeout: TimeSpan.FromMilliseconds(20),
            totalBudget: TimeSpan.FromSeconds(1));
        FeedbackHttpClient client = CreateClient(
            handler,
            options,
            TimeProvider.System,
            new RecordingDelayStrategy());

        FeedbackSendResult result = await client.SendAsync(CreateRequest);

        Assert.True(result.IsSuccess);
        Assert.Equal(FeedbackAttemptFailure.Timeout, result.Attempts[0].Failure);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CapsRetryAfterDeltaAtFiveSeconds()
    {
        HttpResponseMessage retry = Response((HttpStatusCode)429);
        retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        var handler = new QueueHandler(_ => Task.FromResult(retry), Immediate(HttpStatusCode.OK));
        var delays = new RecordingDelayStrategy();
        FeedbackHttpClient client = CreateClient(handler, delays: delays);

        FeedbackSendResult result = await client.SendAsync(CreateRequest);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(delays.Delays));
    }

    [Fact]
    public async Task UsesInjectedClockForAbsoluteRetryAfter()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero));
        HttpResponseMessage retry = Response((HttpStatusCode)429);
        retry.Headers.RetryAfter = new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(3));
        var handler = new QueueHandler(_ => Task.FromResult(retry), Immediate(HttpStatusCode.OK));
        var delays = new RecordingDelayStrategy();
        FeedbackHttpClient client = CreateClient(handler, timeProvider: clock, delays: delays);

        FeedbackSendResult result = await client.SendAsync(CreateRequest);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(delays.Delays));
    }

    [Fact]
    public async Task StopsWhenRetryDelayConsumesTheTotalBudget()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var handler = new QueueHandler(
            Immediate(HttpStatusCode.InternalServerError),
            Immediate(HttpStatusCode.OK));
        var options = new FeedbackHttpClientOptions(
            Endpoint,
            totalBudget: TimeSpan.FromSeconds(1),
            fallbackRetryDelays: [TimeSpan.FromSeconds(2)]);
        var delays = new RecordingDelayStrategy();
        FeedbackHttpClient client = CreateClient(handler, options, clock, delays);

        FeedbackSendResult result = await client.SendAsync(CreateRequest);

        Assert.Equal(FeedbackSendOutcome.BudgetExhausted, result.Outcome);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(delays.Delays));
    }

    [Fact]
    public async Task TransportDoesNotCloseWrappedCallerStream()
    {
        var stream = new SentinelStream([1, 2, 3]);
        var handler = new QueueHandler(Immediate(HttpStatusCode.OK));
        FeedbackHttpClient client = CreateClient(handler, delays: new RecordingDelayStrategy());

        FeedbackSendResult result = await client.SendAsync(uri => new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = new StreamContent(new FeedbackNonDisposingStream(stream)),
        });

        Assert.True(result.IsSuccess);
        Assert.False(stream.IsClosed);
        stream.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnershipWrapperClosesBothStreamsOnEveryExit(bool throwDuringSend)
    {
        var screenshot = new SentinelStream([]);
        var logs = new SentinelStream([]);

        async Task<bool> Send()
        {
            await Task.Yield();
            return throwDuringSend ? throw new InvalidOperationException("send failed") : true;
        }

        if (throwDuringSend)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => FeedbackStreamOwnership.SendAndCloseAsync(Send, screenshot, logs));
        }
        else
        {
            Assert.True(await FeedbackStreamOwnership.SendAndCloseAsync(Send, screenshot, logs));
        }

        Assert.True(screenshot.IsClosed);
        Assert.True(logs.IsClosed);
    }

    private static FeedbackHttpClient CreateClient(
        QueueHandler handler,
        FeedbackHttpClientOptions? options = null,
        TimeProvider? timeProvider = null,
        IRetryDelayStrategy? delays = null) =>
        new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            options ?? new FeedbackHttpClientOptions(Endpoint),
            timeProvider ?? TimeProvider.System,
            delays ?? new RecordingDelayStrategy());

    private static HttpRequestMessage CreateRequest(Uri uri) =>
        new(HttpMethod.Put, uri) { Content = new StringContent("payload") };

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string? body = null) =>
        new(statusCode) { Content = new StringContent(body ?? string.Empty) };

    private static Func<CancellationToken, Task<HttpResponseMessage>> Immediate(
        HttpStatusCode statusCode,
        string? body = null) =>
        _ => Task.FromResult(Response(statusCode, body));

    private sealed class QueueHandler(params Func<CancellationToken, Task<HttpResponseMessage>>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new(responses);

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (!_responses.TryDequeue(out Func<CancellationToken, Task<HttpResponseMessage>>? response))
            {
                throw new InvalidOperationException("No queued feedback response remains.");
            }

            return response(cancellationToken);
        }
    }

    private sealed class RecordingDelayStrategy : IRetryDelayStrategy
    {
        public List<TimeSpan> Delays { get; } = [];

        public ValueTask DelayAsync(
            TimeSpan delay,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            if (timeProvider is ManualTimeProvider manual)
            {
                manual.Advance(delay);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan elapsed)
        {
            _utcNow += elapsed;
            _timestamp += elapsed.Ticks;
        }
    }

    private sealed class SentinelStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool IsClosed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsClosed = true;
            base.Dispose(disposing);
        }
    }
}
