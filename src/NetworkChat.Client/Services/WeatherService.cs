using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace NetworkChat.Client.Services;

/// <summary>
/// Получает текущую погоду в Екатеринбурге через Open-Meteo API.
/// </summary>
public sealed class WeatherService(HttpClient httpClient)
{
    private const string ApiUrl =
        "https://api.open-meteo.com/v1/forecast" +
        "?latitude=56.8389&longitude=60.6057" +
        "&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m" +
        "&timezone=Asia%2FYekaterinburg";

    public async Task<string> GetYekaterinburgWeatherAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(ApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("current", out var current))
            throw new InvalidOperationException("Сервис погоды не вернул текущие погодные данные.");

        var temperature = current.GetProperty("temperature_2m").GetDouble();
        var feelsLike = current.GetProperty("apparent_temperature").GetDouble();
        var humidity = current.GetProperty("relative_humidity_2m").GetInt32();
        var windSpeed = current.GetProperty("wind_speed_10m").GetDouble();
        var weatherCode = current.GetProperty("weather_code").GetInt32();
        var time = current.GetProperty("time").GetString() ?? "не указано";

        var culture = CultureInfo.GetCultureInfo("ru-RU");

        return "Погода в Екатеринбурге: " + GetWeatherDescription(weatherCode) +
               $"; температура {temperature.ToString("0.#", culture)} °C" +
               $"; ощущается как {feelsLike.ToString("0.#", culture)} °C" +
               $"; влажность {humidity}%" +
               $"; ветер {windSpeed.ToString("0.#", culture)} км/ч" +
               $"; время данных: {time} (местное время).";
    }

    private static string GetWeatherDescription(int code) => code switch
    {
        0 => "ясно",
        1 => "преимущественно ясно",
        2 => "переменная облачность",
        3 => "пасмурно",
        45 or 48 => "туман",
        51 or 53 or 55 => "морось",
        56 or 57 => "ледяная морось",
        61 or 63 or 65 => "дождь",
        66 or 67 => "ледяной дождь",
        71 or 73 or 75 => "снег",
        77 => "снежные зёрна",
        80 or 81 or 82 => "ливень",
        85 or 86 => "снегопад",
        95 => "гроза",
        96 or 99 => "гроза с градом",
        _ => "неизвестные погодные условия"
    };
}
