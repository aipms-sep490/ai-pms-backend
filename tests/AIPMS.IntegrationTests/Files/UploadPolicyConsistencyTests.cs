using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using AIPMS.Api.Controllers;
using AIPMS.Application.Features.Files;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AIPMS.IntegrationTests.Files;

public sealed class UploadPolicyConsistencyTests : IClassFixture<AipmsWebApplicationFactory>
{
    private readonly AipmsWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UploadPolicyConsistencyTests(AipmsWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/v1/upload-policies")]
    [InlineData("/v1/upload-policies")]
    public async Task GetAll_ReturnsExactly11Policies_WithCorrectTieredLimits(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var policies = await response.Content.ReadFromJsonAsync<IReadOnlyList<UploadPolicyDto>>();
        Assert.NotNull(policies);
        Assert.Equal(11, policies.Count);

        // Verify the 4 distinct size tiers exist across the 11 policies
        var distinctLimits = policies.Select(p => p.MaxBytes).Distinct().OrderBy(b => b).ToArray();
        Assert.Equal(4, distinctLimits.Length);
        Assert.Equal(UploadPolicies.Tier6MiB, distinctLimits[0]);   // 6 MiB
        Assert.Equal(UploadPolicies.Tier9MiB, distinctLimits[1]);   // 9 MiB
        Assert.Equal(UploadPolicies.Tier11MiB, distinctLimits[2]); // 11 MiB
        Assert.Equal(UploadPolicies.Tier22MiB, distinctLimits[3]); // 22 MiB

        foreach (var policy in policies)
        {
            Assert.False(string.IsNullOrWhiteSpace(policy.ParentType));
            Assert.True(policy.MaxBytes > 0);
            Assert.False(string.IsNullOrWhiteSpace(policy.MaxBytesLabel));
            Assert.NotEmpty(policy.AllowedMimeTypes);
            Assert.NotEmpty(policy.AllowedExtensions);
            Assert.True(policy.MaxFilesPerRequest >= 1);
        }
    }

    [Theory]
    [InlineData("REVIEW_GATE_EVIDENCE", 22 * 1024 * 1024, "22 MiB")]
    [InlineData("DELIVERABLE_VERSION", 22 * 1024 * 1024, "22 MiB")]
    [InlineData("PROJECT_FILE", 22 * 1024 * 1024, "22 MiB")]
    [InlineData("STUDENT_QUALIFICATION", 22 * 1024 * 1024, "22 MiB")]
    [InlineData("COLD_SUBMISSION", 11 * 1024 * 1024, "11 MiB")]
    [InlineData("RESULT_APPEAL", 11 * 1024 * 1024, "11 MiB")]
    [InlineData("CHAT_ATTACHMENT", 11 * 1024 * 1024, "11 MiB")]
    [InlineData("ACADEMIC_ASSISTANT_ASK", 9 * 1024 * 1024, "9 MiB")]
    [InlineData("ACADEMIC_ASSISTANT_STREAM", 9 * 1024 * 1024, "9 MiB")]
    [InlineData("STUDENT_ACCOUNT_IMPORT", 6 * 1024 * 1024, "6 MiB")]
    [InlineData("STUDENT_CURRICULUM", 6 * 1024 * 1024, "6 MiB")]
    public async Task GetByParentType_ReturnsCorrectPolicy(string parentType, long expectedMaxBytes, string expectedLabel)
    {
        var response = await _client.GetAsync($"/api/v1/upload-policies/{parentType}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var policy = await response.Content.ReadFromJsonAsync<UploadPolicyDto>();
        Assert.NotNull(policy);
        Assert.Equal(parentType, policy.ParentType);
        Assert.Equal(expectedMaxBytes, policy.MaxBytes);
        Assert.Equal(expectedLabel, policy.MaxBytesLabel);
    }

    [Theory]
    [InlineData("REVIEW_GATE", "REVIEW_GATE_EVIDENCE")]
    [InlineData("VERSION", "DELIVERABLE_VERSION")]
    [InlineData("REPORT", "PROJECT_FILE")]
    [InlineData("TASK", "PROJECT_FILE")]
    [InlineData("MEETING", "PROJECT_FILE")]
    [InlineData("COLD", "COLD_SUBMISSION")]
    public async Task GetByParentType_Alias_ReturnsCanonicalPolicy(string alias, string expectedCanonical)
    {
        var response = await _client.GetAsync($"/v1/upload-policies/{alias}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var policy = await response.Content.ReadFromJsonAsync<UploadPolicyDto>();
        Assert.NotNull(policy);
        Assert.Equal(expectedCanonical, policy.ParentType);
    }

    [Fact]
    public async Task GetByParentType_Unknown_Returns404NotFound()
    {
        var response = await _client.GetAsync("/api/v1/upload-policies/DEFINITELY_NOT_EXISTING");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void ReflectionConsistency_All11ControllersMatchUploadPoliciesConstants()
    {
        var controllerAssembly = typeof(UploadPoliciesController).Assembly;
        var controllerTypes = controllerAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToArray();

        var endpointsWithLimit = new List<(MethodInfo Method, long Limit)>();

        foreach (var controller in controllerTypes)
        {
            foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                var attr = method.GetCustomAttribute<RequestSizeLimitAttribute>();
                if (attr != null && attr is IRequestSizeLimitMetadata meta && meta.MaxRequestBodySize.HasValue)
                {
                    endpointsWithLimit.Add((method, meta.MaxRequestBodySize.Value));
                }
            }
        }

        // Exactly 11 endpoints must have [RequestSizeLimit]
        Assert.Equal(11, endpointsWithLimit.Count);

        // Every endpoint's limit must match a recognized constant in UploadPolicies
        var validConstants = new[]
        {
            UploadPolicies.StudentAccountImportLimit,
            UploadPolicies.StudentCurriculumLimit,
            UploadPolicies.AcademicAssistantAskLimit,
            UploadPolicies.AcademicAssistantStreamLimit,
            UploadPolicies.ChatAttachmentLimit,
            UploadPolicies.ColdSubmissionLimit,
            UploadPolicies.ResultAppealLimit,
            UploadPolicies.DeliverableVersionLimit,
            UploadPolicies.ProjectFileLimit,
            UploadPolicies.ReviewGateEvidenceLimit,
            UploadPolicies.StudentQualificationLimit
        };

        foreach (var (method, limit) in endpointsWithLimit)
        {
            Assert.Contains(limit, validConstants);
        }

        // Verify the 11 UploadPolicyDto MaxBytes match the endpoints
        var policyLimits = UploadPolicies.All.Select(p => p.MaxBytes).OrderBy(x => x).ToArray();
        var endpointLimits = endpointsWithLimit.Select(e => e.Limit).OrderBy(x => x).ToArray();
        Assert.Equal(endpointLimits, policyLimits);
    }

    [Fact]
    public async Task Upload_ExceedingSizeLimit_Returns413WithErrorCodeAndMaxBytes()
    {
        // Student curriculum preview limit is 6 MiB (6,291,456 bytes).
        // Send a simulated oversize payload (e.g. 7 MiB) with ADMIN role.
        using var client = _factory.CreateAuthenticatedClient(1001, "admin@aipms.test", "Admin User", "ADMIN");

        var boundary = "---------------------------" + DateTime.UtcNow.Ticks.ToString("x");
        var content = new MultipartFormDataContent(boundary);

        // 7 MiB synthetic file content
        var oversizedBytes = new byte[7 * 1024 * 1024];
        var fileContent = new ByteArrayContent(oversizedBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "large_curriculum.xlsx");

        var response = await client.PostAsync("/api/v1/users/curriculum-import/preview", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);

        // 413 must contain errorCode = "UPLOAD_TOO_LARGE" and maxBytes
        Assert.True(problem.Extensions.TryGetValue("errorCode", out var errorCode) ||
                    problem.Extensions.TryGetValue("code", out errorCode));
        Assert.Equal("UPLOAD_TOO_LARGE", errorCode?.ToString());

        Assert.True(problem.Extensions.TryGetValue("maxBytes", out var maxBytesVal));
        var maxBytes = Convert.ToInt64(maxBytesVal?.ToString());
        Assert.Equal(UploadPolicies.StudentCurriculumLimit, maxBytes);
    }
}
