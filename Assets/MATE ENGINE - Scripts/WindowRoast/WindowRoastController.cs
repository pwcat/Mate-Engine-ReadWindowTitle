using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public class WindowRoastController : MonoBehaviour
{
    [NonSerialized] public Animator animator;
    [NonSerialized] public Camera targetCamera;

    [Header("General")]
    public bool enableWindowRoast = true;
    public bool enableRandomMood = true;

    [Header("External Config")]
    public string configFileName = "WindowRoastConfig.json";
    public bool reloadConfigWithF10 = true;

    [Header("Bubble")]
    public Vector3 headOffset = new Vector3(0f, 0.6f, 0f);
    public float bubbleScale = 0.0025f;
    public float showSeconds = 4f;

    [Header("Reaction")]
    public string reactionBool = "HoverFaceTrigger";
    public float reactionActiveSeconds = 1f;
    public float intervalSeconds = 8f;

    [Header("Cooldown")]
    public float sameTitleCooldownSeconds = 180f;

    [Header("Fallback Mood Lines")]
    [TextArea]
    public string[] lines =
    {
        "又在摸鱼啦？",
        "我在偷偷观察你。",
        "这个窗口很可疑哦。",
        "休息一下也可以啦。",
        "今天也要加油一点点。"
    };

    [Header("Roast Rules")]
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

    public string defaultRoast = "这个窗口很可疑哦。";

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    private string lastWindowTitle = "";
    private readonly Dictionary<string, float> titleLastRoastTime = new Dictionary<string, float>();

    private WindowRoastConfig config;
    private string configPath;

    private readonly WindowRoastActivityTracker activityTracker = new WindowRoastActivityTracker();

    private bool isLlmBusy = false;
    private float nextRandomMoodAllowedTime = 0f;

    [NonSerialized] private GameObject bubbleRoot;
    [NonSerialized] private CanvasGroup canvasGroup;
    [NonSerialized] private Text text;
    [NonSerialized] private Transform head;
    [NonSerialized] private Coroutine showRoutine;

    [NonSerialized] private RectTransform bubbleRect;
    [NonSerialized] private RectTransform bgRect;
    [NonSerialized] private RectTransform textRect;

    private IEnumerator Start()
    {
        EnsureDefaultRules();
        LoadExternalConfig();

        Debug.Log("[WindowRoast] Rules count: " + (roastRules == null ? 0 : roastRules.Length));

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (animator == null)
            animator = GetComponent<Animator>();

        float waitTime = 0f;

        while (animator == null && waitTime < 10f)
        {
            animator = FindFirstHumanAnimator();

            if (animator != null)
                break;

            waitTime += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }

        if (animator == null)
        {
            Debug.LogWarning("[WindowRoast] No human Animator found.");
            yield break;
        }

        Debug.Log("[WindowRoast] Animator found: " + animator.name);

        head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null)
        {
            Debug.LogWarning("[WindowRoast] Head bone not found, fallback to animator transform.");
            head = animator.transform;
        }

        CreateBubble();
        StartCoroutine(ReactionLoop());
    }

    private void Update()
    {
        if (!reloadConfigWithF10)
            return;

        if (Input.GetKeyDown(KeyCode.F10))
        {
            LoadExternalConfig();
            Debug.Log("[WindowRoast] Config reloaded by F10.");
        }
    }

    private void LateUpdate()
    {
        if (bubbleRoot == null || head == null)
            return;

        bubbleRoot.transform.position = head.position + headOffset;

        if (targetCamera != null)
        {
            Vector3 direction = bubbleRoot.transform.position - targetCamera.transform.position;
            bubbleRoot.transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    private IEnumerator ReactionLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(intervalSeconds);

            if (!enableWindowRoast && !enableRandomMood)
                continue;

            string title = GetActiveWindowTitle();

            if (string.IsNullOrWhiteSpace(title))
                continue;

            if (IsSensitiveTitle(title))
            {
                Debug.Log("[WindowRoast] Skip sensitive title: " + title);
                continue;
            }

            bool shouldSanitize = config == null || config.sanitizeTitleBeforeLlm;
            string sanitizedTitle = shouldSanitize ? SanitizeTitle(title) : title;

            bool useSanitizedForRules = config != null && config.useSanitizedTitleForRules;
            string titleForRules = useSanitizedForRules ? sanitizedTitle : title;

            WindowRoastActivityTracker.Summary activitySummary =
                activityTracker.RecordAndSummarize(config, sanitizedTitle);

            Debug.Log("[WindowRoast] Window title: " + title);
            Debug.Log("[WindowRoast] Sanitized title: " + sanitizedTitle);

            if (enableWindowRoast && ShouldRoastWindow(title))
            {
                lastWindowTitle = title;
                titleLastRoastTime[title] = Time.time;

                StartCoroutine(GenerateAndShowRoast(titleForRules, sanitizedTitle, activitySummary));
            }
            else
            {
                if (ShouldTriggerRandomMood())
                {
                    WindowRoastActivityTracker.Summary moodSummary =
                        activitySummary ?? activityTracker.SummarizeOnly(config);

                    StartCoroutine(GenerateAndShowMood(moodSummary));
                }
            }
        }
    }

    private IEnumerator GenerateAndShowRoast(
        string titleForRules,
        string sanitizedTitle,
        WindowRoastActivityTracker.Summary activitySummary
    )
    {
        string line = null;
        string requestTitle = sanitizedTitle;

        bool matchedRule = TryGetMatchedRuleName(titleForRules, out string matchedRuleName);

        string currentCategory = "unknown";
        string currentMood = "unknown";

        if (activitySummary != null)
        {
            if (!string.IsNullOrWhiteSpace(activitySummary.currentCategory))
                currentCategory = activitySummary.currentCategory;

            if (!string.IsNullOrWhiteSpace(activitySummary.currentMood))
                currentMood = activitySummary.currentMood;
        }

        if (currentCategory == "unknown" && matchedRule && !string.IsNullOrWhiteSpace(matchedRuleName))
            currentCategory = matchedRuleName;

        bool categoryKnown =
            !string.IsNullOrWhiteSpace(currentCategory) &&
            !string.Equals(currentCategory, "unknown", StringComparison.OrdinalIgnoreCase);

        bool canUseLlm =
            config != null &&
            config.llm != null &&
            config.llm.enabled &&
            config.llm.sendTitleToCloud &&
            !string.IsNullOrWhiteSpace(config.llm.apiBaseUrl) &&
            !string.IsNullOrWhiteSpace(config.llm.model);

        bool shouldUseLlm =
            canUseLlm &&
            (
                (categoryKnown && config.llm.useLlmForKnownTitles) ||
                (!categoryKnown && config.llm.useLlmForUnknownTitles)
            );

        Debug.Log(
            "[WindowRoast] LLM route: " +
            "canUseLlm=" + canUseLlm +
            ", matchedRule=" + matchedRule +
            ", matchedRuleName=" + matchedRuleName +
            ", currentCategory=" + currentCategory +
            ", currentMood=" + currentMood +
            ", categoryKnown=" + categoryKnown +
            ", useKnown=" + (config != null && config.llm != null && config.llm.useLlmForKnownTitles) +
            ", useUnknown=" + (config != null && config.llm != null && config.llm.useLlmForUnknownTitles) +
            ", shouldUseLlm=" + shouldUseLlm
        );

        if (shouldUseLlm && !isLlmBusy)
        {
            isLlmBusy = true;

            yield return WindowRoastLlmClient.RequestRoast(
                config,
                sanitizedTitle,
                currentCategory,
                currentMood,
                categoryKnown,
                activitySummary,
                result =>
                {
                    line = result;
                }
            );

            isLlmBusy = false;

            if (!string.IsNullOrWhiteSpace(line))
            {
                string currentTitle = SanitizeTitle(GetActiveWindowTitle());

                if (requestTitle != currentTitle)
                {
                    Debug.Log("[WindowRoast] Drop stale LLM response. requestTitle=" + requestTitle + ", currentTitle=" + currentTitle);
                    yield break;
                }
            }
        }
        else if (shouldUseLlm && isLlmBusy)
        {
            Debug.Log("[WindowRoast] LLM busy, using local fallback.");
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            line = matchedRule
                ? GenerateRoast(titleForRules)
                : GenerateUnknownFallbackRoast(sanitizedTitle);

            Debug.Log("[WindowRoast] Fallback roast: " + line);
        }
        else
        {
            line = ClampBubbleText(line.Trim());
            Debug.Log("[WindowRoast] LLM roast: " + line);
        }

        Show(line);
        StartCoroutine(TriggerReaction());
    }

    private IEnumerator GenerateAndShowMood(WindowRoastActivityTracker.Summary activitySummary)
    {
        string line = null;

        bool canUseLlm =
            config != null &&
            config.llm != null &&
            config.llm.enabled &&
            config.llm.sendTitleToCloud &&
            config.llm.useLlmForRandomMood &&
            !string.IsNullOrWhiteSpace(config.llm.apiBaseUrl) &&
            !string.IsNullOrWhiteSpace(config.llm.model);

        bool shouldUseLlm =
            canUseLlm &&
            Random.value <= Mathf.Clamp01(config.llm.randomMoodLlmChance);

        if (shouldUseLlm && !isLlmBusy)
        {
            isLlmBusy = true;

            yield return WindowRoastLlmClient.RequestMood(
                config,
                activitySummary,
                result =>
                {
                    line = result;
                }
            );

            isLlmBusy = false;
        }
        else if (shouldUseLlm && isLlmBusy)
        {
            Debug.Log("[WindowRoast] LLM busy, using local mood fallback.");
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            if (lines != null && lines.Length > 0)
                line = lines[Random.Range(0, lines.Length)];
            else
                line = "潮声还在，我也还在。";

            Debug.Log("[WindowRoast] Mood fallback: " + line);
        }
        else
        {
            line = ClampBubbleText(line.Trim());
            Debug.Log("[WindowRoast] LLM mood: " + line);
        }

        Show(line);
        StartCoroutine(TriggerReaction());
    }

    private bool ShouldTriggerRandomMood()
    {
        if (!enableRandomMood)
            return false;

        if (Time.time < nextRandomMoodAllowedTime)
            return false;

        float chance = config != null ? Mathf.Clamp01(config.randomMoodChance) : 0.25f;

        if (Random.value > chance)
            return false;

        float cooldown = config != null ? Mathf.Max(10f, config.randomMoodCooldownSeconds) : 300f;
        nextRandomMoodAllowedTime = Time.time + cooldown;

        return true;
    }

    private IEnumerator TriggerReaction()
    {
        if (animator == null)
            yield break;

        if (!HasParameter(animator, reactionBool))
        {
            Debug.LogWarning("[WindowRoast] Animator parameter not found: " + reactionBool);
            yield break;
        }

        Debug.Log("[WindowRoast] Trigger reaction: " + reactionBool);

        animator.SetBool(reactionBool, true);
        yield return new WaitForSeconds(reactionActiveSeconds);
        animator.SetBool(reactionBool, false);
    }

    public void Show(string msg)
    {
        if (bubbleRoot == null || text == null)
            return;

        text.text = ClampBubbleText(msg);
        ResizeBubbleToText();

        bubbleRoot.SetActive(true);

        if (showRoutine != null)
            StopCoroutine(showRoutine);

        showRoutine = StartCoroutine(ShowBubbleRoutine());
    }

    private string ClampBubbleText(string msg)
    {
        if (string.IsNullOrEmpty(msg))
            return "";

        int maxChars = config != null ? Mathf.Max(8, config.maxBubbleChars) : 38;

        if (msg.Length <= maxChars)
            return msg;

        return msg.Substring(0, maxChars) + "…";
    }

    private void ResizeBubbleToText()
    {
        if (text == null || bgRect == null || textRect == null)
            return;

        float maxWidth = config != null ? config.bubbleMaxWidth : 360f;
        float minWidth = config != null ? config.bubbleMinWidth : 180f;
        float minHeight = config != null ? config.bubbleMinHeight : 70f;
        float paddingX = config != null ? config.bubblePaddingX : 28f;
        float paddingY = config != null ? config.bubblePaddingY : 18f;

        int fontSize = config != null ? Mathf.Max(12, config.bubbleFontSize) : 24;
        text.fontSize = fontSize;

        textRect.sizeDelta = new Vector2(maxWidth - paddingX * 2f, 999f);

        float preferredWidth = Mathf.Clamp(text.preferredWidth + paddingX * 2f, minWidth, maxWidth);

        textRect.sizeDelta = new Vector2(preferredWidth - paddingX * 2f, 999f);

        float preferredHeight = text.preferredHeight + paddingY * 2f;
        preferredHeight = Mathf.Max(minHeight, preferredHeight);

        bgRect.sizeDelta = new Vector2(preferredWidth, preferredHeight);
        textRect.sizeDelta = new Vector2(preferredWidth - paddingX * 2f, preferredHeight - paddingY * 2f);

        if (bubbleRect != null)
            bubbleRect.sizeDelta = bgRect.sizeDelta;
    }

    private IEnumerator ShowBubbleRoutine()
    {
        canvasGroup.alpha = 1f;

        yield return new WaitForSeconds(showSeconds);

        float t = 0f;
        while (t < 0.35f)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, t / 0.35f);
            yield return null;
        }

        bubbleRoot.SetActive(false);
    }

    private void CreateBubble()
    {
        bubbleRoot = new GameObject("WindowRoastBubble");
        bubbleRoot.transform.localScale = Vector3.one * bubbleScale;

        Canvas canvas = bubbleRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        bubbleRect = bubbleRoot.GetComponent<RectTransform>();
        bubbleRect.sizeDelta = new Vector2(260, 80);

        canvasGroup = bubbleRoot.AddComponent<CanvasGroup>();

        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(bubbleRoot.transform, false);

        Image image = bg.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.85f);

        bgRect = bg.GetComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(260, 80);
        bgRect.anchoredPosition = Vector2.zero;

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(bubbleRoot.transform, false);

        text = textObj.AddComponent<Text>();
        text.text = "";
        text.font = Font.CreateDynamicFontFromOSFont("Arial", 24);
        text.fontSize = config != null ? Mathf.Max(12, config.bubbleFontSize) : 24;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.black;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        textRect = textObj.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(230, 60);
        textRect.anchoredPosition = Vector2.zero;

        bubbleRoot.SetActive(false);

        Debug.Log("[WindowRoast] Bubble created.");
    }

    private Animator FindFirstHumanAnimator()
    {
        Animator[] animators = FindObjectsOfType<Animator>();

        foreach (Animator a in animators)
        {
            if (a == null)
                continue;

            if (a.avatar == null)
                continue;

            if (!a.avatar.isHuman)
                continue;

            return a;
        }

        return null;
    }

    private bool HasParameter(Animator a, string parameterName)
    {
        if (a == null || string.IsNullOrWhiteSpace(parameterName))
            return false;

        foreach (AnimatorControllerParameter p in a.parameters)
        {
            if (p.name == parameterName)
                return true;
        }

        return false;
    }

    private string GetActiveWindowTitle()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        IntPtr handle = GetForegroundWindow();
        StringBuilder sb = new StringBuilder(512);
        GetWindowText(handle, sb, sb.Capacity);
        return sb.ToString();
#else
        return string.Empty;
#endif
    }

    private string SanitizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        string result = title;

        string emailToken = config != null ? config.redactedEmail : "[EMAIL]";
        string urlToken = config != null ? config.redactedUrl : "[URL]";
        string pathToken = config != null ? config.redactedPath : "[PATH]";
        string fileToken = config != null ? config.redactedFile : "[FILE]";
        string numberToken = config != null ? config.redactedNumber : "[NUMBER]";
        string bracketToken = config != null ? config.redactedBracketContent : "[PRIVATE]";

        result = Regex.Replace(
            result,
            @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}",
            emailToken,
            RegexOptions.IgnoreCase
        );

        result = Regex.Replace(
            result,
            @"https?://[^\s]+",
            urlToken,
            RegexOptions.IgnoreCase
        );

        result = Regex.Replace(
            result,
            @"www\.[^\s]+",
            urlToken,
            RegexOptions.IgnoreCase
        );

        result = Regex.Replace(
            result,
            @"[A-Za-z]:\\[^\s]+",
            pathToken
        );

        result = Regex.Replace(
            result,
            @"/Users/[^\s]+|/home/[^\s]+",
            pathToken,
            RegexOptions.IgnoreCase
        );

        result = Regex.Replace(
            result,
            @"[\w\-. ]+\.(cs|js|ts|tsx|jsx|py|java|cpp|c|h|hpp|go|rs|json|yaml|yml|md|txt|docx?|xlsx?|pptx?|pdf|png|jpg|jpeg|webp|psd|blend|unity|prefab|asset)",
            fileToken,
            RegexOptions.IgnoreCase
        );

        result = Regex.Replace(
            result,
            @"\b\d{5,}\b",
            numberToken
        );

        result = Regex.Replace(
            result,
            @"\[[^\]]{3,}\]",
            bracketToken
        );

        result = Regex.Replace(result, @"\s{2,}", " ").Trim();

        return result;
    }

    private bool ShouldRoastWindow(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        if (title == lastWindowTitle)
            return false;

        if (titleLastRoastTime.TryGetValue(title, out float lastTime))
        {
            if (Time.time - lastTime < sameTitleCooldownSeconds)
            {
                Debug.Log("[WindowRoast] Skip cooldown title: " + title);
                return false;
            }
        }

        return true;
    }

    private bool TryGetMatchedRuleName(string title, out string matchedRuleName)
    {
        matchedRuleName = "";

        if (string.IsNullOrWhiteSpace(title) || roastRules == null)
            return false;

        string lower = title.ToLowerInvariant();

        foreach (WindowRoastRule rule in roastRules)
        {
            if (rule == null || rule.keywords == null)
                continue;

            foreach (string keyword in rule.keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                if (lower.Contains(keyword.ToLowerInvariant()))
                {
                    matchedRuleName = rule.name;
                    return true;
                }
            }
        }

        return false;
    }

    private string GenerateRoast(string title)
    {
        string lower = title.ToLowerInvariant();

        if (roastRules != null)
        {
            foreach (WindowRoastRule rule in roastRules)
            {
                if (rule == null || rule.keywords == null || rule.responses == null)
                    continue;

                foreach (string keyword in rule.keywords)
                {
                    if (string.IsNullOrWhiteSpace(keyword))
                        continue;

                    if (lower.Contains(keyword.ToLowerInvariant()))
                    {
                        Debug.Log("[WindowRoast] Matched rule: " + rule.name + " keyword: " + keyword);

                        if (rule.responses.Length > 0)
                            return rule.responses[Random.Range(0, rule.responses.Length)];
                    }
                }
            }
        }

        return defaultRoast;
    }

    private string GenerateUnknownFallbackRoast(string sanitizedTitle)
    {
        string[] unknownLines =
        {
            "这个窗口看起来有点神秘。",
            "我还没学会吐槽这个窗口。",
            "这是什么新鲜玩意儿？",
            "未知领域，正在围观。",
            "这个窗口有点意思。"
        };

        return unknownLines[Random.Range(0, unknownLines.Length)];
    }

    private bool IsSensitiveTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        string lower = title.ToLowerInvariant();

        if (config != null && config.sensitiveKeywords != null)
        {
            foreach (string keyword in config.sensitiveKeywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                if (lower.Contains(keyword.ToLowerInvariant()))
                    return true;
            }

            return false;
        }

        return lower.Contains("password")
            || lower.Contains("login")
            || lower.Contains("bank")
            || lower.Contains("paypal")
            || lower.Contains("gmail")
            || lower.Contains("mail")
            || lower.Contains("邮箱")
            || lower.Contains("银行")
            || lower.Contains("支付")
            || lower.Contains("密码");
    }

    private void LoadExternalConfig()
    {
        configPath = Path.Combine(Application.persistentDataPath, configFileName);

        if (!File.Exists(configPath))
        {
            config = new WindowRoastConfig();

            string defaultJson = JsonUtility.ToJson(config, true);
            File.WriteAllText(configPath, defaultJson, Encoding.UTF8);

            Debug.Log("[WindowRoast] Config file created: " + configPath);
        }
        else
        {
            try
            {
                string json = File.ReadAllText(configPath, Encoding.UTF8);
                config = JsonUtility.FromJson<WindowRoastConfig>(json);

                if (config == null)
                {
                    Debug.LogWarning("[WindowRoast] Config parse returned null. Using defaults.");
                    config = new WindowRoastConfig();
                }

                Debug.Log("[WindowRoast] Config loaded: " + configPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WindowRoast] Failed to read config. Using defaults. " + e);
                config = new WindowRoastConfig();
            }
        }

        ApplyConfig(config);
    }

    private void ApplyConfig(WindowRoastConfig c)
    {
        if (c == null)
            return;

        enableWindowRoast = c.enableWindowRoast;
        enableRandomMood = c.enableRandomMood;

        intervalSeconds = Mathf.Max(1f, c.intervalSeconds);
        sameTitleCooldownSeconds = Mathf.Max(0f, c.sameTitleCooldownSeconds);

        bubbleScale = Mathf.Max(0.0001f, c.bubbleScale);
        showSeconds = Mathf.Max(0.5f, c.showSeconds);

        if (text != null)
            text.fontSize = Mathf.Max(12, c.bubbleFontSize);

        if (bubbleRoot != null)
            bubbleRoot.transform.localScale = Vector3.one * bubbleScale;

        headOffset = new Vector3(headOffset.x, c.bubbleHeight, headOffset.z);

        reactionBool = string.IsNullOrWhiteSpace(c.reactionBool) ? reactionBool : c.reactionBool;
        reactionActiveSeconds = Mathf.Max(0.1f, c.reactionActiveSeconds);

        defaultRoast = string.IsNullOrWhiteSpace(c.defaultRoast) ? defaultRoast : c.defaultRoast;

        if (c.moodLines != null && c.moodLines.Length > 0)
            lines = c.moodLines;

        if (c.roastRules != null && c.roastRules.Length > 0)
            roastRules = c.roastRules;

        Debug.Log(
            "[WindowRoast] Config applied. " +
            "enableWindowRoast=" + enableWindowRoast +
            ", enableRandomMood=" + enableRandomMood +
            ", intervalSeconds=" + intervalSeconds +
            ", bubbleHeight=" + headOffset.y +
            ", cooldown=" + sameTitleCooldownSeconds +
            ", reactionBool=" + reactionBool
        );
    }

    private void EnsureDefaultRules()
    {
        if (roastRules != null && roastRules.Length > 0)
            return;

        roastRules = new WindowRoastRule[]
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
    }
}