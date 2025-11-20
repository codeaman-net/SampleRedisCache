using Microsoft.Extensions.Caching.Distributed;
using Scalar.AspNetCore;
using System.Text.Json;
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

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", (IDistributedCache distributedCache ) =>
    {
        var cachedForecast = distributedCache.GetString("weatherforecast");
        if (cachedForecast != null)
        {
            return JsonSerializer.Deserialize<WeatherForecast[]>(cachedForecast);
        }
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        
        distributedCache.SetString("weatherforecast", JsonSerializer.Serialize(forecast), new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = DateTime.Now.AddMinutes(10)
        });
        
        return forecast;
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

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

record RssItem(DateTime PubDate, string Title, string Link);