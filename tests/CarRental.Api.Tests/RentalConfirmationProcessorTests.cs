using CarRental.Api.Data;
using CarRental.Api.Domain;
using CarRental.Api.Email;
using CarRental.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarRental.Api.Tests;

/// <summary>
/// Wspólna logika potwierdzania (ta sama klasa działa w CarRental.Worker i w RentalConfirmationWorker w API).
/// Baza SQLite w pamięci, maile do FakeEmailSender.
/// </summary>
public class RentalConfirmationProcessorTests
{
    private static async Task<Guid> CreatePendingRentalAsync(CarRentalApiFactory factory, int carId = 1)
    {
        var broker = factory.CreateBrokerClient();
        var offer = await TestApi.CreateOfferAsync(broker, TestApi.OfferBody(carId: carId));
        var rental = await TestApi.CreateRentalAsync(broker, offer.OfferId);
        return rental.RentalId;
    }

    private static async Task<RentalStatus?> ProcessAsync(CarRentalApiFactory factory, Guid rentalId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RentalConfirmationProcessor>().ProcessAsync(rentalId, CancellationToken.None);
    }

    private static async Task<Rental> LoadAsync(CarRentalApiFactory factory, Guid rentalId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarRentalDbContext>();
        return await db.Rentals.AsNoTracking().SingleAsync(r => r.Id == rentalId);
    }

    [Fact]
    public async Task Process_FreeCar_ConfirmsAndSendsConfirmationEmail()
    {
        await using var factory = new CarRentalApiFactory();
        var rentalId = await CreatePendingRentalAsync(factory);

        var status = await ProcessAsync(factory, rentalId);

        Assert.Equal(RentalStatus.Confirmed, status);
        Assert.Equal(RentalStatus.Confirmed, (await LoadAsync(factory, rentalId)).Status);
        var mail = Assert.Single(factory.Emails.Sent);
        Assert.Equal("jan.kowalski@example.com", mail.To);
        Assert.Contains("Wynajem potwierdzony", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("Toyota Corolla (2022)", mail.Html, StringComparison.Ordinal);
        Assert.Contains(rentalId.ToString(), mail.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Process_CarWithdrawnFromFleet_RejectsAndSendsRejectionEmail()
    {
        await using var factory = new CarRentalApiFactory();
        var rentalId = await CreatePendingRentalAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CarRentalDbContext>();
            var car = await db.Cars.SingleAsync(c => c.Id == 1);
            car.IsActive = false;
            await db.SaveChangesAsync();
        }

        var status = await ProcessAsync(factory, rentalId);

        Assert.Equal(RentalStatus.Rejected, status);
        var mail = Assert.Single(factory.Emails.Sent);
        Assert.Contains("Wynajem odrzucony", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("wycofany z floty", mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Process_CarAlreadyRentedInThatTime_Rejects()
    {
        await using var factory = new CarRentalApiFactory();
        var rentalId = await CreatePendingRentalAsync(factory);

        // Ktoś inny ma już potwierdzony wynajem tego auta w tym samym terminie (np. wpis z innej drogi niż oferta)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CarRentalDbContext>();
            var now = factory.Clock.GetUtcNow().UtcDateTime;
            var offer = new Offer
            {
                Id = Guid.NewGuid(),
                CarId = 1,
                DriverAge = 40,
                LicenseYears = 20,
                Location = "Warszawa",
                StartDate = TestApi.Start,
                EndDate = TestApi.End,
                DailyPrice = 150m,
                DailyInsurancePrice = 30m,
                TotalPrice = 540m,
                Currency = "PLN",
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(10),
                BrokerName = "OtherGroupBroker"
            };
            var other = offer.CreateRental("Anna", "Nowak", "anna@example.com", now);
            other.Status = RentalStatus.Confirmed;
            db.Offers.Add(offer);
            db.Rentals.Add(other);
            await db.SaveChangesAsync();
        }

        var status = await ProcessAsync(factory, rentalId);

        Assert.Equal(RentalStatus.Rejected, status);
        Assert.Contains("wynajęty", (await LoadAsync(factory, rentalId)).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Process_SameRentalTwice_SecondTimeDoesNothing()
    {
        // Service Bus może dostarczyć tę samą wiadomość drugi raz
        await using var factory = new CarRentalApiFactory();
        var rentalId = await CreatePendingRentalAsync(factory);

        var first = await ProcessAsync(factory, rentalId);
        var second = await ProcessAsync(factory, rentalId);

        Assert.Equal(RentalStatus.Confirmed, first);
        Assert.Null(second);
        Assert.Single(factory.Emails.Sent);
    }

    [Fact]
    public async Task Process_UnknownRental_ReturnsNull()
    {
        await using var factory = new CarRentalApiFactory();

        var status = await ProcessAsync(factory, Guid.NewGuid());

        Assert.Null(status);
        Assert.Empty(factory.Emails.Sent);
    }

    [Fact]
    public async Task Process_EmailFails_StatusIsStillSaved()
    {
        await using var factory = new CarRentalApiFactory
        {
            ConfigureServices = services => services.AddSingleton<IEmailSender, ThrowingEmailSender>()
        };
        var rentalId = await CreatePendingRentalAsync(factory);

        var status = await ProcessAsync(factory, rentalId);

        Assert.Equal(RentalStatus.Confirmed, status);
        Assert.Equal(RentalStatus.Confirmed, (await LoadAsync(factory, rentalId)).Status);
    }

    [Fact]
    public async Task ProcessPending_WaitsForDelay()
    {
        await using var factory = new CarRentalApiFactory();
        var rentalId = await CreatePendingRentalAsync(factory);

        int processedNow;
        using (var scope = factory.Services.CreateScope())
        {
            processedNow = await scope.ServiceProvider.GetRequiredService<RentalConfirmationProcessor>()
                .ProcessPendingAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(11));

        int processedLater;
        using (var scope = factory.Services.CreateScope())
        {
            processedLater = await scope.ServiceProvider.GetRequiredService<RentalConfirmationProcessor>()
                .ProcessPendingAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }

        Assert.Equal(0, processedNow);
        Assert.Equal(1, processedLater);
        Assert.Equal(RentalStatus.Confirmed, (await LoadAsync(factory, rentalId)).Status);
    }

    [Fact]
    public void Templates_EncodeCustomerData()
    {
        var data = new RentalEmailData(Guid.NewGuid(), "x@example.com", "<b>Jan</b>", "Toyota Corolla (2022)",
            TestApi.Start, TestApi.End, 540m, RentalStatus.Confirmed, null);

        var mail = EmailTemplates.RentalConfirmed(data);

        Assert.Contains("&lt;b&gt;Jan&lt;/b&gt;", mail.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Jan</b>", mail.Html, StringComparison.Ordinal);
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("ACS niedostępne (test).");
    }
}
