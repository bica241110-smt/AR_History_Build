using Google.XR.ARCoreExtensions.Samples.Geospatial;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 現在地とターゲットアンカーとの距離を監視し、
/// 通過（接近）を検知してイベントを発行するクラス
/// </summary>
public class RouteProgressMonitor : MonoBehaviour
{
    // 検知距離（インスペクタで設定可能）
    [SerializeField] private float arrivalThreshold = 15.0f; // 中間地点の通過判定
    [SerializeField] private float goalThreshold = 100.0f;    // ★最終目的地の強制ゴール判定

    private List<Transform> _routeAnchors = new List<Transform>();

    private List<Vector2d> _fullRoutePath;
    public int _passedNodeCount = 0;

    public int PassedNodeCount => _passedNodeCount;

    // 通過時に呼ばれるイベント
    public event Action<int, bool> OnTargetReached;

    // 現在監視中のターゲット
    private Transform _currentTarget;
    public Transform CurrentTarget => _currentTarget;

    private int _currentTargetIndex;      // 現在監視中のノード番号
    // private bool _isFinalDestination;     // これが最後の目的地か？
    private Camera _mainCamera;

    // ★追加: 直前に通過したノードのインデックスを記録する変数
    private int _lastPassedIndex = -1;

    public string DestinationName { get; set; } = "ARGeospatialAnchor";
    public bool IsNavigating { get; private set; }

    public bool IsMonitoring => IsNavigating && _fullRoutePath != null;

    [SerializeField] private float reRouteThreshold = 200.0f; // 200m外れたらリルーティング
    public event Action OnRouteDeviated; // 逸脱時に発行するイベント

    public bool EnableDeviationCheck { get; set; } = true;

    // RouteProgressMonitor.cs に追加
    private List<RouteInstruction> _instructions;
    private int _currentInstructionIndex = 0;

    [SerializeField] private GeospatialController GeospatialController;


    private void Start()
    {
        _mainCamera = Camera.main;
    }
    private void Update()
    {
        // 案内中でなければ何もしない
        if (!IsNavigating || _currentTarget == null || _mainCamera == null) return;

        CheckFinalGoalDistance();

        if (!IsNavigating) return;

        Vector3 playerPos = _mainCamera.transform.position;
        Vector3 targetPos = _currentTarget.position;

        // 平面距離 (XZ) のみを計算
        float distanceXZ = Vector2.Distance(
            new Vector2(playerPos.x, playerPos.z),
            new Vector2(targetPos.x, targetPos.z)
        );

        // ※元の3D距離を使いたい場合はここを distanceXZ ではなく元の distance に戻してください
        if (distanceXZ < arrivalThreshold)
        {
            // ★重要: 同じインデックスで二重発火しないようにガード
            if (_currentTargetIndex == _lastPassedIndex)
            {
                _currentTarget = null; // 監視だけ外して帰る
                return;
            }

            // 通過したことを記録
            _lastPassedIndex = _currentTargetIndex;
            _passedNodeCount++;

            // 一旦ターゲットを外す
            _currentTarget = null;

            // リスナーに通知
            OnTargetReached?.Invoke(_currentTargetIndex, false);
        }
    }

    /// <summary>
    /// 最終目的地とのGPS距離を監視し、goalThreshold以内なら強制終了
    /// </summary>
    private void CheckFinalGoalDistance()
    {
        if (GeospatialController == null || _fullRoutePath == null || _fullRoutePath.Count == 0) return;

        var pose = GeospatialController.EarthManager.CameraGeospatialPose;
        Vector2d finalGoal = _fullRoutePath[_fullRoutePath.Count - 1];

        // 最終目的地との水平距離(GPS)を計算
        double distToGoal = GeoCalculator.GetDistance(pose.Latitude, pose.Longitude, finalGoal.y, finalGoal.x);

        if (distToGoal < goalThreshold)
        {
            DebugTextManager.Log("ProgressMonitor", $"【強制GOAL】最終目的地まで残り{distToGoal:F1}mのため終了します。");

            IsNavigating = false;
            _currentTarget = null;

            // 最終ノードのインデックスを渡し、isGoal=true で発火
            OnTargetReached?.Invoke(_fullRoutePath.Count - 1, true);
        }
    }

    // ★追加: アンカー生成クラスから、生成された全アンカーを受け取る
    public void SetAnchors(List<GameObject> anchorObjects)
    {
        _routeAnchors.Clear();
        foreach (var obj in anchorObjects)
        {
            if (obj != null) _routeAnchors.Add(obj.transform);
        }
    }

    public void SetRouteData(List<Vector2d> path, List<RouteInstruction> instructions)
    {
        _fullRoutePath = path;
        _passedNodeCount = 0;
        _lastPassedIndex = -1;
        _instructions = instructions;
        _currentInstructionIndex = 0;
        _currentTarget = null; // 前のターゲットを忘れる
        EnableDeviationCheck = true; // 新しいルートになったらチェックを有効化
    }

    public void IncrementProgress() => _passedNodeCount++;

    /// <summary>
    /// 現在地に基づき、残りの総距離を計算する（ロジックの移設）
    /// </summary>
    public float GetRemainingDistance(double lat, double lon)
    {
        if (_fullRoutePath == null || _passedNodeCount >= _fullRoutePath.Count) return 0f;

        double totalDistance = 0;
        var nextNode = _fullRoutePath[_passedNodeCount];
        totalDistance += GeoCalculator.GetDistance(lat, lon, nextNode.y, nextNode.x);

        for (int i = _passedNodeCount; i < _fullRoutePath.Count - 1; i++)
        {
            var p1 = _fullRoutePath[i];
            var p2 = _fullRoutePath[i + 1];
            totalDistance += GeoCalculator.GetDistance(p1.y, p1.x, p2.y, p2.x);
        }
        return (float)totalDistance;
    }

    /// <summary>
    /// 新しい監視対象を設定する
    /// </summary>
    /// public void SetTarget(Transform target, int index, bool isLast)
    public void SetTarget(Transform target, int index)
    {
        IsNavigating = true; // 案内開始！
        // ★追加: ルートがリセットされた（0番目に戻った）場合、履歴をクリアする
        if (index == 0)
        {
            _lastPassedIndex = -1;
        }

        _currentTarget = target;
        _currentTargetIndex = index;
        // _isFinalDestination = isLast;
    }

    /// <summary>
    /// 監視を停止する
    /// </summary>
    public void StopMonitoring()
    {
        IsNavigating = false; // 案内終了！
        _currentTarget = null;
        _lastPassedIndex = -1; // 停止時もリセット
    }

    /// <summary>
    /// ★修正: 戻り値をboolに変更し、逸脱状態を外部に知らせる
    /// </summary>
    public bool CheckRouteDeviation(double currentLat, double currentLon)
    {
        // 最初のノードを通過する前は逸脱判定を行わない（開始地点への移動中は除外）
        if (!IsNavigating || _fullRoutePath == null || _passedNodeCount == 0 || !EnableDeviationCheck)
            return false;

        var pStart = _fullRoutePath[_passedNodeCount - 1];
        var pEnd = _fullRoutePath[_passedNodeCount];

        double distanceToSegment = GetDistanceToSegment(currentLat, currentLon, pStart.y, pStart.x, pEnd.y, pEnd.x);

        if (distanceToSegment > reRouteThreshold)
        {
            DebugTextManager.Log("Navigation", $"ルート逸脱検知: {distanceToSegment:F1}m");

            // イベントを発行しつつ、戻り値でも true を返す
            OnRouteDeviated?.Invoke();
            return true;
        }

        return false;
    }

    // 点Pから線分ABへの距離を求める（経緯度をメートル近似して計算）
    private double GetDistanceToSegment(double pLat, double pLon, double aLat, double aLon, double bLat, double bLon)
    {
        // GeoCalculator（既存クラス）を利用してメートル単位の相対座標に変換
        float xB = (float)GeoCalculator.GetDistance(aLat, aLon, aLat, bLon) * (bLon < aLon ? -1 : 1);
        float zB = (float)GeoCalculator.GetDistance(aLat, aLon, bLat, aLon) * (bLat < aLat ? -1 : 1);
        float xP = (float)GeoCalculator.GetDistance(aLat, aLon, aLat, pLon) * (pLon < aLon ? -1 : 1);
        float zP = (float)GeoCalculator.GetDistance(aLat, aLon, pLat, aLon) * (pLat < aLat ? -1 : 1);

        Vector2 b = new Vector2(xB, zB);
        Vector2 p = new Vector2(xP, zP);

        if (b.sqrMagnitude < 0.1f) return p.magnitude; // AとBがほぼ同じ点の場合

        float t = Mathf.Clamp01(Vector2.Dot(p, b) / b.sqrMagnitude);
        return Vector2.Distance(p, b * t);
    }

    // 次の曲がり角（指示）の情報を取得するメソッド
    public (string text, int sign, float distance ,int index) GetNextInstructionInfo()
    {
        if (_instructions == null || _currentInstructionIndex >= _instructions.Count)
            return ("", 0, 0f, -1);

        // 現在通過したノード数に基づいて、現在の指示インデックスを更新
        while (_currentInstructionIndex < _instructions.Count - 1 &&
               _passedNodeCount >= _instructions[_currentInstructionIndex].lastNodeIndex)
        {
            _currentInstructionIndex++;
        }

        // 次の「曲がり角」が発生する指示を取得（現在の指示が終了する地点）
        var currentInst = _instructions[_currentInstructionIndex];

        // 曲がり角までの距離 = (現在地から次のノード) + (次のノードから曲がり角ノードまでの経路距離)
        float distanceToTurn = CalculateDistanceToNodeInUnitySpace(currentInst.lastNodeIndex);

        return (currentInst.text, currentInst.sign, distanceToTurn, _currentInstructionIndex);
    }

    /// <summary>
    /// ★新規追加: ARアンカー間の距離を積算して正確な「道のり」を出す
    /// </summary>
    private float CalculateDistanceToNodeInUnitySpace(int targetNodeIndex)
    {
        // まだアンカーリストがセットされていない、またはターゲットがない場合は計算不能
        if (_routeAnchors == null || _routeAnchors.Count == 0 || _currentTarget == null) return 0f;

        // ★修正: ループの上限計算
        // 指示されたノードが、現在表示中のアンカー数より先にある場合のエラー防止
        int limitIndex = Mathf.Min(targetNodeIndex, _routeAnchors.Count - 1);

        if (targetNodeIndex >= _routeAnchors.Count) return 0f;

        float totalDistance = 0f;

        // 1. 【現在地】から【現在のターゲットアンカー】までの距離
        // ユーザーの体感と一致させるため、高さを無視したXZ平面距離推奨だが、
        // LineRenderer上の移動距離なら Vector3.Distance でも大きな違和感はない。
        // ここでは安全のため XZ距離 を採用します。
        Vector3 camPos = _mainCamera.transform.position;
        Vector3 currentTargetPos = _currentTarget.position;

        totalDistance += Vector2.Distance(
            new Vector2(camPos.x, camPos.z),
            new Vector2(currentTargetPos.x, currentTargetPos.z)
        );

        // 2. 【現在のターゲット】から【目的のノード】までの区間距離を積算
        // _currentTargetIndex は「今目指しているノード」なので、
        // ループは _currentTargetIndex から開始して targetNodeIndex の手前まで繋ぐ
        for (int i = _currentTargetIndex; i < limitIndex; i++)
        {
            if (i + 1 < _routeAnchors.Count)
            {
                Transform p1 = _routeAnchors[i];
                Transform p2 = _routeAnchors[i + 1];

                if (p1 != null && p2 != null)
                {
                    totalDistance += Vector2.Distance(
                        new Vector2(p1.position.x, p1.position.z),
                        new Vector2(p2.position.x, p2.position.z)
                    );
                }
            }
        }

        return totalDistance;
    }

    //private float CalculateDistanceToNode(double lat, double lon, int targetNodeIndex)
    //{
    //    if (_fullRoutePath == null || targetNodeIndex >= _fullRoutePath.Count) return 0f;

    //    double total = 0;
    //    // 1. 現在地から次のノード(passedNodeCount)まで
    //    var nextNode = _fullRoutePath[_passedNodeCount];
    //    total += GeoCalculator.GetDistance(lat, lon, nextNode.y, nextNode.x);

    //    // 2. 次のノードからターゲットノードまでの積算
    //    for (int i = _passedNodeCount; i < targetNodeIndex; i++)
    //    {
    //        total += GeoCalculator.GetDistance(_fullRoutePath[i].y, _fullRoutePath[i].x,
    //                                           _fullRoutePath[i + 1].y, _fullRoutePath[i + 1].x);
    //    }
    //    return (float)total;
    //}

    public RouteInstruction GetCurrentInstruction()
    {
        if (_instructions == null || _currentInstructionIndex >= _instructions.Count)
            return null;
        return _instructions[_currentInstructionIndex];
    }
}