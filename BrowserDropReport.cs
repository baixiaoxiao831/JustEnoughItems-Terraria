using System;
using System.Collections.Generic;
using Terraria.GameContent.ItemDropRules;

namespace JustEnoughItemsTerraria;

internal static class BrowserDropReport
{
    private static readonly object hookLock = new();
    private static int users;
    [ThreadStatic] private static int scopeDepth;
    [ThreadStatic] private static bool reporting;
    [ThreadStatic] private static LeadingConditionRule parent;

    internal sealed class Negated(IItemDropRuleCondition inner) : IItemDropRuleCondition
    {
        public IItemDropRuleCondition Inner { get; } = inner;
        public bool CanDrop(DropAttemptInfo info) => !Inner.CanDrop(info);
        public bool CanShowItemDropInUI() => Inner.CanShowItemDropInUI();
        public string GetConditionDescription() => null;
    }

    // One installation for the whole index build, with independent reports per thread.
    internal static IDisposable Capture()
    {
        lock (hookLock)
        {
            if (users == 0)
            {
                On_LeadingConditionRule.ReportDroprates += WithParent;
                try { On_Chains.TryIfDoesntFillConditions.ReportDroprates += WithFailedCondition; }
                catch { On_LeadingConditionRule.ReportDroprates -= WithParent; throw; }
            }
            users++;
            scopeDepth++;
        }
        return new ReportScope();
    }

    private sealed class ReportScope : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            lock (hookLock)
            {
                if (disposed) return;
                disposed = true;
                scopeDepth--;
                if (--users != 0) return;
                try { On_Chains.TryIfDoesntFillConditions.ReportDroprates -= WithFailedCondition; }
                finally { On_LeadingConditionRule.ReportDroprates -= WithParent; }
            }
        }
    }

    internal static void Report(IItemDropRule rule, List<DropRateInfo> drops)
    {
        using var scope = scopeDepth == 0 ? Capture() : null;
        bool previous = reporting;
        var previousParent = parent;
        reporting = true;
        parent = null;
        try { rule.ReportDroprates(drops, new DropRateInfoChainFeed(1f)); }
        finally { parent = previousParent; reporting = previous; }
    }

    private static void WithParent(On_LeadingConditionRule.orig_ReportDroprates original, LeadingConditionRule rule,
        List<DropRateInfo> drops, DropRateInfoChainFeed feed)
    {
        if (!reporting) { original(rule, drops, feed); return; }
        var previous = parent;
        parent = rule;
        try { original(rule, drops, feed); }
        finally { parent = previous; }
    }

    private static void WithFailedCondition(On_Chains.TryIfDoesntFillConditions.orig_ReportDroprates original,
        Chains.TryIfDoesntFillConditions branch, float chance, List<DropRateInfo> drops, DropRateInfoChainFeed feed)
    {
        // LeadingConditionRule adds the positive condition to the feed of *every*
        // branch. Replace only its own last occurrence, in a branch-local copy.
        // Let the native chain handle visibility, probability and dispatch to any
        // custom child. Bestiary reports and actual drops keep their original path.
        if (parent != null && parent.ChainedRules.Contains(branch) && feed.conditions != null)
        {
            int index = feed.conditions.FindLastIndex(c => ReferenceEquals(c, parent.condition));
            if (index >= 0)
            {
                feed = feed.With(1f);
                feed.conditions[index] = new Negated(parent.condition);
            }
        }
        original(branch, chance, drops, feed);
    }
}
