namespace AION_2_Companion.ViewModels;

public sealed class LogEntry
{
    public string Time { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string DirectionColor { get; set; } = "#62D9FF";
    public string Opcode { get; set; } = string.Empty;
    public int Length { get; set; }
    public string Summary { get; set; } = string.Empty;
}
