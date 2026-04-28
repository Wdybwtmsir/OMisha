using Microsoft.AspNetCore.SignalR;
using OMisha.Models;
using System.Collections.Concurrent;
using OMisha.Models;

namespace ChatHub;

public class GameHub : Hub
{
    private static readonly ConcurrentDictionary<string, GameRoom> _rooms = new();

    public async Task<string> JoinRoom(string roomId)
    {
        var connectionId = Context.ConnectionId;

        if (!_rooms.TryGetValue(roomId, out var room))
        {
            room = new GameRoom { RoomId = roomId };
            room.Players.Add(connectionId);
            room.CurrentTurnPlayerId = connectionId; 
            _rooms[roomId] = room;
            await Groups.AddToGroupAsync(connectionId, roomId);
            await Clients.Caller.SendAsync("GameStarted", "Вы создали комнату. Ждём второго игрока...");
            return "created";
        }
        else
        {
            if (room.Players.Count >= 2)
            {
                await Clients.Caller.SendAsync("GameError", "Комната полна");
                return "full";
            }

            room.Players.Add(connectionId);
            await Groups.AddToGroupAsync(connectionId, roomId);
            await Clients.Group(roomId).SendAsync("GameStarted", "Игра начинается! Ваш ход: " +
                (room.CurrentTurnPlayerId == room.Players[0] ? "X" : "O"));
            await SendBoardState(roomId);
            return "joined";
        }
    }

    public async Task MakeMove(string roomId, int position, char symbol)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return;

        var connectionId = Context.ConnectionId;

        if (room.GameOver || room.CurrentTurnPlayerId != connectionId ||
            position < 0 || position > 8 || room.Board[position] != ' ')
            return;

        char playerSymbol = room.Players[0] == connectionId ? 'X' : 'O';
        if (playerSymbol != symbol) return; 

        room.Board[position] = symbol;

        if (CheckWin(room.Board, symbol))
        {
            room.GameOver = true;
            room.Winner = connectionId;
            await Clients.Group(roomId).SendAsync("GameOver", $"Победил игрок {playerSymbol}!");
            await Clients.Group(roomId).SendAsync("BoardUpdate", room.Board);
            return;
        }

        if (room.Board.All(c => c != ' '))
        {
            room.GameOver = true;
            await Clients.Group(roomId).SendAsync("GameOver", "Ничья!");
            await Clients.Group(roomId).SendAsync("BoardUpdate", room.Board);
            return;
        }
        room.CurrentTurnPlayerId = room.Players.First(p => p != connectionId);
        await SendBoardState(roomId);
        await Clients.Group(roomId).SendAsync("TurnChanged",
            room.CurrentTurnPlayerId == room.Players[0] ? "X" : "O");
    }

    public async Task SendChatMessage(string roomId, string playerName, string message)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return;

        var chatMsg = new ChatMessage
        {
            PlayerName = playerName,
            Message = message
        };
        room.ChatHistory.Add(chatMsg);
        await Clients.Group(roomId).SendAsync("ReceiveChatMessage", playerName, message);
    }

    public async Task ResetGame(string roomId)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return;

        room.Board = Enumerable.Repeat(' ', 9).ToArray();
        room.GameOver = false;
        room.Winner = null;
        room.CurrentTurnPlayerId = room.Players.FirstOrDefault();

        await Clients.Group(roomId).SendAsync("GameReset");
        await SendBoardState(roomId);
        await Clients.Group(roomId).SendAsync("TurnChanged",
            room.CurrentTurnPlayerId == room.Players[0] ? "X" : "O");
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        var roomEntry = _rooms.FirstOrDefault(r => r.Value.Players.Contains(connectionId));

        if (roomEntry.Value != null)
        {
            var room = roomEntry.Value;
            room.Players.Remove(connectionId);

            if (room.Players.Count == 1)
            {
                await Clients.Group(room.RoomId).SendAsync("GameOver",
                    "Соперник отключился. Вы победили!");
                room.GameOver = true;
                room.Winner = room.Players[0];
            }
            else
            {
                _rooms.TryRemove(room.RoomId, out _);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task SendBoardState(string roomId)
    {
        if (_rooms.TryGetValue(roomId, out var room))
        {
            await Clients.Group(roomId).SendAsync("BoardUpdate", room.Board);
        }
    }

    private bool CheckWin(char[] board, char symbol)
    {
        int[][] winPatterns = new int[][]
        {
            new[] {0,1,2}, new[] {3,4,5}, new[] {6,7,8},
            new[] {0,3,6}, new[] {1,4,7}, new[] {2,5,8}, 
            new[] {0,4,8}, new[] {2,4,6} 
        };

        return winPatterns.Any(pattern => pattern.All(idx => board[idx] == symbol));
    }
}