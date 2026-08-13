using AIPMS.Api.Extensions;
using AIPMS.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiServices(builder.Configuration);
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is not configured."));

var app = builder.Build();

app.UseApiPipeline();
app.Run();

public partial class Program;
