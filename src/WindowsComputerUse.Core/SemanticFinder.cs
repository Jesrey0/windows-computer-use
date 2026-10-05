namespace WindowsComputerUse.Core;

internal sealed record FindResult<T>(List<T> Matches, List<T> Eligible, int Visited, bool Complete) where T : class;

internal static class SemanticFinder
{
    public static FindResult<T> Find<T>(T root, ElementLocator locator, int maxResults, int maxNodes, int maxDepth,
        Func<T, IReadOnlyList<T>> children, Func<T, ElementLocator, bool> matches) where T : class
    {
        if (!InputLimits.ValidLocator(locator) || maxResults is < 1 or > InputLimits.MaxFindResults ||
            maxNodes is < 1 or > InputLimits.MaxLocatorNodes || maxDepth is < 0 or > InputLimits.MaxLocatorDepth)
            throw new AutomationException("invalid_find", "Invalid locator or find limits.", 400);
        var visited = 0;
        var complete = true;
        T? scope = null;
        if (locator.Ancestor is { } ancestor)
        {
            void ScopeVisit(T node, int depth)
            {
                if (visited >= maxNodes) { complete = false; return; }
                visited++;
                if (matches(node, ancestor))
                {
                    if (scope is not null)
                        throw new AutomationException("locator_ancestor_ambiguous", "Ancestor matched multiple elements.", 409);
                    scope = node;
                }
                var descendants = children(node);
                if (depth == maxDepth)
                {
                    if (descendants.Count > 0) complete = false;
                    return;
                }
                foreach (var child in descendants) ScopeVisit(child, depth + 1);
            }
            ScopeVisit(root, 0);
            if (!complete) throw new AutomationException("locator_limit_exceeded", "Cannot establish a unique ancestor within search limits.");
            if (scope is null) throw new AutomationException("locator_ancestor_not_found", "Ancestor matched no elements.", 404);
        }

        var found = new List<T>();
        var eligible = new List<T>();
        // Reuse remaining node budget for scoped traversal; the scope itself is excluded.
        void Visit(T node, int depth, bool include)
        {
            if (visited >= maxNodes) { complete = false; return; }
            visited++;
            if (include)
            {
                eligible.Add(node);
                if (matches(node, locator))
                {
                    if (found.Count == maxResults) { complete = false; return; }
                    found.Add(node);
                }
            }
            var descendants = children(node);
            if (depth == maxDepth)
            {
                if (descendants.Count > 0) complete = false;
                return;
            }
            foreach (var child in descendants) Visit(child, depth + 1, true);
        }
        if (scope is null) Visit(root, 0, true);
        else
        {
            // Scope root already counted in the ancestor pass. Depth here is relative to scope.
            var descendants = children(scope);
            if (maxDepth == 0 && descendants.Count > 0) complete = false;
            else foreach (var child in descendants) Visit(child, 1, true);
        }
        return new FindResult<T>(found, eligible, visited, complete);
    }
}
