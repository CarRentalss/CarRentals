using Broker.Api.Domain;

namespace Broker.Api.Tests;

public sealed class UserProfileTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    [Theory]
    [InlineData("2000-11-15", 26)] // urodziny były wczoraj
    [InlineData("2000-11-16", 26)] // urodziny dziś
    [InlineData("2000-11-17", 25)] // urodziny jutro
    [InlineData("2008-11-16", 18)]
    public void GetAge_DateOfBirthSet_ReturnsFullYears(string dateOfBirth, int expected)
    {
        var profile = new UserProfile { DateOfBirth = DateOnly.Parse(dateOfBirth, System.Globalization.CultureInfo.InvariantCulture) };

        Assert.Equal(expected, profile.GetAge(Today));
    }

    [Fact]
    public void GetAge_NoDateOfBirth_ReturnsNull()
    {
        Assert.Null(new UserProfile().GetAge(Today));
    }

    [Fact]
    public void GetAge_BornOnLeapDay_CountsBirthdayOnFirstOfMarchInCommonYear()
    {
        var profile = new UserProfile { DateOfBirth = new DateOnly(2004, 2, 29) };

        Assert.Equal(21, profile.GetAge(new DateOnly(2026, 2, 28)));
        Assert.Equal(22, profile.GetAge(new DateOnly(2026, 3, 1)));
    }

    [Theory]
    [InlineData("2018-06-01", 8)]
    [InlineData("2025-11-17", 0)]
    [InlineData("2026-11-16", 0)]
    public void GetLicenseYears_LicenseDateSet_ReturnsFullYears(string obtainedOn, int expected)
    {
        var profile = new UserProfile { LicenseObtainedOn = DateOnly.Parse(obtainedOn, System.Globalization.CultureInfo.InvariantCulture) };

        Assert.Equal(expected, profile.GetLicenseYears(Today));
    }

    [Fact]
    public void GetLicenseYears_DateInFuture_ReturnsZero()
    {
        var profile = new UserProfile { LicenseObtainedOn = Today.AddDays(5) };

        Assert.Equal(0, profile.GetLicenseYears(Today));
    }

    [Fact]
    public void MissingFields_NewProfileWithNamesOnly_ListsDriverData()
    {
        var profile = new UserProfile { FirstName = "Anna", LastName = "Nowak", Email = "anna@example.com" };

        Assert.False(profile.IsComplete);
        Assert.Equal(
            new[] { UserProfile.DateOfBirthField, UserProfile.LicenseObtainedOnField, UserProfile.LocationField },
            profile.MissingFields);
    }

    [Fact]
    public void IsComplete_AllFieldsSet_ReturnsTrue()
    {
        var profile = new UserProfile
        {
            FirstName = "Anna",
            LastName = "Nowak",
            Email = "anna@example.com",
            DateOfBirth = new DateOnly(1998, 5, 14),
            LicenseObtainedOn = new DateOnly(2017, 6, 1),
            Location = "Warszawa"
        };

        Assert.True(profile.IsComplete);
        Assert.Empty(profile.MissingFields);
    }
}
