using ChatHub;
using OMisha.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<GameHub>("/gameHub");

app.Run();