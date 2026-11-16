using CarRental.Api.Domain;

namespace CarRental.Api.Tests;

/// <summary>Testy metod domenowych (przejścia statusów wynajmu, ważność oferty).</summary>
public class DomainTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static Offer NewOffer() => new()
    {
        Id = Guid.NewGuid(),
        CarId = 3,
        StartDate = new DateOnly(2026, 11, 10),
        EndDate = new DateOnly(2026, 11, 13),
        TotalPrice = 480m,
        BrokerName = "OurBroker",
        CreatedAtUtc = Now,
        ExpiresAtUtc = Now.AddMinutes(10)
    };

    [Fact]
    public void IsExpired_ExactlyAtExpiry_ReturnsFalse()
    {
        var offer = NewOffer();

        Assert.False(offer.IsExpired(offer.ExpiresAtUtc));
        Assert.True(offer.IsExpired(offer.ExpiresAtUtc.AddSeconds(1)));
    }

    [Fact]
    public void CreateRental_FromOffer_CopiesTermsAndStartsPending()
    {
        var offer = NewOffer();

        var rental = offer.CreateRental(" Jan ", "Kowalski", "jan@example.com ", Now);

        Assert.Equal(offer.Id, rental.OfferId);
        Assert.Equal(offer.CarId, rental.CarId);
        Assert.Equal(offer.StartDate, rental.StartDate);
        Assert.Equal(offer.EndDate, rental.EndDate);
        Assert.Equal(offer.TotalPrice, rental.TotalPrice);
        Assert.Equal("OurBroker", rental.BrokerName);
        Assert.Equal("Jan", rental.FirstName);
        Assert.Equal("jan@example.com", rental.Email);
        Assert.Equal(RentalStatus.Pending, rental.Status);
        Assert.NotEqual(Guid.Empty, rental.Id);
    }

    [Fact]
    public void Process_InactiveCar_RejectsWithReason()
    {
        var rental = NewOffer().CreateRental("Jan", "Kowalski", "jan@example.com", Now);

        rental.Process(carIsActive: false, Now.AddSeconds(10));

        Assert.Equal(RentalStatus.Rejected, rental.Status);
        Assert.False(string.IsNullOrWhiteSpace(rental.Reason));
        Assert.False(rental.BlocksCar);
    }

    [Fact]
    public void RequestReturn_ConfirmedRental_SetsReturnRequested()
    {
        var rental = NewOffer().CreateRental("Jan", "Kowalski", "jan@example.com", Now);
        rental.Process(carIsActive: true, Now);
        Assert.True(rental.CanRequestReturn);

        rental.RequestReturn(Now.AddDays(1));

        Assert.Equal(RentalStatus.ReturnRequested, rental.Status);
        Assert.Equal(Now.AddDays(1), rental.ReturnRequestedAtUtc);
        Assert.True(rental.CanConfirmReturn);
        Assert.True(rental.BlocksCar);
    }

    [Fact]
    public void RequestReturn_PendingRental_Throws()
    {
        var rental = NewOffer().CreateRental("Jan", "Kowalski", "jan@example.com", Now);

        Assert.Throws<InvalidOperationException>(() => rental.RequestReturn(Now));
    }

    [Fact]
    public void CompleteReturn_ReturnRequestedRental_SetsCompletedAndAttachment()
    {
        var rental = NewOffer().CreateRental("Jan", "Kowalski", "jan@example.com", Now);
        rental.Process(carIsActive: true, Now);
        rental.RequestReturn(Now);

        rental.CompleteReturn("anna", " Bez rys. ", "abc.pdf", "application/pdf", "protokol.pdf", Now.AddHours(1));

        Assert.Equal(RentalStatus.Completed, rental.Status);
        Assert.Equal("anna", rental.CompletedBy);
        Assert.Equal("Bez rys.", rental.ReturnNote);
        Assert.Equal("abc.pdf", rental.AttachmentKey);
        Assert.False(rental.BlocksCar);
    }

    [Theory]
    [InlineData("JAN@example.com ", true)]
    [InlineData("inny@example.com", false)]
    public void HasCustomerEmail_VariousEmails_ComparesIgnoringCase(string email, bool expected)
    {
        var rental = NewOffer().CreateRental("Jan", "Kowalski", "jan@example.com", Now);

        Assert.Equal(expected, rental.HasCustomerEmail(email));
    }

    [Fact]
    public void IsAvailableIn_DifferentCase_ReturnsTrue()
    {
        var car = new Car { Location = "Warszawa" };

        Assert.True(car.IsAvailableIn(" warszawa"));
        Assert.False(car.IsAvailableIn("Kraków"));
    }
}
