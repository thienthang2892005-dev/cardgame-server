
using CardGame.Server.Hubs;
using CardGame.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Đọc port từ biến môi trường (khi deploy Cloud như Render) hoặc mặc định 5000
string port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Đăng ký dịch vụ SignalR
builder.Services.AddSignalR();

// Đăng ký quản lý phòng
builder.Services.AddSingleton<RoomManager>();

var app = builder.Build();

// Trang kiểm tra máy chủ
app.MapGet("/", () =>
    "CardGame Server is running!");

// Địa chỉ giao tiếp của game
app.MapHub<GameHub>("/gameHub");

// Khởi động máy chủ
app.Run();
