using CarRental.Api.Domain;

namespace CarRental.Api.Services;

public static class RentalQueries
{
    /// <summary>
    /// Wynajmy, które blokują auto: czekające, potwierdzone i w trakcie zwrotu.
    /// Odrzucone i zakończone nie blokują. To samo co Rental.BlocksCar, ale jako wyrażenie,
    /// które EF przetłumaczy na SQL (właściwości encji EF w zapytaniu nie przetłumaczy).
    /// </summary>
    public static IQueryable<Rental> BlockingCar(this IQueryable<Rental> rentals, int carId) =>
        rentals.Where(r => r.CarId == carId &&
            (r.Status == RentalStatus.Pending ||
             r.Status == RentalStatus.Confirmed ||
             r.Status == RentalStatus.ReturnRequested));

    /// <summary>Czy termin [start, end) nachodzi na termin wynajmu. Dzień oddania jest wolny dla kolejnego klienta.</summary>
    public static IQueryable<Rental> Overlapping(this IQueryable<Rental> rentals, DateOnly startDate, DateOnly endDate) =>
        rentals.Where(r => r.StartDate < endDate && startDate < r.EndDate);
}
