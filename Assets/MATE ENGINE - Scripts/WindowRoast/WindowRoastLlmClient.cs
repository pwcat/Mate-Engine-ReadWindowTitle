using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class WindowRoastLlmClient
{
    [Serializable]
    private class ChatCompletionRequest
    {
        public string model;
        public ChatMessage[] messages;
        public float temperature;
        public int max_tokens;
        public bool stream;
        public ThinkingOptions thinking;
    }

    [Serializable]
    private class ThinkingOptions
    {
        public string type;
    }

    [Serializable]
    private class ChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    private class ChatCompletionResponse
    {
        public ChatChoice[] choices;
    }

    [Serializable]
    private class ChatChoice
    {
        public ChatMessage message;
    }

    public static IEnumerator RequestRoast(
        WindowRoastConfig config,
        string sanitizedTitle,
        string currentCategory,
        string currentMood,
        bool categoryKnown,
        WindowRoastActivityTracker.Summary activitySummary,
        Action<string> onDone
    )
    {
        onDone?.Invoke(null);

        if (config == null || config.llm == null)
            yield break;

        string endpoint = BuildChatCompletionsUrl(config.llm.apiBaseUrl);
        if (string.IsNullOrWhiteSpace(endpoint))
            yield break;

        string systemPrompt = BuildSystemPrompt(config);
        string userPrompt = BuildUserPrompt(config, sanitizedTitle, currentCategory, currentMood, categoryKnown, activitySummary);

        ChatCompletionRequest requestBody = new ChatCompletionRequest
        {
            model = config.llm.model,
            temperature = config.llm.temperature,
            max_tokens = config.llm.maxTokens,
            stream = false,
            thinking = config.llm.disableThinking
                ? new ThinkingOptions { type = "disabled" }
                : null,
            messages = new[]
            {
        new ChatMessage { role = "system", content = systemPrompt },
        new ChatMessage { role = "user", content = userPrompt }
    }
        };

        string json = JsonUtility.ToJson(requestBody);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrWhiteSpace(config.llm.apiKey))
                request.SetRequestHeader("Authorization", "Bearer " + config.llm.apiKey);

            request.timeout = Mathf.Max(1, Mathf.RoundToInt(config.llm.timeoutSeconds));

            Debug.Log("[WindowRoast] Sending LLM request to: " + endpoint);

            yield return request.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool failed = request.result != UnityWebRequest.Result.Success;
#else
            bool failed = request.isNetworkError || request.isHttpError;
#endif

            if (failed)
            {
                Debug.LogWarning(
                    "[WindowRoast] LLM request failed: " +
                    request.error +
                    " body=" +
                    request.downloadHandler.text
                );
                yield break;
            }

            string responseText = request.downloadHandler.text;

            if (config.llm.debugLogRawResponse)
                Debug.Log("[WindowRoast] LLM raw response: " + responseText);

            string parsedContent = ParseLlmResponse(responseText);

            if (config.llm.debugLogParsedContent)
                Debug.Log("[WindowRoast] LLM parsed content: " + parsedContent);

            string content = CleanModelOutput(parsedContent, config);

            if (config.llm.debugLogParsedContent)
                Debug.Log("[WindowRoast] LLM cleaned content: " + content);

            if (!string.IsNullOrWhiteSpace(content))
                onDone?.Invoke(content);
        }
    }

    public static IEnumerator RequestMood(
    WindowRoastConfig config,
    WindowRoastActivityTracker.Summary activitySummary,
    Action<string> onDone
)
    {
        onDone?.Invoke(null);

        if (config == null || config.llm == null)
            yield break;

        string endpoint = BuildChatCompletionsUrl(config.llm.apiBaseUrl);
        if (string.IsNullOrWhiteSpace(endpoint))
            yield break;

        WindowRoastMoodPromptOption option = PickMoodPromptOption(config);

        string systemPrompt = BuildSystemPrompt(config);
        string userPrompt = BuildMoodUserPrompt(config, option, activitySummary);

        ChatCompletionRequest requestBody = new ChatCompletionRequest
        {
            model = config.llm.model,
            temperature = config.llm.temperature,
            max_tokens = config.llm.maxTokens,
            stream = false,
            thinking = config.llm.disableThinking
                ? new ThinkingOptions { type = "disabled" }
                : null,
            messages = new[]
            {
            new ChatMessage { role = "system", content = systemPrompt },
            new ChatMessage { role = "user", content = userPrompt }
        }
        };

        string json = JsonUtility.ToJson(requestBody);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrWhiteSpace(config.llm.apiKey))
                request.SetRequestHeader("Authorization", "Bearer " + config.llm.apiKey);

            request.timeout = Mathf.Max(1, Mathf.RoundToInt(config.llm.timeoutSeconds));

            Debug.Log("[WindowRoast] Sending LLM mood request to: " + endpoint);

            yield return request.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
        bool failed = request.result != UnityWebRequest.Result.Success;
#else
            bool failed = request.isNetworkError || request.isHttpError;
#endif

            if (failed)
            {
                Debug.LogWarning(
                    "[WindowRoast] LLM mood request failed: " +
                    request.error +
                    " body=" +
                    request.downloadHandler.text
                );
                yield break;
            }

            string responseText = request.downloadHandler.text;

            if (config.llm.debugLogRawResponse)
                Debug.Log("[WindowRoast] LLM mood raw response: " + responseText);

            string parsedContent = ParseLlmResponse(responseText);

            if (config.llm.debugLogParsedContent)
                Debug.Log("[WindowRoast] LLM mood parsed content: " + parsedContent);

            string content = CleanModelOutput(parsedContent, config);

            if (config.llm.debugLogParsedContent)
                Debug.Log("[WindowRoast] LLM mood cleaned content: " + content);

            if (!string.IsNullOrWhiteSpace(content))
                onDone?.Invoke(content);
        }
    }

    private static WindowRoastMoodPromptOption PickMoodPromptOption(WindowRoastConfig config)
    {
        if (config == null || config.moodPromptOptions == null || config.moodPromptOptions.Length == 0)
            return null;

        float total = 0f;

        foreach (WindowRoastMoodPromptOption option in config.moodPromptOptions)
        {
            if (option == null)
                continue;

            total += Mathf.Max(0f, option.weight);
        }

        if (total <= 0f)
            return config.moodPromptOptions[0];

        float roll = UnityEngine.Random.Range(0f, total);
        float acc = 0f;

        foreach (WindowRoastMoodPromptOption option in config.moodPromptOptions)
        {
            if (option == null)
                continue;

            acc += Mathf.Max(0f, option.weight);

            if (roll <= acc)
                return option;
        }

        return config.moodPromptOptions[0];
    }

    private static string BuildMoodUserPrompt(
    WindowRoastConfig config,
    WindowRoastMoodPromptOption option,
    WindowRoastActivityTracker.Summary activitySummary
)
    {
        DateTime now = DateTime.Now;
        string season = GetSeasonName(now.Month);
        string timeText = now.ToString("yyyy-MM-dd HH:mm");

        string optionName = option != null && !string.IsNullOrWhiteSpace(option.name)
            ? option.name
            : "gentle_idle";

        string instruction = option != null && !string.IsNullOrWhiteSpace(option.instruction)
            ? option.instruction
            : "生成一句温柔的桌面宠物心情短句。";

        string activityText = BuildActivityPromptText(activitySummary);

        return
            "这次不是针对某个新窗口，而是桌面伙伴的随机心情发言。\n" +
            "当前本地时间：" + timeText + "\n" +
            "当前季节：" + season + "\n" +
            activityText + "\n\n" +
            "本次心情类型：" + optionName + "\n" +
            "生成要求：" + instruction + "\n\n" +
            "请输出一句适合显示在桌面角色气泡里的短句。\n" +
            "可以温柔、清冷、诗意、轻微吐槽，但不要太长。\n" +
            "格式：台词：一句话\n" +
            "禁止输出思考过程，禁止输出 <think>，禁止解释，禁止换行。";
    }

    private static string BuildChatCompletionsUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "";

        string url = baseUrl.TrimEnd('/');

        if (url.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
            return url;

        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return url + "/chat/completions";

        return url + "/v1/chat/completions";
    }

    private static string BuildSystemPrompt(WindowRoastConfig config)
    {
        if (config == null || config.persona == null)
        {
            return
                "你是用户桌面上的虚拟伙伴。请用中文输出一句简短、可爱、轻微毒舌的吐槽。" +
                "不要输出思考过程，只输出气泡台词。";
        }

        StringBuilder sb = new StringBuilder();

        sb.AppendLine("你是用户桌面上的虚拟伙伴。");
        sb.AppendLine("名字：" + config.persona.name);
        sb.AppendLine("性格：" + config.persona.style);
        sb.AppendLine("背景：" + config.persona.background);
        sb.AppendLine("语言：" + config.persona.language);
        sb.AppendLine("规则：");

        if (config.persona.rules != null)
        {
            foreach (string rule in config.persona.rules)
            {
                if (!string.IsNullOrWhiteSpace(rule))
                    sb.AppendLine("- " + rule);
            }
        }

        sb.AppendLine("- 回复不要超过 " + config.persona.maxReplyChars + " 个中文字符。");
        sb.AppendLine("- 不要输出思考过程。");
        sb.AppendLine("- 不要输出 <think>、</think>、推理、分析、解释。");
        sb.AppendLine("- 只输出最终要显示在气泡里的台词。");
        sb.AppendLine("- 不要换行。");
        sb.AppendLine("- 不要使用“我需要分析”“让我想想”等句式。");

        return sb.ToString();
    }

    private static string BuildUserPrompt(
    WindowRoastConfig config,
    string sanitizedTitle,
    string currentCategory,
    string currentMood,
    bool categoryKnown,
    WindowRoastActivityTracker.Summary activitySummary
)
    {
        string category = string.IsNullOrWhiteSpace(currentCategory) ? "unknown" : currentCategory;
        string mood = string.IsNullOrWhiteSpace(currentMood) ? "unknown" : currentMood;

        string mode = config != null && config.llm != null
            ? config.llm.unknownTitleMode
            : "joke_or_fact";

        string activityText = BuildActivityPromptText(activitySummary);

        if (categoryKnown)
        {
            return
                "当前前台窗口标题已经脱敏如下：\n" +
                sanitizedTitle + "\n\n" +
                "窗口类别：" + category + "\n" +
                "窗口倾向：" + mood + "\n" +
                activityText + "\n\n" +
                "请根据当前窗口类别、窗口标题和最近活动，直接输出一句桌面宠物气泡台词。\n" +
                "如果类别是 productive 或 coding，请偏向安静鼓励、轻吐槽或休息提醒。\n" +
                "如果类别是 entertainment、video 或 game，请偏向温柔提醒、轻松玩笑或含蓄吐槽。\n" +
                "如果触发了长期活动规则，请优先遵循该规则意图。\n" +
                "格式：台词：一句话\n" +
                "禁止输出思考过程，禁止输出 <think>，禁止解释，禁止换行。";
        }

        return
            "当前前台窗口标题已经脱敏如下：\n" +
            sanitizedTitle + "\n\n" +
            "这个窗口没有命中预设类别。\n" +
            activityText + "\n\n" +
            "请根据标题中的应用名、主题或可推测场景，输出一句相关的桌面宠物气泡台词。\n" +
            "优先生成一个轻松的小笑话、冷知识、温柔提醒或好奇吐槽。\n" +
            "如果最近活动显示用户长时间工作，请偏向鼓励或休息提醒。\n" +
            "如果最近活动显示用户在娱乐，请偏向温柔提醒。\n" +
            "模式：" + mode + "\n" +
            "格式：台词：一句话\n" +
            "禁止输出默认句，禁止说“这个窗口很可疑”，禁止解释，禁止思考过程，禁止换行。";
    }

    private static string BuildActivityPromptText(WindowRoastActivityTracker.Summary summary)
    {
        if (summary == null || summary.totalCount <= 0)
            return "最近活动：暂无足够记录。";

        StringBuilder sb = new StringBuilder();

        sb.Append("最近活动：");

        bool first = true;
        if (summary.categoryCounts != null)
        {
            foreach (var kv in summary.categoryCounts)
            {
                if (!first)
                    sb.Append("，");

                sb.Append(kv.Key);
                sb.Append("=");
                sb.Append(kv.Value);
                sb.Append("次");

                first = false;
            }
        }

        sb.Append("。当前类别：");
        sb.Append(string.IsNullOrWhiteSpace(summary.currentCategory) ? "unknown" : summary.currentCategory);

        if (!string.IsNullOrWhiteSpace(summary.currentMood))
        {
            sb.Append("，当前倾向：");
            sb.Append(summary.currentMood);
        }

        if (summary.hasTriggeredRule)
        {
            sb.Append("。触发长期行为规则：");
            sb.Append(summary.triggeredRuleName);
            sb.Append("，意图：");
            sb.Append(summary.triggeredIntent);
        }

        sb.Append("。");

        return sb.ToString();
    }

    private static string ParseLlmResponse(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        try
        {
            ChatCompletionResponse response = JsonUtility.FromJson<ChatCompletionResponse>(responseText);

            if (response != null &&
                response.choices != null &&
                response.choices.Length > 0 &&
                response.choices[0] != null &&
                response.choices[0].message != null)
            {
                return response.choices[0].message.content;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WindowRoast] Failed to parse LLM response: " + e);
        }

        Debug.LogWarning("[WindowRoast] Empty or unsupported LLM response: " + responseText);
        return null;
    }

    private static string CleanModelOutput(string content, WindowRoastConfig config)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        string raw = content;
        string result = content.Trim();

        if (config != null && config.llm != null && config.llm.stripReasoning)
        {
            result = RemoveThinkBlocks(result, config);

            if (string.IsNullOrWhiteSpace(result))
                return null;

            result = ExtractAfterFinalAnswerMarker(result, config);

            if (LooksLikeUnfinishedReasoning(result, config))
            {
                Debug.LogWarning("[WindowRoast] LLM output looks like unfinished reasoning. Fallback will be used. Raw=" + raw);
                return null;
            }
        }

        string[] lines = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0)
            result = lines[lines.Length - 1].Trim();

        result = result.Trim('"', '“', '”', '\'', '‘', '’', ' ', '\t');

        if (string.IsNullOrWhiteSpace(result))
            return null;

        return result;
    }

    private static string RemoveThinkBlocks(string input, WindowRoastConfig config)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string result = input;

        int guard = 0;
        while (guard++ < 8)
        {
            int start = result.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                break;

            int end = result.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);

            if (end >= 0 && end > start)
            {
                result = result.Remove(start, end + "</think>".Length - start).Trim();
            }
            else
            {
                if (config != null && config.llm != null && config.llm.rejectUnfinishedReasoning)
                    return null;

                result = result.Replace("<think>", "", StringComparison.OrdinalIgnoreCase).Trim();
                break;
            }
        }

        result = result.Replace("<think>", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("</think>", "", StringComparison.OrdinalIgnoreCase)
                       .Trim();

        return result;
    }

    private static bool LooksLikeUnfinishedReasoning(string text, WindowRoastConfig config)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        string trimmed = text.TrimStart();
        string lower = trimmed.ToLowerInvariant();

        if (lower.Contains("<think>") || lower.Contains("</think>"))
            return true;

        if (config != null && config.llm != null && config.llm.reasoningStartMarkers != null)
        {
            foreach (string marker in config.llm.reasoningStartMarkers)
            {
                if (string.IsNullOrWhiteSpace(marker))
                    continue;

                if (trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        string[] badStarts =
        {
            "好的，",
            "好，",
            "首先",
            "我需要",
            "让我",
            "我们来",
            "根据窗口标题",
            "用户当前",
            "这个窗口标题",
            "从标题来看",
            "分析"
        };

        foreach (string s in badStarts)
        {
            if (trimmed.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string ExtractAfterFinalAnswerMarker(string text, WindowRoastConfig config)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        if (config == null || config.llm == null || config.llm.finalAnswerMarkers == null)
            return text;

        foreach (string marker in config.llm.finalAnswerMarkers)
        {
            if (string.IsNullOrWhiteSpace(marker))
                continue;

            int idx = text.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return text.Substring(idx + marker.Length).Trim();
        }

        return text;
    }

    private static string GetSeasonName(int month)
    {
        if (month >= 3 && month <= 5)
            return "春";
        if (month >= 6 && month <= 8)
            return "夏";
        if (month >= 9 && month <= 11)
            return "秋";

        return "冬";
    }
}