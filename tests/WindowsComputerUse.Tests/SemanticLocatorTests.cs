using WindowsComputerUse.Core;
using System.Text.Json;
using Xunit;

namespace WindowsComputerUse.Tests;

public class SemanticLocatorTests
{
    private sealed class Node(string? id = null, string? name = null, string type = "Pane")
    {
        public string? Id { get; } = id;
        public string? Name { get; } = name;
        public string Type { get; } = type;
        public List<Node> Children { get; } = [];
    }

    private static ElementLocator ById(string id, ElementLocator? ancestor = null) => new(id, null, null, ancestor);

    private static Node Resolve(Node root, ElementLocator locator) => SemanticLocator.Resolve(root, locator,
        node => node.Children, (node, criteria) =>
            (criteria.AutomationId is null || string.Equals(criteria.AutomationId, node.Id, StringComparison.Ordinal)) &&
            (criteria.Name is null || string.Equals(criteria.Name, node.Name, StringComparison.Ordinal)) &&
            (criteria.ControlType is null || string.Equals(criteria.ControlType, node.Type, StringComparison.Ordinal)));

    [Fact]
    public void ExactMatchesOnlyAndRequiresUniqueTarget()
    {
        var root = new Node();
        root.Children.AddRange([new Node("Edit", "Text", "Edit"), new Node("edit", "Text", "Edit")]);
        Assert.Same(root.Children[0], Resolve(root, new ElementLocator("Edit", "Text", "Edit", null)));
        Assert.Equal("locator_not_found", Assert.Throws<AutomationException>(() => Resolve(root, ById("EDIT"))).Code);
        var ambiguous = Assert.Throws<AutomationException>(() => Resolve(root, new ElementLocator(null, "Text", null, null)));
        Assert.Equal("locator_ambiguous", ambiguous.Code);
        Assert.Equal(409, ambiguous.Status);
    }

    [Fact]
    public void ScopeMustBeUniqueAndTargetMustBeWithinIt()
    {
        var root = new Node();
        var left = new Node("left");
        var right = new Node("right");
        root.Children.AddRange([left, right]);
        left.Children.Add(new Node("editor"));
        right.Children.Add(new Node("editor"));
        Assert.Same(right.Children[0], Resolve(root, ById("editor", ById("right"))));
        Assert.Equal("locator_ancestor_not_found", Assert.Throws<AutomationException>(() =>
            Resolve(root, ById("editor", ById("missing")))).Code);
        var ambiguous = Assert.Throws<AutomationException>(() => Resolve(root,
            ById("editor", new ElementLocator(null, null, "Pane", null))));
        Assert.Equal("locator_ancestor_ambiguous", ambiguous.Code);
        Assert.Equal("locator_not_found", Assert.Throws<AutomationException>(() =>
            Resolve(root, ById("right", ById("right")))).Code); // scope excludes itself
    }

    [Fact]
    public void EveryActionResolvesFreshRatherThanReplayingAnObservedPath()
    {
        var root = new Node();
        var old = new Node("target");
        root.Children.Add(old);
        Assert.Same(old, Resolve(root, ById("target")));
        var replacement = new Node("target");
        root.Children.Clear();
        root.Children.AddRange([new Node("inserted"), replacement]);
        Assert.Same(replacement, Resolve(root, ById("target")));
    }

    [Fact]
    public void APartialSearchNeverReportsAUniqueMatch()
    {
        var root = new Node("target");
        var cursor = root;
        for (var i = 0; i <= InputLimits.MaxLocatorDepth; i++)
        {
            var child = new Node();
            cursor.Children.Add(child);
            cursor = child;
        }
        Assert.Equal("locator_limit_exceeded", Assert.Throws<AutomationException>(() =>
            Resolve(root, ById("target"))).Code);
    }

    [Fact]
    public void NodeBudgetCannotProduceFalseUniqueMatch()
    {
        var root = new Node("target");
        root.Children.AddRange(Enumerable.Range(0, InputLimits.MaxLocatorNodes).Select(_ => new Node()));
        Assert.Equal("locator_limit_exceeded", Assert.Throws<AutomationException>(() =>
            Resolve(root, ById("target"))).Code);
    }

    [Fact]
    public void LocatorValidationRejectsEmptyOversizedAndNestedScopes()
    {
        Assert.False(InputLimits.ValidLocator(null));
        Assert.False(InputLimits.ValidLocator(new ElementLocator(null, null, null, null)));
        Assert.False(InputLimits.ValidLocator(ById("  ")));
        Assert.False(InputLimits.ValidLocator(ById(new string('x', InputLimits.MaxLocatorTextLength + 1))));
        Assert.False(InputLimits.ValidLocator(ById("target", ById("outer", ById("nested")))));
        Assert.True(InputLimits.ValidLocator(new ElementLocator(null, "Seven", "Button", ById("scope"))));
    }

    [Fact]
    public void MinimalHttpLocatorOmitsUnusedFields()
    {
        var request = JsonSerializer.Deserialize<LocatedElementRequest>(
            """{"handle":"0x1234","locator":{"automationId":"editor"}}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(request);
        Assert.True(InputLimits.ValidLocator(request.Locator));
        Assert.Equal("editor", request.Locator.AutomationId);
        Assert.Null(request.Locator.Ancestor);
    }
}
