using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<GameHub>("/gameHub");

app.Run();
public class GameHub : Hub
{
    private static Dictionary<string, GameRoom> _rooms = new();
    private static readonly object _lock = new();

    public async Task<object> JoinRoom(string roomId)
    {
        string connectionId = Context.ConnectionId;

        lock (_lock)
        {
            if (!_rooms.ContainsKey(roomId))
            {
                _rooms[roomId] = new GameRoom
                {
                    RoomId = roomId,
                    Players = new List<string> { connectionId },
                    Board = new char[9],
                    CurrentTurn = 0
                };
                for (int i = 0; i < 9; i++) _rooms[roomId].Board[i] = ' ';

                _rooms[roomId].PlayersSymbols[connectionId] = 'X';
            }
            else
            {
                var room = _rooms[roomId];
                if (room.Players.Count >= 2)
                    return new { success = false, error = "Room is full" };

                room.Players.Add(connectionId);
                room.PlayersSymbols[connectionId] = 'O';
            }
        }

        await Groups.AddToGroupAsync(connectionId, roomId);

        var currentRoom = _rooms[roomId];
        await Clients.Group(roomId).SendAsync("PlayerJoined", currentRoom.Players.Count);

        if (currentRoom.Players.Count == 2)
        {
            await Clients.Group(roomId).SendAsync("GameStart", currentRoom.PlayersSymbols);
            await SendBoardToRoom(roomId);
            await Clients.Group(roomId).SendAsync("SetTurn", currentRoom.Players[currentRoom.CurrentTurn]);
        }

        return new { success = true, symbol = currentRoom.PlayersSymbols[connectionId] };
    }

    public async Task MakeMove(string roomId, int position)
    {
        string connectionId = Context.ConnectionId;

        if (!_rooms.ContainsKey(roomId)) return;

        var room = _rooms[roomId];

        if (room.Players[room.CurrentTurn] != connectionId) return;

        if (room.Board[position] != ' ') return;

        char symbol = room.PlayersSymbols[connectionId];
        room.Board[position] = symbol;

        if (CheckWin(room.Board, symbol))
        {
            room.GameOver = true;
            await Clients.Group(roomId).SendAsync("GameEnd", $"Игрок {symbol} победил!");
            await SendBoardToRoom(roomId);
            return;
        }

        bool isDraw = true;
        for (int i = 0; i < 9; i++)
        {
            if (room.Board[i] == ' ') { isDraw = false; break; }
        }

        if (isDraw)
        {
            room.GameOver = true;
            await Clients.Group(roomId).SendAsync("GameEnd", "Ничья!");
            await SendBoardToRoom(roomId);
            return;
        }
        room.CurrentTurn = room.CurrentTurn == 0 ? 1 : 0;
        await SendBoardToRoom(roomId);
        await Clients.Group(roomId).SendAsync("SetTurn", room.Players[room.CurrentTurn]);
    }

    public async Task SendChat(string roomId, string message)
    {
        await Clients.Group(roomId).SendAsync("NewMessage", Context.ConnectionId, message);
    }

    public async Task ResetGame(string roomId)
    {
        if (!_rooms.ContainsKey(roomId)) return;

        var room = _rooms[roomId];
        for (int i = 0; i < 9; i++) room.Board[i] = ' ';
        room.GameOver = false;
        room.CurrentTurn = 0;

        await SendBoardToRoom(roomId);
        await Clients.Group(roomId).SendAsync("GameReset");
        await Clients.Group(roomId).SendAsync("SetTurn", room.Players[room.CurrentTurn]);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string connectionId = Context.ConnectionId;

        foreach (var room in _rooms.Values)
        {
            if (room.Players.Contains(connectionId))
            {
                room.Players.Remove(connectionId);
                room.PlayersSymbols.Remove(connectionId);

                if (room.Players.Count == 1)
                {
                    await Clients.Group(room.RoomId).SendAsync("GameEnd", "Противник отключился! Вы победили!");
                }
                break;
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task SendBoardToRoom(string roomId)
    {
        if (_rooms.ContainsKey(roomId))
        {
            await Clients.Group(roomId).SendAsync("BoardUpdate", _rooms[roomId].Board);
        }
    }

    private bool CheckWin(char[] board, char symbol)
    {
        int[][] wins = new int[][]
        {
            new[] {0,1,2}, new[] {3,4,5}, new[] {6,7,8},
            new[] {0,3,6}, new[] {1,4,7}, new[] {2,5,8},
            new[] {0,4,8}, new[] {2,4,6}
        };

        foreach (var win in wins)
        {
            if (board[win[0]] == symbol && board[win[1]] == symbol && board[win[2]] == symbol)
                return true;
        }
        return false;
    }
}

public class GameRoom
{
    public string RoomId { get; set; } = "";
    public List<string> Players { get; set; } = new();
    public Dictionary<string, char> PlayersSymbols { get; set; } = new();
    public char[] Board { get; set; } = new char[9];
    public int CurrentTurn { get; set; }
    public bool GameOver { get; set; }
}