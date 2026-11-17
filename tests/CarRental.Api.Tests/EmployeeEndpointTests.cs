using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CarRental.Api.Contracts;
using CarRental.Api.Domain;
using CarRental.Api.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CarRental.Api.Tests;

public class EmployeeEndpointTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.4\n%test protokolu zwrotu\n"u8.ToArray();

    private static async Task<Guid> CreateReturnRequestedRentalAsync(CarRentalApiFactory factory)
    {
        var broker = factory.CreateBrokerClient();
        var rentalId = await TestApi.CreateConfirmedRentalAsync(factory, broker);
        var response = await broker.PostAsJsonAsync($"/api/v1/rentals/{rentalId}/return", new { email = "jan.kowalski@example.com" });
        response.EnsureSuccessStatusCode();
        return rentalId;
    }

    private static MultipartFormDataContent ConfirmForm(byte[] fileBytes, string fileName, string contentType, string note = "Auto czyste, bez nowych rys.")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(note), "Note");
        var file = new ByteArrayContent(fileBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);
        return form;
    }

    [Fact]
    public async Task List_WithBrokerKeyOnly_Returns401()
    {
        await using var factory = new CarRentalApiFactory();
        var broker = factory.CreateBrokerClient();

        var response = await broker.GetAsync("/api/v1/employee/rentals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_LoggedInWithoutEmployeeRole_Returns403()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateEmployeeClient(roles: "Customer");

        var response = await client.GetAsync("/api/v1/employee/rentals");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_Employee_SeesRentalsAndCanFilterByStatus()
    {
        await using var factory = new CarRentalApiFactory();
        var broker = factory.CreateBrokerClient();
        var employee = factory.CreateEmployeeClient();

        // Jeden wynajem w trakcie zwrotu (auto 1) i jeden czekający (auto 2)
        var returning = await CreateReturnRequestedRentalAsync(factory);
        var offer = await TestApi.CreateOfferAsync(broker, TestApi.OfferBody(carId: 2));
        await TestApi.CreateRentalAsync(broker, offer.OfferId);

        var all = await employee.GetFromJsonAsync<List<EmployeeRentalListItem>>("/api/v1/employee/rentals", CarRentalApiFactory.Json);
        var readyForPickup = await employee.GetFromJsonAsync<List<EmployeeRentalListItem>>(
            "/api/v1/employee/rentals?status=ReturnRequested", CarRentalApiFactory.Json);

        Assert.Equal(2, all!.Count);
        var single = Assert.Single(readyForPickup!);
        Assert.Equal(returning, single.RentalId);
        Assert.Equal("Toyota Corolla (2022)", single.Car);
        Assert.Equal("Jan Kowalski", single.CustomerName);
    }

    [Fact]
    public async Task Details_Employee_ReturnsRental()
    {
        await using var factory = new CarRentalApiFactory();
        var employee = factory.CreateEmployeeClient();
        var rentalId = await CreateReturnRequestedRentalAsync(factory);

        var details = await employee.GetFromJsonAsync<EmployeeRentalDetails>($"/api/v1/employee/rentals/{rentalId}", CarRentalApiFactory.Json);

        Assert.NotNull(details);
        Assert.Equal(RentalStatus.ReturnRequested, details.Status);
        Assert.Equal("OurBroker", details.BrokerName);
        Assert.NotNull(details.ReturnRequestedAt);
        Assert.False(details.HasAttachment);
    }

    [Fact]
    public async Task Details_Unknown_Returns404()
    {
        await using var factory = new CarRentalApiFactory();
        var employee = factory.CreateEmployeeClient();

        var response = await employee.GetAsync($"/api/v1/employee/rentals/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmReturn_WithPdf_CompletesRentalAndStoresFile()
    {
        await using var factory = new CarRentalApiFactory();
        var employee = factory.CreateEmployeeClient();
        var rentalId = await CreateReturnRequestedRentalAsync(factory);

        using var form = ConfirmForm(PdfBytes, "protokol.pdf", "application/pdf");
        var response = await employee.PostAsync($"/api/v1/employee/rentals/{rentalId}/confirm-return", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RentalStatusChangedResponse>(CarRentalApiFactory.Json);
        Assert.Equal(RentalStatus.Completed, body!.Status);

        var details = await employee.GetFromJsonAsync<EmployeeRentalDetails>($"/api/v1/employee/rentals/{rentalId}", CarRentalApiFactory.Json);
        Assert.Equal("anna.pracownik", details!.CompletedBy);
        Assert.Equal("Auto czyste, bez nowych rys.", details.ReturnNote);
        Assert.True(details.HasAttachment);

        var attachment = await employee.GetAsync($"/api/v1/employee/rentals/{rentalId}/attachment");
        Assert.Equal(HttpStatusCode.OK, attachment.StatusCode);
        Assert.Equal("application/pdf", attachment.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PdfBytes, await attachment.Content.ReadAsByteArrayAsync());

        // Po zakończeniu auto znowu jest wolne w tym terminie
        var broker = factory.CreateBrokerClient();
        var newOffer = await broker.PostAsJsonAsync("/api/v1/offers", TestApi.OfferBody());
        Assert.Equal(HttpStatusCode.Created, newOffer.StatusCode);
    }

    [Fact]
    public async Task ConfirmReturn_FileWithWrongContent_Returns400()
    {
        await using var factory = new CarRentalApiFactory();
        var employee = factory.CreateEmployeeClient();
        var rentalId = await CreateReturnRequestedRentalAsync(factory);

        // Rozszerzenie .pdf, ale w środku zwykły tekst
        using var form = ConfirmForm("to nie jest pdf"u8.ToArray(), "protokol.pdf", "application/pdf");
        var response = await employee.PostAsync($"/api/v1/employee/rentals/{rentalId}/confirm-return", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmReturn_NotAllowedExtension_Returns400()
    {
        await using var factory = new CarRentalApiFactory();
        var employee = factory.CreateEmployeeClient();
        var rentalId = await CreateReturnRequestedRentalAsync(factory);

        using var form = ConfirmForm("hello"u8.ToArray(), "notatka.txt", "text/plain");
        var response = await employee.PostAsync($"/api/v1/employee/rentals/{rentalId}/confirm-return", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmReturn_ReturnNotRequested_Returns409()
    {
        await using var factory = new CarRentalApiFactory();
        var broker = factory.CreateBrokerClient();
        var employee = factory.CreateEmployeeClient();
        var rentalId = await TestApi.CreateConfirmedRentalAsync(factory, broker);

        using var form = ConfirmForm(PdfBytes, "protokol.pdf", "application/pdf");
        var response = await employee.PostAsync($"/api/v1/employee/rentals/{rentalId}/confirm-return", form);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Attachment_None_Returns404()
    {
        await using var factory = new CarRentalApiFactory();
        var employee = factory.CreateEmployeeClient();
        var rentalId = await CreateReturnRequestedRentalAsync(factory);

        var response = await employee.GetAsync($"/api/v1/employee/rentals/{rentalId}/attachment");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmReturn_SendsReturnEmailToCustomer()
    {
        await using var factory = new CarRentalApiFactory();
        var employee = factory.CreateEmployeeClient();
        var rentalId = await CreateReturnRequestedRentalAsync(factory);
        var mailsBefore = factory.Emails.Sent.Count;

        using var form = ConfirmForm(PdfBytes, "protokol.pdf", "application/pdf");
        var response = await employee.PostAsync($"/api/v1/employee/rentals/{rentalId}/confirm-return", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mail = Assert.Single(factory.Emails.Sent.Skip(mailsBefore));
        Assert.Equal("jan.kowalski@example.com", mail.To);
        Assert.Contains("Zwrot auta potwierdzony", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("Toyota Corolla (2022)", mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmReturn_WithBlobStorage_FileInContainerAndDownloadOnlyForEmployee()
    {
        var container = new FakeBlobContainerClient();
        await using var factory = new CarRentalApiFactory
        {
            ConfigureServices = services => services.AddSingleton<IFileStorage>(new BlobFileStorage(container, TimeProvider.System))
        };
        var employee = factory.CreateEmployeeClient();
        var rentalId = await CreateReturnRequestedRentalAsync(factory);

        using var form = ConfirmForm(PdfBytes, "protokol.pdf", "application/pdf");
        var confirm = await employee.PostAsync($"/api/v1/employee/rentals/{rentalId}/confirm-return", form);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var blob = Assert.Single(container.Blobs);
        Assert.Equal(PdfBytes, blob.Value.Content);
        Assert.EndsWith(".pdf", blob.Key, StringComparison.Ordinal);

        // Plik wychodzi przez API (strumień z Bloba), tylko z rolą Employee
        var download = await employee.GetAsync($"/api/v1/employee/rentals/{rentalId}/attachment");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(PdfBytes, await download.Content.ReadAsByteArrayAsync());

        var asBroker = await factory.CreateBrokerClient().GetAsync($"/api/v1/employee/rentals/{rentalId}/attachment");
        Assert.Equal(HttpStatusCode.Unauthorized, asBroker.StatusCode);
        var notEmployee = await factory.CreateEmployeeClient(roles: "Customer").GetAsync($"/api/v1/employee/rentals/{rentalId}/attachment");
        Assert.Equal(HttpStatusCode.Forbidden, notEmployee.StatusCode);
    }

    [Fact]
    public async Task Attachment_BlobDeleted_Returns404()
    {
        var container = new FakeBlobContainerClient();
        await using var factory = new CarRentalApiFactory
        {
            ConfigureServices = services => services.AddSingleton<IFileStorage>(new BlobFileStorage(container, TimeProvider.System))
        };
        var employee = factory.CreateEmployeeClient();
        var rentalId = await CreateReturnRequestedRentalAsync(factory);
        using var form = ConfirmForm(PdfBytes, "protokol.pdf", "application/pdf");
        (await employee.PostAsync($"/api/v1/employee/rentals/{rentalId}/confirm-return", form)).EnsureSuccessStatusCode();

        container.Blobs.Clear();
        var response = await employee.GetAsync($"/api/v1/employee/rentals/{rentalId}/attachment");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
