using System.ComponentModel.DataAnnotations;

namespace CarRental.Worker;

/// <summary>Ustawienia Workera (sekcja Worker).</summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>Ile wiadomości z kolejki przetwarzamy naraz.</summary>
    [Range(1, 16)]
    public int MaxConcurrentCalls { get; set; } = 2;

    /// <summary>Co ile sekund szukamy wniosków, które czekają za długo (np. wiadomość nie doszła na kolejkę).</summary>
    [Range(5, 3600)]
    public int SweepIntervalSeconds { get; set; } = 60;

    /// <summary>Po ilu sekundach wniosek Pending uznajemy za zgubiony i przetwarzamy go bez wiadomości.</summary>
    [Range(0, 86400)]
    public int SweepAfterSeconds { get; set; } = 120;
}
