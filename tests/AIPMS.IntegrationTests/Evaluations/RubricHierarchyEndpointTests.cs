using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

internal static class HierarchicalRubricSample
{
    public static RubricCriterionInput[] Tree() =>
    [
        new("Requirements", null, 20, null, 0, false)
        {
            Children = [
                new("Problem Definition", null, 40, null, 0, false)
                {
                    Children = [new("Clarity and scope", null, 100, null, 0, false)
                    {
                        Children = [new("Clarity", null, 60, 10, 0, true), new("Scope", null, 40, 20, 1, false)]
                    }]
                },
                new("Requirements Quality", null, 60, 10, 1, true)
            ]
        },
        new("Implementation", null, 80, 10, 1, true)
    ];

    public static RubricCriterionDto[] Flatten(IReadOnlyList<RubricCriterionDto> tree)
    {
        var pending = new Stack<RubricCriterionDto>(tree.Reverse());
        var result = new List<RubricCriterionDto>();
        while (pending.TryPop(out var node))
        {
            result.Add(node);
            foreach (var child in node.Children.Reverse()) pending.Push(child);
        }
        return result.ToArray();
    }
}

public sealed partial class RubricEndpointTests
{
    [Fact]
    public async Task Swagger_preserves_routes_and_describes_recursive_criteria()
    {
        using var app = new RubricFactory(database);
        using var client = app.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/v1/rubrics", "/api/v1/rubrics/{id}",
            "/api/v1/rubrics/{id}/publish", "/api/v1/rubrics/{id}/retire", "/api/v1/rubrics/{id}/versions",
            "/api/v1/evaluations/{id}/draft", "/api/v1/evaluations/{id}/finalize" })
            Assert.True(paths.TryGetProperty(path, out _), path);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var input = schemas.GetProperty("RubricCriterionInput").GetProperty("properties");
        Assert.Equal("#/components/schemas/RubricCriterionInput",
            input.GetProperty("children").GetProperty("items").GetProperty("$ref").GetString());
        var output = schemas.GetProperty("RubricCriterionDto").GetProperty("properties");
        Assert.True(output.TryGetProperty("effectiveWeightPercent", out _));
        Assert.True(output.GetProperty("maxScore").GetProperty("nullable").GetBoolean());
    }

    [Fact]
    public async Task Four_level_tree_round_trips_versions_are_independent_and_draft_deletion_removes_all_descendants()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var draft = await Create(staff, Input(s) with { Criteria = HierarchicalRubricSample.Tree() });
        var all = HierarchicalRubricSample.Flatten(draft.Criteria);
        Assert.Equal(7, all.Length);
        Assert.Equal(4.8m, all.Single(c => c.Name == "Clarity").EffectiveWeightPercent);
        Assert.Equal(draft.Criteria[0].Id, draft.Criteria[0].Children[0].ParentId);
        var published = await Status(staff, draft, "publish");
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{published.Id}", Edit(published))).StatusCode);
        var copy = await Body<RubricDto>(await staff.PostAsJsonAsync($"/api/v1/rubrics/{published.Id}/versions",
            new CreateRubricVersionRequest("COPY_" + Guid.NewGuid().ToString("N"), published.ConcurrencyToken)));
        var copied = HierarchicalRubricSample.Flatten(copy.Criteria);
        Assert.Equal(all.Select(c => c.Name), copied.Select(c => c.Name));
        Assert.Equal(all.Select(c => c.EffectiveWeightPercent), copied.Select(c => c.EffectiveWeightPercent));
        Assert.Empty(all.Select(c => c.Id).Intersect(copied.Select(c => c.Id)));
        Assert.Empty(all.Select(c => c.CriterionId).Intersect(copied.Select(c => c.CriterionId)));
        Assert.All(copied.Where(c => c.ParentId.HasValue), c => Assert.Contains(copied, p => p.Id == c.ParentId));
        var edited = await Body<RubricDto>(await staff.PutAsJsonAsync($"/api/v1/rubrics/{copy.Id}",
            new UpdateRubricRequest("Edited tree", null, HierarchicalRubricSample.Tree(), copy.ConcurrencyToken)));
        Assert.Equal(7, HierarchicalRubricSample.Flatten(edited.Criteria).Length);
        var old = await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{published.Id}"));
        Assert.Equal(JsonSerializer.Serialize(published.Criteria), JsonSerializer.Serialize(old.Criteria));
        await database.Migrate();
        await database.Migrate();
        Assert.Equal(JsonSerializer.Serialize(old), JsonSerializer.Serialize(await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{old.Id}"))));
        var deleted = await staff.DeleteAsync($"/api/v1/rubrics/{edited.Id}?concurrencyToken={edited.ConcurrencyToken}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.RubricCriteria.AnyAsync(c => c.RubricId == copy.Id));
        var copiedDefinitionIds = copied.Select(c => c.CriterionId).ToArray();
        Assert.False(await db.EvaluationCriteria.AnyAsync(c => copiedDefinitionIds.Contains(c.Id)));
    }

    [Fact]
    public async Task Deep_tree_exceeds_default_framework_depth_and_still_round_trips()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        RubricCriterionInput root = new("Leaf", null, 100, 10, 0, true);
        for (var level = 0; level < 80; level++)
            root = new("Group " + level, null, 100, null, 0, false) { Children = [root] };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 2048 };
        var response = await staff.PostAsJsonAsync("/api/v1/rubrics", Input(s) with { Criteria = [root] }, options);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var dto = (await response.Content.ReadFromJsonAsync<RubricDto>(options))!;
        Assert.Equal(81, HierarchicalRubricSample.Flatten(dto.Criteria).Length);
        var publication = await staff.PostAsJsonAsync($"/api/v1/rubrics/{dto.Id}/publish", new ChangeRubricStatusRequest(dto.ConcurrencyToken));
        Assert.True(publication.IsSuccessStatusCode, await publication.Content.ReadAsStringAsync());
        var published = (await publication.Content.ReadFromJsonAsync<RubricDto>(options))!;
        Assert.Equal(100m, HierarchicalRubricSample.Flatten(published.Criteria).Last().EffectiveWeightPercent);
    }

    [Fact]
    public async Task Nested_weight_sum_is_checked_on_publish_and_invalid_leaf_is_rejected_before_saving()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var root = new RubricCriterionInput("Group", null, 100, null, 0, false)
            { Children = [new("Leaf", null, 99.99m, 10, 0, true)] };
        var draft = await Create(staff, Input(s) with { Criteria = [root] });
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/rubrics/{draft.Id}/publish",
            new ChangeRubricStatusRequest(draft.ConcurrencyToken))).StatusCode);
        root = root with { Children = [root.Children[0] with { MaxScore = null }] };
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{draft.Id}",
            new UpdateRubricRequest("Invalid", null, [root], draft.ConcurrencyToken))).StatusCode);
        var unchanged = await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{draft.Id}"));
        Assert.Equal(draft.ConcurrencyToken, unchanged.ConcurrencyToken);
    }

    [Theory]
    [InlineData("SELF")]
    [InlineData("ORPHAN")]
    [InlineData("CROSS_RUBRIC")]
    public async Task Database_rejects_invalid_parent_links(string kind)
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var first = await Create(staff, s);
        var second = await Create(staff, s);
        var child = first.Criteria[0].Id;
        var parent = kind switch { "SELF" => child, "ORPHAN" => long.MaxValue, _ => second.Criteria[0].Id };
        await using var db = database.CreateContext();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.rubric_criteria SET parent_id = {parent} WHERE id = {child}"));
    }

    [Fact]
    public async Task Corrupt_cycle_cannot_be_read_published_or_copied_as_an_empty_tree()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var tree = await Create(staff, Input(s) with { Criteria = HierarchicalRubricSample.Tree() });
        await using var db = database.CreateContext();
        var root = tree.Criteria[0].Id;
        var child = tree.Criteria[0].Children[0].Id;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.rubric_criteria SET parent_id = {child} WHERE id = {root}");
        Assert.Equal(HttpStatusCode.Conflict, (await staff.GetAsync($"/api/v1/rubrics/{tree.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/rubrics/{tree.Id}/publish",
            new ChangeRubricStatusRequest(tree.ConcurrencyToken))).StatusCode);
    }

    [Fact]
    public async Task Audit_failure_restores_whole_tree_after_replacement()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var tree = await Create(staff, Input(s) with { Criteria = HierarchicalRubricSample.Tree() });
        using var failingApp = new RubricFactory(database, true);
        using var failing = failingApp.CreateAuthenticatedClient(s.Users.Staff);
        Assert.Equal(HttpStatusCode.InternalServerError, (await failing.PutAsJsonAsync($"/api/v1/rubrics/{tree.Id}", Edit(tree))).StatusCode);
        var after = await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{tree.Id}"));
        Assert.Equal(JsonSerializer.Serialize(tree), JsonSerializer.Serialize(after));
    }
}
