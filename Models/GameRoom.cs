namespace CardGame.Server.Models
{
    public class GameRoom
    {
        // Mã phòng gồm 6 ký tự
        public string Code { get; }

        // Người tạo phòng
        public string HostConnectionId { get; }

        // Người tham gia phòng
        public string? GuestConnectionId { get; set; }
        public bool IsStarted { get; set; } = false;

        // Trạng thái oẳn tù tì
        public bool IsChoosingGesture { get; set; } = false;

        // Lựa chọn của chủ phòng
        public string? HostGesture { get; set; }

        // Lựa chọn của người tham gia
        public string? GuestGesture { get; set; }

        // Đã xác định người đi trước hay chưa
        public bool IsFirstTurnDecided { get; set; } = false;

        // ConnectionId của người đang được quyền thực hiện lượt.
        public string? CurrentTurnConnectionId { get; set; }
        // Lưu bàn cờ riêng của từng người chơi.
        // Key ngoài: ConnectionId.
        // Key trong: vị trí ô cờ.
        // Value: CardId của quái vật.
        public Dictionary<string, Dictionary<string, string>> BoardsByPlayer
        { get; } = new Dictionary<string, Dictionary<string, string>>();
        // Khóa đồng bộ khi nhiều người cùng thao tác
        public object SyncRoot { get; } = new object();

        public GameRoom(string code, string hostConnectionId)
        {
            Code = code;
            HostConnectionId = hostConnectionId;
        }
    }
}
