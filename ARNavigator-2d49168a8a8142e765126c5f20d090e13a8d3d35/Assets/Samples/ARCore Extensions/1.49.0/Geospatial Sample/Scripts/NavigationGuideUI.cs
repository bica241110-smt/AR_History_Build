using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NavigationGuideUI : MonoBehaviour
{
    public enum NavSignType
    {
        DiagonalLeft = -3, LeftTurn = -2, LeftDirection = -1,
        None = 0,
        RightDirection = 1, RightTurn = 2, DiagonalRight = 3,
        Destination = 4
    }

    [Serializable]
    public struct DistanceThresholds
    {
        public float Far;    // 800m
        public float Middle; // 400m
        public float Near;   // 200m
        public float Nearby; // 130m
    }

    [Header("Transparency Settings")]
    [SerializeField] private CanvasGroup[] fadeTargetPanels;

    [Header("Panel References")]
    [SerializeField] private GameObject guidePanel;
    [SerializeField] private GameObject mapPanel;
    [SerializeField] private GameObject debugPanel;
    [SerializeField] private GameObject ARviewPanel;

    [Header("UI Text References")]
    [SerializeField] private RouteProgressMonitor progressMonitor;
    [SerializeField] private TextMeshProUGUI targetText;
    [SerializeField] private TextMeshProUGUI distanceText;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI warnText;
    [SerializeField] private TextMeshProUGUI researchText;
    [SerializeField] private TextMeshProUGUI nextTurnText;

    [Header("Speed & Safety")]
    [SerializeField] private SpeedCalculator SpeedCalculator;
    [SerializeField] private float safetySpeedThreshold = 20.0f;
    [SerializeField] private float timeToFade = 3.0f;
    [SerializeField] private float fadedAlpha = 0.3f;
    [SerializeField] private float fadeSpeed = 5.0f;

    [Header("Navigation Thresholds")]
    [SerializeField]
    private DistanceThresholds thresholds = new DistanceThresholds
    {
        Far = 800f,
        Middle = 400f,
        Near = 200f,
        Nearby = 130f
    };

    [Header("Audio Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip infoSEClip;
    [SerializeField] private AudioClip FarCallClip;
    [SerializeField] private AudioClip MiddleCallClip;
    [SerializeField] private AudioClip nearCallClip;
    [SerializeField] private AudioClip leftTurnClip;
    [SerializeField] private AudioClip rightTurnClip;
    [SerializeField] private AudioClip goalClip;
    [SerializeField] private AudioClip negiraiClip;

    private int _lastVoiceInstructionIndex = -1;
    private float _lastDistanceCall = -1;
    private bool _isFinishGuidancePlaying = false;
    private Coroutine _currentGuidanceCoroutine;
    private float _lastPlayTime = 0f;
    private const float PlayCooldown = 2.0f;
    private float _highSpeedTimer = 0f;

    public bool isDebugPanelVisible { get; private set; } = false;

    private void Start()
    {
        if (debugPanel) debugPanel.SetActive(false);
        if (ARviewPanel) ARviewPanel.SetActive(false);
        UpdateTimeDisplay();
    }

    private void Update()
    {
        bool isNavigating = (progressMonitor != null && progressMonitor.IsNavigating);

        UpdatePanelVisibility(isNavigating);

        if (!isNavigating) return;

        HandleSafetyFade();
    }

    private void UpdatePanelVisibility(bool isNavigating)
    {
        if (mapPanel != null && mapPanel.activeSelf != isNavigating)
        {
            mapPanel.SetActive(isNavigating);
        }

        if (guidePanel != null && guidePanel.activeSelf != isNavigating)
        {
            guidePanel.SetActive(isNavigating);
            if (isNavigating && targetText != null)
            {
                targetText.text = $"目的地: {progressMonitor.DestinationName}";
            }
        }
    }

    private void HandleSafetyFade()
    {
        if (SpeedCalculator == null) return;

        if (SpeedCalculator.CurrentKmh > safetySpeedThreshold)
        {
            _highSpeedTimer += Time.deltaTime;
        }
        else
        {
            _highSpeedTimer = 0f;
        }

        float targetAlpha = 1.0f;
        bool isDangerSpeed = (_highSpeedTimer >= timeToFade);

        if (isDangerSpeed)
        {
            targetAlpha = fadedAlpha;
        }

        foreach (var panelGroup in fadeTargetPanels)
        {
            if (panelGroup == null) continue;
            panelGroup.alpha = Mathf.Lerp(panelGroup.alpha, targetAlpha, Time.deltaTime * fadeSpeed);
            panelGroup.blocksRaycasts = (panelGroup.alpha > 0.8f);
        }
    }

    public void UpdateAccuracyDisplay(bool isAccurate, bool isTracking)
    {
        if (warnText == null) return;

        if (isTracking && isAccurate)
        {
            warnText.gameObject.SetActive(false);
        }
        else
        {
            warnText.gameObject.SetActive(true);
            warnText.text = "<color=yellow>精度が不足しています。周囲を見回してください。</color>";
        }
    }

    public void UpdateReSearchingDisplay(bool isSearching)
    {
        if (researchText == null) return;

        researchText.gameObject.SetActive(isSearching);

        if (distanceText != null)
        {
            if (isSearching)
            {
                distanceText.alpha = 0.5f;
            }
            else
            {
                distanceText.alpha = 1.0f;
            }
        }
    }

    public void ToggleDebugPanels()
    {
        isDebugPanelVisible = !isDebugPanelVisible;
        if (debugPanel) debugPanel.SetActive(isDebugPanelVisible);
        if (ARviewPanel) ARviewPanel.SetActive(isDebugPanelVisible);
    }

    public void UpdateDistanceDisplay(float distanceInMeters)
    {
        if (distanceText == null) return;

        if (distanceInMeters >= 1000f)
        {
            float km = distanceInMeters / 1000f;
            distanceText.text = $"{km:F1} km";
        }
        else
        {
            distanceText.text = $"{Mathf.CeilToInt(distanceInMeters)} m";
        }
    }

    public void UpdateTimeDisplay()
    {
        if (timeText != null)
        {
            timeText.text = DateTime.Now.ToString("HH:mm");
        }
    }

    public void UpdateTurnInstruction(int signValue, float distance, int index)
    {
        if (nextTurnText != null)
        {
            if (distance > thresholds.Far)
            {
                nextTurnText.text = "";
            }
        }

        HandleVoiceGuidance(signValue, distance, index);
    }

    private void HandleVoiceGuidance(int signValue, float distance, int index)
    {
        if (audioSource == null) return;
        NavSignType sign = (NavSignType)signValue;

        if (index != _lastVoiceInstructionIndex)
        {
            _lastVoiceInstructionIndex = index;
            _lastDistanceCall = float.MaxValue;
        }

        if (distance <= thresholds.Far && _lastDistanceCall > thresholds.Far)
        {
            PlayGuidance(FarCallClip, signValue);
            _lastDistanceCall = thresholds.Far;
        }
        else if (distance <= thresholds.Middle && _lastDistanceCall > thresholds.Middle)
        {
            PlayGuidance(MiddleCallClip, signValue);
            _lastDistanceCall = thresholds.Middle;
        }
        else if (distance <= thresholds.Near && _lastDistanceCall > thresholds.Near)
        {
            if (sign != NavSignType.None && sign != NavSignType.Destination)
            {
                PlayGuidance(nearCallClip, signValue);
            }
            _lastDistanceCall = thresholds.Near;
        }
        else if (distance <= thresholds.Nearby && _lastDistanceCall > thresholds.Nearby)
        {
            if (sign == NavSignType.Destination)
            {
                PlayGuidance(goalClip);
            }
            else if (sign != NavSignType.None)
            {
                PlayGuidance(null, signValue);
            }
            _lastDistanceCall = thresholds.Nearby;
        }
    }

    private void PlayGuidance(AudioClip distanceClip, int sign = 0)
    {
        if (Time.time - _lastPlayTime < PlayCooldown) return;
        _lastPlayTime = Time.time;

        if (_currentGuidanceCoroutine != null)
        {
            StopCoroutine(_currentGuidanceCoroutine);
        }
        _currentGuidanceCoroutine = StartCoroutine(PlayGuidanceRoutine(distanceClip, sign));
    }

    private IEnumerator PlayGuidanceRoutine(AudioClip distanceClip, int sign)
    {
        audioSource.Stop();

        if (infoSEClip != null)
        {
            audioSource.PlayOneShot(infoSEClip);
            yield return new WaitForSeconds(infoSEClip.length);
        }

        if (distanceClip != null)
        {
            audioSource.PlayOneShot(distanceClip);
            yield return new WaitForSeconds(distanceClip.length);
        }

        AudioClip directionClip = null;
        if (sign < 0)
        {
            directionClip = leftTurnClip;
        }
        else if (sign > 0 && sign != 4)
        {
            directionClip = rightTurnClip;
        }

        if (directionClip != null)
        {
            audioSource.PlayOneShot(directionClip);
        }

        _currentGuidanceCoroutine = null;
    }

    public void PlayFinishGuidance()
    {
        if (audioSource != null && !_isFinishGuidancePlaying)
        {
            StartCoroutine(PlayFinishGuidanceRoutine());
        }
    }

    private IEnumerator PlayFinishGuidanceRoutine()
    {
        _isFinishGuidancePlaying = true;
        if (goalClip != null)
        {
            audioSource.PlayOneShot(goalClip);
            yield return new WaitForSeconds(goalClip.length);
        }
        yield return new WaitForSeconds(1.0f);
        if (negiraiClip != null)
        {
            audioSource.PlayOneShot(negiraiClip);
            yield return new WaitForSeconds(negiraiClip.length);
        }
        _isFinishGuidancePlaying = false;
    }
}

//using System;
//using System.Collections;
//using TMPro;
//using UnityEngine;
//using UnityEngine.UI;

//public class NavigationGuideUI : MonoBehaviour
//{
//    // --- 1. 方向を定義するEnum ---
//    public enum NavSignType
//    {
//        DiagonalLeft = -3,
//        LeftTurn = -2,
//        LeftDirection = -1,
//        None = 0,
//        RightDirection = 1,
//        RightTurn = 2,
//        DiagonalRight = 3,
//        Destination = 4
//    }

//    // --- 2. 距離の閾値を管理する設定 ---
//    [Serializable]
//    public struct DistanceThresholds
//    {
//        public float Far;    // 500m
//        public float Middle; // 300m
//        public float Near;   // 100m
//        public float Nearby;   // 100m
//        public float AudioBuffer; // 50m (音声再生判定の遊び)
//    }

//    [Header("Transparency Settings")]
//    [Tooltip("走行中に透明化したいパネルをここにすべて入れてください")]
//    [SerializeField] private CanvasGroup[] fadeTargetPanels;

//    [Header("Always Visible Panels")]
//    [SerializeField] private GameObject infoPanel;  // 常に表示
//    [SerializeField] private GameObject arrowPanel; // 常に表示

//    // 特定のロジックで個別に制御が必要なパネルは、参照を保持しておきます
//    [Header("Individual Panel References")]
//    [SerializeField] private GameObject guidePanel;
//    [SerializeField] private GameObject mapPanel;
//    [SerializeField] private GameObject debugPanel;
//    [SerializeField] private GameObject ARviewPanel;

//    [Header("References")]
//    [SerializeField] private RouteProgressMonitor progressMonitor; // 監視クラスへの参照
//    // [SerializeField] private RectTransform arrowIcon;              // 回転させる矢印画像 (UI)
//    [SerializeField] private TextMeshProUGUI targetText;      // 目的地表示テキスト (UI)
//    [SerializeField] private TextMeshProUGUI distanceText;    // 距離表示テキスト (UI)
//    [SerializeField] private TextMeshProUGUI timeText;   // 時刻表示テキスト (UI)
//    [SerializeField] private TextMeshProUGUI warnText;   // 警告表示テキスト (UI)
//    [SerializeField] private TextMeshProUGUI researchText; // 再検索メッセージ表示テキスト (UI)

//    [Header("Speed Display")]
//    [SerializeField] private SpeedCalculator SpeedCalculator; // 計算クラスへの参照
//    [SerializeField] private TextMeshProUGUI speedText;       // 速度を表示するUI

//    [Header("Safety Settings")]
//    [SerializeField] private float safetySpeedThreshold = 20.0f; // 閾値
//    [SerializeField] private float timeToFade = 3.0f;            // 持続時間
//    [SerializeField] private float fadedAlpha = 0.3f;            // 半透明時のアルファ
//    [SerializeField] private float fadeSpeed = 5.0f;             // フェード速度

//    [Header("Debug Settings")]
//    [SerializeField] private Button DebugButton; // デバッグパネル表示切替ボタン

//    [Header("Update Settings")]
//    [SerializeField] private float uiUpdateInterval = 0.5f; // 更新間隔
//    private float _uiUpdateTimer = 0f;

//    [Header("Navigation Settings")]
//    [SerializeField]
//    private DistanceThresholds thresholds = new DistanceThresholds
//    {
//        Far = 800f,
//        Middle = 400f,
//        Near = 200f,
//        Nearby = 130f,
//        AudioBuffer = 50f
//    };

//    [SerializeField] private TextMeshProUGUI nextTurnText; // "300m先 右方向"
//    [Header("Turn Icons")]
//    [SerializeField] private GameObject leftTurnArrow;  // 左曲がり用の画像
//    [SerializeField] private GameObject rightTurnArrow; // 右曲がり用の画像

//    [Header("Audio Settings")]
//    [SerializeField] private AudioSource audioSource;
//    [SerializeField] private AudioClip infoSEClip;   // ポーン
//    [SerializeField] private AudioClip FarCallClip;   // 500m先、
//    [SerializeField] private AudioClip MiddleCallClip;   // 300m先、
//    [SerializeField] private AudioClip nearCallClip;   // この先、
//    [SerializeField] private AudioClip leftTurnClip;   // 左方向です
//    [SerializeField] private AudioClip rightTurnClip;  // 右方向です
//    [SerializeField] private AudioClip goalClip; //目的地が近付いています
//    [SerializeField] private AudioClip negiraiClip; //ナビを終了します　おつかれさま

//    private int _lastVoiceInstructionIndex = -1; // 音声の重複再生防止用
//    private int _lastDistanceCall = -1; // 500, 300, 200 などの距離コール重複防止
//    private bool _isFinishGuidancePlaying = false;

//    private Coroutine _currentGuidanceCoroutine;
//    private float _lastPlayTime = 0f;
//    private const float PlayCooldown = 2.0f; // 2秒間は次の案内を流さない

//    public bool isDebugPanelVisible { get; private set; } = false;


//    private Camera _mainCamera;
//    private float _highSpeedTimer = 0f;

//    private void Start()
//    {
//        if (Camera.main != null)
//        {
//            _mainCamera = Camera.main;
//        }

//        // --- 追記：ボタンのクリックイベントを登録 ---
//        if (DebugButton != null)
//        {
//            DebugButton.onClick.AddListener(ToggleDebugPanels);
//            DebugButton.gameObject.SetActive(false);
//        }

//        if (arrowPanel != null)
//        {
//            arrowPanel.SetActive(false);
//        }

//        // 初期状態を反映（起動時は非表示にする場合）
//        SetDebugPanelsVisible(isDebugPanelVisible);
//        UpdateTimeDisplay();
//    }

//    private void Update()
//    {
//        bool isNavigating = (progressMonitor != null && progressMonitor.IsNavigating);
//        if (mapPanel != null && mapPanel.activeSelf != isNavigating)
//        {
//            mapPanel.SetActive(isNavigating);
//        }
//        //if (guidePanel != null && mapPanel != null && guidePanel.activeSelf != isNavigating && mapPanel.activeSelf != isNavigating)
//        //{
//        //    guidePanel.SetActive(isNavigating);
//        //    if (targetText != null) targetText.text = $"目的地: {progressMonitor.DestinationName}";
//        //}
//        if (guidePanel != null && guidePanel.activeSelf != isNavigating)
//        {
//            guidePanel.SetActive(isNavigating);
//            if (isNavigating && targetText != null)
//            {
//                targetText.text = $"目的地: {progressMonitor.DestinationName}";
//            }
//        }

//        if (!isNavigating) return;

//        // 2. 安全フェード処理（滑らかさのため毎フレーム）
//        HandleSafetyFade();

//    }

//    private void HandleSafetyFade()
//    {
//        if (SpeedCalculator == null) return;

//        // SpeedCalculatorから現在の速度(float)を取得
//        float currentKmh = SpeedCalculator.CurrentKmh;

//        // 速度判定
//        if (currentKmh > safetySpeedThreshold)
//        {
//            _highSpeedTimer += Time.deltaTime;
//        }
//        else
//        {
//            _highSpeedTimer = 0f;
//        }

//        // フェード処理
//        bool isDangerSpeed = (_highSpeedTimer >= timeToFade);
//        float targetAlpha = isDangerSpeed ? fadedAlpha : 1.0f;
//        //uiCanvasGroup.alpha = Mathf.Lerp(uiCanvasGroup.alpha, targetAlpha, Time.deltaTime * fadeSpeed);
//        //uiCanvasGroup.blocksRaycasts = !isDangerSpeed;

//        // インスペクタで登録したパネルだけをループで回して処理
//        foreach (var panelGroup in fadeTargetPanels)
//        {
//            if (panelGroup == null) continue;

//            // 滑らかにフェード
//            panelGroup.alpha = Mathf.Lerp(panelGroup.alpha, targetAlpha, Time.deltaTime * fadeSpeed);

//            // 透明時はボタンなどを押せないようにする
//            panelGroup.blocksRaycasts = (panelGroup.alpha > 0.8f);
//        }
//    }

//    /// <summary>
//    /// 精度状態を更新し、変化があった場合のみUIを切り替える
//    /// </summary>
//    public void UpdateAccuracyDisplay(bool isAccurate, bool isTracking)
//    {
//        if (warnText != null)
//        {
//            // トラッキング中で、かつ精度が低い場合のみ警告を表示
//            if (isTracking && isAccurate)
//            {
//                warnText.gameObject.SetActive(false);
//            }
//            else
//            {
//                warnText.text = $"<color=yellow>精度が不足しています。周囲を見回してください。</color>";
//                warnText.gameObject.SetActive(true);
//            }
//        }
//    }

//    // 再検索状態の表示を切り替えるメソッド
//    public void UpdateReSearchingDisplay(bool isSearching)
//    {
//        if (researchText == null) return;

//        if (isSearching)
//        {
//            // researchText.text = "<color=orange>ルートから外れました。再検索しています...</color>";
//            researchText.gameObject.SetActive(true);

//            // 再検索中は距離表示などを一時的に隠す、またはグレーアウトするのも親切です
//            if (distanceText != null) distanceText.alpha = 0.5f;
//        }
//        else
//        {
//            researchText.gameObject.SetActive(false);
//            if (distanceText != null) distanceText.alpha = 1.0f;
//        }
//    }

//    // --- 追記：表示・非表示を切り替えるメソッド ---
//    public void ToggleDebugPanels()
//    {
//        // 状態を反転
//        isDebugPanelVisible = !isDebugPanelVisible;

//        // パネルの表示状態を更新
//        SetDebugPanelsVisible(isDebugPanelVisible);
//    }

//    private void SetDebugPanelsVisible(bool visible)
//    {
//        if (debugPanel != null) debugPanel.SetActive(visible);
//        if (ARviewPanel != null) ARviewPanel.SetActive(visible);

//        // ボタンのテキストや色を変えたい場合はここに追記
//        // 例: DebugButton.GetComponentInChildren<TextMeshProUGUI>().text = visible ? "Hide Debug" : "Show Debug";
//    }

//    public void UpdateDistanceDisplay(float distanceInMeters)
//    {
//        if (distanceText == null) return;

//        if (distanceInMeters >= 1000f)
//        {
//            // 1km以上は「1.2 km」のように表示
//            float distanceInKm = distanceInMeters / 1000f;
//            distanceText.text = $"{distanceInKm:F1} km";
//        }
//        else
//        {
//            // 1km未満は「350 m」のように表示
//            distanceText.text = $"{Mathf.CeilToInt(distanceInMeters)} m";
//        }
//    }

//    public void UpdateTimeDisplay()
//    {
//        if (timeText != null)
//        {
//            // "14:30" 形式で表示
//            timeText.text = DateTime.Now.ToString("HH:mm");
//        }
//    }

//public void UpdateTurnInstruction(string text, int signValue, float distance, int index)
//{
//    if (nextTurnText == null) return;

//    NavSignType sign = (NavSignType)signValue;

//    // --- 1. 500m以上離れている場合は案内を隠す ---
//    if (distance > thresholds.Far)
//    {
//        nextTurnText.text = "";
//        if (leftTurnArrow != null) leftTurnArrow.SetActive(false);
//        if (rightTurnArrow != null) rightTurnArrow.SetActive(false);
//        return;
//    }

//    // --- 3. アイコンとテキストの表示 ---
//    // if (leftTurnArrow != null) leftTurnArrow.SetActive(sign < 0);
//    // if (rightTurnArrow != null) rightTurnArrow.SetActive(sign > 0 && sign != NavSignType.Destination);

//    //string directionName = GetDirectionName(sign);
//    //if (!string.IsNullOrEmpty(directionName))
//    //{
//    //    nextTurnText.text = $"{distStr}先、{directionName}です";
//    //}
//    //if (!string.IsNullOrEmpty(directionName))
//    //{
//    //    nextTurnText.text = $"{distStr}先、{directionName}です";
//    //}
//    //else
//    //{
//    //    // 方向がない場合（Noneなど）はテキストを消すか、距離だけ出すなど
//    //    if (sign == NavSignType.Destination) nextTurnText.text = $"{distStr}先、目的地です";
//    //    else nextTurnText.text = "";
//    //}

//    // 音声処理へ
//    HandleVoiceGuidance(signValue, distance, index);
//}

//    private void HandleVoiceGuidance(int signValue, float distance, int index)
//    {
//        if (audioSource == null) return;
//        NavSignType sign = (NavSignType)signValue;

//        // 指示（index）が変わったときだけリセット
//        if (index != _lastVoiceInstructionIndex)
//        {
//            _lastVoiceInstructionIndex = index;
//            _lastDistanceCall = -1;
//            // クールタイムを無視して新しい案内に備えるため、必要ならここで _lastPlayTime = 0 にしても良い
//        }

//        // --- 音声トリガーの条件を「その案内地点で一度だけ」に固定 ---

//        // 800m判定
//        if (distance <= thresholds.Far && distance > thresholds.Middle && _lastDistanceCall < 800)
//        {
//            PlayGuidance(FarCallClip, signValue);
//            _lastDistanceCall = 800; // 800より大きい数値を入れることで、このindexでは二度と通らない
//        }
//        // 400m判定
//        else if (distance <= thresholds.Middle && distance > thresholds.Near && _lastDistanceCall < 400)
//        {
//            PlayGuidance(MiddleCallClip, signValue);
//            _lastDistanceCall = 400;
//        }
//        // 200m判定
//        else if (distance <= thresholds.Near && distance > thresholds.Nearby && _lastDistanceCall < 200)
//        {
//            if (sign != NavSignType.None && sign != NavSignType.Destination)
//            {
//                PlayGuidance(nearCallClip, signValue);
//            }
//            _lastDistanceCall = 200;
//        }
//        // 130m（直前・目的地）判定
//        else if (distance <= thresholds.Nearby && _lastDistanceCall < 130)
//        {
//            if (sign == NavSignType.Destination)
//            {
//                PlayGuidance(goalClip);
//            }
//            else if (sign != NavSignType.None)
//            {
//                // 距離コールなしの方向案内のみ
//                PlayGuidance(null, signValue);
//            }
//            _lastDistanceCall = 130;
//        }
//    }

//    //private void HandleVoiceGuidance(int signValue, float distance, int index)
//    //{
//    //    if (audioSource == null) return;
//    //    NavSignType sign = (NavSignType)signValue;

//    //    // 指示が変わったらリセット
//    //    if (index != _lastVoiceInstructionIndex)
//    //    {
//    //        _lastVoiceInstructionIndex = index;
//    //        _lastDistanceCall = -1;
//    //    }

//    //    // --- 4. 自動車でも確実になる音声トリガー (AudioBufferを使わない判定) ---

//    //    // 800mフェーズに入った瞬間
//    //    if (distance <= thresholds.Far && distance > thresholds.Middle && _lastDistanceCall != 800)
//    //    {
//    //        PlayGuidance(FarCallClip, signValue);
//    //        _lastDistanceCall = 800;
//    //    }
//    //    // 400mフェーズに入った瞬間
//    //    else if (distance <= thresholds.Middle && distance > thresholds.Near && _lastDistanceCall != 400)
//    //    {
//    //        PlayGuidance(MiddleCallClip, signValue);
//    //        _lastDistanceCall = 400;
//    //    }
//    //    // 200mフェーズに入った瞬間
//    //    else if (distance <= thresholds.Near && _lastDistanceCall != 200 && sign != NavSignType.None)
//    //    {
//    //        PlayGuidance(nearCallClip, signValue);
//    //        _lastDistanceCall = 200;
//    //    }
//    //    // 130mフェーズに入った瞬間
//    //    else if (distance <= thresholds.Nearby && _lastDistanceCall != 130 && sign != NavSignType.None)
//    //    {
//    //        if (sign == NavSignType.Destination) PlayGuidance(goalClip);
//    //        else PlayGuidance(null, signValue);
//    //        _lastDistanceCall = 130;
//    //    }
//    //}

//    private void PlayGuidance(AudioClip distanceClip, int sign = 0)
//    {
//        // 1. クールタイム（短時間の連続再生防止）
//        if (Time.time - _lastPlayTime < PlayCooldown) return;
//        _lastPlayTime = Time.time;

//        // 2. すでに再生中の案内があれば止める
//        if (_currentGuidanceCoroutine != null)
//        {
//            StopCoroutine(_currentGuidanceCoroutine);
//        }

//        _currentGuidanceCoroutine = StartCoroutine(PlayGuidanceRoutine(distanceClip, sign));
//    }

//    private IEnumerator PlayGuidanceRoutine(AudioClip distanceClip, int sign)
//    {
//        audioSource.Stop();
//        // 1. ポーン（infoSE）
//        if (infoSEClip != null)
//        {
//            audioSource.PlayOneShot(infoSEClip);
//            yield return new WaitForSeconds(infoSEClip.length);
//        }

//        // 2. 「〇〇m先」
//        if (distanceClip != null)
//        {
//            audioSource.PlayOneShot(distanceClip);
//            yield return new WaitForSeconds(distanceClip.length);
//        }

//        // 3. 「右方向です」などの方向案内
//        AudioClip directionClip = null;
//        if (sign < 0) directionClip = leftTurnClip;
//        else if (sign > 0 && sign != 4) directionClip = rightTurnClip;

//        if (directionClip != null)
//        {
//            audioSource.PlayOneShot(directionClip);
//        }

//        _currentGuidanceCoroutine = null;
//    }

//    public void PlayFinishGuidance()
//    {
//        // 二重再生防止
//        if (audioSource != null && !_isFinishGuidancePlaying)
//        {
//            StartCoroutine(PlayFinishGuidanceRoutine());
//        }
//    }

//    private IEnumerator PlayFinishGuidanceRoutine()
//    {
//        _isFinishGuidancePlaying = true;

//        // 目的地到着案内
//        if (goalClip != null)
//        {
//            audioSource.PlayOneShot(goalClip);
//            yield return new WaitForSeconds(goalClip.length);
//        }

//        yield return new WaitForSeconds(1.0f);

//        if (negiraiClip != null)
//        {
//            audioSource.PlayOneShot(negiraiClip);
//            yield return new WaitForSeconds(negiraiClip.length);
//        }

//        _isFinishGuidancePlaying = false;
//    }
//}