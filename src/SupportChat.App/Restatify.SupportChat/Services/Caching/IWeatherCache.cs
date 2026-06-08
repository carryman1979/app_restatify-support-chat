using Restatify.SupportChat.DataContracts;
using System.Collections.Immutable;

namespace Restatify.SupportChat.Services.Caching;

public interface IWeatherCache
{
    ValueTask<IImmutableList<WeatherForecast>> GetForecast(CancellationToken token);
}
