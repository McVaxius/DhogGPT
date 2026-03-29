using System.Threading;
using System.Threading.Channels;
using DhogGPT.Models;
using DhogGPT.Services.Chat;
using DhogGPT.Services.Diagnostics;
using DhogGPT.Services.Translation.Providers;

namespace DhogGPT.Services.Translation;

public sealed class TranslationCoordinator : IDisposable
{
    private readonly Configuration configuration;
    private readonly TranslationCacheService cache;
    private readonly ITranslationProvider provider;
    private readonly SessionHealthService sessionHealth;
    private readonly Channel<TranslationRequest> queue;
    private readonly CancellationTokenSource disposeCts = new();
    private readonly Task workerTask;
    private readonly object historyLock = new();
    private readonly List<TranslationHistoryItem> history = [];
    private int queuedCount;

    public TranslationCoordinator(
        Configuration configuration,
        TranslationCacheService cache,
        ITranslationProvider provider,
        SessionHealthService sessionHealth)
    {
        this.configuration = configuration;
        this.cache = cache;
        this.provider = provider;
        this.sessionHealth = sessionHealth;

        queue = Channel.CreateUnbounded<TranslationRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        workerTask = Task.Run(ProcessQueueAsync);
    }

    public event Action<TranslationResult>? InboundTranslationReady;

    public event Action<TranslationResult>? TranslationCompleted;

    public event Action? HistoryUpdated;

    public int PendingCount => Volatile.Read(ref queuedCount);

    public IReadOnlyList<TranslationHistoryItem> GetHistorySnapshot()
    {
        lock (historyLock)
        {
            return history.ToArray();
        }
    }

    public ValueTask QueueIncomingAsync(TranslationRequest request)
    {
        Interlocked.Increment(ref queuedCount);
        sessionHealth.UpdateQueueDepth(PendingCount);
        return queue.Writer.WriteAsync(request);
    }

    public async Task<TranslationResult> TranslateImmediatelyAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        var result = await TranslateCoreAsync(request, cancellationToken).ConfigureAwait(false);
        if (request.RecordInHistory)
            RecordCompletedTranslation(result);
        return result;
    }

    public void RecordTranslationResult(TranslationResult result)
    {
        if (!result.Request.RecordInHistory)
            return;

        RecordCompletedTranslation(result);
    }

    public void Dispose()
    {
        queue.Writer.TryComplete();
        disposeCts.Cancel();

        try
        {
            workerTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[DhogGPT] Translation worker shutdown reported: {ex.Message}");
        }
        finally
        {
            disposeCts.Dispose();
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var request in queue.Reader.ReadAllAsync(disposeCts.Token).ConfigureAwait(false))
        {
            try
            {
                var result = await TranslateCoreAsync(request, disposeCts.Token).ConfigureAwait(false);
                if (request.RecordInHistory)
                    RecordCompletedTranslation(result);

                if (request.IsInbound && result.Success && result.HasMeaningfulTranslation)
                    InboundTranslationReady?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[DhogGPT] Translation queue failure: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref queuedCount);
                sessionHealth.UpdateQueueDepth(PendingCount);
            }
        }
    }

    private async Task<TranslationResult> TranslateCoreAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var preservedPayloadText = PreservedPayloadTranslationText.Prepare(request);
        var requestForTranslation = preservedPayloadText.ApplyTo(request);

        if (cache.TryGet(request.SourceLanguage, request.TargetLanguage, requestForTranslation.Text, out var cachedText, out var cachedDetectedLanguage))
        {
            var cachedResult = TranslationResult.Succeeded(
                request,
                preservedPayloadText.Restore(cachedText),
                provider.Name,
                "cache",
                cachedDetectedLanguage,
                TimeSpan.Zero,
                fromCache: true);

            sessionHealth.RecordSuccess(cachedResult.ProviderName, cachedResult.Endpoint, cachedResult.Duration);
            return cachedResult;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, disposeCts.Token);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.RequestTimeoutSeconds, 5, 60)));

        var result = await provider.TranslateAsync(requestForTranslation, timeoutCts.Token).ConfigureAwait(false);
        if (result.Success)
        {
            var translatedText = preservedPayloadText.Restore(result.TranslatedText);
            cache.Store(
                request.SourceLanguage,
                request.TargetLanguage,
                requestForTranslation.Text,
                result.TranslatedText,
                result.DetectedSourceLanguage);

            var restoredResult = TranslationResult.Succeeded(
                request,
                translatedText,
                result.ProviderName,
                result.Endpoint,
                result.DetectedSourceLanguage,
                result.Duration,
                result.FromCache);
            sessionHealth.RecordSuccess(restoredResult.ProviderName, restoredResult.Endpoint, restoredResult.Duration);
            return restoredResult;
        }

        var failedResult = TranslationResult.Failed(
            request,
            result.ProviderName,
            result.Endpoint,
            result.Error,
            result.Duration);
        sessionHealth.RecordFailure(failedResult.ProviderName, failedResult.Endpoint, failedResult.Error, failedResult.Duration);
        return failedResult;
    }

    private void AddHistory(TranslationResult result)
    {
        lock (historyLock)
        {
            history.Insert(0, TranslationHistoryItem.FromResult(result));

            var limit = Math.Clamp(configuration.HistoryLimit, 5, 200);
            if (history.Count > limit)
                history.RemoveRange(limit, history.Count - limit);
        }

        HistoryUpdated?.Invoke();
    }

    private void RecordCompletedTranslation(TranslationResult result)
    {
        AddHistory(result);
        TranslationCompleted?.Invoke(result);
    }
}
