using System.Net;
using System.Net.Http.Json;
using CarRental.Api.Contracts;
using CarRental.Api.Domain;
using CarRental.Api.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace CarRental.Api.Tests;

/// <summary>Wysyłka wniosku na kolejkę rental-requests (fałszywy ServiceBusSender, bez Azure).</summary>
public class RentalQueueTests
{
    private static CarRentalApiFactory FactoryWithQueue(FakeServiceBusSender sender) => new()
    {
        ConfigureServices = services =>
            services.AddSingleton<IRentalRequestPublisher>(new ServiceBusRentalRequestPublisher(sender))
    };

    [Fact]
    public async Task Publisher_SendsJsonWithRentalIdAndMessageId()
    {
        var sender = new FakeServiceBusSender();
        var publisher = new ServiceBusRentalRequestPublisher(sender);
        var rentalId = Guid.NewGuid();

        await publisher.PublishAsync(rentalId, CancellationToken.None);

        var message = Assert.Single(sender.Sent);
        Assert.Equal(rentalId.ToString(), message.MessageId);
        Assert.Equal("application/json", message.ContentType);
        Assert.Equal(rentalId, RentalRequestMessage.TryParse(message.Body.ToString())!.RentalId);
        Assert.Contains("\"rentalId\"", message.Body.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateRental_WithQueue_Returns202AndSendsMessage()
    {
        var sender = new FakeServiceBusSender();
        await using var factory = FactoryWithQueue(sender);
        var client = factory.CreateBrokerClient();
        var offer = await TestApi.CreateOfferAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/rentals", TestApi.RentalBody(offer.OfferId));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var rental = await response.Content.ReadFromJsonAsync<RentalStatusChangedResponse>(CarRentalApiFactory.Json);
        Assert.Equal(RentalStatus.Pending, rental!.Status);
        var message = Assert.Single(sender.Sent);
        Assert.Equal(rental.RentalId, RentalRequestMessage.TryParse(message.Body.ToString())!.RentalId);
    }

    [Fact]
    public async Task CreateRental_Rejected_SendsNothing()
    {
        var sender = new FakeServiceBusSender();
        await using var factory = FactoryWithQueue(sender);
        var client = factory.CreateBrokerClient();

        var response = await client.PostAsJsonAsync("/api/v1/rentals", TestApi.RentalBody(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task CreateRental_QueueDown_StillReturns202()
    {
        // Wniosek jest już zapisany, Worker zbierze go później (PendingRentalSweeper)
        await using var factory = FactoryWithQueue(new FakeServiceBusSender { Fail = true });
        var client = factory.CreateBrokerClient();
        var offer = await TestApi.CreateOfferAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/rentals", TestApi.RentalBody(offer.OfferId));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("nie json")]
    [InlineData("{\"rentalId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"inne\":1}")]
    public void TryParse_InvalidBody_ReturnsNull(string? body)
    {
        Assert.Null(RentalRequestMessage.TryParse(body));
    }
}
