using System.Collections.Concurrent;
using System.Security.Cryptography;
using CardGame.Server.Models;

namespace CardGame.Server.Services
{
    public class RoomManager
    {
        // Danh sách các phòng hiện có
        private readonly ConcurrentDictionary<string, GameRoom> _rooms
            = new ConcurrentDictionary<string, GameRoom>();

        // Những ký tự dễ nhầm lẫn được loại bỏ
        private const string Characters =
            "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        // Tạo phòng mới
        public GameRoom CreateRoom(string connectionId)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                string code = GenerateRoomCode();

                GameRoom room = new GameRoom(code, connectionId);

                if (_rooms.TryAdd(code, room))
                {
                    return room;
                }
            }

            throw new InvalidOperationException(
                "Không thể tạo mã phòng. Vui lòng thử lại!"
            );
        }

        // Sinh mã phòng ngẫu nhiên gồm 6 ký tự
        private string GenerateRoomCode()
        {
            char[] code = new char[6];

            for (int i = 0; i < code.Length; i++)
            {
                int index = RandomNumberGenerator.GetInt32(
                    Characters.Length
                );

                code[i] = Characters[index];
            }

            return new string(code);
        }

        // Người chơi tham gia phòng
        public bool TryJoinRoom(
            string code,
            string connectionId,
            out GameRoom? room,
            out string error)
        {
            room = null;
            error = "";

            if (string.IsNullOrWhiteSpace(code))
            {
                error = "Vui lòng nhập mã phòng!";
                return false;
            }

            code = code.Trim().ToUpperInvariant();

            if (!_rooms.TryGetValue(code, out GameRoom? foundRoom))
            {
                error = "Phòng không tồn tại!";
                return false;
            }

            lock (foundRoom.SyncRoot)
            {
                if (foundRoom.HostConnectionId == connectionId)
                {
                    error = "Bạn đang là chủ phòng!";
                    return false;
                }

                if (foundRoom.GuestConnectionId != null)
                {
                    error = "Phòng đã đủ người chơi!";
                    return false;
                }

                foundRoom.GuestConnectionId = connectionId;
                room = foundRoom;

                return true;
            }
        }

        // Xóa hoặc cập nhật phòng khi người chơi ngắt kết nối
        // Xóa hoặc cập nhật phòng khi người chơi ngắt kết nối.
        // Trả về ConnectionId của đối thủ nếu đang trong trận.
        public string? RemoveConnection(string connectionId)
        {
            string? opponentToNotify = null;

            foreach (var pair in _rooms)
            {
                GameRoom room = pair.Value;

                lock (room.SyncRoot)
                {
                    if (room.HostConnectionId == connectionId)
                    {
                        if (room.IsStarted)
                            opponentToNotify = room.GuestConnectionId;

                        _rooms.TryRemove(pair.Key, out _);
                    }
                    else if (room.GuestConnectionId == connectionId)
                    {
                        if (room.IsStarted)
                        {
                            // Đang đấu mà Guest thoát: báo Host và đóng phòng.
                            opponentToNotify = room.HostConnectionId;
                            _rooms.TryRemove(pair.Key, out _);
                        }
                        else
                        {
                            room.GuestConnectionId = null;
                        }
                    }
                }
            }

            return opponentToNotify;
        }

        // Người chơi chủ động rời trận. Trả về ConnectionId của đối thủ.
        public string? LeaveRoom(string roomCode, string connectionId)
        {
            if (string.IsNullOrWhiteSpace(roomCode))
                return null;

            string code = roomCode.Trim().ToUpperInvariant();

            if (!_rooms.TryGetValue(code, out GameRoom? room))
                return null;

            lock (room.SyncRoot)
            {
                bool isHost = room.HostConnectionId == connectionId;
                bool isGuest = room.GuestConnectionId == connectionId;

                if (!isHost && !isGuest)
                    return null;

                // Có người rời thì phòng này kết thúc.
                _rooms.TryRemove(code, out _);

                return isHost
                    ? room.GuestConnectionId
                    : room.HostConnectionId;
            }
        }
        public bool TryStartGame(
            string connectionId,
            out GameRoom? room,
            out string error)
        {
            room = null;
            error = "";

            foreach (var pair in _rooms)
            {
                GameRoom foundRoom = pair.Value;

                lock (foundRoom.SyncRoot)
                {
                    if (foundRoom.HostConnectionId != connectionId)
                        continue;

                    if (foundRoom.GuestConnectionId == null)
                    {
                        error = "Phòng chưa đủ hai người chơi!";
                        return false;
                    }

                    if (foundRoom.IsStarted ||
                        foundRoom.IsChoosingGesture)
                    {
                        error = "Trận đấu hoặc lượt chọn đã bắt đầu!";
                        return false;
                    }

                    // Mở lượt oẳn tù tì.
                    foundRoom.IsChoosingGesture = true;

                    foundRoom.HostGesture = null;
                    foundRoom.GuestGesture = null;

                    foundRoom.IsFirstTurnDecided = false;
                    foundRoom.CurrentTurnConnectionId = null;

                    room = foundRoom;
                    return true;
                }
            }

            error = "Bạn không phải chủ phòng hoặc không còn trong phòng!";
            return false;
        }

        public bool TryEndTurn(
    string roomCode,
    string connectionId,
    out GameRoom? room,
    out string nextPlayerConnectionId,
    out string error)
        {
            room = null;
            nextPlayerConnectionId = "";
            error = "";

            if (!_rooms.TryGetValue(
                roomCode.Trim().ToUpperInvariant(),
                out GameRoom? foundRoom))
            {
                error = "Phòng không tồn tại!";
                return false;
            }

            lock (foundRoom.SyncRoot)
            {
                if (!foundRoom.IsStarted)
                {
                    error = "Trận đấu chưa bắt đầu!";
                    return false;
                }

                // Chỉ hai thành viên trong phòng mới được chuyển lượt.
                bool isHost =
                    foundRoom.HostConnectionId == connectionId;

                bool isGuest =
                    foundRoom.GuestConnectionId == connectionId;

                if (!isHost && !isGuest)
                {
                    error = "Bạn không thuộc phòng này!";
                    return false;
                }

                // Kiểm tra có đúng lượt của người gửi không.
                if (foundRoom.CurrentTurnConnectionId != connectionId)
                {
                    error = "Hiện tại chưa đến lượt của bạn!";
                    return false;
                }

                // Chuyển lượt cho người còn lại.
                nextPlayerConnectionId = isHost
                    ? foundRoom.GuestConnectionId!
                    : foundRoom.HostConnectionId;

                foundRoom.CurrentTurnConnectionId =
                    nextPlayerConnectionId;

                room = foundRoom;

                return true;
            }
        }

        public bool TrySubmitBattleSnapshot(
            string roomCode,
            string connectionId,
            out GameRoom? room,
            out string nextPlayerConnectionId,
            out string error)
        {
            room = null;
            nextPlayerConnectionId = "";
            error = "";

            if (!_rooms.TryGetValue(
                roomCode.Trim().ToUpperInvariant(),
                out GameRoom? foundRoom))
            {
                error = "Phòng không tồn tại!";
                return false;
            }

            lock (foundRoom.SyncRoot)
            {
                if (!foundRoom.IsStarted)
                {
                    error = "Trận đấu chưa bắt đầu!";
                    return false;
                }

                bool isHost =
                    foundRoom.HostConnectionId == connectionId;

                bool isGuest =
                    foundRoom.GuestConnectionId == connectionId;

                if (!isHost && !isGuest)
                {
                    error = "Bạn không thuộc phòng này!";
                    return false;
                }

                if (foundRoom.CurrentTurnConnectionId != connectionId)
                {
                    error = "Hiện tại chưa đến lượt của bạn!";
                    return false;
                }

                // Chỉ chuyển lượt sau khi snapshot được gửi hợp lệ.
                nextPlayerConnectionId = isHost
                    ? foundRoom.GuestConnectionId!
                    : foundRoom.HostConnectionId;

                foundRoom.CurrentTurnConnectionId =
                    nextPlayerConnectionId;

                room = foundRoom;

                return true;
            }
        }

        public bool TryValidateTurn(
    string roomCode,
    string connectionId,
    out GameRoom? room,
    out string error)
        {
            room = null;
            error = "";

            if (!_rooms.TryGetValue(
                roomCode.Trim().ToUpperInvariant(),
                out GameRoom? foundRoom))
            {
                error = "Phòng không tồn tại!";
                return false;
            }

            lock (foundRoom.SyncRoot)
            {
                if (!foundRoom.IsStarted)
                {
                    error = "Trận đấu chưa bắt đầu!";
                    return false;
                }

                bool isHost = foundRoom.HostConnectionId == connectionId;
                bool isGuest = foundRoom.GuestConnectionId == connectionId;

                if (!isHost && !isGuest)
                {
                    error = "Bạn không thuộc phòng này!";
                    return false;
                }

                if (foundRoom.CurrentTurnConnectionId != connectionId)
                {
                    error = "Hiện tại chưa đến lượt của bạn!";
                    return false;
                }

                room = foundRoom;
                return true;
            }
        }

        public bool TrySummonMonster(
            string roomCode,
            string connectionId,
            string cardId,
            int row,
            int col,
            out GameRoom? room,
            out string error)
        {
            room = null;
            error = "";

            if (string.IsNullOrWhiteSpace(roomCode))
            {
                error = "Mã phòng không hợp lệ!";
                return false;
            }

            if (string.IsNullOrWhiteSpace(cardId))
            {
                error = "Mã quái vật không hợp lệ!";
                return false;
            }

            if (row < 0 || row >= 5 || col < 0 || col >= 3)
            {
                error = "Vị trí triệu hồi không hợp lệ!";
                return false;
            }

            if (!_rooms.TryGetValue(
                roomCode.Trim().ToUpperInvariant(),
                out GameRoom? foundRoom))
            {
                error = "Phòng không tồn tại!";
                return false;
            }

            lock (foundRoom.SyncRoot)
            {
                if (!foundRoom.IsStarted)
                {
                    error = "Trận đấu chưa bắt đầu!";
                    return false;
                }

                bool isHost = foundRoom.HostConnectionId == connectionId;
                bool isGuest = foundRoom.GuestConnectionId == connectionId;

                if (!isHost && !isGuest)
                {
                    error = "Bạn không thuộc phòng này!";
                    return false;
                }

                if (foundRoom.CurrentTurnConnectionId != connectionId)
                {
                    error = "Hiện tại chưa đến lượt của bạn!";
                    return false;
                }
                room = foundRoom;
                return true;
            }
        }

        public bool TrySubmitGesture(
            string roomCode,
            string connectionId,
            string gesture,
            out GameRoom? room,
            out string result,
            out string hostGesture,
            out string guestGesture,
            out string firstPlayerConnectionId,
            out string error)
        {
            room = null;
            result = "";
            hostGesture = "";
            guestGesture = "";
            firstPlayerConnectionId = "";
            error = "";

            if (string.IsNullOrWhiteSpace(roomCode))
            {
                error = "Mã phòng không hợp lệ!";
                return false;
            }

            // Chuẩn hóa lựa chọn.
            gesture = gesture.Trim().ToLowerInvariant();

            if (gesture != "rock" &&
                gesture != "paper" &&
                gesture != "scissors")
            {
                error = "Lựa chọn không hợp lệ!";
                return false;
            }

            if (!_rooms.TryGetValue(
                roomCode.Trim().ToUpperInvariant(),
                out GameRoom? foundRoom))
            {
                error = "Phòng không tồn tại!";
                return false;
            }

            lock (foundRoom.SyncRoot)
            {
                if (!foundRoom.IsChoosingGesture)
                {
                    error = "Phòng hiện không trong lượt oẳn tù tì!";
                    return false;
                }

                bool isHost =
                    foundRoom.HostConnectionId == connectionId;

                bool isGuest =
                    foundRoom.GuestConnectionId == connectionId;

                if (!isHost && !isGuest)
                {
                    error = "Bạn không thuộc phòng này!";
                    return false;
                }

                // Không cho một người chọn nhiều lần trong cùng lượt.
                if (isHost && foundRoom.HostGesture != null)
                {
                    error = "Bạn đã chọn rồi, hãy chờ đối thủ!";
                    return false;
                }

                if (isGuest && foundRoom.GuestGesture != null)
                {
                    error = "Bạn đã chọn rồi, hãy chờ đối thủ!";
                    return false;
                }

                // Lưu lựa chọn.
                if (isHost)
                    foundRoom.HostGesture = gesture;
                else
                    foundRoom.GuestGesture = gesture;

                room = foundRoom;

                // Chưa đủ hai lựa chọn.
                if (foundRoom.HostGesture == null ||
                    foundRoom.GuestGesture == null)
                {
                    result = "Waiting";
                    return true;
                }

                hostGesture = foundRoom.HostGesture;
                guestGesture = foundRoom.GuestGesture;

                // Kiểm tra hòa.
                if (hostGesture == guestGesture)
                {
                    result = "Draw";

                    // Xóa lựa chọn để bắt đầu lượt mới.
                    foundRoom.HostGesture = null;
                    foundRoom.GuestGesture = null;

                    return true;
                }

                // Xác định người thắng.
                bool hostWins =
                    (hostGesture == "rock" && guestGesture == "scissors") ||
                    (hostGesture == "paper" && guestGesture == "rock") ||
                    (hostGesture == "scissors" && guestGesture == "paper");

                firstPlayerConnectionId = hostWins
                    ? foundRoom.HostConnectionId
                    : foundRoom.GuestConnectionId!;

                // Bắt đầu trận đấu.
                foundRoom.IsStarted = true;
                foundRoom.IsChoosingGesture = false;
                foundRoom.IsFirstTurnDecided = true;

                foundRoom.CurrentTurnConnectionId =
                    firstPlayerConnectionId;

                result = hostWins ? "HostWon" : "GuestWon";

                return true;
            }
        }


    }
}
