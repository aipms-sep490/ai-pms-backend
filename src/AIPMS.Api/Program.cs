using AIPMS.AI;
using AIPMS.Api;
using AIPMS.Api.Extensions;
using AIPMS.Application;
using AIPMS.Infrastructure;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddAI();
builder.Services.AddApi();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.MaximumReceiveMessageSize = 16 * 1024;
    options.AddFilter<AIPMS.Api.Hubs.ChatHubFilter>();
});

var app = builder.Build();

app.Logger.LogInformation("Starting AI-PMS API");
app.UseApiPipeline();
app.MapHub<AIPMS.Api.Hubs.ChatHub>("/hubs/chat", options=>options.CloseOnAuthenticationExpiration=true);
app.Run();

public partial class Program;
