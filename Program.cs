using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using CsvHelper;
using System.Globalization;
using ScottPlot;
using System.Drawing;

namespace EtsyScraper
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string url;

            // If no arguments are provided, ask the user to enter a shop URL
            if (args.Length < 1)
            {
                WriteInfo("Please provide a shop URL for scraping:");
                url = Console.ReadLine();
            }
            else
            {
                url = args[0];
            }

            WriteInfo("URL provided: " + url);

            // Extract the shop name from the provided URL
            var match = Regex.Match(url, @"etsy\.com/(?:[a-z]{2}/)?shop/([^/?#]+)");
            if (!match.Success)
            {
                WriteError("Invalid Etsy shop URL. Please provide a valid shop URL.");
                return;
            }
            string shopName = match.Groups[1].Value;

            // Construct the proper URL format for reviews with a high page number to determine max pages
            string highPageUrl = $"https://www.etsy.com/ca/shop/{shopName}/reviews?ref=pagination&page=10000";
            WriteInfo("Sending request to determine the maximum page count...");

            // Initialize HttpClient to send HTTP requests
            HttpClientHandler handler = new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            HttpClient client = new HttpClient(handler);
            // Set the User-Agent header to mimic a browser request
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/84.0.4147.105 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");

            try
            {
                // Send a request to determine the maximum page count
                var response = await client.GetAsync(highPageUrl);
                if (!response.IsSuccessStatusCode)
                {
                    WriteError("Failed to determine the maximum page count: " + response.StatusCode);
                    return;
                }

                // Extract the redirected URL to determine the maximum page number
                var redirectedUrl = response.RequestMessage.RequestUri.ToString();
                var maxPageMatch = Regex.Match(redirectedUrl, @"page=(\d+)");
                if (!maxPageMatch.Success)
                {
                    WriteError("Could not determine the maximum page count.");
                    return;
                }
                int maxPages = int.Parse(maxPageMatch.Groups[1].Value);
                WriteSuccess($"Maximum possible pages: {maxPages}");

                // Ask the user how many pages they want to parse
                int pagesToParse;
                do
                {
                    WriteInfo($"How many pages would you like to parse? (Max: {maxPages})");
                    if (!int.TryParse(Console.ReadLine(), out pagesToParse) || pagesToParse < 1 || pagesToParse > maxPages)
                    {
                        WriteError("Invalid input. Please enter a number between 1 and " + maxPages);
                    }
                    else
                    {
                        break;
                    }
                } while (true);

                // Ask the user in which format they want the result file saved
                WriteInfo("In which format would you like to save the result? (Enter 'json', 'csv', or 'both')");
                string formatChoice;
                do
                {
                    formatChoice = Console.ReadLine()?.ToLower();
                    if (formatChoice != "json" && formatChoice != "csv" && formatChoice != "both")
                    {
                        WriteError("Invalid input. Please enter 'json', 'csv', or 'both'");
                    }
                    else
                    {
                        break;
                    }
                } while (true);

                List<StoreProductReviews> allShopReviews = new List<StoreProductReviews>();

                for (int page = 1; page <= pagesToParse; page++)
                {
                    string pageUrl = $"https://www.etsy.com/ca/shop/{shopName}/reviews?ref=pagination&page={page}";
                    WriteInfo($"Processing page {page}...");
                    WriteInfo("Sending request to URL...");

                    // Send an asynchronous GET request to the URL for each page
                    response = await client.GetAsync(pageUrl);

                    // Check if response is successful
                    if (!response.IsSuccessStatusCode)
                    {
                        WriteError("Request error: " + response.StatusCode);
                        return;
                    }

                    WriteSuccess("Response received successfully.");

                    // Extract reviews from the page
                    var shopReviews = await StoreProductReviews.GetReviewsFromShopPage(response);
                    WriteInfo($"Reviews from the shop (Page {page}):");
                    foreach (var review in shopReviews)
                    {
                        WriteDetail($"Product URL: {review.ProductUrl}, Product Name: {review.ProductName}");
                    }

                    // Add reviews to the list of all reviews
                    allShopReviews.AddRange(shopReviews);
                }

                // Calculate sales count and remove duplicates
                var salesCount = allShopReviews.GroupBy(r => r.ProductUrl)
                                               .ToDictionary(g => g.Key, g => g.Count());

                List<StoreProductReviews> uniqueShopReviews = allShopReviews.GroupBy(r => r.ProductUrl)
                                                                           .Select(g => g.First())
                                                                           .ToList();

                // Add sales count to each unique review
                foreach (var review in uniqueShopReviews)
                {
                    review.SalesCount = salesCount[review.ProductUrl];
                }

                // Fetch product details for each unique review
                WriteInfo("Fetching product details...");
                foreach (var review in uniqueShopReviews)
                {
                    await review.GetProductDetails(client);
                    WriteDetail($"Fetched details for: {review.ProductName}");
                }

                // Sort reviews by sales count in descending order
                uniqueShopReviews = uniqueShopReviews.OrderByDescending(r => r.SalesCount).ToList();

                // Save parsed reviews to the chosen format(s)
                if (formatChoice == "json" || formatChoice == "both")
                {
                    string json = JsonConvert.SerializeObject(uniqueShopReviews, Formatting.Indented);
                    File.WriteAllText("shop_reviews.json", json);
                    WriteSuccess("Reviews saved to shop_reviews.json");
                }

                if (formatChoice == "csv" || formatChoice == "both")
                {
                    using (var writer = new StreamWriter("shop_reviews.csv"))
                    using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csv.WriteHeader<StoreProductReviews>();
                        csv.NextRecord();
                        foreach (var review in uniqueShopReviews)
                        {
                            csv.WriteRecord(review);
                            csv.NextRecord();
                        }
                    }
                    WriteSuccess("Reviews saved to shop_reviews.csv");
                }

                // Ask user if they want to generate a plot
                if (uniqueShopReviews.Any())
                {
                    bool shouldPlot = AskUserForPlotting();
                    if (shouldPlot)
                    {
                        PlotTopProducts(uniqueShopReviews);
                    }
                    else
                    {
                        WriteInfo("Skipping plot generation.");
                    }
                }
            }
            catch (HttpRequestException e)
            {
                // Handle any request errors
                WriteError("Request error: " + e.Message);
            }
        }

        static void WriteInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        static void WriteSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        static void WriteError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        static void WriteDetail(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        static bool AskUserForPlotting()
        {
            while (true)
            {
                WriteInfo("Would you like to generate a plot of the top 10 products? (yes/no)");
                string response = Console.ReadLine().Trim().ToLower();
                if (response == "yes" || response == "y")
                {
                    return true;
                }
                else if (response == "no" || response == "n")
                {
                    return false;
                }
                else
                {
                    WriteError("Invalid input. Please enter 'yes' or 'no'.");
                }
            }
        }

        static void PlotTopProducts(List<StoreProductReviews> reviews)
        {
            WriteInfo("Generating plot of top products...");

            // Take top 10 products by sales count
            var topProducts = reviews.OrderByDescending(r => r.SalesCount).Take(10).ToList();

            // Create plot
            var plt = new Plot(600, 400);

            // Prepare data for plotting
            double[] salesCounts = topProducts.Select(r => (double)r.SalesCount).ToArray();
            string[] productNames = topProducts.Select(r => TruncateString(r.ProductName, 20)).ToArray();

            // Create bar plot
            var bar = plt.AddBar(salesCounts);
            bar.FillColor = Color.Blue;

            // Customize the plot
            plt.XTicks(productNames);
            plt.XAxis.TickLabelStyle(rotation: 45);
            plt.Title("Top 10 Products by Sales Count");
            plt.YAxis.Label("Sales Count");
            plt.XAxis.Label("Product Name");

            // Save the plot
            plt.SaveFig("top_products.png");
            WriteSuccess("Plot saved as top_products.png");
        }

        static string TruncateString(string str, int maxLength)
        {
            return str.Length <= maxLength ? str : str.Substring(0, maxLength - 3) + "...";
        }
    }

    // Class to represent reviews from the store
    class StoreProductReviews
    {
        public string ProductUrl { get; set; }
        public string ProductName { get; set; }
        public int SalesCount { get; set; }
        public string Currency { get; set; }
        public string Price { get; set; }
        public string ProductImage { get; set; }

        // Method to get all reviews from the seller's shop page
        public static async Task<List<StoreProductReviews>> GetReviewsFromShopPage(HttpResponseMessage response)
        {
            List<StoreProductReviews> shopReviews = new List<StoreProductReviews>();

            Console.WriteLine("Extracting reviews from the shop page...");

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(await response.Content.ReadAsStringAsync());

            // Find all review nodes on the page
            var reviewNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'review-item')]//div[contains(@class, 'wt-grid__item-xs-12 wt-grid__item-lg-10 listing-group wt-pl-xs-0 wt-pr-xs-0')]");

            if (reviewNodes != null)
            {
                foreach (var reviewNode in reviewNodes)
                {
                    // Extract product URL and product name from the review link
                    var productLinkNode = reviewNode.SelectSingleNode(".//a[contains(@class, 'wt-display-block wt-text-link-no-underline')]");
                    string productUrl = productLinkNode?.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(productUrl) && !productUrl.StartsWith("https://"))
                    {
                        productUrl = "https://www.etsy.com" + productUrl;
                    }
                    string productName = productLinkNode?.GetAttributeValue("aria-label", "Unknown Product");

                    // Add review to the list
                    shopReviews.Add(new StoreProductReviews
                    {
                        ProductUrl = productUrl,
                        ProductName = productName
                    });
                }
            }
            else
            {
                Console.WriteLine("No reviews found on the shop page");
            }

            return shopReviews;
        }

        public async Task GetProductDetails(HttpClient client)
        {
            try
            {
                var response = await client.GetAsync(ProductUrl);
                if (response.IsSuccessStatusCode)
                {
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(await response.Content.ReadAsStringAsync());

                    SetPriceAndCurrency(GetPrice(htmlDoc));
                    ProductImage = GetImage(htmlDoc);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching details for {ProductUrl}: {ex.Message}");
            }
        }

        private void SetPriceAndCurrency(string priceString)
        {
            Match match = Regex.Match(priceString, @"([A-Z]{1,3}\$?)\s*([\d,]+(?:\.\d{2})?)");
            if (match.Success)
            {
                Currency = match.Groups[1].Value;
                Price = match.Groups[2].Value;
            }
        }

        private static string GetPrice(HtmlDocument htmlDoc)
        {
            var priceNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@data-appears-component-name='price']//p[contains(@class, 'wt-text-title-larger') or contains(@class, 'wt-text-title-03 wt-mr-xs-2')]");
            return priceNode?.InnerText.Trim() ?? "";
        }

        private static string GetImage(HtmlDocument htmlDoc)
        {
            var imageNode = htmlDoc.DocumentNode.SelectSingleNode("//img[@data-index='0']");
            return imageNode?.GetAttributeValue("src", "") ?? "";
        }
    }
}