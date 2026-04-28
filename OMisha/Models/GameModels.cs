namespace OMisha.Models;

public class GameRoom
{
    public string RoomId { get; set; } = Guid.NewGuid().ToString();
    public List<string> Players { get; set; } = new(); 
    public string? CurrentTurnPlayerId { get; set; }
    public char[] Board { get; set; } = Enumerable.Repeat(' ', 9).ToArray();
    public bool GameOver { get; set; } = false;
    public string? Winner { get; set; } 
    public List<ChatMessage> ChatHistory { get; set; } = new();
}

public class ChatMessage
{
    public string PlayerName { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}