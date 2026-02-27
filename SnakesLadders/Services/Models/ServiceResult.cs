namespace SnakesLadders.Services;

public sealed class ServiceResult<T>
{
    public bool Success { get; }
    public string? Error { get; }
    public T? Value { get; }

    private ServiceResult(bool success, T? value, string? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    public static ServiceResult<T> Ok(T value) => new(true, value, null);

    public static ServiceResult<T> Fail(string error) => new(false, default, error);
}

public sealed class AutoRollDispatch
{
    public required string RoomCode { get; init; }
    public required string PlayerId { get; init; }
    public required Contracts.TurnEnvelope Payload { get; init; }
}
