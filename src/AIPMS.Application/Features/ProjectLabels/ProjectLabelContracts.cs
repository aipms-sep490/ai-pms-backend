using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.ProjectLabels;

public static class TaskTaxonomyCatalog
{
    public static readonly IReadOnlySet<string> ItCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Requirement/SRS", "Architecture/DB", "UI/UX Design", "Frontend", "Backend",
        "Integration/External API", "Testing/QA", "Fix Bug/Refactor", "DevOps/Deploy"
    };

    public static readonly IReadOnlySet<string> MktCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Market Research", "Strategy & STP", "Content & Copy", "Creative/Visual",
        "Campaign/Channel", "Budget & Media", "Analytics & Report", "Revision/Brand Fix"
    };

    public static readonly IReadOnlySet<string> IntegrationCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Product-Market Align", "Landing & Conversion", "Event & Tracking Spec",
        "Pilot & A/B Experiment", "Joint Demo & Pitch"
    };

    public static readonly IReadOnlySet<string> ItSubStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CODING", "PR_OPENED", "QA_TESTING", "BUG_RETURNED", "MERGED_DEPLOYED"
    };

    public static readonly IReadOnlySet<string> MktSubStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "DRAFTING", "INTERNAL_REVIEW", "MENTOR_BRAND_REVIEW", "REVISION_NEEDED", "SCHEDULED_LIVE", "MEASURED"
    };

    public static readonly IReadOnlySet<string> HandoffFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NONE", "WAITING_FOR_IT", "WAITING_FOR_MKT"
    };

    public static string? ResolveCategoryDiscipline(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return null;
        var norm = category.Trim();
        if (ItCategories.Contains(norm)) return "IT";
        if (MktCategories.Contains(norm)) return "MKT";
        if (IntegrationCategories.Contains(norm)) return "INTEGRATION";
        return "UNKNOWN";
    }
}

public sealed record ProjectLabelDto(
    long Id,
    long ProjectId,
    string Slug,
    string Name,
    string ColorHex,
    string? Description,
    bool IsSystem);

public sealed record SaveProjectLabelRequest(
    string Slug,
    string Name,
    string ColorHex,
    string? Description = null);

public sealed record UpdateTaskTaxonomyRequest(
    string? TaskCategory,
    string? DisciplineSubStatus,
    string? HandoffFlag,
    IReadOnlyList<string>? Labels,
    long? PrimaryMajorId = null,
    string? ConcurrencyToken = null);

public sealed record TaskTaxonomyStateDto(
    long TaskId,
    string TaskCategory,
    string? DisciplineSubStatus,
    string HandoffFlag,
    DateTime? HandoffFlaggedAt,
    IReadOnlyList<string> Labels,
    string ConcurrencyToken);
