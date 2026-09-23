using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Evaluations.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class RubricHierarchyTests
{
    private static RubricCriterionRecord Node(long id, long? parent, decimal weight, int order = 0, bool group = false) =>
        new(id, id, "Criterion " + id, null, weight, group ? null : 10m, order, !group) { ParentId = parent };

    private static RubricCriterionRecord[] Tree() =>
    [
        Node(1, null, 20, group: true), Node(2, null, 80, 1),
        Node(3, 1, 40, group: true), Node(4, 1, 60, 1),
        Node(5, 3, 100, group: true), Node(6, 5, 60), Node(7, 5, 40, 1)
    ];

    [Fact]
    public void Four_levels_multiply_local_weights_and_only_leaves_contribute_to_score()
    {
        var tree = Tree();
        RubricRules.EnsurePublishable(tree);
        var leaves = RubricHierarchy.Leaves(tree);
        Assert.Equal(new long[] { 6, 7, 4, 2 }, leaves.Select(c => c.Id));
        Assert.Equal(new[] { 4.8m, 3.2m, 12m, 80m }, leaves.Select(c => c.EffectiveWeightPercent));
        Assert.Equal(100m, leaves.Sum(c => c.EffectiveWeightPercent));
        var scores = leaves.Select(c => new EvaluationScoreRecord(c.Id, c.Name, c.Description,
            c.EffectiveWeightPercent, c.MaxScore!.Value, c.SortOrder, c.IsRequired, c.Id == 6 ? 5m : 10m, null)).ToArray();
        Assert.Equal(9.76m, EvaluationScoring.FinalTotal(scores));
    }

    [Fact]
    public void Dto_and_new_version_input_keep_all_descendants_and_sibling_order()
    {
        var tree = Tree().Reverse().ToArray();
        var rubric = new RubricRecord(1, 1, 1, "R", "Rubric", null, "DRAFT", 1, 1,
            Guid.NewGuid().ToString(), false, DateTime.UtcNow, DateTime.UtcNow, tree);
        var dto = rubric.ToDto();
        Assert.Equal(4.8m, dto.Criteria[0].Children[0].Children[0].Children[0].EffectiveWeightPercent);
        Assert.Equal(5, dto.Criteria[0].Children[0].Children[0].Children[0].ParentId);
        var copy = RubricHierarchy.ToInputs(tree);
        Assert.Equal("Criterion 6", copy[0].Children[0].Children[0].Children[0].Name);
        Assert.True(new RubricTreeInputValidator().Validate(copy).IsValid);
    }

    [Theory]
    [InlineData("SELF")]
    [InlineData("CYCLE")]
    [InlineData("ORPHAN")]
    [InlineData("DUPLICATE")]
    public void Malformed_flat_graphs_fail_instead_of_hiding_unreachable_nodes(string kind)
    {
        RubricCriterionRecord[] tree = kind switch
        {
            "SELF" => [Node(1, 1, 100)],
            "CYCLE" => [Node(1, 2, 100), Node(2, 1, 100)],
            "ORPHAN" => [Node(1, 99, 100)],
            _ => [Node(1, null, 50), Node(1, null, 50, 1)]
        };
        Assert.Throws<ConflictException>(() => RubricHierarchy.WithEffectiveWeights(tree));
        Assert.Throws<ConflictException>(() => RubricRules.EnsurePublishable(tree));
    }

    [Theory]
    [InlineData("SUM")]
    [InlineData("ORDER")]
    [InlineData("MAX_SCORE")]
    [InlineData("PARENT_SCORE")]
    [InlineData("PARENT_REQUIRED")]
    [InlineData("NO_REQUIRED_LEAF")]
    public void Publish_checks_each_sibling_group_and_each_leaf(string kind)
    {
        var tree = Tree();
        if (kind == "SUM") tree[6] = tree[6] with { WeightPercent = 39.99m };
        if (kind == "ORDER") tree[6] = tree[6] with { SortOrder = 0 };
        if (kind == "MAX_SCORE") tree[6] = tree[6] with { MaxScore = null };
        if (kind == "PARENT_SCORE") tree[0] = tree[0] with { MaxScore = 10 };
        if (kind == "PARENT_REQUIRED") tree[0] = tree[0] with { IsRequired = true };
        if (kind == "NO_REQUIRED_LEAF") tree = tree.Select(c => c with { IsRequired = false }).ToArray();
        Assert.Throws<ConflictException>(() => RubricRules.EnsurePublishable(tree));
    }

    [Fact]
    public void Request_cycles_and_shared_nodes_are_rejected_without_recursive_validation()
    {
        var children = new List<RubricCriterionInput>();
        var parent = new RubricCriterionInput("Group", null, 100, null, 0, false) { Children = children };
        children.Add(parent);
        Assert.False(new RubricTreeInputValidator().Validate([parent]).IsValid);
        children.Clear();
        var leaf = new RubricCriterionInput("Leaf", null, 100, 10, 0, true);
        children.Add(leaf);
        Assert.False(new RubricTreeInputValidator().Validate([parent, leaf]).IsValid);
    }

    [Theory]
    [InlineData("EMPTY_NAME")]
    [InlineData("NULL_NODE")]
    [InlineData("NULL_CHILDREN")]
    [InlineData("MAX_SCORE")]
    [InlineData("WEIGHT")]
    public void Both_create_and_update_validate_nested_nodes(string kind)
    {
        var leaf = new RubricCriterionInput("Leaf", null, 100, 10, 0, true);
        leaf = kind switch
        {
            "EMPTY_NAME" => leaf with { Name = " " },
            "NULL_NODE" => null!,
            "NULL_CHILDREN" => leaf with { Children = null! },
            "MAX_SCORE" => leaf with { MaxScore = 10.001m },
            _ => leaf with { WeightPercent = 100.001m }
        };
        RubricCriterionInput[] tree = [new("Group", null, 100, null, 0, false) { Children = [leaf] }];
        CreateRubricRequest create = new(1, 1, "R", "Rubric", null, tree);
        UpdateRubricRequest update = new("Rubric", null, tree, Guid.NewGuid().ToString());
        Assert.False(new CreateRubricRequestValidator().Validate(create).IsValid);
        Assert.False(new UpdateRubricRequestValidator().Validate(update).IsValid);
    }

    [Fact]
    public void Deep_hierarchies_are_iterative_and_request_budget_counts_descendants()
    {
        var records = Enumerable.Range(1, RubricHierarchy.MaxNodes).Select(i =>
            Node(i, i == 1 ? null : i - 1, 100, group: i < RubricHierarchy.MaxNodes)).ToArray();
        RubricRules.EnsurePublishable(records);
        Assert.Equal(100m, Assert.Single(RubricHierarchy.Leaves(records)).EffectiveWeightPercent);
        var input = RubricHierarchy.ToInputs(records);
        Assert.True(new RubricTreeInputValidator().Validate(input).IsValid);
        Assert.False(new RubricTreeInputValidator().Validate(
            [new("Extra", null, 100, null, 0, false) { Children = input }]).IsValid);
    }
}
