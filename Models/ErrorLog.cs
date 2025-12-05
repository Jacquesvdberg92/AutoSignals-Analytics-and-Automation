public class ErrorLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? Source { get; set; }
    public string? AdditionalData { get; set; }
}
