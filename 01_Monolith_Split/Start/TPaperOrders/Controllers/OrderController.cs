using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace TPaperOrders
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrderController
    {
        private readonly PaperDbContext _context;

        private readonly ILogger<OrderController> _logger;

        private readonly IHttpClientFactory _clientFactory;

        public OrderController(PaperDbContext context, ILogger<OrderController> logger, IHttpClientFactory clientFactory)
        {
            _context = context;
            _logger = logger;
            _clientFactory = clientFactory;
        }

        [HttpGet]
        [Route("create/{quantity}")]
        public async Task<IActionResult> ProcessEdiOrder(decimal quantity, CancellationToken cts)
        {
            _logger.LogInformation("Processed a request.");

            var order = new EdiOrder
            {
                ClientId = 1,
                DeliveryId = 1,
                Notes = "Test order",
                ProductCode = 1,
                Quantity = quantity
            };

            EdiOrder savedOrder = (await _context.EdiOrder.AddAsync(order, cts)).Entity;
            await _context.SaveChangesAsync(cts);

            Delivery savedDelivery = await CreateDeliveryForOrder(savedOrder, cts);

            string responseMessage = $"Accepted EDI message {order.Id} and created delivery {savedOrder.Id}";

            return new OkObjectResult(responseMessage);
        }

        private async Task<Delivery> CreateDeliveryForOrder(EdiOrder savedOrder, CancellationToken cts)
        {
            string url =
                $"http://localhost:11978/api/delivery/create/{savedOrder.ClientId}/{savedOrder.Id}/{savedOrder.ProductCode}/{savedOrder.Quantity}";

            using var httpClient = _clientFactory.CreateClient();
            var uriBuilder = new UriBuilder(url);

            using var result = await httpClient.GetAsync(uriBuilder.Uri);
            if (!result.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await result.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<Delivery>(content);
        }
    }
}
