using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text;

// ============================================
// 1. 数据类定义（必须与服务器 JSON 严格匹配）
// ============================================

/// <summary>
/// 单个选项数据
/// </summary>
[System.Serializable]
public class OptionData
{
    public string text; // 选项显示文字
}

/// <summary>
/// 服务器返回的事件数据
/// </summary>
[System.Serializable]
public class EventResponse
{
    public string description;   // 事件描述
    public OptionData[] options; // 三个选项
}

/// <summary>
/// 发送给服务器的请求体（携带层数和历史摘要）
/// </summary>
[System.Serializable]
public class RequestPayload
{
    public int layer;          // 当前层数
    public string[] history;   // 最近事件描述（用于上下文）
}

// ============================================
// 2. 主管理器（挂载在 Canvas 上）
// ============================================
public class DungeonManager : MonoBehaviour
{
    [Header("UI 组件绑定")]
    public Text descriptionText;   // 中央描述
    public Text hpText;            // 血量显示
    public Text layerText;         // 层数显示
    public Button optionButtonA;   // 选项按钮 A
    public Button optionButtonB;   // 选项按钮 B
    public Button optionButtonC;   // 选项按钮 C
    public GameObject restartButton; // 重新开始按钮（游戏结束显示）
    public GameObject retryButton;    // 【新增】重试按钮（网络失败时显示）

    [Header("游戏状态")]
    private int currentLayer = 1;          // 当前层数（1~10）
    private int currentHP = 20;            // 当前血量
    private const int MAX_HP = 20;         // 最大血量
    private const int MAX_LAYER = 10;      // 总层数
    private bool isGameOver = false;       // 是否游戏结束（胜利/失败）
    private bool isLoading = false;        // 【新增】是否正在请求数据（防止连点）

    // 上下文记忆（存储最近 N 层事件描述，用于 AI 生成连贯剧情）
    private List<string> historySummaries = new List<string>();
    private const int MAX_HISTORY_COUNT = 3; // 只记忆最近3层

    // Loading 动画协程引用
    private Coroutine loadingCoroutine = null;

    // ============================================
    // Unity 生命周期
    // ============================================
    void Start()
    {
        ResetGameState(); // 初始化或重置游戏状态
    }

    // ----- 初始化或重置状态（只重置变量，不额外请求）-----
    private void ResetGameState()
    {
        currentLayer = 1;
        currentHP = MAX_HP;
        isGameOver = false;
        isLoading = false;
        historySummaries.Clear();          // 清空历史记忆
        UpdateStatusUI();                  // 刷新血量和层数显示

        // 显示选项按钮，隐藏结束/重试按钮
        SetButtonsActive(true);
        if (restartButton != null) restartButton.SetActive(false);
        if (retryButton != null) retryButton.SetActive(false);

        // 清空所有按钮监听（防止事件叠加）
        ClearButtonListeners();

        // 开始请求第1层事件
        StartCoroutine(FetchEventFromServer(currentLayer));
    }

    // ----- 控制三个选项按钮的显示/隐藏 -----
    private void SetButtonsActive(bool isActive)
    {
        optionButtonA.gameObject.SetActive(isActive);
        optionButtonB.gameObject.SetActive(isActive);
        optionButtonC.gameObject.SetActive(isActive);
    }

    // ----- 【新增】控制选项按钮的可交互性（灰显/可点）-----
    private void SetButtonsInteractable(bool interactable)
    {
        optionButtonA.interactable = interactable;
        optionButtonB.interactable = interactable;
        optionButtonC.interactable = interactable;
    }

    // ----- 更新状态栏（HP 和 层数）-----
    private void UpdateStatusUI()
    {
        if (hpText != null)
            hpText.text = $"❤️ HP: {currentHP} / {MAX_HP}";
        if (layerText != null && !isGameOver)
            layerText.text = $"🏚️ 第 {currentLayer} 层";
        // 游戏结束时 layerText 会被覆盖为胜利/陨落文字
    }

    // ----- 清空所有按钮的旧监听（防止内存泄漏或重复触发）-----
    private void ClearButtonListeners()
    {
        optionButtonA.onClick.RemoveAllListeners();
        optionButtonB.onClick.RemoveAllListeners();
        optionButtonC.onClick.RemoveAllListeners();
    }

    // ============================================
    // 核心：网络请求协程（含 Loading 与 重试逻辑）
    // ============================================
    /// <summary>
    /// 向服务器请求当前层的事件数据
    /// </summary>
    /// <param name="layer">请求的层数</param>
    /// <param name="isRetry">是否为重试（仅用于日志扩展）</param>
    IEnumerator FetchEventFromServer(int layer, bool isRetry = false)
    {
        // 防止同时发起多个请求
        if (isLoading) yield break;
        isLoading = true;
        isGameOver = false; // 如果有错误状态，先重置

        // 1. UI 状态：禁用选项按钮（防止点击），隐藏重试按钮，启动 Loading 动画
        SetButtonsInteractable(false);
        if (retryButton != null) retryButton.SetActive(false);

        // 启动 Loading 文字动画（转圈效果）
        if (loadingCoroutine != null) StopCoroutine(loadingCoroutine);
        loadingCoroutine = StartCoroutine(AnimateLoadingText());

        // 2. 构造请求体（包含层数和历史摘要）
        RequestPayload payload = new RequestPayload();
        payload.layer = layer;
        payload.history = historySummaries.ToArray(); // 转为数组
        string jsonBody = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        string url = "https://dungeon.seamus.icu/api/event";
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        // 3. 发送请求并等待响应
        yield return request.SendWebRequest();

        // 4. 停止 Loading 动画
        if (loadingCoroutine != null)
        {
            StopCoroutine(loadingCoroutine);
            loadingCoroutine = null;
        }

        // 5. 处理返回结果
        bool success = false;

        if (request.result == UnityWebRequest.Result.Success)
        {
            string rawJson = request.downloadHandler.text;
            Debug.Log($"服务器返回: {rawJson}");

            try
            {
                EventResponse eventData = JsonUtility.FromJson<EventResponse>(rawJson);
                // 校验数据完整性（至少三个选项）
                if (eventData != null && eventData.options != null && eventData.options.Length >= 3)
                {
                    // 成功：更新 UI 并写入历史
                    UpdateUIWithEvent(eventData);
                    success = true;
                }
                else
                {
                    descriptionText.text = "⚠️ 地牢信息扭曲（JSON格式异常），请重试。";
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"解析异常: {e.Message}");
                descriptionText.text = "⚠️ 解码地牢卷轴失败...";
            }
        }
        else
        {
            // 网络请求失败（超时、服务器错误等）
            Debug.LogError($"网络请求失败: {request.error} (状态码: {request.responseCode})");
            Debug.LogError($"服务器返回详情: {request.downloadHandler.text}");

            // 针对 500 错误给出友好提示
            if (request.responseCode == 500)
            {
                descriptionText.text = "🔥 地牢深处传来咆哮（服务器繁忙），点击下方按钮重试！";
            }
            else
            {
                descriptionText.text = $"⏰ 连接地牢超时（{request.error}），请重试。";
            }
        }

        request.Dispose(); // 释放资源
        isLoading = false;

        // 根据请求结果更新 UI 交互状态
        if (success)
        {
            // 成功：启用选项按钮，隐藏重试按钮
            SetButtonsInteractable(true);
            if (retryButton != null) retryButton.SetActive(false);
        }
        else
        {
            // 失败：禁用选项按钮（防止错误状态下的点击），显示重试按钮
            SetButtonsInteractable(false);
            if (retryButton != null) retryButton.SetActive(true);
        }
    }

    // ----- 【新增】Loading 文字动画（让 ⏳ 旋转）-----
    IEnumerator AnimateLoadingText()
    {
        string[] frames = { "⏳ 传送门开启中", "⏳ 传送门开启中.", "⏳ 传送门开启中..", "⏳ 传送门开启中..." };
        int index = 0;
        while (true)
        {
            descriptionText.text = frames[index % frames.Length];
            index++;
            yield return new WaitForSeconds(0.3f);
        }
    }

    // ----- 用 AI 数据刷新 UI（描述 + 三个按钮）-----
    private void UpdateUIWithEvent(EventResponse data)
    {
        // 1. 更新描述文字
        descriptionText.text = data.description;

        // 2. 绑定三个选项按钮的文字和点击事件
        BindButtonWithOption(optionButtonA, data.options[0].text, 0);
        BindButtonWithOption(optionButtonB, data.options[1].text, 1);
        BindButtonWithOption(optionButtonC, data.options[2].text, 2);

        // 3. 将当前事件描述存入历史记忆（FIFO，保留最近3条）
        historySummaries.Add(data.description);
        if (historySummaries.Count > MAX_HISTORY_COUNT)
        {
            historySummaries.RemoveAt(0);
        }
        Debug.Log($"当前历史记忆条数: {historySummaries.Count}");
    }

    // ----- 为单个按钮绑定文字和点击回调（闭包捕获索引）-----
    private void BindButtonWithOption(Button btn, string optionText, int optionIndex)
    {
        btn.onClick.RemoveAllListeners(); // 清空旧监听
        Text btnText = btn.GetComponentInChildren<Text>();
        if (btnText != null) btnText.text = optionText;

        int capturedIndex = optionIndex; // 捕获索引，避免闭包陷阱
        btn.onClick.AddListener(() => OnOptionSelected(capturedIndex));
    }

    // ============================================
    // 核心游戏循环（选项选择后的处理）
    // ============================================
    private void OnOptionSelected(int optionIndex)
    {
        // 游戏已结束或正在加载数据，拒绝操作
        if (isGameOver || isLoading) return;

        Debug.Log($"玩家选择了选项 {optionIndex}，当前层数 {currentLayer}");

        // 1. C# 计算扣血（AI 完全不知道这个数值）
        int damage = CalculateHPChange(optionIndex, currentLayer);
        currentHP -= damage;
        currentHP = Mathf.Clamp(currentHP, 0, MAX_HP);
        UpdateStatusUI();

        // 2. 判定是否死亡
        if (currentHP <= 0)
        {
            GameOver(false);
            return;
        }

        // 3. 判定是否通关（到达第10层且存活）
        if (currentLayer >= MAX_LAYER)
        {
            GameOver(true);
            return;
        }

        // 4. 均未触发，进入下一层
        currentLayer++;
        UpdateStatusUI(); // 刷新层数显示

        // 5. 请求下一层的新事件（自动禁用按钮并显示 Loading）
        StartCoroutine(FetchEventFromServer(currentLayer));
    }

    // ----- 核心算法：根据层数和选项计算扣血（保留原逻辑，仅数值微调）-----
    private int CalculateHPChange(int optionIndex, int currentLayer)
    {
        // 基础伤害：层数越高怪物越强（原为 1.2，现改为 1.5 略微提升难度）
        float baseDamage = currentLayer * 1.5f;
        // 选项系数：0-冒险，1-谨慎，2-取巧（原系数为 1.2,0.5,0.8，体现不同策略）
        float[] multipliers = { 1.2f, 0.5f, 0.8f };
        float rawDamage = baseDamage * multipliers[optionIndex];
        // 向上取整，保底至少 1 点伤害
        int finalDamage = Mathf.CeilToInt(rawDamage);
        finalDamage = Mathf.Max(1, finalDamage);
        // 加入随机抖动（±1）增加变化
        finalDamage += Random.Range(-1, 2);
        finalDamage = Mathf.Max(1, finalDamage);
        Debug.Log($"第{currentLayer}层 | 选项{optionIndex} | 扣血:{finalDamage}");
        return finalDamage;
    }

    // ----- 游戏结束（统一处理胜利/失败）-----
    private void GameOver(bool isWin)
    {
        isGameOver = true;
        SetButtonsInteractable(false);  // 禁用按钮点击
        SetButtonsActive(false);        // 隐藏选项按钮
        if (restartButton != null) restartButton.SetActive(true);
        if (retryButton != null) retryButton.SetActive(false); // 隐藏重试

        if (isWin)
        {
            descriptionText.text = "🎉 圣光笼罩着你！你成功穿越了10层地牢！";
            if (layerText != null) layerText.text = "🏆 胜利";
        }
        else
        {
            descriptionText.text = $"💀 你倒在了第 {currentLayer} 层... 黑暗吞噬了你。";
            if (layerText != null) layerText.text = "⚰️ 陨落";
        }
        Debug.Log(isWin ? "游戏胜利" : "游戏失败");
    }

    // ============================================
    // 外部调用接口（绑定给 UI 按钮）
    // ============================================

    // ----- 重新开始（绑定 RestartButton）-----
    public void ResetGame()
    {
        // 停止所有协程（包括 Loading 动画和请求）
        StopAllCoroutines();
        isLoading = false;
        ResetGameState(); // 重置所有状态并请求第1层
    }

    // ----- 【新增】重试当前层（绑定 RetryButton）-----
    // 特点：不改变层数、血量、历史，仅重新请求 AI 生成新事件
    public void RetryCurrentLayer()
    {
        if (isGameOver) return; // 已结束则不能重试（应点重启）
        if (isLoading) return;  // 已在加载中，防止重复

        Debug.Log($"手动重试第 {currentLayer} 层...");
        StartCoroutine(FetchEventFromServer(currentLayer, true));
    }
}