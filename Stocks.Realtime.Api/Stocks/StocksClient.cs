using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace Stocks.Realtime.Api.Stocks;

internal sealed class StocksClient(
    HttpClient httpClient,
    IConfiguration configuration,
    IMemoryCache memoryCache,
    ILogger<StocksClient> logger)
{
    public async Task<StockPriceResponse?> GetDataForTicker(string ticker)
    {
        logger.LogInformation("Getting stock price information for {Ticker}", ticker);

        StockPriceResponse? stockPriceResponse = await memoryCache.GetOrCreateAsync($"stocks-{ticker}", async entry =>
        {
            entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

            return await GetStockPrice(ticker);
        });

        if (stockPriceResponse is null)
        {
            logger.LogWarning("Failed to get stock price information for {Ticker}", ticker);
        }
        else
        {
            logger.LogInformation(
                "Completed getting stock price information for {Ticker}, {@Stock}",
                ticker,
                stockPriceResponse);
        }

        return stockPriceResponse;
    }

    private async Task<StockPriceResponse?> GetStockPrice(string ticker)
    {
        //string tickerDataString = await httpClient.GetStringAsync(
        //    $"?function=TIME_SERIES_INTRADAY&symbol={ticker}&interval=15min&apikey={configuration["Stocks:ApiKey"]}");

        //AlphaVantageData? tickerData = JsonConvert.DeserializeObject<AlphaVantageData>(tickerDataString);

        //TimeSeriesEntry? lastPrice = tickerData?.TimeSeries.FirstOrDefault().Value;

        //if (lastPrice is null)
        //{
        //    return null;
        //}

        //return new StockPriceResponse(ticker, decimal.Parse(lastPrice.High, CultureInfo.InvariantCulture));

        string apiKey = configuration["Stocks:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("API key is missing or invalid.");
            return null;
        }

        try
        {
            // Ensure base URL is set in HttpClient (e.g., https://www.alphavantage.co/query)
            string url = $"?function=TIME_SERIES_INTRADAY&symbol={ticker}&interval=15min&apikey={apiKey}";
            HttpResponseMessage response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("HTTP error for {Ticker}: {StatusCode}", ticker, response.StatusCode);
                return null;
            }

            string tickerDataString = await response.Content.ReadAsStringAsync();
            logger.LogDebug("API Response for {Ticker}: {Response}", ticker, tickerDataString);

            // Check for error responses
            Dictionary<string, string>? errorResponse = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                tickerDataString);
            if (errorResponse != null && (errorResponse.TryGetValue("Error Message", out string errorMessage) || errorResponse.TryGetValue("Information", out errorMessage)))
            {
                logger.LogWarning("API error for {Ticker}: {ErrorMessage}", ticker, errorMessage);
                return null;
            }

            AlphaVantageData? tickerData;
            try
            {
                tickerData = JsonConvert.DeserializeObject<AlphaVantageData>(tickerDataString);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Deserialization error for {Ticker}", ticker);
                return null;
            }

            if (tickerData == null || tickerData.MetaData == null || tickerData.TimeSeries == null || !tickerData.TimeSeries.Any())
            {
                logger.LogWarning("No valid time series data for {Ticker}", ticker);
                return null;
            }

            TimeSeriesEntry? lastPrice = tickerData.TimeSeries.FirstOrDefault().Value;
            if (lastPrice == null)
            {
                logger.LogWarning("No valid time series entry for {Ticker}", ticker);
                return null;
            }

            if (!decimal.TryParse(lastPrice.High, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal highPrice))
            {
                logger.LogWarning("Failed to parse high price for {Ticker}: {HighPrice}", ticker, lastPrice.High);
                return null;
            }

            return new StockPriceResponse(ticker, highPrice);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP request error for {Ticker}", ticker);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error for {Ticker}", ticker);
            return null;
        }
    }
}
