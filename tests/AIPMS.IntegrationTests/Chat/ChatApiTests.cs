using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Chat;
using AIPMS.IntegrationTests.FinalSubmissions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace AIPMS.IntegrationTests.Chat;

public sealed class ChatApiTests(FinalSubmissionDraftDatabaseFixture fixture) : IClassFixture<FinalSubmissionDraftDatabaseFixture>
{
    [Fact]
    public async Task REST_contract_uses_string_ids_and_enforces_auth_and_hidden_resources()
    {
        var s=await fixture.Seed();await using var app=new Factory(fixture.ConnectionString);
        using var anonymous=app.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/chat/conversations")).StatusCode);
        using var student=app.CreateAuthenticatedClient(s.Users.Student);
        var opened=await student.PostAsync($"/api/v1/chat/projects/{s.ProjectId}/conversation",null);opened.EnsureSuccessStatusCode();
        var c=(await opened.Content.ReadFromJsonAsync<ChatConversationDto>())!;
        var sent=await student.PostAsJsonAsync($"/api/v1/chat/conversations/{c.Id}/messages",new ChatSendRequest(Guid.NewGuid(),"API message"));sent.EnsureSuccessStatusCode();
        var message=(await sent.Content.ReadFromJsonAsync<ChatMessageDto>())!;Assert.Equal("1",message.Sequence);
        using var outsider=app.CreateAuthenticatedClient(s.Users.Admin);
        Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync($"/api/v1/chat/conversations/{c.Id}/messages")).StatusCode);
        var swagger=await student.GetStringAsync("/swagger/v1/swagger.json");Assert.Contains("/api/v1/chat/conversations/{id}/messages",swagger);
    }
    [Fact]
    public async Task Disabled_feature_fails_closed()
    {
        await using var app=new Factory(fixture.ConnectionString,false);using var client=app.CreateAuthenticatedClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/api/v1/chat/conversations")).StatusCode);
    }
    private sealed class Factory(string connection,bool enabled=true):AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:DefaultConnection"]=connection,["Chat:Enabled"]=enabled.ToString(),["Chat:RealtimeEnabled"]="false"}));
        }
    }
}
