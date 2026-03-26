using DhogGPT.Models;

namespace DhogGPT.Services.Translation.Providers;

public interface ITranslationProvider
{
    string Name { get; }

    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}
