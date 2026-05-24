using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class WindowRoastActivityTracker
{
    public class Observation
    {
        public float time;
        public string category;
        public string mood;
        public string sanitizedTitle;
    }

    public class Summary
    {
        public string currentCategory = "unknown";
        public string currentMood = "unknown";

        public int totalCount;
        public Dictionary<string, int> categoryCounts = new Dictionary<string, int>();

        public string triggeredRuleName = "";
        public string triggeredIntent = "";
        public bool hasTriggeredRule;
    }

    private readonly List<Observation> observations = new List<Observation>();
    private readonly Dictionary<string, float> ruleLastTriggeredTime = new Dictionary<string, float>();

    public Summary RecordAndSummarize(WindowRoastConfig config, string sanitizedTitle)
    {
        Summary summary = new Summary();

        if (config == null || config.activity == null || !config.activity.enabled)
            return summary;

        string category = DetectCategory(config, sanitizedTitle, out string mood);

        observations.Add(new Observation
        {
            time = Time.time,
            category = category,
            mood = mood,
            sanitizedTitle = sanitizedTitle
        });

        float maxWindowSeconds = GetMaxWindowSeconds(config);
        CleanupOldObservations(maxWindowSeconds);

        summary.currentCategory = category;
        summary.currentMood = mood;

        BuildCounts(summary);
        CheckTriggeredRules(config, summary);

        Debug.Log("[WindowRoast] Activity summary: " + ToDebugString(summary));

        return summary;
    }

    public Summary SummarizeOnly(WindowRoastConfig config)
    {
        Summary summary = new Summary();

        if (config == null || config.activity == null || !config.activity.enabled)
            return summary;

        float maxWindowSeconds = GetMaxWindowSeconds(config);
        CleanupOldObservations(maxWindowSeconds);

        BuildCounts(summary);
        CheckTriggeredRules(config, summary);

        return summary;
    }

    public string ToPromptSummary(Summary summary)
    {
        if (summary == null || summary.totalCount <= 0)
            return "暂无足够的窗口活动记录。";

        StringBuilder sb = new StringBuilder();

        sb.Append("最近窗口活动统计：");

        bool first = true;
        foreach (KeyValuePair<string, int> kv in summary.categoryCounts)
        {
            if (!first)
                sb.Append("，");

            sb.Append(kv.Key);
            sb.Append("=");
            sb.Append(kv.Value);
            sb.Append("次");

            first = false;
        }

        sb.Append("。当前类别：");
        sb.Append(summary.currentCategory);

        if (!string.IsNullOrWhiteSpace(summary.currentMood))
        {
            sb.Append("，当前倾向：");
            sb.Append(summary.currentMood);
        }

        if (summary.hasTriggeredRule)
        {
            sb.Append("。触发规则：");
            sb.Append(summary.triggeredRuleName);
            sb.Append("，意图：");
            sb.Append(summary.triggeredIntent);
        }

        return sb.ToString();
    }

    private string DetectCategory(WindowRoastConfig config, string sanitizedTitle, out string mood)
    {
        mood = "unknown";

        if (config == null ||
            config.activity == null ||
            config.activity.categories == null ||
            string.IsNullOrWhiteSpace(sanitizedTitle))
        {
            return "unknown";
        }

        string lower = sanitizedTitle.ToLowerInvariant();

        foreach (WindowRoastActivityCategory category in config.activity.categories)
        {
            if (category == null || category.keywords == null)
                continue;

            foreach (string keyword in category.keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                if (lower.Contains(keyword.ToLowerInvariant()))
                {
                    mood = string.IsNullOrWhiteSpace(category.mood) ? "unknown" : category.mood;
                    return string.IsNullOrWhiteSpace(category.name) ? "unknown" : category.name;
                }
            }
        }

        return "unknown";
    }

    private float GetMaxWindowSeconds(WindowRoastConfig config)
    {
        float maxMinutes = 45f;

        if (config != null && config.activity != null)
        {
            maxMinutes = Mathf.Max(maxMinutes, config.activity.activityWindowMinutes);

            if (config.activity.rules != null)
            {
                foreach (WindowRoastActivityRule rule in config.activity.rules)
                {
                    if (rule == null)
                        continue;

                    maxMinutes = Mathf.Max(maxMinutes, rule.windowMinutes);
                }
            }
        }

        return Mathf.Max(1f, maxMinutes) * 60f;
    }

    private void CleanupOldObservations(float maxWindowSeconds)
    {
        float now = Time.time;

        observations.RemoveAll(obs => obs == null || now - obs.time > maxWindowSeconds);
    }

    private void BuildCounts(Summary summary)
    {
        summary.categoryCounts.Clear();

        foreach (Observation obs in observations)
        {
            if (obs == null)
                continue;

            string category = string.IsNullOrWhiteSpace(obs.category) ? "unknown" : obs.category;

            if (!summary.categoryCounts.ContainsKey(category))
                summary.categoryCounts[category] = 0;

            summary.categoryCounts[category]++;
            summary.totalCount++;
        }
    }

    private void CheckTriggeredRules(WindowRoastConfig config, Summary summary)
    {
        if (config == null ||
            config.activity == null ||
            config.activity.rules == null ||
            summary == null)
        {
            Debug.Log("[WindowRoast] Activity rules skipped: config/activity/rules/summary is null.");
            return;
        }

        float now = Time.time;

        foreach (WindowRoastActivityRule rule in config.activity.rules)
        {
            if (rule == null)
            {
                Debug.Log("[WindowRoast] Activity rule skipped: rule is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.category))
            {
                Debug.Log("[WindowRoast] Activity rule skipped: empty category. name=" + rule.name);
                continue;
            }

            float windowSeconds = Mathf.Max(1f, rule.windowMinutes) * 60f;
            int count = CountCategoryInWindow(rule.category, windowSeconds);

            Debug.Log(
                "[WindowRoast] Activity rule check: " +
                "name=" + rule.name +
                ", category=" + rule.category +
                ", windowMinutes=" + rule.windowMinutes +
                ", minCount=" + rule.minCount +
                ", count=" + count
            );

            if (count < rule.minCount)
                continue;

            string ruleName = string.IsNullOrWhiteSpace(rule.name)
                ? rule.category
                : rule.name;

            if (ruleLastTriggeredTime.TryGetValue(ruleName, out float lastTime))
            {
                float cooldownSeconds = Mathf.Max(0f, rule.cooldownMinutes) * 60f;
                float elapsed = now - lastTime;

                if (elapsed < cooldownSeconds)
                {
                    Debug.Log(
                        "[WindowRoast] Activity rule cooldown: " +
                        ruleName +
                        ", elapsed=" + elapsed +
                        ", cooldown=" + cooldownSeconds
                    );
                    continue;
                }
            }

            ruleLastTriggeredTime[ruleName] = now;

            summary.hasTriggeredRule = true;
            summary.triggeredRuleName = ruleName;
            summary.triggeredIntent = string.IsNullOrWhiteSpace(rule.intent)
                ? "notice"
                : rule.intent;

            Debug.Log(
                "[WindowRoast] Activity rule triggered: " +
                summary.triggeredRuleName +
                ", intent=" +
                summary.triggeredIntent
            );

            return;
        }
    }

    private int CountCategoryInWindow(string category, float windowSeconds)
    {
        int count = 0;
        float now = Time.time;

        foreach (Observation obs in observations)
        {
            if (obs == null)
                continue;

            if (now - obs.time > windowSeconds)
                continue;

            if (obs.category == category)
                count++;
        }

        return count;
    }

    private string ToDebugString(Summary summary)
    {
        if (summary == null)
            return "null";

        StringBuilder sb = new StringBuilder();

        sb.Append("current=");
        sb.Append(summary.currentCategory);
        sb.Append(", total=");
        sb.Append(summary.totalCount);

        foreach (KeyValuePair<string, int> kv in summary.categoryCounts)
        {
            sb.Append(", ");
            sb.Append(kv.Key);
            sb.Append("=");
            sb.Append(kv.Value);
        }

        if (summary.hasTriggeredRule)
        {
            sb.Append(", triggered=");
            sb.Append(summary.triggeredRuleName);
            sb.Append("/");
            sb.Append(summary.triggeredIntent);
        }

        return sb.ToString();
    }
}