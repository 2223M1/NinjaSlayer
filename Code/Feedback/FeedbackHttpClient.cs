using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace NinjaSlayer.Code.Feedback;

public sealed class FeedbackHttpClientOptions
{
    public FeedbackHttpClientOptions(
        Uri endpoint,
        int maxAttempts = 3,
        TimeSpan? attemptTimeout = null,
        TimeSpan? totalBudget = null,
        TimeSpan? maxRetryDelay = null,
        int maxErrorResponseBytes = 4 * 1024,
        IReadOnlyList<TimeSpan>? fallbackRetryDelays = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Feedback endpoint must be absolute.", nameof(endpoint));
        }

        Endpoint = endpoint;
        MaxAttempts = maxAttempts;
        AttemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(10);
        TotalBudget = totalBudget ?? TimeSpan.FromSeconds(35);
        MaxRetryDelay = maxRetryDelay ?? TimeSpan.FromSeconds(5);
        MaxErrorResponseBytes = maxErrorResponseBytes;
        TimeSpan[] retryDelays = fallbackRetryDelays is null
            ? [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1)]
            : [.. fallbackRetryDelays];
        FallbackRetryDelays = Array.AsReadOnly(retryDelays);

        if (MaxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        if (AttemptTimeout <= TimeSpan.Zero || TotalBudget <= TimeSpan.Zero || MaxRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptTimeout), "Feedback time limits must be positive.");
        }

        if (MaxErrorResponseBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxErrorResponseBytes));
        }

        if (FallbackRetryDelays.Count == 0 || FallbackRetryDelays.Any(delay => delay < TimeSpan.Zero))
        {
            throw new ArgumentException("At least one non-negative fallback retry delay is required.", nameof(fallbackRetryDelays));
        }
    }

    public Uri Endpoint { get; }

    public int MaxAttempts { get; }

    public TimeSpan AttemptTimeout { get; }

    public TimeSpan TotalBudget { get; }

    public TimeSpan MaxRetryDelay { get; }

    public int MaxErrorResponseBytes { get; }

    public IReadOnlyList<TimeSpan> FallbackRetryDelays { get; }

    internal TimeSpan GetFallbackRetryDelay(int retryNumber)
    {
        int index = Math.Clamp(retryNumber - 1, 0, FallbackRetryDelays.Count - 1);
        return FallbackRetryDelays[index];
    }
}

public interface IRetryDelayStrategy
{
    ValueTask DelayAsync(TimeSpan delay, TimeProvider timeProvider, CancellationToken cancellationToken);
}

public sealed class SystemRetryDelayStrategy : IRetryDelayStrategy
{
    public static SystemRetryDelayStrategy Instance { get; } = new();

    private SystemRetryDelayStrategy()
    {
    }

    public ValueTask DelayAsync(
        TimeSpan delay,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        new(Task.Delay(delay, timeProvider, cancellationToken));
}

public enum FeedbackSendOutcome
{
    Succeeded,
    Rejected,
    RetryExhausted,
    BudgetExhausted,
}

public enum FeedbackAttemptFailure
{
    None,
    HttpStatus,
    Network,
    Timeout,
}

public sealed record FeedbackAttemptDiagnostic(
    int Attempt,
    HttpStatusCode? StatusCode,
    FeedbackAttemptFailure Failure,
    string? Detail);

public sealed record FeedbackSendResult(
    FeedbackSendOutcome Outcome,
    IReadOnlyList<FeedbackAttemptDiagnostic> Attempts)
{
    public bool IsSuccess => Outcome == FeedbackSendOutcome.Succeeded;
}

public sealed class FeedbackHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly FeedbackHttpClientOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IRetryDelayStrategy _retryDelayStrategy;

    public FeedbackHttpClient(
        HttpClient httpClient,
        FeedbackHttpClientOptions options,
        TimeProvider timeProvider,
        IRetryDelayStrategy retryDelayStrategy)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _retryDelayStrategy = retryDelayStrategy ?? throw new ArgumentNullException(nameof(retryDelayStrategy));
    }

    public async Task<FeedbackSendResult> SendAsync(
        Func<Uri, HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestFactory);

        long startedAt = _timeProvider.GetTimestamp();
        var diagnostics = new List<FeedbackAttemptDiagnostic>(_options.MaxAttempts);
        for (int attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan remaining = GetRemainingBudget(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                return Complete(FeedbackSendOutcome.BudgetExhausted, diagnostics);
            }

            TimeSpan attemptTimeout = remaining < _options.AttemptTimeout ? remaining : _options.AttemptTimeout;
            TimeSpan? serverRetryDelay = null;
            using HttpRequestMessage request = requestFactory(_options.Endpoint)
                ?? throw new InvalidOperationException("Feedback request factory returned null.");
            using var timeoutSource = new CancellationTokenSource(attemptTimeout, _timeProvider);
            using var attemptSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);
            try
            {
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    attemptSource.Token);
                if (response.IsSuccessStatusCode)
                {
                    diagnostics.Add(new FeedbackAttemptDiagnostic(
                        attempt,
                        response.StatusCode,
                        FeedbackAttemptFailure.None,
                        null));
                    return Complete(FeedbackSendOutcome.Succeeded, diagnostics);
                }

                string responseBody;
                try
                {
                    responseBody = await ReadBoundedResponseAsync(
                        response.Content,
                        _options.MaxErrorResponseBytes,
                        attemptSource.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    responseBody = "Response body read timed out.";
                }
                catch (HttpRequestException ex)
                {
                    responseBody = $"Response body read failed: {ex.Message}";
                }
                catch (IOException ex)
                {
                    responseBody = $"Response body read failed: {ex.Message}";
                }

                diagnostics.Add(new FeedbackAttemptDiagnostic(
                    attempt,
                    response.StatusCode,
                    FeedbackAttemptFailure.HttpStatus,
                    responseBody));
                if (!IsRetryable(response.StatusCode))
                {
                    return Complete(FeedbackSendOutcome.Rejected, diagnostics);
                }

                serverRetryDelay = GetServerRetryDelay(response.Headers.RetryAfter);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                diagnostics.Add(new FeedbackAttemptDiagnostic(
                    attempt,
                    null,
                    FeedbackAttemptFailure.Timeout,
                    $"Timed out after {attemptTimeout.TotalSeconds:0.###} seconds."));
            }
            catch (HttpRequestException ex)
            {
                diagnostics.Add(new FeedbackAttemptDiagnostic(
                    attempt,
                    null,
                    FeedbackAttemptFailure.Network,
                    ex.Message));
            }

            if (attempt >= _options.MaxAttempts)
            {
                return Complete(FeedbackSendOutcome.RetryExhausted, diagnostics);
            }

            TimeSpan retryDelay = serverRetryDelay ?? _options.GetFallbackRetryDelay(attempt);
            if (!await DelayWithinBudgetAsync(retryDelay, startedAt, cancellationToken))
            {
                return Complete(FeedbackSendOutcome.BudgetExhausted, diagnostics);
            }
        }

        return Complete(FeedbackSendOutcome.RetryExhausted, diagnostics);
    }

    private async ValueTask<bool> DelayWithinBudgetAsync(
        TimeSpan requestedDelay,
        long startedAt,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = GetRemainingBudget(startedAt);
        if (remaining <= TimeSpan.Zero)
        {
            return false;
        }

        TimeSpan delay = requestedDelay < TimeSpan.Zero ? TimeSpan.Zero : requestedDelay;
        if (delay > _options.MaxRetryDelay)
        {
            delay = _options.MaxRetryDelay;
        }

        if (delay > remaining)
        {
            delay = remaining;
        }

        if (delay > TimeSpan.Zero)
        {
            await _retryDelayStrategy.DelayAsync(delay, _timeProvider, cancellationToken);
        }

        return GetRemainingBudget(startedAt) > TimeSpan.Zero;
    }

    private TimeSpan? GetServerRetryDelay(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        return retryAfter?.Date is { } date
            ? date - _timeProvider.GetUtcNow()
            : null;
    }

    private TimeSpan GetRemainingBudget(long startedAt) =>
        _options.TotalBudget - _timeProvider.GetElapsedTime(startedAt);

    private static FeedbackSendResult Complete(
        FeedbackSendOutcome outcome,
        List<FeedbackAttemptDiagnostic> diagnostics) =>
        new(outcome, diagnostics.AsReadOnly());

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 || (int)statusCode >= 500;

    private static async Task<string> ReadBoundedResponseAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        byte[] buffer = new byte[maxBytes];
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, totalRead);
    }
}
