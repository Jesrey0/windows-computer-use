namespace WindowsComputerUse.Core;

// Searches the live tree, not a prior observation. All candidates must be examined
// before declaring a match unique; exceeding the budget is never treated as no match.
internal static class SemanticLocator
{
    public static T Resolve<T>(T root, ElementLocator locator, Func<T, IReadOnlyList<T>> children,
        Func<T, ElementLocator, bool> matches) where T : class
    {
        if (!InputLimits.ValidLocator(locator))
            throw new AutomationException("invalid_locator", "Use one or more nonempty exact fields and at most one ancestor.", 400);

        var visited = 0;
        T FindUnique(T start, ElementLocator criteria, string prefix, bool includeStart)
        {
            T? found = null;
            void Visit(T node, int depth, bool eligible)
            {
                if (++visited > InputLimits.MaxLocatorNodes)
                    throw new AutomationException("locator_limit_exceeded", "Locator node limit exceeded.");
                if (eligible && matches(node, criteria))
                {
                    if (found is not null)
                        throw new AutomationException(prefix + "_ambiguous", "Locator matched multiple elements.", 409);
                    found = node;
                }
                var descendants = children(node);
                if (depth == InputLimits.MaxLocatorDepth)
                {
                    if (descendants.Count > 0)
                        throw new AutomationException("locator_limit_exceeded", "Locator depth limit reached.");
                    return;
                }
                foreach (var child in descendants) Visit(child, depth + 1, true);
            }
            Visit(start, 0, includeStart);
            return found ?? throw new AutomationException(prefix + "_not_found", "Locator matched no elements.", 404);
        }

        var scope = locator.Ancestor is null ? root : FindUnique(root, locator.Ancestor, "locator_ancestor", true);
        return FindUnique(scope, locator, "locator", locator.Ancestor is null);
    }
}
