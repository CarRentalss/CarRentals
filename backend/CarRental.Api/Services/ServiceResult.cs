using System.Diagnostics.CodeAnalysis;

namespace CarRental.Api.Services;

/// <summary>Rodzaj błędu biznesowego. Kontroler zamienia go na kod HTTP z kontraktu.</summary>
public enum ServiceErrorType
{
    Validation, // 400
    Forbidden,  // 403
    NotFound,   // 404
    Conflict,   // 409
    Gone        // 410
}

public sealed record ServiceError(ServiceErrorType Type, string Message, string? Field = null)
{
    public static ServiceError Validation(string field, string message) => new(ServiceErrorType.Validation, message, field);
    public static ServiceError Forbidden(string message) => new(ServiceErrorType.Forbidden, message);
    public static ServiceError NotFound(string message) => new(ServiceErrorType.NotFound, message);
    public static ServiceError Conflict(string message) => new(ServiceErrorType.Conflict, message);
    public static ServiceError Gone(string message) => new(ServiceErrorType.Gone, message);
}

/// <summary>Wynik operacji: albo wartość, albo błąd. Dzięki temu serwisy nie rzucają wyjątków przy zwykłych błędach.</summary>
public sealed class ServiceResult<T>
{
    private ServiceResult(T? value, ServiceError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public ServiceError? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static ServiceResult<T> Success(T value) => new(value, null);
    public static ServiceResult<T> Failure(ServiceError error) => new(default, error);

    public static implicit operator ServiceResult<T>(T value) => Success(value);
    public static implicit operator ServiceResult<T>(ServiceError error) => Failure(error);
}
