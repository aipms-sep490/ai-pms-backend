using AIPMS.Application.Features.Deliverables.Services;

namespace AIPMS.Application.Features.Files;

public sealed record UploadPolicyDto(
    string ParentType,
    long MaxBytes,
    string MaxBytesLabel,
    IReadOnlyList<string> AllowedMimeTypes,
    IReadOnlyList<string> AllowedExtensions,
    int MaxFilesPerRequest,
    bool RequiresVersionAnchor
);

public static class UploadPolicies
{
    // The published per-file size tiers. Assistant endpoints use MultipartTransportCeiling
    // for the HTTP envelope while AssistantAttachmentReader enforces 10 MiB/file and 20 MiB/request.
    public const long Tier6MiB = 6 * 1024 * 1024;
    public const long Tier10MiB = 10 * 1024 * 1024;
    public const long Tier11MiB = 11 * 1024 * 1024;

    /// <summary>
    /// Trần vận chuyển HTTP multipart (22 * 1024 * 1024 bytes) trên controller [RequestSizeLimit].
    /// Khoảng chênh so với trần file 20 MiB là khoảng dư cho multipart boundary và headers.
    /// </summary>
    public const long MultipartTransportCeiling = 22 * 1024 * 1024;

    /// <summary>
    /// Trần file hiệu lực do UploadValidator áp đặt. RequestSizeLimit lớn hơn trần này
    /// chỉ là khoảng dư cho header multipart, KHÔNG phải giới hạn file.
    /// Giới hạn công bố phải là min(RequestSizeLimit, trần này).
    /// </summary>
    public const long EffectiveFileLimit = UploadValidator.MaxBytes; // 20 * 1024 * 1024

    // The 11 endpoint constants (reading from the tiers)
    public const long StudentAccountImportLimit = Tier6MiB;
    public const long StudentCurriculumLimit = Tier6MiB;
    public const long AcademicAssistantAskLimit = MultipartTransportCeiling;
    public const long AcademicAssistantStreamLimit = MultipartTransportCeiling;
    public const int AcademicAssistantMaxFiles = 4;
    public const long AcademicAssistantFileLimit = Tier10MiB;
    public const long AcademicAssistantTotalLimit = 20 * 1024 * 1024;
    public const long ChatAttachmentLimit = Tier11MiB;
    public const long ColdSubmissionLimit = Tier11MiB;
    public const long ResultAppealLimit = Tier11MiB;
    public const long DeliverableVersionLimit = EffectiveFileLimit;
    public const long ProjectFileLimit = EffectiveFileLimit;
    public const long ReviewGateEvidenceLimit = EffectiveFileLimit;
    public const long StudentQualificationLimit = EffectiveFileLimit;

    public static readonly IReadOnlyList<UploadPolicyDto> All = new[]
    {
        new UploadPolicyDto(
            ParentType: "REVIEW_GATE_EVIDENCE",
            MaxBytes: ReviewGateEvidenceLimit,
            MaxBytesLabel: "20 MiB",
            AllowedMimeTypes: ["application/pdf", "image/png", "image/jpeg", "application/zip"],
            AllowedExtensions: [".pdf", ".png", ".jpg", ".jpeg", ".zip"],
            MaxFilesPerRequest: 1,
            RequiresVersionAnchor: true
        ),
        new UploadPolicyDto(
            ParentType: "DELIVERABLE_VERSION",
            MaxBytes: DeliverableVersionLimit,
            MaxBytesLabel: "20 MiB",
            AllowedMimeTypes: ["application/pdf", "application/zip", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/x-rar-compressed"],
            AllowedExtensions: [".pdf", ".zip", ".docx", ".rar"],
            MaxFilesPerRequest: 1,
            RequiresVersionAnchor: true
        ),
        new UploadPolicyDto(
            ParentType: "PROJECT_FILE",
            MaxBytes: ProjectFileLimit,
            MaxBytesLabel: "20 MiB",
            AllowedMimeTypes: ["application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/zip", "image/png", "image/jpeg"],
            AllowedExtensions: [".pdf", ".docx", ".zip", ".png", ".jpg", ".jpeg"],
            MaxFilesPerRequest: 1,
            RequiresVersionAnchor: false
        ),
        new UploadPolicyDto(
            ParentType: "STUDENT_QUALIFICATION",
            MaxBytes: StudentQualificationLimit,
            MaxBytesLabel: "20 MiB",
            AllowedMimeTypes: ["application/pdf", "image/png", "image/jpeg"],
            AllowedExtensions: [".pdf", ".png", ".jpg", ".jpeg"],
            MaxFilesPerRequest: 1,
            RequiresVersionAnchor: false
        ),
        new UploadPolicyDto(
            ParentType: "COLD_SUBMISSION",
            MaxBytes: ColdSubmissionLimit,
            MaxBytesLabel: "11 MiB",
            AllowedMimeTypes: ["application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/zip", "image/png", "image/jpeg"],
            AllowedExtensions: [".pdf", ".docx", ".zip", ".png", ".jpg", ".jpeg"],
            MaxFilesPerRequest: 1,
            RequiresVersionAnchor: true
        ),
        new UploadPolicyDto(
            ParentType: "RESULT_APPEAL",
            MaxBytes: ResultAppealLimit,
            MaxBytesLabel: "11 MiB",
            AllowedMimeTypes: ["application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/zip", "image/png", "image/jpeg"],
            AllowedExtensions: [".pdf", ".docx", ".zip", ".png", ".jpg", ".jpeg"],
            MaxFilesPerRequest: 1,
            RequiresVersionAnchor: true
        ),
        new UploadPolicyDto(
            ParentType: "CHAT_ATTACHMENT",
            MaxBytes: ChatAttachmentLimit,
            MaxBytesLabel: "11 MiB",
            AllowedMimeTypes: ["application/pdf", "image/png", "image/jpeg", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/zip"],
            AllowedExtensions: [".pdf", ".png", ".jpg", ".jpeg", ".docx", ".zip"],
            MaxFilesPerRequest: 1,
            RequiresVersionAnchor: false
        ),
        new UploadPolicyDto(
            ParentType: "ACADEMIC_ASSISTANT_ASK",
            MaxBytes: AcademicAssistantFileLimit,
            MaxBytesLabel: "10 MiB/tệp; 20 MiB/tổng",
            AllowedMimeTypes: ["application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "text/plain", "text/markdown"],
            AllowedExtensions: [".pdf", ".docx", ".txt", ".md"],
            MaxFilesPerRequest: AcademicAssistantMaxFiles,
            RequiresVersionAnchor: false
        ),
        new UploadPolicyDto(
            ParentType: "ACADEMIC_ASSISTANT_STREAM",
            MaxBytes: AcademicAssistantFileLimit,
            MaxBytesLabel: "10 MiB/tệp; 20 MiB/tổng",
            AllowedMimeTypes: ["application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "text/plain", "text/markdown"],
            AllowedExtensions: [".pdf", ".docx", ".txt", ".md"],
            MaxFilesPerRequest: AcademicAssistantMaxFiles,
            RequiresVersionAnchor: false
        ),
        new UploadPolicyDto(
            ParentType: "STUDENT_ACCOUNT_IMPORT",
            MaxBytes: StudentAccountImportLimit,
            MaxBytesLabel: "6 MiB",
            AllowedMimeTypes: ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/vnd.ms-excel", "text/csv"],
            AllowedExtensions: [".xlsx", ".xls", ".csv"],
            MaxFilesPerRequest: 1,
            RequiresVersionAnchor: false
        ),
        new UploadPolicyDto(
            ParentType: "STUDENT_CURRICULUM",
            MaxBytes: StudentCurriculumLimit,
            MaxBytesLabel: "6 MiB",
            AllowedMimeTypes: ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/vnd.ms-excel", "text/csv"],
            AllowedExtensions: [".xlsx", ".xls", ".csv"],
            MaxFilesPerRequest: 1,
            RequiresVersionAnchor: false
        )
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["REVIEW_GATE"] = "REVIEW_GATE_EVIDENCE",
        ["GATE_EVIDENCE"] = "REVIEW_GATE_EVIDENCE",
        ["VERSION"] = "DELIVERABLE_VERSION",
        ["DELIVERABLE"] = "DELIVERABLE_VERSION",
        ["REPORT"] = "PROJECT_FILE",
        ["MEETING"] = "PROJECT_FILE",
        ["TASK"] = "PROJECT_FILE",
        ["FILE"] = "PROJECT_FILE",
        ["PROJECT_FILES"] = "PROJECT_FILE",
        ["QUALIFICATION"] = "STUDENT_QUALIFICATION",
        ["QUALIFICATION_CERTIFICATE"] = "STUDENT_QUALIFICATION",
        ["COLD"] = "COLD_SUBMISSION",
        ["COLD_FILE"] = "COLD_SUBMISSION",
        ["CORRECTION_REQUEST"] = "RESULT_APPEAL",
        ["APPEAL"] = "RESULT_APPEAL",
        ["CHAT"] = "CHAT_ATTACHMENT",
        ["CURRICULUM"] = "STUDENT_CURRICULUM",
        ["CURRICULUM_IMPORT"] = "STUDENT_CURRICULUM",
        ["ACCOUNT_IMPORT"] = "STUDENT_ACCOUNT_IMPORT",
        ["STUDENT_IMPORT"] = "STUDENT_ACCOUNT_IMPORT",
        ["ASSISTANT_ASK"] = "ACADEMIC_ASSISTANT_ASK",
        ["ASSISTANT_STREAM"] = "ACADEMIC_ASSISTANT_STREAM"
    };

    public static UploadPolicyDto? Find(string parentType)
    {
        if (string.IsNullOrWhiteSpace(parentType)) return null;
        var direct = All.FirstOrDefault(p => string.Equals(p.ParentType, parentType, StringComparison.OrdinalIgnoreCase));
        if (direct is not null) return direct;
        if (Aliases.TryGetValue(parentType.Trim(), out var canonical))
            return All.FirstOrDefault(p => string.Equals(p.ParentType, canonical, StringComparison.OrdinalIgnoreCase));
        return null;
    }
}
