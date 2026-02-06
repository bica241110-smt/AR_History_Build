using System; // Actionを使うために必要
using System.Collections;
using SimpleJSON;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class GooglePlaceSearcher : MonoBehaviour
{
    public string apiKey;

    [Header("UI References")]
    public GameObject candidateButtonPrefab;
    public Transform searchResultsPanel;
    public TMP_InputField inputField;
    [SerializeField] private NavigationGuideUI NavigationGuideUI;
    [SerializeField] private DestinationMeshPlacer destinationMeshPlacer;

    // 内部状態でユーザーの選択を待つためのフラグと変数
    private bool _isSelectionDone = false;
    private double _selectedLat;
    private double _selectedLon;
    private string _selectedName;

    private Coroutine _currentSearchCoroutine;

    void Start()
    {
        TextAsset keyFile = Resources.Load<TextAsset>("PlacesApiKey");
        if (keyFile != null) apiKey = keyFile.text;
    }

    // ---------------------------------------------------------
    // 外部から呼ぶためのメイン関数
    // ---------------------------------------------------------
    public void StartSearchProcess(string query, Action<double, double, string> onLocationSelected)
    {
        // 前の検索が残っていたら止める
        if (_currentSearchCoroutine != null) 
        {
            StopCoroutine(_currentSearchCoroutine);
        }
        _currentSearchCoroutine = StartCoroutine(SearchAndSelectFlow(query, onLocationSelected));
        StartCoroutine(SearchAndSelectFlow(query, onLocationSelected));
    }

    // ---------------------------------------------------------
    // 検索 -> 選択待機 -> 結果返却 を行うコルーチン
    // ---------------------------------------------------------
    private IEnumerator SearchAndSelectFlow(string query, Action<double, double, string> onComplete)
    {
        // 1. まずフラグをリセット
        _isSelectionDone = false;

        // 2. Google Places API で検索実行
        yield return SearchPlaceApiCoroutine(query);

        // 3. ユーザーがボタンを押すまで待機
        yield return new WaitUntil(() => _isSelectionDone);

        // 4. 選択されたらコールバックで緯度経度と名前を返す
        // (修正: ログの第二引数を _selectedLon に修正しました)
        DebugTextManager.Log("Places", $"最終決定: {_selectedName} ({_selectedLat}, {_selectedLon})");

        // 呼び出し元に double(緯度, 経度) と名前を渡す
        onComplete?.Invoke(_selectedLat, _selectedLon, _selectedName);

        // 候補のクリア
        ClearCandidates();
    }

    // ---------------------------------------------------------
    // API通信部分
    // ---------------------------------------------------------
    private IEnumerator SearchPlaceApiCoroutine(string query)
    {
        string url = "https://places.googleapis.com/v1/places:searchText";
        string jsonBody = $"{{\"textQuery\": \"{query}\", \"languageCode\": \"ja\"}}";

        UnityWebRequest www = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("X-Goog-Api-Key", apiKey);
        www.SetRequestHeader("X-Goog-FieldMask", "places.location,places.displayName");

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            DebugTextManager.Log("Places", "Error: " + www.error);
            yield break;
        }

        ParseAndShow(www.downloadHandler.text);
    }

    // ---------------------------------------------------------
    // パースとボタン生成
    // ---------------------------------------------------------
    private void ParseAndShow(string json)
    {
        JSONNode rootNode = JSON.Parse(json);
        if (rootNode["places"] == null) return;

        JSONArray placesArray = rootNode["places"].AsArray;

        // 生成前に一度既存のものを消す
        ClearCandidates();

        int count = Mathf.Min(placesArray.Count, 5);

        // ボタンのサイズと開始位置の設定
        float startX = 182f;
        float startY = 612f;
        float startZ = 0f;
        float width = 500f;
        float height = 60f;
        float spacing = 5f;

        // inputField と同じ x, z を使い、y のみ inputField - 100 にする（searchResultsPanel のローカル座標系で扱う）
        if (inputField != null && searchResultsPanel != null)
        {
            RectTransform inputRect = inputField.GetComponent<RectTransform>();
            if (inputRect != null)
            {
                // inputField のワールド位置を searchResultsPanel のローカル空間に変換
                Vector3 inputWorldPos = inputRect.transform.position;
                Vector3 localPos = searchResultsPanel.transform.InverseTransformPoint(inputWorldPos);

                startX = localPos.x;
                startY = localPos.y - 100f;
                startZ = localPos.z;
            }
        }

        for (int i = 0; i < count; i++)
        {
            var placeNode = placesArray[i];

            string name = placeNode["displayName"]["text"].Value;
            // JSONからdoubleで取得
            double lat = placeNode["location"]["latitude"].AsDouble;
            double lng = placeNode["location"]["longitude"].AsDouble;

            // ボタン生成
            GameObject buttonObj = Instantiate(candidateButtonPrefab, searchResultsPanel);

            // --- ここから配置・サイズ設定 ---
            RectTransform rect = buttonObj.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(width, height);
                float currentY = startY - (i * (height + spacing));
                // inputField と同じ x,z を使い、y を調整してローカル位置で配置
                rect.localPosition = new Vector3(startX, currentY, startZ);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
            }
            // ---------------------------

            TMP_Text buttonText = buttonObj.GetComponentInChildren<TMP_Text>();
            if (buttonText != null) buttonText.text = name;

            Button btn = buttonObj.GetComponent<Button>();
            if (btn != null)
            {
                // リスナー登録：ここでも double のまま渡す
                btn.onClick.AddListener(() => OnCandidateSelected(name, lat, lng));
            }
        }
    }

    // ---------------------------------------------------------
    // 候補が選択されたときの処理
    // ---------------------------------------------------------
    // ★重要修正: 引数を float から double に変更
    private void OnCandidateSelected(string name, double lat, double lng)
    {
        // データを保存・反映
        _selectedName = name;
        _selectedLat = lat;
        _selectedLon = lng;

        inputField.text = name;
        _isSelectionDone = true;
        destinationMeshPlacer.SetFinalDestination(lat, lng, name); // ここで目的地を記憶させる
        // ボタンをすべて削除
        ClearCandidates();
    }

    // ヘルパー関数：候補ボタンの削除
    private void ClearCandidates()
    {
        foreach (Transform child in searchResultsPanel)
        {
            Destroy(child.gameObject);
        }
    }
}