namespace AIPMS.Application.Features.StudentRosters;

public sealed record RosterExportQuery(long SemesterId, long? DepartmentId = null, long? MajorId = null, long? TeamId = null);
public sealed record RosterFile(byte[] Content, string FileName, string ContentType);

