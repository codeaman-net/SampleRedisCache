using Microsoft.Extensions.Caching.Distributed;
using Scalar.AspNetCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "SampleRedisCache";
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapGet("/weatherforecast", async ([FromServices] IDistributedCache distributedCache ) =>
    {
        var cachedForecast = distributedCache.GetString("weatherforecast");
        if (cachedForecast != null)
        {
            var cachedResult = JsonSerializer.Deserialize<WeatherForecast[]>(cachedForecast);
            return Results.Ok(cachedResult);
        }
        
        var httpClient = new HttpClient();
        var response = await httpClient.GetAsync("https://api.open-meteo.com/v1/forecast?latitude=41.0138&longitude=28.9497&hourly=temperature_2m,weather_code");
        var content = await response.Content.ReadAsStringAsync();
        
        var weatherData = JsonSerializer.Deserialize<WeatherData>(content);
        
        var forecast = weatherData.Hourly.Time.Select((time, index) => new WeatherForecast(
            DateTime.Parse(time),
            (int)weatherData.Hourly.Temperature2m[index],
            WeatherCondition.FromCode(weatherData.Hourly.WeatherCode[index]).Description
        )).ToArray();
        
        distributedCache.SetString("weatherforecast", JsonSerializer.Serialize(forecast), new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = DateTime.Now.AddMinutes(10)
        });
        
        return Results.Ok(forecast);
    })
    .WithName("GetWeatherForecast");

app.MapGet("/rss-feeds/", async ([FromQuery] string link, [FromServices] IDistributedCache distributedCache) =>
{
    if (string.IsNullOrEmpty(link))
    {
        return Results.BadRequest("Link is required");
    }

    var key = $"_rss_link_{link}";

    if (distributedCache.GetString(key) != null)
    {
        var cachedItem = JsonSerializer.Deserialize<RssItem[]>(distributedCache.GetString(key));
        return Results.Ok(cachedItem);
    }
    
    var httpClient = new HttpClient();
    var response = await httpClient.GetAsync(link);
    var content = await response.Content.ReadAsStringAsync();

    var doc = new XmlDocument();
    doc.LoadXml(content);
    
    var items = doc.SelectNodes("//item");
    
    var rssItems = items?.OfType<XmlNode>().Select(item => new 
        RssItem
        (
            DateTime.Parse(item.SelectSingleNode("pubDate").InnerText),
            item.SelectSingleNode("title").InnerText,
            item.SelectSingleNode("link").InnerText
        )).ToList();
    
    distributedCache.SetString(key, JsonSerializer.Serialize(rssItems), new DistributedCacheEntryOptions
    {
        AbsoluteExpiration = DateTime.Now.AddMinutes(5)
    });
    return  Results.Ok(rssItems);
}).WithName("GetRssFeeds");

app.Run();

public record WeatherData(
    double Latitude,
    double Longitude,
    double GenerationtimeMs,
    int UtcOffsetSeconds,
    string Timezone,
    string TimezoneAbbreviation,
    double Elevation,
    [property: JsonPropertyName("hourlyUnits")] HourlyUnits HourlyUnits,
    [property: JsonPropertyName("hourly")] HourlyData Hourly
);

public record HourlyUnits(
    string Time,
    [property: JsonPropertyName("temperature_2m")] string Temperature2m,
    [property: JsonPropertyName("weather_code")] string WeatherCode
);

public record HourlyData(
    [property: JsonPropertyName("time")] List<string> Time,
    [property: JsonPropertyName("temperature_2m")] List<double> Temperature2m,
    [property: JsonPropertyName("weather_code")] List<int> WeatherCode
);


public record WeatherForecast(DateTime Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public record WeatherCondition(int Code, string Description)
{
    private static readonly Dictionary<int, WeatherCondition> AllConditions = new()
    {
        { 0, new WeatherCondition(0, "Açık gökyüzü") },
        { 1, new WeatherCondition(1, "Çoğunlukla açık") },
        { 2, new WeatherCondition(2, "Yer yer bulutlu") },
        { 3, new WeatherCondition(3, "Kapalı") },
        { 45, new WeatherCondition(45, "Sis") },
        { 48, new WeatherCondition(48, "Kırağı birikintili sis") },
        { 51, new WeatherCondition(51, "Hafif çisenti") },
        { 53, new WeatherCondition(53, "Orta şiddetli çisenti") },
        { 55, new WeatherCondition(55, "Yoğun şiddetli çisenti") },
        { 56, new WeatherCondition(56, "Hafif dondurucu çisenti") },
        { 57, new WeatherCondition(57, "Yoğun dondurucu çisenti") },
        { 61, new WeatherCondition(61, "Çok hafif yağmur") },
        { 63, new WeatherCondition(63, "Orta şiddetli yağmur") },
        { 65, new WeatherCondition(65, "Yoğun şiddetli yağmur") },
        { 66, new WeatherCondition(66, "Hafif dondurucu yağmur") },
        { 67, new WeatherCondition(67, "Yoğun dondurucu yağmur") },
        { 71, new WeatherCondition(71, "Çok hafif kar yağışı") },
        { 73, new WeatherCondition(73, "Orta şiddetli kar yağışı") },
        { 75, new WeatherCondition(75, "Yoğun şiddetli kar yağışı") },
        { 77, new WeatherCondition(77, "Kar tanecikleri") },
        { 80, new WeatherCondition(80, "Çok hafif sağanak yağmur") },
        { 81, new WeatherCondition(81, "Orta sağanak yağmur") },
        { 82, new WeatherCondition(82, "Şiddetli sağanak yağmur") },
        { 85, new WeatherCondition(85, "Hafif kar sağanağı") },
        { 86, new WeatherCondition(86, "Yoğun kar sağanağı") },
        { 95, new WeatherCondition(95, "Gök gürültülü fırtına") },
        { 96, new WeatherCondition(96, "Hafif dolulu gök gürültülü fırtına") },
        { 99, new WeatherCondition(99, "Yoğun dolulu gök gürültülü fırtına") },
    };
    
    public static WeatherCondition FromCode(int code)
    {
        return AllConditions.TryGetValue(code, out var condition) ? condition : new WeatherCondition(-1, $"Bilinmeyen Hava Durumu Kodu: {code}");
    }
    public string GetCategory() => Code switch
    {
        0 or 1 or 2 or 3 => "Güneşli/Bulutlu",
        >= 51 and <= 67 => "Yağmurlu",
        >= 71 and <= 77 => "Karlı",
        >= 80 and <= 82 => "Sağanak",
        >= 95 => "Fırtınalı",
        _ => "Diğer"
    };
}

public record RssItem(DateTime PubDate, string Title, string Link);