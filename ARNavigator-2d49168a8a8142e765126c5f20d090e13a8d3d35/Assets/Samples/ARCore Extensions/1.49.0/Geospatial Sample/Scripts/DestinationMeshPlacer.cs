using Google.XR.ARCoreExtensions;
using Google.XR.ARCoreExtensions.Samples.Geospatial;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using static UnityEngine.GraphicsBuffer;

public class DestinationMeshPlacer : MonoBehaviour
{
    [Header("References")]
    public GeospatialController GeospatialController;
    public GameObject DestinationPrefab;
    public GameObject CornerPrefab;
    public RouteLineRenderer routeLineRenderer;
    public RouteProgressMonitor progressMonitor;
    public NavigationGuideUI navigationGuideUI;

    [Header("Smoothing Settings")]
    public float HeightSmoothSpeed = 2.0f; // 高さを補正するスピード（小さいほど滑らか）
    //private float _currentVerticalOffset = 0f; // 蓄積された高さ補正量

    [Header("Route Settings")]
    public float AltitudeOffset = 1.5f;     // 地形からの高さ
    public float GroundVisualOffset = 0.05f; // Zファイティング防止の浮かし
    public float MaxAltitudeGapFromCamera = 1.0f;
    public const int MAX_ACTIVE_ANCHORS = 20;

    [Header("Accuracy Gate Thresholds")]
    public double MaxHorizontalAccuracy = 10.0;
    public double MaxYawAccuracy = 15.0;

    private List<Vector2d> _fullRoutePath;
    private LinkedList<GameObject> _activeAnchors = new LinkedList<GameObject>();
    private int _nextSpawnIndex = 0;
    private double _routeStartLat = double.NaN;
    private double _routeStartLon = double.NaN;

    // Root anchor（座標安定化用）
    private GameObject _persistentRootAnchor = null;
    private Vector3 _lastRootWorldPos = Vector3.zero;

    // 再描画制御
    private float _redrawTimer = 0f;
    private const float REDRAW_INTERVAL = 0.5f;

    private bool _wasTracking = false;
    private bool _wasAccurate = false;

    // ★修正: ルート生成の一意性を保つID（非同期競合対策）
    private Guid _currentRouteId;

    private bool? _lastAccurateStateForVisuals = null;

    private double _finalTargetLat = double.NaN;
    private double _finalTargetLon = double.NaN;
    private string _finalTargetName = "";
    private bool _isRerouting = false;
    private bool _wasDeviated = false;

    private void Update()
    {
        // タイマーは常に回す（精度復帰時に即更新できるように）
        _redrawTimer += Time.deltaTime;

        var earth = GeospatialController?.EarthManager;
        if (earth == null) return;

        bool isTracking = earth.EarthTrackingState == TrackingState.Tracking;

        // ① 状態変化チェック
        if (isTracking != _wasTracking)
        {
            if (isTracking)
            {
                DebugTextManager.Log("Status", "<color=green>位置特定完了</color>");
                if (routeLineRenderer != null) routeLineRenderer.gameObject.SetActive(true);
            }
            else
            {
                DebugTextManager.Log("Status", "<color=red>位置特定中… 周囲を見回してください</color>");
                if (routeLineRenderer != null) routeLineRenderer.gameObject.SetActive(false);
            }

            if (navigationGuideUI != null) navigationGuideUI.UpdateAccuracyDisplay(false, isTracking);
            _wasTracking = isTracking;
        }

        if (!isTracking) return;

        var pose = earth.CameraGeospatialPose;
        bool isAccurate = pose.HorizontalAccuracy <= MaxHorizontalAccuracy &&
                          pose.OrientationYawAccuracy <= MaxYawAccuracy;

        if (isAccurate != _wasAccurate)
        {
            if (isAccurate)
            {
                DebugTextManager.Log("Status", "<color=green>位置精度：良好</color>");
                if (routeLineRenderer != null) routeLineRenderer.gameObject.SetActive(true);
                // ★修正: 精度復帰時は即座に再描画をリクエスト
                _redrawTimer = REDRAW_INTERVAL;
                if (routeLineRenderer != null) routeLineRenderer.SetAlpha(0.7f);
            }
            else
            {
                DebugTextManager.Log("Status", $"<color=yellow>精度不足 H:{pose.HorizontalAccuracy:F1}m / Y:{pose.OrientationYawAccuracy:F1}°\n周囲を見回してください</color>");
                if (routeLineRenderer != null) routeLineRenderer.SetAlpha(0.2f);
            }

            // --- 追記：UIへの通知（精度が変わった時だけ実行される） ---
            if (navigationGuideUI != null)
            {
                navigationGuideUI.UpdateAccuracyDisplay(isAccurate, isTracking);
            }
            _wasAccurate = isAccurate;
        }

        // if (!isAccurate) return;

        // ② 再描画制御
        bool needRedrawByTime = _redrawTimer >= REDRAW_INTERVAL;
        bool needRedrawByMove = false;

        if (_persistentRootAnchor != null)
        {
            float dist = Vector3.Distance(_lastRootWorldPos, _persistentRootAnchor.transform.position);
            needRedrawByMove = dist > 0.05f; // 5cm以上のズレで再描画
        }

        if (needRedrawByTime || needRedrawByMove)
        {
            UpdateRouteLineFromAnchors();

            // UIの時計を更新（UI側に public void UpdateTimeDisplay() がある前提）
            navigationGuideUI.UpdateTimeDisplay();
            // 残り距離の更新
            float remainDistance = progressMonitor.GetRemainingDistance(pose.Latitude, pose.Longitude);
            
            var nextStep = progressMonitor.GetNextInstructionInfo();
            navigationGuideUI.UpdateDistanceDisplay(remainDistance);
            navigationGuideUI.UpdateTurnInstruction(nextStep.sign, nextStep.distance, nextStep.index);

            _redrawTimer = 0f;
            if (_persistentRootAnchor != null) _lastRootWorldPos = _persistentRootAnchor.transform.position;
        }

        // ★追加: ルート逸脱の監視
        if (progressMonitor != null && progressMonitor.IsMonitoring && !_isRerouting)
        {
            // progressMonitor側に CheckRouteDeviation メソッドがあることが前提です
            if (progressMonitor.CheckRouteDeviation(pose.Latitude, pose.Longitude))
            {
                if (!_wasDeviated)
                {
                    HandleRouteDeviated();
                    _wasDeviated = true;
                }
            }
            //else
            //{
            //    _wasDeviated = false;
            //}
        }
    }

    // ─────────────────────────────
    // ルート開始・更新ロジック
    // ─────────────────────────────
    public void SetRouteStartCoordinate(double lat, double lon)
    {
        _routeStartLat = lat;
        _routeStartLon = lon;
    }

    public void StartDynamicRoute(List<Vector2d> routePoints, List<RouteInstruction> instructions, string destinationName)
    {
        if (routePoints == null || routePoints.Count == 0) return;

        // 再検索用に最終目的地を保存
        var finalPoint = routePoints[routePoints.Count - 1];
        _finalTargetLat = finalPoint.y;
        _finalTargetLon = finalPoint.x;
        _finalTargetName = destinationName;
        _isRerouting = false;
        _wasDeviated = false;

        ClearCurrentRoute();

        // 新しいルートIDを発行
        _currentRouteId = Guid.NewGuid();

        // Start地点の挿入ロジック
        //本当に信用していいのか！？
        //if (!double.IsNaN(_routeStartLat) && !double.IsNaN(_routeStartLon))
        //{
        //    var startPos = new Vector2d(_routeStartLon, _routeStartLat);
        //    double distance = GeoCalculator.GetDistance(
        //        _routeStartLat, _routeStartLon,
        //        routePoints[0].y, routePoints[0].x
        //    );

        //    if (distance > 1.0)
        //    {
        //        routePoints.Insert(0, startPos);
        //        foreach (var inst in instructions)
        //        {
        //            inst.lastNodeIndex += 1;
        //        }
        //    }
        //}

        _fullRoutePath = routePoints;

        if (progressMonitor != null)
        {
            progressMonitor.DestinationName = destinationName;
            progressMonitor.OnTargetReached -= OnNextAnchorReached;
            progressMonitor.OnTargetReached += OnNextAnchorReached;
            progressMonitor.SetRouteData(routePoints, instructions);

            SyncAnchorsToMonitor();
        }

        int initialCount = Mathf.Min(MAX_ACTIVE_ANCHORS, _fullRoutePath.Count);
        for (int i = 0; i < initialCount; i++)
        {
            SpawnNextAnchor();
        }

        UpdateMonitorTarget();
    }

    private void OnNextAnchorReached(int passedIndex, bool isGoal)
    {
        if (passedIndex >= 1 && _activeAnchors.Count > 0)
        {
            GameObject anchorToRemove = _activeAnchors.First.Value;
            _activeAnchors.RemoveFirst();

            // LineRendererの親子付け解除（以前の修正を維持）
            if (routeLineRenderer != null && routeLineRenderer.transform.parent == anchorToRemove.transform)
            {
                routeLineRenderer.transform.SetParent(this.transform);
            }

            if (anchorToRemove == _persistentRootAnchor) _persistentRootAnchor = null;
            if (anchorToRemove != null) Destroy(anchorToRemove);

            SyncAnchorsToMonitor();
        }

        if (isGoal)
        {
            StartCoroutine(FinishRouteRoutine());
            return;
        }

        SpawnNextAnchor();
        UpdateMonitorTarget();
        UpdateRouteLineFromAnchors();
    }

    private void SpawnNextAnchor()
    {
        if (_fullRoutePath == null || _nextSpawnIndex >= _fullRoutePath.Count) return;

        Vector2d p = _fullRoutePath[_nextSpawnIndex];

        double heading = (_nextSpawnIndex > 0)
            ? GeoCalculator.GetDirection(_fullRoutePath[_nextSpawnIndex - 1].y, _fullRoutePath[_nextSpawnIndex - 1].x, p.y, p.x)
            : 0.0;

        bool isCorner = (_nextSpawnIndex > 0 && _nextSpawnIndex < _fullRoutePath.Count - 1);

        // キャプチャ用にローカル変数へ
        var currentId = _currentRouteId;

        PlaceDestination(p.y, p.x, AltitudeOffset, heading, isCorner, currentId, (anchor) =>
        {
            // ★修正: ルートが変更されていたり、クリアされていたら追加しない
            if (currentId != _currentRouteId)
            {
                if (anchor != null) Destroy(anchor);
                return;
            }

            if (anchor != null && !_activeAnchors.Contains(anchor))
            {
                _activeAnchors.AddLast(anchor);

                SyncAnchorsToMonitor();

                // ★追加: これが最初のアンカーだった場合、または現在ターゲットがない場合、
                // モニターのターゲットを更新する
                if (_activeAnchors.Count == 1 || (progressMonitor != null && progressMonitor.CurrentTarget == null))
                {
                    UpdateMonitorTarget();
                }

                // 追加直後にライン更新（初期ロード時の見た目を良くする）
                if (_activeAnchors.Count >= 2) UpdateRouteLineFromAnchors();
            }
        });

        _nextSpawnIndex++;
    }

    // ★新規追加: RouteProgressMonitorへ現在のアンカーリストを送るヘルパー
    private void SyncAnchorsToMonitor()
    {
        if (progressMonitor != null)
        {
            // LinkedList -> List 変換
            List<GameObject> currentList = new List<GameObject>(_activeAnchors);
            progressMonitor.SetAnchors(currentList);
        }
    }

    // ─────────────────────────────
    // LineRenderer 描画ロジック
    // ─────────────────────────────
    public void UpdateRouteLineFromAnchors()
    {
        if (routeLineRenderer == null || _activeAnchors.Count < 2) return;

        // カメラの現在の世界座標（高さ）を取得
        float cameraWorldY = Camera.main.transform.position.y;
        // ユーザーが立っているはずの「想定地面高さ」
        // float estimatedGroundY = cameraWorldY - AltitudeOffset;
        float estimatedGroundY = AltitudeOffset;

        List<GameObject> confirmed = new List<GameObject>();

        foreach (var a in _activeAnchors)
        {
            if (a == null) continue;

            var geoAnchor = a.GetComponent<ARGeospatialAnchor>();
            // Tracking以外も許容するかは要検討だが、基本はTrackingのみ
            if (geoAnchor != null && geoAnchor.trackingState != TrackingState.Tracking)
            {
                continue;
            }

            // 原点チェック (Resolve直後は(0,0,0)になることがあるため除外)
            if (a.transform.position.sqrMagnitude < 0.0001f) continue;

            confirmed.Add(a);

            // ★2. 「次の区間」だけ描画するため、2つ見つかったら検索を打ち切る
            if (confirmed.Count >= 3) break;
        }

        if (confirmed.Count < 2) return;

        // Root更新ロジック
        if (_persistentRootAnchor == null || !_persistentRootAnchor.activeInHierarchy)
        {
            _persistentRootAnchor = confirmed[0];
            _lastRootWorldPos = _persistentRootAnchor.transform.position;
        }
        else
        {
            // リストの先頭が変わっている（通過した）場合、Rootを更新する
            if (confirmed[0] != _persistentRootAnchor && confirmed.Contains(_persistentRootAnchor) == false)
            {
                _persistentRootAnchor = confirmed[0];
            }
        }

        // LineRendererの親子付け更新
        if (routeLineRenderer.transform.parent != _persistentRootAnchor.transform)
        {
            routeLineRenderer.transform.SetParent(_persistentRootAnchor.transform, true); // worldPositionStays=true
            //// ★★★ 修正ポイント：位置だけでなく、回転もリセットする ★★★
            //routeLineRenderer.transform.localPosition = Vector3.zero;
            //routeLineRenderer.transform.localRotation = Quaternion.identity; // ← これを追加！
        }

        routeLineRenderer.transform.localPosition = Vector3.zero;
        Vector3 anchorEuler = _persistentRootAnchor.transform.eulerAngles;
        routeLineRenderer.transform.rotation = Quaternion.Euler(0, anchorEuler.y, 0);

        // ローカル座標計算
        List<Vector3> locals = new List<Vector3>();
        foreach (var c in confirmed)
        {
            // 線の高さの補正を行う処理
            Vector3 worldPos = c.transform.position;

            // ★修正ポイント：カメラベースの「想定地面」と、アンカーの「実際の高さ」を比較
            float altitudeDiff = worldPos.y - estimatedGroundY;

            // もし高さがしきい値を超えてズレていたら、強制的に補正する
            if (Mathf.Abs(altitudeDiff) > MaxAltitudeGapFromCamera)
            {
                // 許容範囲内にクランプ（制限）する
                float clampedY = estimatedGroundY + Mathf.Sign(altitudeDiff) * MaxAltitudeGapFromCamera;
                worldPos.y = clampedY;
            }
            //

            // ★ 重要：アンカーではなく、水平に直した「LineRenderer自身」から見た相対座標を計算する
            Vector3 lp = routeLineRenderer.transform.InverseTransformPoint(worldPos);

            // Zファイティング防止のオフセット
            lp.y += GroundVisualOffset;
            locals.Add(lp);
        }

        routeLineRenderer.gameObject.SetActive(true);
        routeLineRenderer.DrawRouteLocal(locals);
    }

    private void UpdateMonitorTarget()
    {
        if (_activeAnchors == null || _activeAnchors.Count == 0) return;

        int currentCount = progressMonitor.PassedNodeCount;
        GameObject targetGO = null;

        if (currentCount > 0 && _activeAnchors.Count >= 2)
        {
            // 通過済みが残っているなら、2番目の要素が「現在の目標」
            targetGO = _activeAnchors.First.Next.Value;
        }
        else
        {
            // 開始直後などは、先頭の要素が「現在の目標」
            targetGO = _activeAnchors.First.Value;
        }

        if (targetGO != null)
        {
            progressMonitor.SetTarget(targetGO.transform, currentCount);
            DebugTextManager.Log("Navigation", $"Target updated to node {currentCount}: {targetGO.name}");
        }
    }

    public void PlaceDestination(double lat, double lon, double alt, double heading, bool isCorner, Action<GameObject> onComplete)
    {
        // Guid.Empty を渡すことで「これはルートの一部ではない単発の配置です」と伝えます
        PlaceDestination(lat, lon, alt, heading, isCorner, Guid.Empty, onComplete);
    }

    public void PlaceDestination(double lat, double lon, double alt, double heading, bool isCorner, Guid routeId, Action<GameObject> onComplete)
    {
        if (GeospatialController?.AnchorManager == null) return;
        Quaternion rot = Quaternion.Euler(0, (float)heading, 0);

        StartCoroutine(ResolveOnTerrainRoutine(lat, lon, rot, isCorner, routeId, onComplete));
    }

    private IEnumerator ResolveOnTerrainRoutine(double lat, double lon, Quaternion rot, bool isCorner, Guid routeId, Action<GameObject> onComplete)
    {
        var promise = GeospatialController.AnchorManager.ResolveAnchorOnTerrainAsync(lat, lon, AltitudeOffset, rot);
        yield return promise;

        // ★修正ポイント: routeId が Guid.Empty (単発配置) の場合は、IDチェックをスキップする
        if (routeId != Guid.Empty && routeId != _currentRouteId)
        {
            // IDが指定されているのに、現在のルートIDと違う＝古いリクエストなので破棄
            yield break;
        }

        // ★修正: 待機中にルートが変わっていたら何もしない
        if (routeId != _currentRouteId) yield break;

        if (promise.Result.TerrainAnchorState == TerrainAnchorState.Success && promise.Result.Anchor != null)
        {
            InstantiateUnderAnchor(promise.Result.Anchor.gameObject, isCorner);
            onComplete?.Invoke(promise.Result.Anchor.gameObject);
        }
        else
        {
            // 失敗時のログ
            Debug.LogWarning($"Terrain Anchor Failed: {promise.Result.TerrainAnchorState}");
        }
    }

    private void InstantiateUnderAnchor(GameObject anchorGO, bool isCorner)
    {
        var prefab = (isCorner && CornerPrefab != null) ? CornerPrefab : DestinationPrefab;
        if (prefab != null)
        {
            var go = Instantiate(prefab, anchorGO.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.SetActive(true);
        }
    }

    public void ClearCurrentRoute()
    {
        // ★修正: IDを更新して、実行中の非同期処理を無効化
        _currentRouteId = Guid.NewGuid();

        _persistentRootAnchor = null;
        _nextSpawnIndex = 0;
        _redrawTimer = 0f;

        if (progressMonitor != null)
        {
            progressMonitor.OnTargetReached -= OnNextAnchorReached;
            progressMonitor.StopMonitoring();
            // ★追加: クリア時も同期して、モニター内のアンカーリストを空にする
            SyncAnchorsToMonitor();
        }

        if (routeLineRenderer != null)
        {
            // ★修正: 念のため親子付けを解除して非アクティブ化
            routeLineRenderer.transform.SetParent(this.transform);
            routeLineRenderer.gameObject.SetActive(false);
        }

        foreach (var a in _activeAnchors) if (a != null) Destroy(a);
        _activeAnchors.Clear();
    }

    private void UpdateLineVisuals(bool isAccurate, bool isTracking)
    {
        if (_lastAccurateStateForVisuals == isAccurate) return;

        if (routeLineRenderer != null)
        {
            if (!isTracking)
            {
                routeLineRenderer.gameObject.SetActive(false);
            }
            else
            {
                routeLineRenderer.gameObject.SetActive(true);
                // 以前追加した SetAlpha メソッドを呼び出す
                routeLineRenderer.SetAlpha(isAccurate ? 0.7f : 0.2f);
            }
        }

        _lastAccurateStateForVisuals = isAccurate;
    }

    private void HandleRouteDeviated()
    {
        if (_isRerouting) return;
        DebugTextManager.Log("Navigation", "<color=yellow>ルートから外れました。再検索します...</color>");
        var earth = GeospatialController.EarthManager;
        if (earth.CameraGeospatialPose.HorizontalAccuracy > MaxHorizontalAccuracy)
        {
            DebugTextManager.Log("Navigation", "ルート逸脱を検知しましたが、精度不足のため再検索を待機中...");
            return;
        }
        StartCoroutine(RerouteRoutine());
    }

    private IEnumerator RerouteRoutine()
    {
        _isRerouting = true;

        // 1. UI表示 (UI側にメソッドがあることが前提)
        if (navigationGuideUI != null) navigationGuideUI.UpdateReSearchingDisplay(true);

        // 2. 現在地の取得
        var pose = GeospatialController.EarthManager.CameraGeospatialPose;

        SetRouteStartCoordinate(pose.Latitude, pose.Longitude);

        // 3. 再検索のリクエスト
        // シーン内の RouteRequest コンポーネントを探して呼び出す
        var routeRequest = FindFirstObjectByType<RouteRequest>();
        if (routeRequest != null && !double.IsNaN(_finalTargetLat))
        {
            // 既存のアンカーとラインを一旦クリア
            ClearCurrentRoute();

            // RouteRequest側のルート生成メソッドを呼び出す
            yield return StartCoroutine(routeRequest.RequestRouteAndPlace(
                pose.Latitude, pose.Longitude,
                _finalTargetLat, _finalTargetLon,
                _finalTargetName
            ));

            yield return new WaitForSeconds(3.0f);
        }

        if (navigationGuideUI != null) navigationGuideUI.UpdateReSearchingDisplay(false);
        _isRerouting = false;
    }

    public void SetFinalDestination(double lat, double lon, string name)
    {
        _finalTargetLat = lat;
        _finalTargetLon = lon;
        _finalTargetName = name;
        DebugTextManager.Log("Navigation", $"目的地がセットされました: {name}");
    }

    private IEnumerator FinishRouteRoutine()
    {
        // 1. 進行状況の監視を止める 
        // これにより NavigationGuideUI の Update 内で guidePanel が非アクティブになります
        if (progressMonitor != null) progressMonitor.StopMonitoring();

        // 2. 到着の音声（労い）を流す
        if (navigationGuideUI != null)
        {
            navigationGuideUI.PlayFinishGuidance();
        }

        // 3. 数秒間、目的地マーカーやラインを表示したままにする（余韻）
        yield return new WaitForSeconds(5.0f);

        // 4. アンカー、ライン、内部データを全てクリアする
        ClearCurrentRoute();

        DebugTextManager.Log("Navigation", "ルート案内が正常に終了しました。");
    }
}