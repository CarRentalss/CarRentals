using System.Globalization;
using System.Net;
using CarRental.Api.Domain;

namespace CarRental.Api.Email;

/// <summary>Dane wynajmu potrzebne w mailu (bez encji EF, żeby mail dało się złożyć po zamknięciu transakcji).</summary>
public sealed record RentalEmailData(
    Guid RentalId,
    string Email,
    string FirstName,
    string Car,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalPrice,
    RentalStatus Status,
    string? Reason)
{
    public static RentalEmailData From(Rental rental) => new(
        rental.Id,
        rental.Email,
        rental.FirstName,
        $"{rental.Car.Make} {rental.Car.Model} ({rental.Car.Year})",
        rental.StartDate,
        rental.EndDate,
        rental.TotalPrice,
        rental.Status,
        rental.Reason);
}

/// <summary>Proste szablony maili po polsku. Dane od klienta zawsze przez HtmlEncode.</summary>
public static class EmailTemplates
{
    private static readonly CultureInfo Pl = PolishCulture();

    public static EmailMessage RentalConfirmed(RentalEmailData d) => Build(
        d,
        "Wynajem potwierdzony",
        $"Twój wynajem auta {d.Car} jest potwierdzony.",
        "Auto czeka na Ciebie w wybranym terminie. Szczegóły i zgłoszenie zwrotu znajdziesz w zakładce Moje wynajmy.");

    public static EmailMessage RentalRejected(RentalEmailData d) => Build(
        d,
        "Wynajem odrzucony",
        $"Niestety nie możemy potwierdzić wynajmu auta {d.Car}.",
        $"Powód: {d.Reason ?? "brak"}. Wybierz inne auto albo inny termin i poproś o nową ofertę.");

    public static EmailMessage ReturnCompleted(RentalEmailData d) => Build(
        d,
        "Zwrot auta potwierdzony",
        $"Pracownik potwierdził zwrot auta {d.Car}. Wynajem jest zakończony.",
        "Dziękujemy, że skorzystałeś z naszej wypożyczalni.");

    private static EmailMessage Build(RentalEmailData d, string title, string lead, string footer)
    {
        var subject = $"Car Rentals: {title} ({d.Car})";
        var dates = $"{d.StartDate.ToString("d MMMM yyyy", Pl)} - {d.EndDate.ToString("d MMMM yyyy", Pl)}";
        var price = d.TotalPrice.ToString("N2", Pl) + " PLN";
        var number = d.RentalId.ToString();

        string E(string s) => WebUtility.HtmlEncode(s);

        var html = $"""
            <!DOCTYPE html>
            <html lang="pl">
            <body style="font-family: Arial, sans-serif; color: #1f2933; line-height: 1.5;">
              <h1 style="font-size: 20px;">{E(title)}</h1>
              <p>Dzień dobry {E(d.FirstName)},</p>
              <p>{E(lead)}</p>
              <table style="border-collapse: collapse;">
                <tr><td style="padding: 2px 12px 2px 0;">Auto</td><td><strong>{E(d.Car)}</strong></td></tr>
                <tr><td style="padding: 2px 12px 2px 0;">Termin</td><td>{E(dates)}</td></tr>
                <tr><td style="padding: 2px 12px 2px 0;">Cena</td><td>{E(price)}</td></tr>
                <tr><td style="padding: 2px 12px 2px 0;">Numer wynajmu</td><td>{E(number)}</td></tr>
              </table>
              <p>{E(footer)}</p>
              <p style="color: #6b7280; font-size: 12px;">Wiadomość wysłana automatycznie, nie odpowiadaj na nią.</p>
            </body>
            </html>
            """;

        var text = $"""
            {title}

            Dzień dobry {d.FirstName},
            {lead}

            Auto: {d.Car}
            Termin: {dates}
            Cena: {price}
            Numer wynajmu: {number}

            {footer}
            """;

        return new EmailMessage(d.Email, subject, html, text);
    }

    // Obraz bez ICU (tryb invariant) nie zna pl-PL, wtedy daty wyjdą po angielsku, ale mail i tak pójdzie
    private static CultureInfo PolishCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo("pl-PL");
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
