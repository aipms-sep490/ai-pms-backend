namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class AssistantConversation
{
    public long Id { get; set; }
    public long ActorId { get; set; }
    public long? ProjectId { get; set; }
    public long? DepartmentId { get; set; }
    public string Scope { get; set; } = "ACADEMIC";
    public string Title { get; set; } = "Cuộc trò chuyện mới";
    public int Revision { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class AssistantConversationTurn
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public int Revision { get; set; }
    public string Query { get; set; } = "";
    public long RunId { get; set; }
}
