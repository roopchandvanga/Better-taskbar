namespace Facet.Core.Palette;

/// <summary>
/// Subsequence fuzzy matcher tuned for launcher-style queries. Higher is better; null = no match.
/// Rewards: match at index 0, word starts, contiguous runs. Penalises gaps. Shorter targets win ties.
/// </summary>
public static class FuzzyMatcher
{
    private const int StartBonus = 8;
    private const int WordStartBonus = 6;
    private const int ContiguousBonus = 7;
    private const int GapPenaltyPerChar = 3;
    private const int MaxGapPenalty = 9;

    public static int? Score(string query, string target)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        if (string.IsNullOrEmpty(target)) return null;

        var q = query.ToLowerInvariant();
        var t = target.ToLowerInvariant();

        int? best = null;
        for (var start = t.IndexOf(q[0]); start >= 0; start = t.IndexOf(q[0], start + 1))
        {
            var s = ScoreFrom(q, t, target, start);
            if (s is int v && (best is null || v > best)) best = v;
        }

        return best is int b ? b * 100 - target.Length : null;
    }

    private static int? ScoreFrom(string q, string t, string original, int start)
    {
        var score = 0;
        var prev = -1;
        for (var qi = 0; qi < q.Length; qi++)
        {
            int idx;
            if (qi == 0)
            {
                idx = start;
            }
            else if (prev + 1 < t.Length && t[prev + 1] == q[qi])
            {
                idx = prev + 1;
            }
            else
            {
                var first = t.IndexOf(q[qi], prev + 1);
                if (first < 0) return null;
                idx = first;
                for (var k = first; k < t.Length; k++)
                {
                    if (t[k] == q[qi] && IsWordStart(original, k)) { idx = k; break; }
                }
            }

            score += 1;
            if (idx == 0) score += StartBonus;
            if (IsWordStart(original, idx)) score += WordStartBonus;
            if (prev >= 0)
            {
                if (idx == prev + 1) score += ContiguousBonus;
                else score -= Math.Min(MaxGapPenalty, GapPenaltyPerChar * (idx - prev - 1));
            }
            prev = idx;
        }
        return score;
    }

    private static bool IsWordStart(string s, int i) =>
        i == 0 || !char.IsLetterOrDigit(s[i - 1]) || (char.IsUpper(s[i]) && char.IsLower(s[i - 1]));
}
