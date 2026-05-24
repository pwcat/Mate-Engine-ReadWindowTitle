using System;

[Serializable]
public class WindowRoastConfig
{
    public bool enableWindowRoast = true;
    public bool enableRandomMood = true;

    public float intervalSeconds = 8f;
    public float sameTitleCooldownSeconds = 180f;
    public float bubbleHeight = 0.6f;
    public float bubbleScale = 0.0025f;
    public float showSeconds = 4f;

    public int bubbleFontSize = 24;
    public float bubbleMinWidth = 180f;
    public float bubbleMaxWidth = 360f;
    public float bubbleMinHeight = 70f;
    public float bubblePaddingX = 28f;
    public float bubblePaddingY = 18f;
    public int maxBubbleChars = 38;


    public string reactionBool = "HoverFaceTrigger";
    public float reactionActiveSeconds = 1f;

    public string defaultRoast = "这个窗口很可疑哦。";

    public string[] moodLines =
    {
        "我在偷偷观察你。",
        "这个窗口很可疑哦。",
        "今天也要加油一点点。"
    };

    public float randomMoodCooldownSeconds = 300f;
    public float randomMoodChance = 0.35f;

    public string[] sensitiveKeywords =
    {
        "password",
        "login",
        "bank",
        "paypal",
        "gmail",
        "mail",
        "邮箱",
        "银行",
        "支付",
        "密码"
    };

    public bool sanitizeTitleBeforeLlm = true;
    public bool useSanitizedTitleForRules = false;

    public string redactedEmail = "[EMAIL]";
    public string redactedUrl = "[URL]";
    public string redactedPath = "[PATH]";
    public string redactedFile = "[FILE]";
    public string redactedNumber = "[NUMBER]";
    public string redactedBracketContent = "[PRIVATE]";

    public WindowRoastMoodPromptOption[] moodPromptOptions =
{
    new WindowRoastMoodPromptOption
    {
        name = "seasonal_poem",
        weight = 35f,
        instruction = "根据当前季节、时间，生成一句带有古诗词意象的短句。可以引用公版古诗词片段，也可以原创。语气温柔克制。"
    },
    new WindowRoastMoodPromptOption
    {
        name = "gentle_idle",
        weight = 25f,
        instruction = "生成一句温柔的闲聊、陪伴、轻微吐槽或提醒，不需要引用任何典故。"
    }
};

    public WindowRoastRule[] roastRules =
    {
        new WindowRoastRule
        {
            name = "Coding",
            keywords = new[] { "visual studio", "vs code", "visual studio code", ".cs", ".js", ".py", "rider", "github" },
            responses = new[]
            {
                "又在和 bug 约会啦？",
                "代码不跑，你先别跑。",
                "这个 bug 看起来很爱你。"
            }
        },
        new WindowRoastRule
        {
            name = "Unity",
            keywords = new[] { "unity" },
            responses = new[]
            {
                "你又在驯服引擎了。",
                "Unity 今天也很有个性。",
                "场景没炸就是胜利。"
            }
        },
        new WindowRoastRule
        {
            name = "Video",
            keywords = new[] { "youtube", "bilibili", "哔哩哔哩" },
            responses = new[]
            {
                "学习资料真好看呢～",
                "这个视频会自己学完吗？",
                "再看五分钟就努力，对吧？"
            }
        },
        new WindowRoastRule
        {
            name = "Game",
            keywords = new[] { "steam", "epic games" },
            responses = new[]
            {
                "生产力悄悄下线了。",
                "游戏启动，努力暂停。",
                "今天的任务是快乐。"
            }
        },
        new WindowRoastRule
        {
            name = "Office",
            keywords = new[] { "excel", "word", "powerpoint", "wps" },
            responses = new[]
            {
                "表格怪兽又出现了。",
                "文档正在凝视你。",
                "办公气息突然浓了。"
            }
        }
    };

    public WindowRoastLlmConfig llm = new WindowRoastLlmConfig();
    public WindowRoastPersonaConfig persona = new WindowRoastPersonaConfig();
    public WindowRoastActivityConfig activity = new WindowRoastActivityConfig();
}

[Serializable]
public class WindowRoastLlmConfig
{
    public bool enabled = false;

    // OpenAI-compatible endpoint.
    // DeepSeek example: https://api.deepseek.com
    // Ollama example: http://localhost:11434/v1
    public string apiBaseUrl = "https://api.deepseek.com";

    // Keep this local only. Do not commit your real key.
    // Ollama can use "ollama" or empty string.
    public string apiKey = "";

    public string model = "deepseek-v4-flash";
    public float timeoutSeconds = 12f;
    public int maxTokens = 80;
    public float temperature = 0.8f;
    public bool disableThinking = true;

    // Privacy guard. If false, never send window title to model endpoint.
    public bool sendTitleToCloud = false;

    // If API fails or output is unusable, use local rule-based response.
    public bool fallbackToRules = true;

    // LLM routing.
    public bool useLlmForKnownTitles = true;
    public bool useLlmForUnknownTitles = true;
    public string unknownTitleMode = "joke_or_fact";

    // Reasoning-model output cleanup.
    public bool stripReasoning = true;
    public bool rejectUnfinishedReasoning = true;

    // Debug logging.
    public bool debugLogRawResponse = false;
    public bool debugLogParsedContent = true;

    // Reserved for future debugging.
    public bool allowReasoningFallbackPreview = false;

    public bool useLlmForRandomMood = true;
    public float randomMoodLlmChance = 0.8f;

    public string[] reasoningStartMarkers =
    {
        "<think>",
        "思考：",
        "思考:",
        "推理：",
        "推理:",
        "分析：",
        "分析:",
        "我需要分析",
        "我需要先",
        "让我分析",
        "让我想想",
        "我们需要",
        "嗯，"
    };

    public string[] reasoningEndMarkers =
    {
        "</think>"
    };

    public string[] finalAnswerMarkers =
    {
        "最终回答：",
        "最终回答:",
        "回答：",
        "回答:",
        "台词：",
        "台词:",
        "气泡：",
        "气泡:",
        "最终台词：",
        "最终台词:"
    };
}



[Serializable]
public class WindowRoastPersonaConfig
{
    public string name = "桌面伙伴";
    public string style = "可爱、轻微毒舌、温柔、不吵闹";
    public string background = "你是陪伴用户工作的桌面虚拟角色。";
    public string language = "zh-CN";

    public int maxReplyChars = 32;

    public string[] rules =
    {
        "只输出一句话",
        "不要解释",
        "不要复述完整窗口标题",
        "不要泄露隐私信息",
        "不要攻击用户本人",
        "语气轻松可爱"
    };
}

[Serializable]
public class WindowRoastActivityConfig
{
    public bool enabled = false;

    // Future use: sliding-window behavior tracking.
    public float activityWindowMinutes = 45f;

    public WindowRoastActivityCategory[] categories =
    {
        new WindowRoastActivityCategory
        {
            name = "coding",
            keywords = new[] { "visual studio", "vs code", "visual studio code", ".cs", ".js", ".py", "rider", "github" },
            mood = "productive"
        },
        new WindowRoastActivityCategory
        {
            name = "video",
            keywords = new[] { "youtube", "bilibili", "哔哩哔哩", "netflix" },
            mood = "entertainment"
        },
        new WindowRoastActivityCategory
        {
            name = "game",
            keywords = new[] { "steam", "epic games", "wuthering waves", "鸣潮" },
            mood = "entertainment"
        },
        new WindowRoastActivityCategory
        {
            name = "office",
            keywords = new[] { "excel", "word", "powerpoint", "wps" },
            mood = "productive"
        }
    };

    public WindowRoastActivityRule[] rules =
    {
        new WindowRoastActivityRule
        {
            name = "codingEncourage",
            category = "coding",
            windowMinutes = 45f,
            minCount = 30,
            intent = "encourage",
            cooldownMinutes = 30f
        },
        new WindowRoastActivityRule
        {
            name = "longWorkBreak",
            category = "coding",
            windowMinutes = 90f,
            minCount = 50,
            intent = "rest_reminder",
            cooldownMinutes = 45f
        },
        new WindowRoastActivityRule
        {
            name = "entertainmentReminder",
            category = "game",
            windowMinutes = 30f,
            minCount = 10,
            intent = "gentle_reminder",
            cooldownMinutes = 30f
        }
    };
}

[Serializable]
public class WindowRoastActivityCategory
{
    public string name;
    public string[] keywords;
    public string mood;
}

[Serializable]
public class WindowRoastActivityRule
{
    public string name;
    public string category;
    public float windowMinutes;
    public int minCount;
    public string intent;
    public float cooldownMinutes;
}

[Serializable]
public class WindowRoastMoodPromptOption
{
    public string name;
    public float weight;
    public string instruction;
}