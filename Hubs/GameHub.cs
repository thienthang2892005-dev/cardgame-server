using Microsoft.AspNetCore.SignalR;
using CardGame.Server.Services;

namespace CardGame.Server.Hubs
{
    public class GameHub : Hub
    {
        private readonly RoomManager _roomManager;

        public GameHub(RoomManager roomManager)
        {
            _roomManager = roomManager;
        }

        // Người chơi kết nối
        public override async Task OnConnectedAsync()
        {
            Console.WriteLine(
                $"Người chơi kết nối: {Context.ConnectionId}"
            );

            await base.OnConnectedAsync();
        }

        // Gửi danh sách người chơi đến cả phòng
        private async Task SendRoomUpdated(
            CardGame.Server.Models.GameRoom room)
        {
            await Clients.Group(room.Code).SendAsync(
                "RoomUpdated",
                room.Code,
                room.HostConnectionId,
                room.GuestConnectionId
            );
        }

        // Tạo phòng mới
        public async Task<string> CreateRoom()
        {
            var room = _roomManager.CreateRoom(
                Context.ConnectionId
            );

            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                room.Code
            );

            Console.WriteLine(
                $"Đã tạo phòng: {room.Code}"
            );

            // Cập nhật danh sách người chơi
            await SendRoomUpdated(room);

            return room.Code;
        }

        // Tham gia phòng
        public async Task<string> JoinRoom(string code)
        {
            bool success = _roomManager.TryJoinRoom(
                code,
                Context.ConnectionId,
                out var room,
                out string error
            );

            if (!success || room == null)
            {
                throw new HubException(error);
            }

            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                room.Code
            );

            Console.WriteLine(
                $"Người chơi đã tham gia phòng: {room.Code}"
            );

            // Cập nhật danh sách cho cả hai người chơi
            await SendRoomUpdated(room);

            return room.Code;
        }


        public async Task StartGame()
        {
            bool success = _roomManager.TryStartGame(
                Context.ConnectionId,
                out var room,
                out string error
            );

            if (!success || room == null)
            {
                throw new HubException(error);
            }

            Console.WriteLine(
                $"Phòng {room.Code} bắt đầu chọn Búa - Kéo - Bao!"
            );

            // Thông báo cho cả hai người mở màn oẳn tù tì.
            await Clients.Group(room.Code).SendAsync(
                "GestureSelectionStarted",
                room.Code
            );
        }


        public async Task SendMonsterSummoned(
            string roomCode,
            string cardId,
            int row,
            int col)
        {
            bool success = _roomManager.TrySummonMonster(
                roomCode,
                Context.ConnectionId,
                cardId,
                row,
                col,
                out var room,
                out string error
            );

            if (!success || room == null)
            {
                throw new HubException(error);
            }

            // Thông báo cho đối thủ biết quái vật vừa được triệu hồi.
            await Clients.OthersInGroup(room.Code).SendAsync(
                "MonsterSummoned",
                cardId,
                row,
                col
            );
        }

        public async Task EndTurn(string roomCode)
        {
            bool success = _roomManager.TryEndTurn(
                roomCode,
                Context.ConnectionId,
                out var room,
                out string nextPlayerConnectionId,
                out string error
            );

            if (!success || room == null)
            {
                throw new HubException(error);
            }

            // Thông báo cho cả hai người chơi biết ai được đi tiếp.
            await Clients.Group(room.Code).SendAsync(
                "TurnChanged",
                nextPlayerConnectionId
            );
        }

        public async Task SubmitBattleSnapshot(
            string roomCode,
            string snapshotJson)
        {
            if (string.IsNullOrWhiteSpace(snapshotJson))
            {
                throw new HubException("Snapshot không hợp lệ!");
            }

            // Giới hạn dữ liệu để tránh gửi payload quá lớn.
            if (snapshotJson.Length > 1_000_000)
            {
                throw new HubException("Snapshot quá lớn!");
            }

            bool success = _roomManager.TrySubmitBattleSnapshot(
                roomCode,
                Context.ConnectionId,
                out var room,
                out string nextPlayerConnectionId,
                out string error
            );

            if (!success || room == null)
            {
                throw new HubException(error);
            }

            // Gửi kết quả bàn cờ cho đối thủ trước.
            await Clients.OthersInGroup(room.Code).SendAsync(
                "BattleSnapshotReceived",
                snapshotJson
            );

            // Sau đó mới thông báo chuyển lượt cho cả hai người.
            await Clients.Group(room.Code).SendAsync(
                "TurnChanged",
                nextPlayerConnectionId
            );
        }
        // Người chơi chủ động rời trận.
        public async Task LeaveRoom(string roomCode)
        {
            if (string.IsNullOrWhiteSpace(roomCode))
                return;

            string? opponentId = _roomManager.LeaveRoom(
                roomCode,
                Context.ConnectionId
            );

            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                roomCode.Trim().ToUpperInvariant()
            );

            if (opponentId != null)
            {
                await Clients.Client(opponentId).SendAsync("OpponentLeft");
            }
        }
        // Người chơi ngắt kết nối
        public override async Task OnDisconnectedAsync(
    Exception? exception)
        {
            Console.WriteLine(
                $"Người chơi ngắt kết nối: {Context.ConnectionId}"
            );

            string? opponentId = _roomManager.RemoveConnection(
                Context.ConnectionId
            );

            // Đối thủ còn lại sẽ thắng.
            if (opponentId != null)
            {
                await Clients.Client(opponentId).SendAsync("OpponentLeft");
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task SubmitGesture(
            string roomCode,
            string gesture)
        {
            bool success = _roomManager.TrySubmitGesture(
                roomCode,
                Context.ConnectionId,
                gesture,
                out var room,
                out string result,
                out string hostGesture,
                out string guestGesture,
                out string firstPlayerConnectionId,
                out string error
            );

            if (!success || room == null)
            {
                throw new HubException(error);
            }

            // Người đầu tiên chọn phải chờ người còn lại.
            if (result == "Waiting")
            {
                await Clients.Caller.SendAsync(
                    "GestureWaiting"
                );

                return;
            }

            Console.WriteLine(
                $"Oẳn tù tì phòng {room.Code}: " +
                $"Host = {hostGesture}, Guest = {guestGesture}, " +
                $"Kết quả = {result}"
            );

            // Chỉ tiết lộ lựa chọn sau khi cả hai đã chọn.
            await Clients.Group(room.Code).SendAsync(
                "GestureResult",
                result,
                hostGesture,
                guestGesture,
                firstPlayerConnectionId
            );

            // Nếu hòa thì cho hai người chọn lại.
            if (result == "Draw")
            {
                return;
            }

            // Nếu có người thắng, bắt đầu trận đấu.
            await Clients.Group(room.Code).SendAsync(
                "GameStarted",
                room.Code
            );

            Console.WriteLine(
                $"Phòng {room.Code} bắt đầu trận đấu! " +
                $"Người đi trước: {firstPlayerConnectionId}"
            );
        }

    }
}
