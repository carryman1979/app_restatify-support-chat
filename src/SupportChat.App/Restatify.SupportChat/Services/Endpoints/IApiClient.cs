using System.Collections.Immutable;
using Restatify.SupportChat.DataContracts;

namespace Restatify.SupportChat.Services.Endpoints;

[Headers("Content-Type: application/json")]
public interface IApiClient
{
	[Get("/api/weatherforecast")]
	Task<ApiResponse<IImmutableList<WeatherForecast>>> GetWeather(CancellationToken cancellationToken = default);
}
