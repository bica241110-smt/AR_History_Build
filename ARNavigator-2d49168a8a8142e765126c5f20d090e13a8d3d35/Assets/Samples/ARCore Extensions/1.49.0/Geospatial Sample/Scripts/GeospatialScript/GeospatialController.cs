// <copyright file="GeospatialController.cs" company="Google LLC">
//
// Copyright 2022 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// </copyright>
//-----------------------------------------------------------------------

#if !ENABLE_LEGACY_INPUT_MANAGER
// Input.location will not work at runtime with out the old input system.
// Given that sample has not been ported to support new input
// Check that Project Settings > Player > Other Settings > Active Input Handling
// is set to Both or Input Manager (Old)
#error Input.location API requires Active Input Handling to be set to Input Manager (Old) or Both
#endif

#if !ENABLE_INPUT_SYSTEM
// The camera's pose driver in ARF5 needs Input System (New) but given we need Input Manager
// (Old) for Input.location (see above) ARF5 needs both.
// Check that Project Settings > Player > Other Settings > Active Input Handling
// is set to Both
#error The camera's pose driver needs Input System (New) so set Active Input Handling to Both
#endif // !ENABLE_INPUT_SYSTEM

namespace Google.XR.ARCoreExtensions.Samples.Geospatial
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using Unity.XR.CoreUtils;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;

    using UnityEngine.XR.ARFoundation;
    using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID
    using UnityEngine.Android;
#endif

    /// <summary>
    /// Geospatial サンプルのコントローラ。
    /// AR コンポーネントと UI を管理し、ジオスペーシャル機能、アンカーの配置、履歴の復元等を行います。
    /// </summary>
    [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1118:ParameterMustNotSpanMultipleLines",
        Justification = "Bypass source check.")]
    public class GeospatialController : MonoBehaviour
    {
        [Header("AR Components")]

        // <summary>
        // 押すと目的地の座標を取得するボタン
        // </summary>
        public Button GetRoadsButton;
        /// <summary>
        /// 使用する XROrigin。
        /// </summary>
        public XROrigin Origin;

        /// <summary>
        /// 使用する ARSession。
        /// </summary>
        public ARSession Session;

        /// <summary>
        /// 使用する ARAnchorManager。
        /// </summary>
        public ARAnchorManager AnchorManager;

        /// <summary>
        /// 使用する ARRaycastManager。
        /// </summary>
        public ARRaycastManager RaycastManager;

        /// <summary>
        /// 使用する AREarthManager。
        /// </summary>
        public AREarthManager EarthManager;

        /// <summary>
        /// 使用する ARStreetscapeGeometryManager。
        /// </summary>
        public ARStreetscapeGeometryManager StreetscapeGeometryManager;

        /// <summary>
        /// 使用する ARCoreExtensions。
        /// </summary>
        public ARCoreExtensions ARCoreExtensions;

        /// <summary>
        /// 建物メッシュ描画用の StreetscapeGeometry マテリアル群。
        /// </summary>
        public List<Material> StreetscapeGeometryMaterialBuilding;

        /// <summary>
        /// 地形メッシュ描画用の StreetscapeGeometry マテリアル。
        /// </summary>
        public Material StreetscapeGeometryMaterialTerrain;

        [Header("UI Elements")]

        /// <summary>
        /// Geospatial アンカー表示用のプレハブ。
        /// </summary>
        public GameObject GeospatialPrefab;

        /// <summary>
        /// Terrain アンカー表示用のプレハブ。
        /// </summary>
        public GameObject TerrainPrefab;

        /// <summary>
        /// プライバシー確認用のキャンバス。
        /// </summary>
        public GameObject PrivacyPromptCanvas;

        /// <summary>
        /// VPS 利用可能性通知用のキャンバス。
        /// </summary>
        public GameObject VPSCheckCanvas;

        /// <summary>
        /// AR 表示コンテンツを含むキャンバス。
        /// </summary>
        public GameObject ARViewCanvas;

        /// <summary>
        /// すべてのアンカーと履歴をクリアするボタン。
        /// </summary>
        public Button ClearAllButton;

        /// <summary>
        /// Streetscape Geometry の表示切替トグル。
        /// </summary>
        public Toggle GeometryToggle;

        /// <summary>
        /// アンカー設定パネル表示ボタン。
        /// </summary>
        public Button AnchorSettingButton;

        /// <summary>
        /// アンカー設定パネル本体。
        /// </summary>
        public GameObject AnchorSettingPanel;

        /// <summary>
        /// Geospatial アンカートグル。
        /// </summary>
        public Toggle GeospatialAnchorToggle;

        /// <summary>
        /// Terrain アンカートグル。
        /// </summary>
        public Toggle TerrainAnchorToggle;

        /// <summary>
        /// Rooftop アンカートグル。
        /// </summary>
        public Toggle RooftopAnchorToggle;

        /// <summary>
        /// 実行時情報表示用パネル。
        /// </summary>
        public GameObject InfoPanel;

        /// <summary>
        /// GeospatialPose 情報を表示する Text。
        /// </summary>
        public Text InfoText;

        /// <summary>
        /// 画面下部のスナックバー用 Text。
        /// </summary>
        public Text SnackBarText;

        /// <summary>
        /// デバッグ情報表示用 Text（デバッグビルド時のみ有効）。
        /// </summary>
        public Text DebugText;

        /// <summary>
        /// Help message shown while localizing.
        /// </summary>
        private const string _localizingMessage = "Localizing your device to set anchor.";

        /// <summary>
        /// Help message shown while initializing Geospatial functionalities.
        /// </summary>
        private const string _localizationInitializingMessage =
            "Initializing Geospatial functionalities.";

        /// <summary>
        /// Help message shown when <see cref="AREarthManager.EarthTrackingState"/> is not tracking
        /// or the pose accuracies are beyond thresholds.
        /// </summary>
        private const string _localizationInstructionMessage =
            "Point your camera at buildings, stores, and signs near you.";

        /// <summary>
        /// Help message shown when location fails or hits timeout.
        /// </summary>
        private const string _localizationFailureMessage =
            "Localization not possible.\n" +
            "Close and open the app to restart the session.";

        /// <summary>
        /// Help message shown when localization is completed.
        /// </summary>
        private const string _localizationSuccessMessage = "Localization completed.";

        /// <summary>
        /// The timeout period waiting for localization to be completed.
        /// </summary>
        private const float _timeoutSeconds = 180;

        /// <summary>
        /// Indicates how long a information text will display on the screen before terminating.
        /// </summary>
        private const float _errorDisplaySeconds = 3;

        /// <summary>
        /// The key name used in PlayerPrefs which indicates whether the privacy prompt has
        /// displayed at least one time.
        /// </summary>
        private const string _hasDisplayedPrivacyPromptKey = "HasDisplayedGeospatialPrivacyPrompt";

        /// <summary>
        /// The key name used in PlayerPrefs which stores geospatial anchor history data.
        /// The earliest one will be deleted once it hits storage limit.
        /// </summary>
        private const string _persistentGeospatialAnchorsStorageKey = "PersistentGeospatialAnchors";

        /// <summary>
        /// The limitation of how many Geospatial Anchors can be stored in local storage.
        /// </summary>
        private const int _storageLimit = 20;

        /// <summary>
        /// Accuracy threshold for orientation yaw accuracy in degrees that can be treated as
        /// localization completed.
        /// </summary>
        private const double _orientationYawAccuracyThreshold = 25;

        /// <summary>
        /// Accuracy threshold for heading degree that can be treated as localization completed.
        /// </summary>
        private const double _headingAccuracyThreshold = 25;

        /// <summary>
        /// Accuracy threshold for altitude and longitude that can be treated as localization
        /// completed.
        /// </summary>
        private const double _horizontalAccuracyThreshold = 20;

        /// <summary>
        /// Determines if the anchor settings panel is visible in the UI.
        /// </summary>
        private bool _showAnchorSettingsPanel = false;

        /// <summary>
        /// Represents the current anchor type of the anchor being placed in the scene.
        /// </summary>
        private AnchorType _anchorType = AnchorType.Geospatial;

        /// <summary>
        /// Determines if streetscape geometry is rendered in the scene.
        /// </summary>
        private bool _streetscapeGeometryVisibility = false;

        /// <summary>
        /// Determines which building material will be used for the current building mesh.
        /// </summary>
        private int _buildingMatIndex = 0;

        /// <summary>
        /// Dictionary of streetscapegeometry handles to render objects for rendering
        /// streetscapegeometry meshes.
        /// </summary>
        private Dictionary<TrackableId, GameObject> _streetscapegeometryGOs =
            new Dictionary<TrackableId, GameObject>();

        /// <summary>
        /// ARStreetscapeGeometries added in the last Unity Update.
        /// </summary>
        List<ARStreetscapeGeometry> _addedStreetscapeGeometries =
            new List<ARStreetscapeGeometry>();

        /// <summary>
        /// ARStreetscapeGeometries updated in the last Unity Update.
        /// </summary>
        List<ARStreetscapeGeometry> _updatedStreetscapeGeometries =
            new List<ARStreetscapeGeometry>();

        /// <summary>
        /// ARStreetscapeGeometries removed in the last Unity Update.
        /// </summary>
        List<ARStreetscapeGeometry> _removedStreetscapeGeometries =
            new List<ARStreetscapeGeometry>();

        /// <summary>
        /// Determines if streetscape geometry should be removed from the scene.
        /// </summary>
        private bool _clearStreetscapeGeometryRenderObjects = false;

        private bool _waitingForLocationService = false;
        private bool _isInARView = false;
        private bool _isReturning = false;
        private bool _isLocalizing = false;
        private bool _enablingGeospatial = false;
        private bool _shouldResolvingHistory = false;
        private float _localizationPassedTime = 0f;
        private float _configurePrepareTime = 3f;
        private GeospatialAnchorHistoryCollection _historyCollection = null;
        private List<GameObject> _anchorObjects = new List<GameObject>();
        private IEnumerator _startLocationService = null;
        private IEnumerator _asyncCheck = null;
        public static event Action<GeospatialPose> OnGeospatialPoseSampled;
        private Coroutine _poseSampleCoroutine = null;

        [SerializeField] private DestinationMeshPlacer DestinationPlacer;
        [SerializeField] private DebugTextManager DebugTextManager;
        [SerializeField] private GooglePlaceSearcher GooglePlaceSearcher;
        [SerializeField] private RouteRequest RouteRequest;
        [SerializeField] private MapTileUI MapTileUI;

        /// <summary>
        /// プライバシー確認の「Get Started」ボタンが押された時の処理。
        /// プレイヤープレファレンスに記録して AR 表示へ切り替えます。
        /// </summary>
        public void OnGetStartedClicked()
        {
            PlayerPrefs.SetInt(_hasDisplayedPrivacyPromptKey, 1);
            PlayerPrefs.Save();
            SwitchToARView(true);
        }

        /// <summary>
        /// プライバシー確認の「Learn More」ボタンが押された時の処理。
        /// 関連ドキュメントページをブラウザで開きます。
        /// </summary>
        public void OnLearnMoreClicked()
        {
            Application.OpenURL(
                "https://developers.google.com/ar/data-privacy");
        }

        /// <summary>
        /// AR View 上の「Clear All」ボタンが押された時の処理。
        /// 管理しているすべてのアンカーと履歴を破棄して UI を更新します。
        /// </summary>
        public void OnClearAllClicked()
        {
            foreach (var anchor in _anchorObjects)
            {
                Destroy(anchor);
            }

            _anchorObjects.Clear();
            _historyCollection.Collection.Clear();
            SnackBarText.text = "Anchor(s) cleared!";
            ClearAllButton.gameObject.SetActive(false);
            SaveGeospatialAnchorHistory();
        }

        /// <summary>
        /// AR View の「Continue」ボタンが押された時の処理。
        /// VPS チェックキャンバスを閉じます。
        /// </summary>
        public void OnContinueClicked()
        {
            VPSCheckCanvas.SetActive(false);
        }

        /// <summary>
        /// Streetscape Geometry の表示トグルが変更されたときの処理。
        /// </summary>
        /// <param name="enabled">表示する場合は true、非表示は false。</param>
        public void OnGeometryToggled(bool enabled)
        {
            _streetscapeGeometryVisibility = enabled;
            if (!_streetscapeGeometryVisibility)
            {
                _clearStreetscapeGeometryRenderObjects = true;
            }
        }

        /// <summary>
        /// アンカー設定パネルの表示切替ボタンが押されたときの処理。
        /// パネルの表示状態をトグルします。
        /// </summary>
        public void OnAnchorSettingButtonClicked()
        {
            _showAnchorSettingsPanel = !_showAnchorSettingsPanel;
            if (_showAnchorSettingsPanel)
            {
                SetAnchorPanelState(true);
            }
            else
            {
                SetAnchorPanelState(false);
            }
        }

        /// <summary>
        /// Geospatial アンカートグルが切り替えられたときの処理。
        /// アンカー種別を Geospatial に設定します。
        /// </summary>
        /// <param name="enabled">使用可能にする場合は true（未使用だが署名維持）。</param>
        public void OnGeospatialAnchorToggled(bool enabled)
        {
            _anchorType = AnchorType.Geospatial;
            SetAnchorPanelState(false);
        }

        /// <summary>
        /// Terrain アンカートグルが切り替えられたときの処理。
        /// アンカー種別を Terrain に設定します。
        /// </summary>
        /// <param name="enabled">使用可能にする場合は true（未使用だが署名維持）。</param>
        public void OnTerrainAnchorToggled(bool enabled)
        {
            _anchorType = AnchorType.Terrain;
            SetAnchorPanelState(false);
        }

        /// <summary>
        /// Rooftop アンカートグルが切り替えられたときの処理。
        /// アンカー種別を Rooftop に設定します。
        /// </summary>
        /// <param name="enabled">使用可能にする場合は true（未使用だが署名維持）。</param>
        public void OnRooftopAnchorToggled(bool enabled)
        {
            _anchorType = AnchorType.Rooftop;
            SetAnchorPanelState(false);
        }

        /// <summary>
        /// Unity の Awake() イベントハンドラ。
        /// 初期チェック（XROrigin、ARSession、ARCoreExtensions の存在確認）とアプリ設定を行います。
        /// </summary>
        public void Awake()
        {
            // Lock screen to portrait.
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortraitUpsideDown = false;
            // Screen.orientation = ScreenOrientation.Portrait;

            // Enable geospatial sample to target 60fps camera capture frame rate
            // on supported devices.
            // Note, Application.targetFrameRate is ignored when QualitySettings.vSyncCount != 0.
            Application.targetFrameRate = 60;

            if (Origin == null)
            {
                Debug.LogError("Cannot find XROrigin.");
            }

            if (Session == null)
            {
                Debug.LogError("Cannot find ARSession.");
            }

            if (ARCoreExtensions == null)
            {
                Debug.LogError("Cannot find ARCoreExtensions.");
            }
        }

        /// <summary>
        /// Unity の OnEnable() イベントハンドラ。
        /// 位置サービス開始、UI 初期化、イベント購読、履歴ロード、ポーズサンプリングコルーチン起動などを行います。
        /// </summary>
        public void OnEnable()
        {
            _startLocationService = StartLocationService();
            StartCoroutine(_startLocationService);

            _isReturning = false;
            _enablingGeospatial = false;
            InfoPanel.SetActive(false);
            GeometryToggle.gameObject.SetActive(false);
            AnchorSettingButton.gameObject.SetActive(false);
            AnchorSettingPanel.gameObject.SetActive(false);
            GeospatialAnchorToggle.gameObject.SetActive(false);
            TerrainAnchorToggle.gameObject.SetActive(false);
            RooftopAnchorToggle.gameObject.SetActive(false);
            ClearAllButton.gameObject.SetActive(false);
            DebugText.gameObject.SetActive(Debug.isDebugBuild && EarthManager != null);
            GeometryToggle.onValueChanged.AddListener(OnGeometryToggled);
            AnchorSettingButton.onClick.AddListener(OnAnchorSettingButtonClicked);
            GeospatialAnchorToggle.onValueChanged.AddListener(OnGeospatialAnchorToggled);
            TerrainAnchorToggle.onValueChanged.AddListener(OnTerrainAnchorToggled);
            RooftopAnchorToggle.onValueChanged.AddListener(OnRooftopAnchorToggled);

            _localizationPassedTime = 0f;
            _isLocalizing = true;
            SnackBarText.text = _localizingMessage;

            LoadGeospatialAnchorHistory();
            _shouldResolvingHistory = _historyCollection.Collection.Count > 0;

            SwitchToARView(PlayerPrefs.HasKey(_hasDisplayedPrivacyPromptKey));

            if (StreetscapeGeometryManager == null)
            {
                Debug.LogWarning("StreetscapeGeometryManager must be set in the " +
                    "GeospatialController Inspector to render StreetscapeGeometry.");
            }

            if (StreetscapeGeometryMaterialBuilding.Count == 0)
            {
                Debug.LogWarning("StreetscapeGeometryMaterialBuilding in the " +
                    "GeospatialController Inspector must contain at least one material " +
                    "to render StreetscapeGeometry.");
                return;
            }

            if (StreetscapeGeometryMaterialTerrain == null)
            {
                Debug.LogWarning("StreetscapeGeometryMaterialTerrain must be set in the " +
                    "GeospatialController Inspector to render StreetscapeGeometry.");
                return;
            }

            // get access to ARstreetscapeGeometries in ARStreetscapeGeometryManager
            if (StreetscapeGeometryManager)
            {
                StreetscapeGeometryManager.StreetscapeGeometriesChanged += GetStreetscapeGeometry;
            }

            if (GetRoadsButton != null)
            {
                GetRoadsButton.onClick.AddListener(OnGetRoadsButtonClicked);

            }
            // １秒ごとに現在位置を取得するコルーチンを起動
            if (_poseSampleCoroutine == null)
            {
                _poseSampleCoroutine = StartCoroutine(PoseSamplingRoutine());
            }
        }

        /// <summary>
        /// Unity の OnDisable() イベントハンドラ。
        /// 立ち上げたコルーチンの停止、位置サービス停止、アンカー破棄、履歴保存、イベント解除を行います。
        /// </summary>
        public void OnDisable()
        {
            StopCoroutine(_asyncCheck);
            _asyncCheck = null;
            StopCoroutine(_startLocationService);
            _startLocationService = null;
            Debug.Log("Stop location services.");
            Input.location.Stop();

            foreach (var anchor in _anchorObjects)
            {
                Destroy(anchor);
            }

            _anchorObjects.Clear();
            SaveGeospatialAnchorHistory();

            if (StreetscapeGeometryManager)
            {
                StreetscapeGeometryManager.StreetscapeGeometriesChanged -=
                    GetStreetscapeGeometry;
            }
            // １秒ごとに現在位置を取得するコルーチンを停止
            if (_poseSampleCoroutine != null)
            {
                StopCoroutine(_poseSampleCoroutine);
                _poseSampleCoroutine = null;
            }
        }

        /// <summary>
        /// Unity の毎フレーム Update() ハンドラ。
        /// セッション・トラッキング状態を監視し、ローカライズ状態や Streetscape の更新、タッチ入力処理などを行います。
        /// </summary>
        public void Update()
        {
            if (!_isInARView)
            {
                return;
            }

            UpdateDebugInfo();

            // Check session error status.
            LifecycleUpdate();
            if (_isReturning)
            {
                return;
            }

            if (ARSession.state != ARSessionState.SessionInitializing &&
                ARSession.state != ARSessionState.SessionTracking)
            {
                return;
            }

            // Check feature support and enable Geospatial API when it's supported.
            var featureSupport = EarthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);
            switch (featureSupport)
            {
                case FeatureSupported.Unknown:
                    return;
                case FeatureSupported.Unsupported:
                    ReturnWithReason("The Geospatial API is not supported by this device.");
                    return;
                case FeatureSupported.Supported:
                    if (ARCoreExtensions.ARCoreExtensionsConfig.GeospatialMode ==
                        GeospatialMode.Disabled)
                    {
                        Debug.Log("Geospatial sample switched to GeospatialMode.Enabled.");
                        ARCoreExtensions.ARCoreExtensionsConfig.GeospatialMode =
                            GeospatialMode.Enabled;
                        ARCoreExtensions.ARCoreExtensionsConfig.StreetscapeGeometryMode =
                            StreetscapeGeometryMode.Enabled;
                        _configurePrepareTime = 3.0f;
                        _enablingGeospatial = true;
                        return;
                    }

                    break;
            }

            // Waiting for new configuration to take effect.
            if (_enablingGeospatial)
            {
                _configurePrepareTime -= Time.deltaTime;
                if (_configurePrepareTime < 0)
                {
                    _enablingGeospatial = false;
                }
                else
                {
                    return;
                }
            }

            // Check earth state.
            var earthState = EarthManager.EarthState;
            if (earthState == EarthState.ErrorEarthNotReady)
            {
                SnackBarText.text = _localizationInitializingMessage;
                return;
            }
            else if (earthState != EarthState.Enabled)
            {
                string errorMessage =
                    "Geospatial sample encountered an EarthState error: " + earthState;
                Debug.LogWarning(errorMessage);
                SnackBarText.text = errorMessage;
                return;
            }

            // Check earth localization.
            bool isSessionReady = ARSession.state == ARSessionState.SessionTracking &&
                Input.location.status == LocationServiceStatus.Running;
            var earthTrackingState = EarthManager.EarthTrackingState;
            var pose = earthTrackingState == TrackingState.Tracking ?
                EarthManager.CameraGeospatialPose : new GeospatialPose();
            if (!isSessionReady || earthTrackingState != TrackingState.Tracking ||
                pose.OrientationYawAccuracy > _orientationYawAccuracyThreshold ||
                pose.HorizontalAccuracy > _horizontalAccuracyThreshold)
            {
                // Lost localization during the session.
                if (!_isLocalizing)
                {
                    _isLocalizing = true;
                    _localizationPassedTime = 0f;
                    GeometryToggle.gameObject.SetActive(false);
                    AnchorSettingButton.gameObject.SetActive(false);
                    AnchorSettingPanel.gameObject.SetActive(false);
                    GeospatialAnchorToggle.gameObject.SetActive(false);
                    TerrainAnchorToggle.gameObject.SetActive(false);
                    RooftopAnchorToggle.gameObject.SetActive(false);
                    ClearAllButton.gameObject.SetActive(false);
                    foreach (var go in _anchorObjects)
                    {
                        go.SetActive(false);
                    }
                }

                if (_localizationPassedTime > _timeoutSeconds)
                {
                    Debug.LogError("Geospatial sample localization timed out.");
                    ReturnWithReason(_localizationFailureMessage);
                }
                else
                {
                    _localizationPassedTime += Time.deltaTime;
                    SnackBarText.text = _localizationInstructionMessage;
                }
            }
            else if (_isLocalizing)
            {
                // Finished localization.
                _isLocalizing = false;
                _localizationPassedTime = 0f;
                GeometryToggle.gameObject.SetActive(true);
                AnchorSettingButton.gameObject.SetActive(true);
                ClearAllButton.gameObject.SetActive(_anchorObjects.Count > 0);
                SnackBarText.text = _localizationSuccessMessage;
                foreach (var go in _anchorObjects)
                {
                    go.SetActive(true);
                }
                ResolveHistory();
            }
            else
            {
                if (_streetscapeGeometryVisibility)
                {
                    foreach (
                        ARStreetscapeGeometry streetscapegeometry in _addedStreetscapeGeometries)
                    {
                        InstantiateRenderObject(streetscapegeometry);
                    }

                    foreach (
                        ARStreetscapeGeometry streetscapegeometry in _updatedStreetscapeGeometries)
                    {
                        // This second call to instantiate is required if geometry is toggled on
                        // or off after the app has started.
                        InstantiateRenderObject(streetscapegeometry);
                        UpdateRenderObject(streetscapegeometry);
                    }

                    foreach (
                        ARStreetscapeGeometry streetscapegeometry in _removedStreetscapeGeometries)
                    {
                        DestroyRenderObject(streetscapegeometry);
                    }
                }
                else if (_clearStreetscapeGeometryRenderObjects)
                {
                    _clearStreetscapeGeometryRenderObjects = false;
                }

                if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began
                    && !EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)
                    && _anchorObjects.Count < _storageLimit)
                {
                    // Set anchor on screen tap.
                    // 今回は必要ないためコメントアウト
                    // PlaceAnchorByScreenTap(Input.GetTouch(0).position);
                }

                // Hide anchor settings and toggles if the storage limit has been reached.
                if (_anchorObjects.Count >= _storageLimit)
                {
                    AnchorSettingButton.gameObject.SetActive(false);
                    AnchorSettingPanel.gameObject.SetActive(false);
                    GeospatialAnchorToggle.gameObject.SetActive(false);
                    TerrainAnchorToggle.gameObject.SetActive(false);
                    RooftopAnchorToggle.gameObject.SetActive(false);
                }
                else
                {
                    AnchorSettingButton.gameObject.SetActive(true);
                }
            }

            InfoPanel.SetActive(true);
            if (earthTrackingState == TrackingState.Tracking)
            {
                InfoText.text = string.Format(
                "Latitude/Longitude: {1}°, {2}°{0}" +
                "Horizontal Accuracy: {3}m{0}" +
                "Altitude: {4}m{0}" +
                "Vertical Accuracy: {5}m{0}" +
                "Eun Rotation: {6}{0}" +
                "Orientation Yaw Accuracy: {7}°",
                Environment.NewLine,
                pose.Latitude.ToString("F6"),
                pose.Longitude.ToString("F6"),
                pose.HorizontalAccuracy.ToString("F6"),
                pose.Altitude.ToString("F2"),
                pose.VerticalAccuracy.ToString("F2"),
                pose.EunRotation.ToString("F1"),
                pose.OrientationYawAccuracy.ToString("F1"));
            }
            else
            {
                InfoText.text = "GEOSPATIAL POSE: not tracking";
            }
        }

        /// <summary>
        /// 検索ボタン押下時の処理。
        /// 入力に応じてキーワード検索のルート取得処理を開始します。
        /// </summary>
        public void OnGetRoadsButtonClicked()
        {
            if (EarthManager == null) return;

            var position = EarthManager.CameraGeospatialPose;
            double currentLat = position.Latitude;
            double currentLon = position.Longitude;

            string query = GooglePlaceSearcher.inputField.text;

            // テキストボックスが未入力の場合何もせずreturn
            if (string.IsNullOrEmpty(query))
            {
                return;
            }

            GooglePlaceSearcher.StartSearchProcess(query, (double targetLat, double targetLon, string placeName) =>
            {
                DebugTextManager.Log("Places", $"検索完了: {placeName} ({targetLat}, {targetLon})");

                if (DestinationPlacer != null)
                {
                    DestinationPlacer.ClearCurrentRoute();
                }

                StartCoroutine(MapTileUI.LoadMapTile(targetLat, targetLon));
                // OverpassRequest経由でなく直接RouteRequestを使うように変更
                StartCoroutine(RouteRequest.RequestRouteAndPlace(currentLat, currentLon, targetLat, targetLon, placeName));
                
            });
        }
        /// <summary>
        /// ルート検索のラッパーコルーチン
        /// </summary>
        //public IEnumerator HandleRouteCoroutine(double currentLat, double currentLon, double targetLat, double targetLon, string placeName)
        //{
        //    if (RouteRequest != null)
        //    {
        //        // ★修正: 二重呼び出しを削除し、ここでのみ呼び出す
        //        // 現在地(start) -> バス停(target) へのルートをリクエスト
        //        yield return StartCoroutine(
        //    }
        //}


        /// <summary>
        /// 新しいルート取得時に既存メッシュをリセット（アンカー破棄ではなく、ルートラインのみクリア）
        /// </summary>
        //private void ResetARMeshesForNewRoute()
        //{
        //    // RouteLineRenderer（複数存在する場合を含む）をクリア／無効化
        //    var routeRenderers = UnityEngine.Object.FindObjectsByType<RouteLineRenderer>(FindObjectsSortMode.None);
        //    foreach (var r in routeRenderers)
        //    {
        //        if (r == null) continue;
        //        var lr = r.GetComponent<LineRenderer>();
        //        if (lr != null)
        //        {
        //            lr.positionCount = 0;
        //            lr.enabled = false;
        //        }
        //        r.gameObject.SetActive(false);
        //    }

        //    // DestinationPlacer に紐づく routeLineRenderer があれば個別にクリア
        //    if (DestinationPlacer != null && DestinationPlacer.routeLineRenderer != null)
        //    {
        //        var rlr = DestinationPlacer.routeLineRenderer;
        //        var lr2 = rlr.GetComponent<LineRenderer>();
        //        if (lr2 != null)
        //        {
        //            lr2.positionCount = 0;
        //            lr2.enabled = false;
        //        }
        //        rlr.gameObject.SetActive(false);
        //    }

        //    if (SnackBarText != null)
        //    {
        //        SnackBarText.text = "新しいルートを取得しています...";
        //    }

        //    DebugTextManager.Log("Route", "ResetARMeshesForNewRoute: 既存ルートラインをクリアしました。");
        //}

        //private void ResetARMeshes()
        //{
        //    // 管理下のアンカーと履歴をクリア（既存の処理を再利用）
        //    OnClearAllClicked();

        //    // シーン内の ARAnchor（DestinationMeshPlacer が作ったものも含む）を全て破棄
        //    var anchors = UnityEngine.Object.FindObjectsByType<ARAnchor>(FindObjectsSortMode.None);
        //    foreach (var a in anchors)
        //    {
        //        // anchor が null でないことを確認して破棄
        //        if (a != null && a.gameObject != null)
        //        {
        //            Destroy(a.gameObject);
        //        }
        //    }

        //    // Streetscape のレンダーオブジェクトを全削除
        //    DestroyAllRenderObjects();

        //    // RouteLineRenderer（複数存在する場合を含む）をクリア／無効化
        //    var routeRenderers = UnityEngine.Object.FindObjectsByType<RouteLineRenderer>(FindObjectsSortMode.None);
        //    foreach (var r in routeRenderers)
        //    {
        //        if (r == null) continue;
        //        var lr = r.GetComponent<LineRenderer>();
        //        if (lr != null)
        //        {
        //            lr.positionCount = 0;
        //            lr.enabled = false;
        //        }
        //        r.gameObject.SetActive(false);
        //    }

        //    // DestinationPlacer に紐づく routeLineRenderer があれば個別にクリア
        //    if (DestinationPlacer != null && DestinationPlacer.routeLineRenderer != null)
        //    {
        //        var rlr = DestinationPlacer.routeLineRenderer;
        //        var lr2 = rlr.GetComponent<LineRenderer>();
        //        if (lr2 != null)
        //        {
        //            lr2.positionCount = 0;
        //            lr2.enabled = false;
        //        }
        //        rlr.gameObject.SetActive(false);
        //    }

        //    // ユーザー向け通知
        //    if (SnackBarText != null)
        //    {
        //        SnackBarText.text = "ARメッシュをリセットしました。";
        //    }
        //    Debug.Log("ResetARMeshes: cleared anchors, streetscape render objects and route lines.");
        //}

        /// <summary>
        /// OverpassRequest など外部から緯度経度を受け取って DestinationMeshPlacer に渡すための公開受け口。
        /// DestinationPlacer がインスペクタで設定されていればそれを使い、未設定ならシーン内から探して実行します。
        /// </summary>
        /// <param name="lat">目的地の緯度（度）</param>
        /// <param name="lon">目的地の経度（度）</param>
        /// <param name="altitude">高度（省略可）</param>
        /// <param name="heading">見た目の向き（度）</param>
        //public void PlaceDestinationFromLatLon(double lat, double lon, double altitude = double.NaN, double heading = 0.0)
        //{
        //    // 既にインスペクタで割り当てられている場合はそれを使う
        //    if (DestinationPlacer != null)
        //    {
        //        DestinationPlacer.PlaceDestination(lat, lon, altitude, heading, false, -1, null);
        //        return;
        //    }

        //    // 割り当てがない場合はシーン内を検索して自動で実行
        //    var dp = UnityEngine.Object.FindFirstObjectByType<DestinationMeshPlacer>();
        //    if (dp != null)
        //    {
        //        dp.PlaceDestination(lat, lon, altitude, heading, false, -1, null);
        //        return;
        //    }

        //    // どちらも見つからなければユーザーへ通知（SnackBarText は既存フィールド）
        //    if (SnackBarText != null)
        //    {
        //        SnackBarText.text = "DestinationPlacer が見つかりません。DestinationMeshPlacer をシーンに追加してください。";
        //    }
        //    Debug.LogWarning("PlaceDestinationFromLatLon: DestinationMeshPlacer not found in scene.");
        //}

        /// <summary>
        /// ARStreetscapeGeometryManager からの StreetscapeGeometriesChanged イベントを受け取り、
        /// 追加・更新・削除リストを更新します。
        /// </summary>
        /// <param name="eventArgs">変更イベントの引数</param>
        private void GetStreetscapeGeometry(ARStreetscapeGeometriesChangedEventArgs eventArgs)
        {
            _addedStreetscapeGeometries = eventArgs.Added;
            _updatedStreetscapeGeometries = eventArgs.Updated;
            _removedStreetscapeGeometries = eventArgs.Removed;
        }

        /// <summary>
        /// 指定された ARStreetscapeGeometry のメッシュを描画するためのレンダーオブジェクトを作成します。
        /// 既存オブジェクトがある場合は何もしません。
        /// </summary>
        /// <param name="streetscapegeometry">描画対象の StreetscapeGeometry</param>
        private void InstantiateRenderObject(ARStreetscapeGeometry streetscapegeometry)
        {
            if (streetscapegeometry.mesh == null)
            {
                return;
            }

            // Check if a render object already exists for this streetscapegeometry and
            // create one if not.
            if (_streetscapegeometryGOs.ContainsKey(streetscapegeometry.trackableId))
            {
                return;
            }

            GameObject renderObject = new GameObject(
                "StreetscapeGeometryMesh", typeof(MeshFilter), typeof(MeshRenderer));

            if (renderObject)
            {
                renderObject.transform.position = new Vector3(0, 0.5f, 0);
                renderObject.GetComponent<MeshFilter>().mesh = streetscapegeometry.mesh;

                // Add a material with transparent diffuse shader.
                if (streetscapegeometry.streetscapeGeometryType ==
                    StreetscapeGeometryType.Building)
                {
                    renderObject.GetComponent<MeshRenderer>().material =
                        StreetscapeGeometryMaterialBuilding[_buildingMatIndex];
                    _buildingMatIndex =
                        (_buildingMatIndex + 1) % StreetscapeGeometryMaterialBuilding.Count;
                }
                else
                {
                    renderObject.GetComponent<MeshRenderer>().material =
                        StreetscapeGeometryMaterialTerrain;
                }

                renderObject.transform.position = streetscapegeometry.pose.position;
                renderObject.transform.rotation = streetscapegeometry.pose.rotation;

                _streetscapegeometryGOs.Add(streetscapegeometry.trackableId, renderObject);
            }
        }

        /// <summary>
        /// 指定した StreetscapeGeometry に対応するレンダーオブジェクトの位置と回転を更新します。
        /// </summary>
        /// <param name="streetscapegeometry">更新対象の StreetscapeGeometry</param>
        private void UpdateRenderObject(ARStreetscapeGeometry streetscapegeometry)
        {
            if (_streetscapegeometryGOs.ContainsKey(streetscapegeometry.trackableId))
            {
                GameObject renderObject = _streetscapegeometryGOs[streetscapegeometry.trackableId];
                renderObject.transform.position = streetscapegeometry.pose.position;
                renderObject.transform.rotation = streetscapegeometry.pose.rotation;
            }
        }

        /// <summary>
        /// 指定した StreetscapeGeometry に対応するレンダーオブジェクトを破棄します。
        /// </summary>
        /// <param name="streetscapegeometry">破棄対象の StreetscapeGeometry</param>
        private void DestroyRenderObject(ARStreetscapeGeometry streetscapegeometry)
        {
            if (_streetscapegeometryGOs.ContainsKey(streetscapegeometry.trackableId))
            {
                var geometry = _streetscapegeometryGOs[streetscapegeometry.trackableId];
                _streetscapegeometryGOs.Remove(streetscapegeometry.trackableId);
                Destroy(geometry);
            }
        }

        /// <summary>
        /// 登録されているすべての StreetscapeGeometry レンダーオブジェクトを破棄します。
        /// </summary>
        private void DestroyAllRenderObjects()
        {
            var keys = _streetscapegeometryGOs.Keys;
            foreach (var key in keys)
            {
                var renderObject = _streetscapegeometryGOs[key];
                Destroy(renderObject);
            }

            _streetscapegeometryGOs.Clear();
        }

        /// <summary>
        /// アンカー設定パネル内の UI 要素（トグル等）の表示／非表示を切り替えます。
        /// </summary>
        /// <param name="state">表示する場合は true、非表示は false。</param>
        private void SetAnchorPanelState(bool state)
        {
            AnchorSettingPanel.gameObject.SetActive(state);
            GeospatialAnchorToggle.gameObject.SetActive(state);
            TerrainAnchorToggle.gameObject.SetActive(state);
            RooftopAnchorToggle.gameObject.SetActive(state);
        }

        /// <summary>
        /// 屋上アンカー解決の非同期結果を待ち、成功時はプレハブを配置して履歴に保存します。
        /// </summary>
        /// <param name="promise">ResolveAnchorOnRooftopPromise の Promise</param>
        /// <param name="history">保存する GeospatialAnchorHistory</param>
        /// <returns>コルーチン列挙子</returns>
        private IEnumerator CheckRooftopPromise(ResolveAnchorOnRooftopPromise promise,
            GeospatialAnchorHistory history)
        {
            yield return promise;

            var result = promise.Result;
            if (result.RooftopAnchorState == RooftopAnchorState.Success &&
                result.Anchor != null)
            {
                // Adjust the scale of the prefab anchor object to maintain visibility when it is
                // far away.
                result.Anchor.gameObject.transform.localScale *= GetRooftopAnchorScale(
                    result.Anchor.gameObject.transform.position,
                    Camera.main.transform.position);
                GameObject anchorGO = Instantiate(TerrainPrefab,
                    result.Anchor.gameObject.transform);
                anchorGO.transform.parent = result.Anchor.gameObject.transform;

                _anchorObjects.Add(result.Anchor.gameObject);
                _historyCollection.Collection.Add(history);

                SnackBarText.text = GetDisplayStringForAnchorPlacedSuccess();

                ClearAllButton.gameObject.SetActive(_anchorObjects.Count > 0);
                SaveGeospatialAnchorHistory();
            }
            else
            {
                SnackBarText.text = GetDisplayStringForAnchorPlacedFailure();
            }

            yield break;
        }

        /// <summary>
        /// 地形アンカー解決の非同期結果を待ち、成功時はプレハブを配置して履歴に保存します。
        /// </summary>
        /// <param name="promise">ResolveAnchorOnTerrainPromise の Promise</param>
        /// <param name="history">保存する GeospatialAnchorHistory</param>
        /// <returns>コルーチン列挙子</returns>
        private IEnumerator CheckTerrainPromise(ResolveAnchorOnTerrainPromise promise,
            GeospatialAnchorHistory history)
        {
            yield return promise;

            var result = promise.Result;
            if (result.TerrainAnchorState == TerrainAnchorState.Success &&
                result.Anchor != null)
            {
                GameObject anchorGO = Instantiate(TerrainPrefab,
                    result.Anchor.gameObject.transform);
                anchorGO.transform.parent = result.Anchor.gameObject.transform;

                _anchorObjects.Add(result.Anchor.gameObject);
                _historyCollection.Collection.Add(history);

                SnackBarText.text = GetDisplayStringForAnchorPlacedSuccess();

                ClearAllButton.gameObject.SetActive(_anchorObjects.Count > 0);
                SaveGeospatialAnchorHistory();
            }
            else
            {
                SnackBarText.text = GetDisplayStringForAnchorPlacedFailure();
            }

            yield break;
        }

        /// <summary>
        /// 指定したアンカーとカメラ位置に基づいて、屋上アンカーの見た目スケールを調整する係数を返します。
        /// </summary>
        /// <param name="anchor">アンカー位置</param>
        /// <param name="camera">カメラ位置</param>
        /// <returns>スケール係数（通常 1～2 の範囲）</returns>
        private float GetRooftopAnchorScale(Vector3 anchor, Vector3 camera)
        {
            float distance =
                Mathf.Sqrt(
                    Mathf.Pow(anchor.x - camera.x, 2.0f)
                    + Mathf.Pow(anchor.y - camera.y, 2.0f)
                    + Mathf.Pow(anchor.z - camera.z, 2.0f));
            float mapDistance = Mathf.Min(Mathf.Max(2.0f, distance), 20.0f);
            return (mapDistance - 2.0f) / (20.0f - 2.0f) + 1.0f;
        }

        // 今回は必要ないためコメントアウト
        //private void PlaceAnchorByScreenTap(Vector2 position)
        //{
        //    if (_streetscapeGeometryVisibility)
        //    {
        //        // Raycast against streetscapeGeometry.
        //        List<XRRaycastHit> hitResults = new List<XRRaycastHit>();
        //        if (RaycastManager.RaycastStreetscapeGeometry(position, ref hitResults))
        //        {
        //            if (_anchorType == AnchorType.Rooftop || _anchorType == AnchorType.Terrain)
        //            {
        //                var streetscapeGeometry =
        //                    StreetscapeGeometryManager.GetStreetscapeGeometry(
        //                        hitResults[0].trackableId);
        //                if (streetscapeGeometry == null)
        //                {
        //                    return;
        //                }

        //                if (_streetscapegeometryGOs.ContainsKey(streetscapeGeometry.trackableId))
        //                {
        //                    Pose modifiedPose = new Pose(hitResults[0].pose.position,
        //                        Quaternion.LookRotation(Vector3.right, Vector3.up));

        //                    GeospatialAnchorHistory history =
        //                        CreateHistory(modifiedPose, _anchorType);

        //                    // Anchor returned will be null, the coroutine will handle creating
        //                    // the anchor when the promise is done.
        //                    PlaceARAnchor(history, modifiedPose, hitResults[0].trackableId);
        //                }
        //            }
        //            else
        //            {
        //                GeospatialAnchorHistory history = CreateHistory(hitResults[0].pose,
        //                    _anchorType);
        //                var anchor = PlaceARAnchor(history, hitResults[0].pose,
        //                    hitResults[0].trackableId);
        //                if (anchor != null)
        //                {
        //                    _historyCollection.Collection.Add(history);
        //                }

        //                ClearAllButton.gameObject.SetActive(_anchorObjects.Count > 0);
        //                SaveGeospatialAnchorHistory();
        //            }
        //        }

        //        return;
        //    }

        //    // Raycast against detected planes.
        //    List<ARRaycastHit> planeHitResults = new List<ARRaycastHit>();
        //    RaycastManager.Raycast(
        //        position, planeHitResults, TrackableType.Planes | TrackableType.FeaturePoint);
        //    if (planeHitResults.Count > 0)
        //    {
        //        GeospatialAnchorHistory history = CreateHistory(planeHitResults[0].pose,
        //            _anchorType);

        //        if (_anchorType == AnchorType.Rooftop)
        //        {
        //            // The coroutine will create the anchor when the promise is done.
        //            Quaternion eunRotation = CreateRotation(history);
        //            ResolveAnchorOnRooftopPromise rooftopPromise =
        //                AnchorManager.ResolveAnchorOnRooftopAsync(
        //                    history.Latitude, history.Longitude,
        //                    0, eunRotation);

        //            StartCoroutine(CheckRooftopPromise(rooftopPromise, history));
        //            return;
        //        }

        //        var anchor = PlaceGeospatialAnchor(history);
        //        if (anchor != null)
        //        {
        //            _historyCollection.Collection.Add(history);
        //        }

        //        ClearAllButton.gameObject.SetActive(_anchorObjects.Count > 0);
        //        SaveGeospatialAnchorHistory();
        //    }
        //}

        private GeospatialAnchorHistory CreateHistory(Pose pose, AnchorType anchorType)
        {
            GeospatialPose geospatialPose = EarthManager.Convert(pose);

            GeospatialAnchorHistory history = new GeospatialAnchorHistory(
                geospatialPose.Latitude, geospatialPose.Longitude, geospatialPose.Altitude,
                anchorType, geospatialPose.EunRotation);
            return history;
        }

        /// <summary>
        /// 保存履歴に含まれる回転情報から AR 上の回転を生成します。
        /// EunRotation が未設定の場合は Heading から変換します。
        /// </summary>
        /// <param name="history">対象の GeospatialAnchorHistory</param>
        /// <returns>回転クォータニオン</returns>
        private Quaternion CreateRotation(GeospatialAnchorHistory history)
        {
            Quaternion eunRotation = history.EunRotation;
            if (eunRotation == Quaternion.identity)
            {
                // This history is from a previous app version and EunRotation was not used.
                eunRotation =
                    Quaternion.AngleAxis(180f - (float)history.Heading, Vector3.up);
            }

            return eunRotation;
        }

        /// <summary>
        /// Geospatial / Rooftop / Terrain の履歴情報から ARAnchor を配置します。
        /// Rooftop と Terrain は非同期コルーチンで配置し、Geospatial は即時で配置します。
        /// </summary>
        /// <param name="history">配置情報</param>
        /// <param name="pose">任意の基準 Pose（Streetscape 用など）</param>
        /// <param name="trackableId">StreetscapeGeometry の TrackableId（必要な場合）</param>
        /// <returns>配置された ARAnchor（直ちに得られない場合は null）</returns>
        private ARAnchor PlaceARAnchor(GeospatialAnchorHistory history, Pose pose = new Pose(),
            TrackableId trackableId = new TrackableId())
        {
            Quaternion eunRotation = CreateRotation(history);
            ARAnchor anchor = null;
            switch (history.AnchorType)
            {
                case AnchorType.Rooftop:
                    ResolveAnchorOnRooftopPromise rooftopPromise =
                        AnchorManager.ResolveAnchorOnRooftopAsync(
                            history.Latitude, history.Longitude,
                            0, eunRotation);

                    StartCoroutine(CheckRooftopPromise(rooftopPromise, history));
                    return null;

                case AnchorType.Terrain:
                    ResolveAnchorOnTerrainPromise terrainPromise =
                        AnchorManager.ResolveAnchorOnTerrainAsync(
                            history.Latitude, history.Longitude,
                            0, eunRotation);

                    StartCoroutine(CheckTerrainPromise(terrainPromise, history));
                    return null;

                case AnchorType.Geospatial:
                    ARStreetscapeGeometry streetscapegeometry =
                        StreetscapeGeometryManager.GetStreetscapeGeometry(trackableId);
                    if (streetscapegeometry != null)
                    {
                        anchor = StreetscapeGeometryManager.AttachAnchor(
                            streetscapegeometry, pose);
                    }

                    if (anchor != null)
                    {
                        _anchorObjects.Add(anchor.gameObject);
                        _historyCollection.Collection.Add(history);
                        ClearAllButton.gameObject.SetActive(_anchorObjects.Count > 0);
                        SaveGeospatialAnchorHistory();

                        SnackBarText.text = GetDisplayStringForAnchorPlacedSuccess();
                    }
                    else
                    {
                        SnackBarText.text = GetDisplayStringForAnchorPlacedFailure();
                    }

                    break;
            }

            return anchor;
        }

        /// <summary>
        /// GeospatialAnchorHistory を元に Geospatial アンカー（または Terrain/Rooftop）を配置します。
        /// Terrain の場合は非同期で解決します。
        /// </summary>
        /// <param name="history">アンカー履歴</param>
        /// <returns>配置された ARGeospatialAnchor（直ちに得られない場合は null）</returns>
        private ARGeospatialAnchor PlaceGeospatialAnchor(
            GeospatialAnchorHistory history)
        {
            bool terrain = history.AnchorType == AnchorType.Terrain;
            Quaternion eunRotation = CreateRotation(history);
            ARGeospatialAnchor anchor = null;

            if (terrain)
            {
                // Anchor returned will be null, the coroutine will handle creating the
                // anchor when the promise is done.
                ResolveAnchorOnTerrainPromise promise =
                    AnchorManager.ResolveAnchorOnTerrainAsync(
                        history.Latitude, history.Longitude,
                        0, eunRotation);

                StartCoroutine(CheckTerrainPromise(promise, history));
                return null;
            }
            else
            {
                anchor = AnchorManager.AddAnchor(
                    history.Latitude, history.Longitude, history.Altitude, eunRotation);
            }

            if (anchor != null)
            {
                GameObject anchorGO = history.AnchorType == AnchorType.Geospatial ?
                    Instantiate(GeospatialPrefab, anchor.transform) :
                    Instantiate(TerrainPrefab, anchor.transform);
                anchor.gameObject.SetActive(!terrain);
                anchorGO.transform.parent = anchor.gameObject.transform;
                _anchorObjects.Add(anchor.gameObject);
                SnackBarText.text = GetDisplayStringForAnchorPlacedSuccess();
            }
            else
            {
                SnackBarText.text = GetDisplayStringForAnchorPlacedFailure();
            }

            return anchor;
        }

        /// <summary>
        /// 保存された履歴を順次復元してアンカーを配置します。
        /// </summary>
        private void ResolveHistory()
        {
            if (!_shouldResolvingHistory)
            {
                return;
            }

            _shouldResolvingHistory = false;
            foreach (var history in _historyCollection.Collection)
            {
                switch (history.AnchorType)
                {
                    case AnchorType.Rooftop:
                        PlaceARAnchor(history);
                        break;
                    case AnchorType.Terrain:
                        PlaceARAnchor(history);
                        break;
                    default:
                        PlaceGeospatialAnchor(history);
                        break;
                }
            }

            ClearAllButton.gameObject.SetActive(_anchorObjects.Count > 0);
            SnackBarText.text = string.Format("{0} anchor(s) set from history.",
                _anchorObjects.Count);
        }

        /// <summary>
        /// PlayerPrefs から Geospatial アンカー履歴を読み込み、24 時間より古い履歴を除外します。
        /// </summary>
        private void LoadGeospatialAnchorHistory()
        {
            if (PlayerPrefs.HasKey(_persistentGeospatialAnchorsStorageKey))
            {
                _historyCollection = JsonUtility.FromJson<GeospatialAnchorHistoryCollection>(
                    PlayerPrefs.GetString(_persistentGeospatialAnchorsStorageKey));

                // Remove all records created more than 24 hours and update stored history.
                DateTime current = DateTime.Now;
                _historyCollection.Collection.RemoveAll(
                    data => current.Subtract(data.CreatedTime).Days > 0);
                PlayerPrefs.SetString(_persistentGeospatialAnchorsStorageKey,
                    JsonUtility.ToJson(_historyCollection));
                PlayerPrefs.Save();
            }
            else
            {
                _historyCollection = new GeospatialAnchorHistoryCollection();
            }
        }

        /// <summary>
        /// 現在の履歴を PlayerPrefs に保存します。容量上限を超える古い履歴は削除します。
        /// </summary>
        private void SaveGeospatialAnchorHistory()
        {
            // Sort the data from latest record to earliest record.
            _historyCollection.Collection.Sort((left, right) =>
                right.CreatedTime.CompareTo(left.CreatedTime));

            // Remove the earliest data if the capacity exceeds storage limit.
            if (_historyCollection.Collection.Count > _storageLimit)
            {
                _historyCollection.Collection.RemoveRange(
                    _storageLimit, _historyCollection.Collection.Count - _storageLimit);
            }

            PlayerPrefs.SetString(
                _persistentGeospatialAnchorsStorageKey, JsonUtility.ToJson(_historyCollection));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// AR 表示（ARView）と関連コンポーネントの有効/無効を切り替えます。
        /// 有効にする場合は AR の可用性チェックコルーチンを開始します。
        /// </summary>
        /// <param name="enable">AR 表示に切り替える場合は true、非表示は false。</param>
        private void SwitchToARView(bool enable)
        {
            _isInARView = enable;
            Origin.gameObject.SetActive(enable);
            Session.gameObject.SetActive(enable);
            ARCoreExtensions.gameObject.SetActive(enable);
            ARViewCanvas.SetActive(enable);
            PrivacyPromptCanvas.SetActive(!enable);
            VPSCheckCanvas.SetActive(false);
            if (enable && _asyncCheck == null)
            {
                _asyncCheck = AvailabilityCheck();
                StartCoroutine(_asyncCheck);
            }
        }

        /// <summary>
        /// デバイスでの VPS 利用可能性や必要な権限を確認する非同期コルーチン。
        /// </summary>
        /// <returns>IEnumerator（コルーチン）</returns>
        private IEnumerator AvailabilityCheck()
        {
            if (ARSession.state == ARSessionState.None)
            {
                yield return ARSession.CheckAvailability();
            }

            // Waiting for ARSessionState.CheckingAvailability.
            yield return null;

            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                yield return ARSession.Install();
            }

            // Waiting for ARSessionState.Installing.
            yield return null;
#if UNITY_ANDROID

            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Debug.Log("Requesting camera permission.");
                Permission.RequestUserPermission(Permission.Camera);
                yield return new WaitForSeconds(3.0f);
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                // User has denied the request.
                Debug.LogWarning(
                    "Failed to get the camera permission. VPS availability check isn't available.");
                yield break;
            }
#endif

            while (_waitingForLocationService)
            {
                yield return null;
            }

            if (Input.location.status != LocationServiceStatus.Running)
            {
                Debug.LogWarning(
                    "Location services aren't running. VPS availability check is not available.");
                yield break;
            }

            // Update event is executed before coroutines so it checks the latest error states.
            if (_isReturning)
            {
                yield break;
            }

            var location = Input.location.lastData;
            var vpsAvailabilityPromise =
                AREarthManager.CheckVpsAvailabilityAsync(location.latitude, location.longitude);
            yield return vpsAvailabilityPromise;

            Debug.LogFormat("VPS Availability at ({0}, {1}): {2}",
                location.latitude, location.longitude, vpsAvailabilityPromise.Result);
            VPSCheckCanvas.SetActive(vpsAvailabilityPromise.Result != VpsAvailability.Available);
        }

        /// <summary>
        /// 位置情報サービスを開始するコルーチン。
        /// 権限リクエスト、サービス開始、初期化完了待ちを行います。
        /// </summary>
        /// <returns>IEnumerator（コルーチン）</returns>
        private IEnumerator StartLocationService()
        {
            _waitingForLocationService = true;
#if UNITY_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Debug.Log("Requesting the fine location permission.");
                Permission.RequestUserPermission(Permission.FineLocation);
                yield return new WaitForSeconds(3.0f);
            }
#endif

            if (!Input.location.isEnabledByUser)
            {
                Debug.Log("Location service is disabled by the user.");
                _waitingForLocationService = false;
                yield break;
            }

            Debug.Log("Starting location service.");
            Input.location.Start();

            while (Input.location.status == LocationServiceStatus.Initializing)
            {
                yield return null;
            }

            _waitingForLocationService = false;
            if (Input.location.status != LocationServiceStatus.Running)
            {
                Debug.LogWarningFormat(
                    "Location service ended with {0} status.", Input.location.status);
                Input.location.Stop();
            }
        }

        /// <summary>
        /// 毎フレームのライフサイクルチェックを行い、アプリ終了やエラー状態からの復帰処理を行います。
        /// </summary>
        private void LifecycleUpdate()
        {
            // Pressing 'back' button quits the app.
            if (Input.GetKeyUp(KeyCode.Escape))
            {
                Application.Quit();
            }

            if (_isReturning)
            {
                return;
            }

            // Only allow the screen to sleep when not tracking.
            var sleepTimeout = SleepTimeout.NeverSleep;
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                sleepTimeout = SleepTimeout.SystemSetting;
            }

            Screen.sleepTimeout = sleepTimeout;

            // Quit the app if ARSession is in an error status.
            string returningReason = string.Empty;
            if (ARSession.state != ARSessionState.CheckingAvailability &&
                ARSession.state != ARSessionState.Ready &&
                ARSession.state != ARSessionState.SessionInitializing &&
                ARSession.state != ARSessionState.SessionTracking)
            {
                returningReason = string.Format(
                    "Geospatial sample encountered an ARSession error state {0}.\n" +
                    "Please restart the app.",
                    ARSession.state);
            }
            else if (Input.location.status == LocationServiceStatus.Failed)
            {
                returningReason =
                    "Geospatial sample failed to start location service.\n" +
                    "Please restart the app and grant the fine location permission.";
            }
            else if (Origin == null || Session == null || ARCoreExtensions == null)
            {
                returningReason = string.Format(
                    "Geospatial sample failed due to missing AR Components.");
            }

            ReturnWithReason(returningReason);
        }

        /// <summary>
        /// エラー理由を受け取り UI 表示と終了処理を行います。
        /// 理由が空文字列の場合は何もしません。
        /// </summary>
        /// <param name="reason">終了理由のメッセージ</param>
        private void ReturnWithReason(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return;
            }

            GeometryToggle.gameObject.SetActive(false);
            AnchorSettingButton.gameObject.SetActive(false);
            AnchorSettingPanel.gameObject.SetActive(false);
            GeospatialAnchorToggle.gameObject.SetActive(false);
            TerrainAnchorToggle.gameObject.SetActive(false);
            RooftopAnchorToggle.gameObject.SetActive(false);
            ClearAllButton.gameObject.SetActive(false);
            InfoPanel.SetActive(false);

            Debug.LogError(reason);
            SnackBarText.text = reason;
            _isReturning = true;
            Invoke(nameof(QuitApplication), _errorDisplaySeconds);
        }

        /// <summary>
        /// アプリケーションを終了します（Invoke で呼び出されます）。
        /// </summary>
        private void QuitApplication()
        {
            Application.Quit();
        }

        /// <summary>
        /// デバッグ表示用の情報を組み立てて DebugText に出力します。
        /// </summary>
        private void UpdateDebugInfo()
        {
            if (!Debug.isDebugBuild || EarthManager == null)
            {
                return;
            }

            var pose = EarthManager.EarthState == EarthState.Enabled &&
                EarthManager.EarthTrackingState == TrackingState.Tracking ?
                EarthManager.CameraGeospatialPose : new GeospatialPose();
            var supported = EarthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);
            DebugText.text =
                $"IsReturning: {_isReturning}\n" +
                $"IsLocalizing: {_isLocalizing}\n" +
                $"SessionState: {ARSession.state}\n" +
                $"LocationServiceStatus: {Input.location.status}\n" +
                $"FeatureSupported: {supported}\n" +
                $"EarthState: {EarthManager.EarthState}\n" +
                $"EarthTrackingState: {EarthManager.EarthTrackingState}\n" +
                $"  LAT/LNG: {pose.Latitude:F6}, {pose.Longitude:F6}\n" +
                $"  HorizontalAcc: {pose.HorizontalAccuracy:F6}\n" +
                $"  ALT: {pose.Altitude:F2}\n" +
                $"  VerticalAcc: {pose.VerticalAccuracy:F2}\n" +
                $". EunRotation: {pose.EunRotation:F2}\n" +
                $"  OrientationYawAcc: {pose.OrientationYawAccuracy:F2}";
        }

        /// <summary>
        /// アンカー配置成功時に表示する文字列を生成します。
        /// </summary>
        /// <returns>成功表示用の文字列</returns>
        private string GetDisplayStringForAnchorPlacedSuccess()
        {
            return string.Format(
                    "{0} / {1} Anchor(s) Set!", _anchorObjects.Count, _storageLimit);
        }

        /// <summary>
        /// アンカー配置失敗時に表示する文字列を生成します。
        /// </summary>
        /// <returns>失敗表示用の文字列</returns>
        private string GetDisplayStringForAnchorPlacedFailure()
        {
            return string.Format(
                    "Failed to set a {0} anchor!", _anchorType);
        }

        /// <summary>
        /// 1 秒ごとに GeospatialPose をサンプリングし、OnGeospatialPoseSampled イベントで配信するコルーチン。
        /// </summary>
        /// <returns>IEnumerator（コルーチン）</returns>
        private IEnumerator PoseSamplingRoutine()
        {
            var wait = new WaitForSeconds(1f);
            while (true)
            {
                if (EarthManager != null &&
                    EarthManager.EarthState == EarthState.Enabled &&
                    EarthManager.EarthTrackingState == TrackingState.Tracking)
                {
                    var pose = EarthManager.CameraGeospatialPose;
                    // イベントで配信（購読者がいなければ何もしない）
                    OnGeospatialPoseSampled?.Invoke(pose);
                }

                yield return wait;
            }
        }
    }
}
