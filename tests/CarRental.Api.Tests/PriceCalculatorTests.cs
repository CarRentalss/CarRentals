using CarRental.Api.Domain;
using CarRental.Api.Options;
using CarRental.Api.Pricing;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Tests;

/// <summary>Testy jednostkowe wyceny (logika biznesowa).</summary>
public class PriceCalculatorTests
{
    private static readonly Car Car = new() { Id = 1, Make = "Test", Model = "Car", BasePricePerDay = 100m, InsurancePricePerDay = 20m };
    private static readonly DateOnly Start = new(2026, 11, 10);

    private static PriceCalculator CreateCalculator() => new(Microsoft.Extensions.Options.Options.Create(new PricingOptions()));

    [Fact]
    public void ExperiencedDriver_PaysBasePrice()
    {
        var quote = CreateCalculator().Calculate(Car, driverAge: 30, licenseYears: 10, Start, Start.AddDays(3));

        Assert.Equal(100m, quote.DailyPrice);
        Assert.Equal(20m, quote.DailyInsurancePrice);
        Assert.Equal(3, quote.Days);
        Assert.Equal(360m, quote.TotalPrice);
    }

    [Fact]
    public void YoungDriver_PaysMore()
    {
        var quote = CreateCalculator().Calculate(Car, driverAge: 22, licenseYears: 4, Start, Start.AddDays(2));

        Assert.Equal(125m, quote.DailyPrice);
        Assert.Equal(25m, quote.DailyInsurancePrice);
        Assert.Equal(300m, quote.TotalPrice);
    }

    [Fact]
    public void NewDriver_PaysMore()
    {
        var quote = CreateCalculator().Calculate(Car, driverAge: 40, licenseYears: 1, Start, Start.AddDays(1));

        Assert.Equal(120m, quote.DailyPrice);
        Assert.Equal(24m, quote.DailyInsurancePrice);
    }

    [Fact]
    public void YoungAndNewDriver_BothMultipliersApply()
    {
        var calculator = CreateCalculator();

        Assert.Equal(1.5m, calculator.GetRiskMultiplier(20, 1));
        Assert.Equal(150m, calculator.Calculate(Car, 20, 1, Start, Start.AddDays(1)).DailyPrice);
    }

    [Theory]
    [InlineData(25, 3, 1.0)]
    [InlineData(24, 3, 1.25)]
    [InlineData(25, 2, 1.2)]
    public void Multiplier_Boundaries(int age, int licenseYears, double expected)
    {
        Assert.Equal((decimal)expected, CreateCalculator().GetRiskMultiplier(age, licenseYears));
    }

    [Fact]
    public void EndDateNotAfterStart_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateCalculator().Calculate(Car, 30, 10, Start, Start));
    }

    [Fact]
    public void Prices_AreRoundedToGrosze()
    {
        var car = new Car { BasePricePerDay = 99.99m, InsurancePricePerDay = 10.01m };

        var quote = CreateCalculator().Calculate(car, 22, 10, Start, Start.AddDays(1));

        Assert.Equal(124.99m, quote.DailyPrice); // 99.99 * 1.25 = 124.9875
        Assert.Equal(12.51m, quote.DailyInsurancePrice); // 10.01 * 1.25 = 12.5125
    }
}
