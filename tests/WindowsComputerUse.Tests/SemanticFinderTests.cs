using WindowsComputerUse.Core;
using Xunit;

namespace WindowsComputerUse.Tests;

public class SemanticFinderTests
{
    private sealed class Node(string id = "", string name = "")
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public List<Node> Children { get; } = [];
    }

    private static FindResult<Node> Find(Node root, ElementLocator locator, int results = 100,
        int nodes = 2000, int depth = 20) => SemanticFinder.Find(root, locator, results, nodes, depth,
            node => node.Children, (node, criteria) =>
                (criteria.AutomationId is null || string.Equals(node.Id, criteria.AutomationId, StringComparison.Ordinal)) &&
                (criteria.Name is null || string.Equals(node.Name, criteria.Name, StringComparison.Ordinal)) &&
                (criteria.ControlType is null || criteria.ControlType == "Pane"));

    [Fact]
    public void MultipleExactMatchesCanBeComplete()
    {
        var root = new Node();
        root.Children.AddRange([new("id"), new("id"), new("ID")]);
        var result = Find(root, new("id"));
        Assert.Equal(2, result.Matches.Count);
        Assert.True(result.Complete);
        Assert.Equal(4, result.Visited);
    }

    [Fact]
    public void AncestorMustBeUniqueAndScopeExcludesItself()
    {
        var root = new Node();
        var scope = new Node("scope");
        scope.Children.Add(new Node("target"));
        root.Children.AddRange([scope, new Node("target")]);
        Assert.Single(Find(root, new("target", Ancestor: new("scope"))).Matches);
        Assert.Empty(Find(root, new("scope", Ancestor: new("scope"))).Matches);
        root.Children.Add(new Node("scope"));
        Assert.Equal("locator_ancestor_ambiguous", Assert.Throws<AutomationException>(() =>
            Find(root, new("target", Ancestor: new("scope")))).Code);
    }

    [Fact]
    public void NodeDepthAndResultBudgetsNeverClaimCompleteness()
    {
        var root = new Node("match");
        root.Children.Add(new Node("match"));
        Assert.False(Find(root, new("match"), nodes: 1).Complete);
        Assert.False(Find(root, new("match"), depth: 0).Complete);
        Assert.False(Find(root, new("match"), results: 1).Complete);
        Assert.True(Find(root, new("match"), depth: 1).Complete);
        Assert.True(Find(new Node("match"), new("match"), depth: 0).Complete);
    }

    [Fact]
    public void IncompleteScopeCannotPretendToBeUnique()
    {
        var root = new Node("scope");
        root.Children.Add(new Node("target"));
        Assert.Equal("locator_limit_exceeded", Assert.Throws<AutomationException>(() =>
            Find(root, new("target", Ancestor: new("scope")), depth: 0)).Code);
    }

    [Fact]
    public void ScopedSearchSharesNodeBudgetAndReportsPartialResults()
    {
        var root = new Node("root");
        var scope = new Node("scope");
        scope.Children.Add(new Node("target"));
        root.Children.Add(scope);
        // Two nodes to establish ancestor uniqueness; only one remaining to
        // enter the scope subtree, so the target cannot be called absent.
        var result = Find(root, new("target", Ancestor: new("scope")), nodes: 3);
        Assert.False(result.Complete);
        Assert.Empty(result.Matches);
        Assert.Equal(3, result.Visited);
    }
}
