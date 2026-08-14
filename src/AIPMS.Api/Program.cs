using AIPMS.AI;
using AIPMS.Api;
using AIPMS.Api.Extensions;
using AIPMS.Application;
using AIPMS.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddAI();
builder.Services.AddApi();

var app = builder.Build();

app.Logger.LogInformation("Starting AI-PMS API");
app.UseApiPipeline();
app.Run();

public partial class Program;
