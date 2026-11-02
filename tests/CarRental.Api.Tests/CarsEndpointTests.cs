using System.Net;
using System.Net.Http.Json;
using CarRental.Api.Contracts;
using CarRental.Api.Data;

namespace CarRental.Api.Tests;

public class CarsEndpointTests
{
    [Fact]
    public async Task GetCars_WithoutApiKey_Returns401()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/cars");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCars_WithWrongApiKey_Returns401()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient("this-key-does-not-exist");

        var response = await client.GetAsync("/cars");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCars_WithValidKey_ReturnsWholeFleet()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var cars = await client.GetFromJsonAsync<List<CarResponse>>("/cars", CarRentalApiFactory.Json);

        Assert.NotNull(cars);
        Assert.Equal(SeedData.Cars.Length, cars.Count);
        Assert.All(cars, c =>
        {
            Assert.False(string.IsNullOrEmpty(c.Make));
            Assert.False(string.IsNullOrEmpty(c.Model));
            Assert.True(c.Year > 2000);
            Assert.True(c.Available);
        });
    }

    [Fact]
    public async Task GetCars_OtherGroupKey_AlsoWorks()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient(CarRentalApiFactory.OtherBrokerKey);

        var response = await client.GetAsync("/cars");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCars_FilterByYearAndMake()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var byYear = await client.GetFromJsonAsync<List<CarResponse>>("/cars?year=2022", CarRentalApiFactory.Json);
        var byMake = await client.GetFromJsonAsync<List<CarResponse>>("/cars?make=toyota", CarRentalApiFactory.Json);

        Assert.NotNull(byYear);
        Assert.NotEmpty(byYear);
        Assert.All(byYear, c => Assert.Equal(2022, c.Year));
        Assert.Equal(SeedData.Cars.Count(c => c.Year == 2022), byYear.Count);

        Assert.NotNull(byMake);
        Assert.Equal(3, byMake.Count);
        Assert.All(byMake, c => Assert.Equal("Toyota", c.Make));
    }

    [Fact]
    public async Task GetCar_Existing_ReturnsCar()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var car = await client.GetFromJsonAsync<CarResponse>("/cars/1", CarRentalApiFactory.Json);

        Assert.NotNull(car);
        Assert.Equal("Toyota", car.Make);
        Assert.Equal("Corolla", car.Model);
        Assert.Equal("Warszawa", car.Location);
    }

    [Fact]
    public async Task GetCar_Unknown_Returns404()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.GetAsync("/cars/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCar_RentedToday_IsNotAvailable()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var today = DateOnly.FromDateTime(factory.Clock.GetUtcNow().UtcDateTime);

        var offer = await TestApi.CreateOfferAsync(client, TestApi.OfferBody(start: today, end: today.AddDays(2)));
        await TestApi.CreateRentalAsync(client, offer.OfferId);

        var car = await client.GetFromJsonAsync<CarResponse>("/cars/1", CarRentalApiFactory.Json);

        Assert.NotNull(car);
        Assert.False(car.Available);
    }

    [Fact]
    public async Task Health_IsPublic()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
