using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.Projects.DTOs;

public static class ProjectProgressAnalysisDtoMapper
{
    public static ProjectProgressAnalysisDto ToDto(
        long projectId,
        DateTime generatedAtUtc,
        DateTime analysisTimeUtc,
        string dataStatus,
        string riskLevel,
        double? riskScore,
        double? confidence,
        string trendStatus,
        ProgressSummaryDto progressSummary,
        FeatureSnapshotDto featureSnapshot,
        IReadOnlyList<RiskFactorDto> factors,
        IReadOnlyList<string> recommendations,
        string ruleVersion,
        string featureVersion,
        string modelVersion,
        string? limitations) =>
        new(
            projectId,
            generatedAtUtc,
            analysisTimeUtc,
            dataStatus,
            riskLevel,
            riskScore,
            confidence,
            trendStatus,
            progressSummary,
            featureSnapshot,
            factors,
            recommendations,
            ruleVersion,
            featureVersion,
            modelVersion,
            limitations);
}
