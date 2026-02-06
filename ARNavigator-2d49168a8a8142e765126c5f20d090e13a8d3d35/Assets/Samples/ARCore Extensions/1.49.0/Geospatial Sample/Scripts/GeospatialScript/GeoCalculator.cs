using System;
using UnityEngine;

public class GeoCalculator : MonoBehaviour
{
    // 定数
    public const double EarthRadius = 6378137.0; // WGS84
    public const double Deg2Rad = Math.PI / 180.0;

    /// <summary>
    /// WGS84 緯度経度から ENU (East-North-Up) 座標系の相対メートル座標を計算する。
    /// すべて double で計算し、最後に Vector2d で返す。
    /// </summary>
    /// <param name="lon">対象の経度</param>
    /// <param name="lat">対象の緯度</param>
    /// <param name="refLon">基準点の経度</param>
    /// <param name="refLat">基準点の緯度</param>
    /// <returns>x=East(m), y=North(m)</returns>
    public static Vector2d GeoToENU(double lon, double lat, double refLon, double refLat)
    {
        // 緯度経度の差分
        double dLon = (lon - refLon) * Deg2Rad;
        double dLat = (lat - refLat) * Deg2Rad;

        // 基準緯度（ラジアン）
        double refLatRad = refLat * Deg2Rad;

        // 東西方向 (East): 経度差 × (地球半径 × cos(緯度))
        double east = EarthRadius * dLon * Math.Cos(refLatRad);

        // 南北方向 (North): 緯度差 × 地球半径
        double north = EarthRadius * dLat;

        return new Vector2d(east, north);
    }

    /// <summary>
    /// 緯度経度から距離を計算（メートル単位）
    /// </summary>
    public static double GetDistance(double lat1, double lon1, double lat2, double lon2)
    {
        // ... (既存のコード: 変更なし) ...
        const double R = 6371000;
        double radLat1 = lat1 * Math.PI / 180.0;
        double radLat2 = lat2 * Math.PI / 180.0;
        double deltaLat = (lat2 - lat1) * Math.PI / 180.0;
        double deltaLon = (lon2 - lon1) * Math.PI / 180.0;

        double a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                   Math.Cos(radLat1) * Math.Cos(radLat2) *
                   Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    /// <summary>
    /// 現在地から目的地への方位角（北=0°、東=90°）
    /// </summary>
    public static double GetDirection(double lat1, double lon1, double lat2, double lon2)
    {
        // ... (既存のコード: 変更なし) ...
        double φ1 = lat1 * Mathf.Deg2Rad;
        double φ2 = lat2 * Mathf.Deg2Rad;
        double Δλ = (lon2 - lon1) * Mathf.Deg2Rad;

        double y = Math.Sin(Δλ) * Math.Cos(φ2);
        double x = Math.Cos(φ1) * Math.Sin(φ2) -
                   Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        double θ = Math.Atan2(y, x);
        double bearing = (θ * Mathf.Rad2Deg + 360) % 360;

        return bearing;
    }

    /// <summary>
    /// 近傍探索での距離比較用に距離の二乗を返す
    /// </summary>
    public static double GetDistanceSquared(double lat1, double lon1, double lat2, double lon2)
    {
        // ... (既存のコード: 変更なし) ...
        const double R = 6371000;
        double radLat1 = lat1 * Math.PI / 180.0;
        double radLat2 = lat2 * Math.PI / 180.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;

        double x = dLon * Math.Cos((radLat1 + radLat2) / 2.0);
        double y = dLat;

        return (R * R) * (x * x + y * y);
    }
}