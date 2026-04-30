using Microsoft.AspNetCore.SignalR;

namespace FirstSignalR;

public class GameHub : Hub
{
    private static Dictionary<string, GameRoom> rooms = new Dictionary<string, GameRoom>();
    private static readonly object roomLock = new object();

    public async Task JoinRoom(string roomId)
    {
        string connectionId = Context.ConnectionId;
        string mySymbol = "X";
        bool isSpectator = false;

        lock (roomLock)
        {
            if (!rooms.ContainsKey(roomId))
            {
                rooms[roomId] = new GameRoom();
                rooms[roomId].PlayerX = connectionId;
                rooms[roomId].CurrentPlayer = "X";
                mySymbol = "X";
                Console.WriteLine($"[{DateTime.Now}] Создана комната {roomId}. Игрок {connectionId} играет X");
            }
            else
            {
                var room = rooms[roomId];

                if (room.PlayerX != null && room.PlayerO != null)
                {
          
                    room.Spectators.Add(connectionId);
                    isSpectator = true;
                    Console.WriteLine($"[{DateTime.Now}] Зритель {connectionId} присоединился к комнате {roomId}");
                }
                else if (room.PlayerO == null && room.PlayerX != connectionId)
                {
                    room.PlayerO = connectionId;
                    mySymbol = "O";
                    Console.WriteLine($"[{DateTime.Now}] Игрок {connectionId} присоединился к комнате {roomId} как O");
                }
                else
                {
                    return;
                }
            }
        }

        await Groups.AddToGroupAsync(connectionId, roomId);

        if (isSpectator)
        {
            await Clients.Caller.SendAsync("SetSpectator");
        }
        else
        {
            await Clients.Caller.SendAsync("SetSymbol", mySymbol);
        }

        var currentRoom = rooms[roomId];
        int playerCount = (currentRoom.PlayerX != null ? 1 : 0) + (currentRoom.PlayerO != null ? 1 : 0);
        int spectatorCount = currentRoom.Spectators.Count;
        await Clients.Group(roomId).SendAsync("PlayerCount", playerCount, spectatorCount);

        if (currentRoom.PlayerX != null && currentRoom.PlayerO != null)
        {
            Console.WriteLine($"[{DateTime.Now}] В комнате {roomId} оба игрока. Начинаем игру!");
            await Clients.Group(roomId).SendAsync("GameReady");
            await UpdateBoard(roomId);
            await SendTurn(roomId);
        }
    }

    public async Task MakeMove(string roomId, int position)
    {
        string connectionId = Context.ConnectionId;

        if (!rooms.ContainsKey(roomId))
        {
            Console.WriteLine($"[{DateTime.Now}] Ошибка: комната {roomId} не найдена");
            return;
        }

        var room = rooms[roomId];

        if (room.Spectators.Contains(connectionId))
        {
            await Clients.Caller.SendAsync("Error", "Вы зритель и не можете ходить!");
            return;
        }

        string mySymbol = room.PlayerX == connectionId ? "X" : "O";

        Console.WriteLine($"[{DateTime.Now}] Игрок {mySymbol} пытается сделать ход в позицию {position}");
        Console.WriteLine($"[{DateTime.Now}] Текущий игрок: {room.CurrentPlayer}, GameOver: {room.GameOver}");

        if (room.GameOver)
        {
            Console.WriteLine($"[{DateTime.Now}] Игра уже окончена");
            return;
        }

        if (room.CurrentPlayer != mySymbol)
        {
            Console.WriteLine($"[{DateTime.Now}] Ошибка: сейчас ход {room.CurrentPlayer}, а не {mySymbol}");
            await Clients.Caller.SendAsync("Error", $"Сейчас не ваш ход! Ходит {room.CurrentPlayer}");
            return;
        }

        if (room.Board[position] != "")
        {
            Console.WriteLine($"[{DateTime.Now}] Ошибка: позиция {position} уже занята");
            await Clients.Caller.SendAsync("Error", "Эта клетка уже занята");
            return;
        }


        room.Board[position] = mySymbol;
        Console.WriteLine($"[{DateTime.Now}] Игрок {mySymbol} поставил {mySymbol} в позицию {position}");
        await UpdateBoard(roomId);


        if (CheckWin(room.Board, mySymbol))
        {
            room.GameOver = true;
            Console.WriteLine($"[{DateTime.Now}] Игрок {mySymbol} победил!");
            await Clients.Group(roomId).SendAsync("GameEnd", $"Игрок {mySymbol} победил!");
            return;
        }

        bool isFull = true;
        for (int i = 0; i < 9; i++)
        {
            if (room.Board[i] == "")
            {
                isFull = false;
                break;
            }
        }

        if (isFull)
        {
            room.GameOver = true;
            Console.WriteLine($"[{DateTime.Now}] Ничья!");
            await Clients.Group(roomId).SendAsync("GameEnd", "Ничья!");
            return;
        }

        room.CurrentPlayer = room.CurrentPlayer == "X" ? "O" : "X";
        Console.WriteLine($"[{DateTime.Now}] Ход переходит к {room.CurrentPlayer}");

        await SendTurn(roomId);
        await Clients.Group(roomId).SendAsync("TurnChanged", room.CurrentPlayer);
    }

    public async Task SendMessage(string roomId, string message)
    {
        string connectionId = Context.ConnectionId;

        if (!rooms.ContainsKey(roomId)) return;

        var room = rooms[roomId];
        string playerName;

        if (room.PlayerX == connectionId)
            playerName = "X";
        else if (room.PlayerO == connectionId)
            playerName = "O";
        else
            playerName = "Зритель";

        await Clients.Group(roomId).SendAsync("NewMessage", playerName, message);
    }

    public async Task ResetGame(string roomId)
    {
        string connectionId = Context.ConnectionId;

        if (!rooms.ContainsKey(roomId)) return;

        var room = rooms[roomId];

        if (room.PlayerX != connectionId && room.PlayerO != connectionId)
        {
            await Clients.Caller.SendAsync("Error", "Только игроки могут сбросить игру!");
            return;
        }

        for (int i = 0; i < 9; i++)
            room.Board[i] = "";

        room.GameOver = false;
        room.CurrentPlayer = "X";

        Console.WriteLine($"[{DateTime.Now}] Игра в комнате {roomId} сброшена");

        await UpdateBoard(roomId);
        await SendTurn(roomId);
        await Clients.Group(roomId).SendAsync("GameReset");
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string connectionId = Context.ConnectionId;
        string roomIdToRemove = null;
        string otherPlayerId = null;

        lock (roomLock)
        {
            foreach (var kvp in rooms)
            {
                var room = kvp.Value;
                if (room.PlayerX == connectionId || room.PlayerO == connectionId)
                {
                    otherPlayerId = room.PlayerX == connectionId ? room.PlayerO : room.PlayerX;
                    roomIdToRemove = kvp.Key;
                    break;
                }
                else if (room.Spectators.Contains(connectionId))
                {
                    room.Spectators.Remove(connectionId);
                    Console.WriteLine($"[{DateTime.Now}] Зритель {connectionId} отключился от комнаты {kvp.Key}");
                    return;
                }
            }

            if (roomIdToRemove != null)
            {
                rooms.Remove(roomIdToRemove);
                Console.WriteLine($"[{DateTime.Now}] Игрок {connectionId} отключился. Комната {roomIdToRemove} удалена");
            }
        }

        if (otherPlayerId != null)
        {
            await Clients.Client(otherPlayerId).SendAsync("GameEnd", "Соперник отключился! Вы победили!");
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task UpdateBoard(string roomId)
    {
        if (rooms.ContainsKey(roomId))
        {
            var board = rooms[roomId].Board;
            Console.WriteLine($"[{DateTime.Now}] Отправка обновления поля: [{string.Join(",", board)}]");
            await Clients.Group(roomId).SendAsync("BoardUpdate", board);
        }
    }

    private async Task SendTurn(string roomId)
    {
        if (rooms.ContainsKey(roomId))
        {
            string currentPlayer = rooms[roomId].CurrentPlayer;
            Console.WriteLine($"[{DateTime.Now}] Отправка информации о ходе: {currentPlayer}");
            await Clients.Group(roomId).SendAsync("Turn", currentPlayer);
        }
    }

    private bool CheckWin(string[] board, string symbol)
    {
        int[][] wins = {
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
    public string? PlayerX { get; set; }
    public string? PlayerO { get; set; }
    public List<string> Spectators { get; set; } = new List<string>();
    public string[] Board { get; set; } = new string[9];
    public string CurrentPlayer { get; set; } = "X";
    public bool GameOver { get; set; } = false;

    public GameRoom()
    {
        for (int i = 0; i < 9; i++)
            Board[i] = "";
    }
}