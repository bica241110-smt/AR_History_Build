using Google.XR.ARCoreExtensions.Samples.Geospatial;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

public class MapTileUI : MonoBehaviour
{
    [Header("UI Components")]
    public RawImage mapImage;  // ← Canvas 上の RawImage を指定
    // public RouteDrawer routeDrawer;
    public int zoom = 15;

    // 現在表示中のタイル座標を保持する変数
    private int tileX, tileY;

    private Vector2Int osmVec;

    // LoadMapTile の引数を緯度経度として明確化 (lat, lon)
    public IEnumerator LoadMapTile(double lat, double lon)
    {
        yield return new WaitForSecondsRealtime(1);

        // 緯度経度からタイル座標を計算
        osmVec = LatLonToTile(lat, lon, zoom);

        // クラスメンバ変数に保存（LatLonToPixelで使用するため）
        tileX = osmVec.x;
        tileY = osmVec.y;

        DebugTextManager.Set("OSM", $"Loading tile at Zoom: {zoom}, X: {tileX}, Y: {tileY}");

        string url = $"https://tile.openstreetmap.org/{zoom}/{tileX}/{tileY}.png";
        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Texture2D tex = DownloadHandlerTexture.GetContent(req);
                if (tex != null)
                {
                    if (mapImage != null)
                    {
                        mapImage.texture = tex;
                        // UV Rect調整 (必要なら)
                        mapImage.uvRect = new Rect(0, 0, 1, 1);
                        mapImage.enabled = true;
                        Debug.Log("Map tile loaded and applied to RawImage.");
                    }
                    else
                    {
                        Debug.LogError("mapImage is not assigned in the Inspector.");
                    }
                }
                else
                {
                    Debug.LogError("Download returned null texture.");
                }
            }
            else
            {
                long code = req.responseCode;
                Debug.LogError($"Failed to load map tile: {req.error}, responseCode: {code}");
            }
        }
    }

    public Vector2Int LatLonToTile(double lat, double lon, int zoom)
    {
        double latRad = lat * Mathf.Deg2Rad;
        double n = Math.Pow(2.0, zoom);
        double x = (lon + 180.0) / 360.0 * n;
        double y = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;
        int tileXInt = (int)Math.Floor(x);
        int tileYInt = (int)Math.Floor(y);
        return new Vector2Int(tileXInt, tileYInt);
    }

    // 修正：タイル内のピクセル座標（現在表示中のタイルを原点にしたUI座標）を返す
    // 引数を double に変更
    public Vector2 LatLonToPixel(double lat, double lon)
    {
        //int tileSize = 256;

        // --- 修正箇所 ---
        // RawImage の実際の RectTransform のサイズを取得する
        float currentUISize = mapImage.rectTransform.rect.width;
        // OSMの標準タイルサイズ
        float originalTileSize = 256f;
        // 表示サイズと標準サイズの比率（スケール）を計算
        float scale = currentUISize / originalTileSize;
        // ----------------

        double latRad = lat * Math.PI / 180.0; // Mathf.Deg2Rad ではなく double 精度で計算
        double n = Math.Pow(2.0, zoom);

        //double xf = (lon + 180.0) / 360.0 * n; // タイル単位（小数点含む）

        //// メルカトル図法の緯度計算
        //// Math.Tan, Math.Cos を使用して double 精度を維持
        //double yf = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;

        //double pixelXGlobal = xf * tileSize;
        //double pixelYGlobal = yf * tileSize;

        // 現在表示しているタイルの左上グローバルピクセル座標
        // LoadMapTile で保存した tileX, tileY を使用
        //double originX = tileX * tileSize;
        //double originY = tileY * tileSize;

        // UI座標系への変換
        // OSMのタイル座標系: 原点は左上、Yは下向き
        // Unity UI (RawImage): 原点は中心または左下が多いが、ここではタイル内の相対座標(0~256)を計算
        // 必要に応じて Y 軸を反転させる
        // 例: 原点(tileX, tileY)からの相対位置
        //double dx = pixelXGlobal - originX;
        //double dy = pixelYGlobal - originY;

        // UnityのCanvas座標系（Yが上向き）に合わせる場合、256 - dy にする等の調整が必要かもしれないが、
        // RouteDrawerの実装に依存するため、ここでは単純な相対座標を返す。
        // もし上下反転している場合は dy = tileSize - dy; などを検討してください。

        // OSM: Y is down. Unity UI: Y is usually up. 
        // If we want coordinates relative to the top-left of the image:
        // dx is correct (positive right). dy is positive down.
        // To convert to Unity UI (assuming pivot is top-left, Y is down in some systems or Y is up in others):
        // ここでは、単純に「ロードした画像の左上からのピクセルオフセット」として返します。

        //// UI表示用に float にキャスト
        //return new Vector2((float)dx, (float)(256.0 - dy)); // Y反転の例 (左下原点の場合)

        double xf = (lon + 180.0) / 360.0 * n;
        double yf = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;

        // 標準の 256px ベースでの相対座標を計算
        double dx = (xf - tileX) * originalTileSize;
        double dy = (yf - tileY) * originalTileSize;

        // スケールを適用して、現在のUIサイズに合わせる
        // Y方向：Unity UIが左下原点の場合、(256 - dy) * scale
        return new Vector2((float)(dx * scale), (float)((originalTileSize - dy) * scale));
    }
}