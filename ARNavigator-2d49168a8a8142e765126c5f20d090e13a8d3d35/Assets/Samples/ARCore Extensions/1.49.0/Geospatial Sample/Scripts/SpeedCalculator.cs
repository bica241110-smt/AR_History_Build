using Google.XR.ARCoreExtensions;
using Google.XR.ARCoreExtensions.Samples.Geospatial;
using System.Collections.Generic;
using UnityEngine;

public class SpeedCalculator : MonoBehaviour
{
    // 外部公開プロパティ
    public float CurrentKmh { get; private set; } = 0f;

    [Header("Settings")]
    [SerializeField] private float _targetInterval = 0.5f; // 0.5秒間の移動距離を見る
    [SerializeField] private float _minMoveThreshold = 0.2f; // 0.5秒で20cm以下の移動はノイズとみなす
    [SerializeField] private float _smoothingFactor = 0.1f; // 数値の変化を滑らかにする (0に近いほど滑らかだが遅延する)

    private struct Sample
    {
        public double Lat;
        public double Lon;
        public float Time;
        // Altは使わないので削除
    }

    private readonly Queue<Sample> _samples = new Queue<Sample>();
    private const float _maxKeepSeconds = 5.0f;

    private void OnEnable()
    {
        GeospatialController.OnGeospatialPoseSampled += HandlePoseSampled;
    }

    private void OnDisable()
    {
        GeospatialController.OnGeospatialPoseSampled -= HandlePoseSampled;
    }

    private void HandlePoseSampled(GeospatialPose pose)
    {
        float now = Time.time;

        // 1. 高度(Alt)は速度計算に使わないため保存しない（水平精度のみ重視）
        var current = new Sample
        {
            Lat = pose.Latitude,
            Lon = pose.Longitude,
            Time = now
        };
        _samples.Enqueue(current);

        // 古いデータを掃除
        while (_samples.Count > 0 && now - _samples.Peek().Time > _maxKeepSeconds)
        {
            _samples.Dequeue();
        }

        // 比較対象の過去フレームを探す
        float targetTime = now - _targetInterval;
        Sample? olderSample = null;

        foreach (var s in _samples)
        {
            if (s.Time <= targetTime) olderSample = s;
            else break;
        }

        if (olderSample.HasValue)
        {
            var s = olderSample.Value;
            float dt = now - s.Time;

            if (dt > 0.1f) // 0除算防止＆あまりに短すぎる時間は無視
            {
                // 2. 水平距離のみ計算する (高度は無視)
                double distanceM = GeoCalculator.GetDistance(s.Lat, s.Lon, current.Lat, current.Lon);

                // 3. ノイズゲート: 移動距離があまりに小さい場合は「止まっている」とみなす
                if (distanceM < _minMoveThreshold)
                {
                    distanceM = 0;
                }

                // 速度計算 (m/s)
                double speedMps = distanceM / dt;

                // 時速変換 (km/h)
                float rawKmh = (float)(speedMps * 3.6);

                // 4. スムージング (急激な数値変動を抑える)
                // 現在の値から新しい値へ、少しずつ近づける
                CurrentKmh = Mathf.Lerp(CurrentKmh, rawKmh, _smoothingFactor);
            }
        }
    }
}