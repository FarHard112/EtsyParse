using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using System.IO;

namespace EtsyScraper
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string url;

            // If no arguments are provided, ask the user to enter a URL
            if (args.Length < 1)
            {
                Console.WriteLine("Please provide a product URL for scraping:");
                url = Console.ReadLine();
            }
            else
            {
                url = args[0];
            }

            Console.WriteLine("URL provided: " + url);

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
                Console.WriteLine("Sending request to URL...");
                // Send an asynchronous GET request to the URL
                var response = await client.GetAsync(url);

                // Check if response is successful
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Request error: " + response.StatusCode);
                    return;
                }

                Console.WriteLine("Response received successfully.");

                // Load the response HTML into an HtmlDocument for parsing
                var htmlDoc = new HtmlDocument();
                var responseContent = await response.Content.ReadAsStringAsync();
                htmlDoc.LoadHtml(responseContent);
                Console.WriteLine("HTML loaded into HtmlDocument.");

                // Extract product details from the HTML document
                var product = new Product
                {
                    Name = GetName(htmlDoc),
                    Image = GetImage(htmlDoc),
                    Price = GetPrice(htmlDoc)
                };

                // Log the extracted product details
                Console.WriteLine("Product details extracted:");
                Console.WriteLine("Name: " + product.Name);
                Console.WriteLine("Image: " + product.Image);
                Console.WriteLine("Price: " + product.Price);

                // Serialize the product details to JSON format
                List<Product> products = new List<Product> { product };
                string jsonData = JsonConvert.SerializeObject(products, Formatting.Indented);
                Console.WriteLine("Serialized JSON data: " + jsonData);

                // Write the JSON data to a file
                await File.WriteAllTextAsync("result.json", jsonData);
                Console.WriteLine("JSON data written to result.json");
            }
            catch (HttpRequestException e)
            {
                // Handle any request errors
                Console.WriteLine("Request error: " + e.Message);
            }
        }

        // Method to extract the product name from the HTML document
        static string GetName(HtmlDocument htmlDoc)
        {
            Console.WriteLine("Extracting product name...");
            // Use XPath to find the product name node
            var nameNode = htmlDoc.DocumentNode.SelectSingleNode("/html/body/main/div[1]/div[3]/div/div/div[1]/div[2]/div/div[4]/h1");
            if (nameNode != null)
            {
                Console.WriteLine("Product name found: " + nameNode.InnerText.Trim());
            }
            else
            {
                Console.WriteLine("Product name not found.");
            }
            // Return the product name or an empty string if not found
            return nameNode?.InnerText.Trim() ?? "";
        }

        // Method to extract the product image URL from the HTML document
        static string GetImage(HtmlDocument htmlDoc)
        {
            Console.WriteLine("Extracting product image...");
            // Use XPath to find the product image node with data-index='0'
            var imageNode = htmlDoc.DocumentNode.SelectSingleNode("//img[@data-index='0']");
            if (imageNode != null)
            {
                Console.WriteLine("Product image found: " + imageNode.GetAttributeValue("src", ""));
            }
            else
            {
                Console.WriteLine("Product image not found.");
            }
            // Return the image URL or an empty string if not found
            return imageNode?.GetAttributeValue("src", "") ?? "";
        }

        // Method to extract the product price from the HTML document
        static string GetPrice(HtmlDocument htmlDoc)
        {
            Console.WriteLine("Extracting product price...");
            // Use XPath to find the product price node
            var priceNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@data-appears-component-name='price']//p[contains(@class, 'wt-text-title-larger') or contains(@class, 'wt-text-title-03 wt-mr-xs-2')]");
            if (priceNode != null)
            {
                Console.WriteLine("Product price found: " + priceNode.InnerText.Trim());
            }
            else
            {
                Console.WriteLine("Product price not found.");
            }
            // Return the product price or an empty string if not found
            return priceNode?.InnerText.Trim() ?? "";
        }
    }

    // Class to represent a product with name, image, and price properties
    class Product
    {
        public string Name { get; set; }
        public string Image { get; set; }
        public string Price { get; set; }
    }
}