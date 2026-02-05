using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class RouteLineRenderer : MonoBehaviour
{
    private LineRenderer lineRenderer;

    // アルファ値を少し上げ、視認性を確保
    private static readonly Color RouteColor = new Color(0f, 1f, 1f, 0.7f);

    void Awake()
    {
        InitializeLineRenderer();
    }

    private void InitializeLineRenderer()
    {
        if (lineRenderer != null) return;

        lineRenderer = GetComponent<LineRenderer>();

        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
            DebugTextManager.Log("LineRenderer", "LineRenderer を新規追加しました。");
        }

        // --- 修正ポイント①: 初期化で座標系を決めない ---
        lineRenderer.enabled = false; // 最初の描画命令が来るまで無効化
        lineRenderer.positionCount = 0;

        // --- 修正ポイント②: AR/URPに強いマテリアル設定 ---
        if (lineRenderer.material == null || lineRenderer.material.shader.name == "Sprites/Default")
        {
            // URPのUnlitシェーダーを優先的に探し、なければUnlit/Colorを使用
            Shader arShader = Shader.Find("Universal Render Pipeline/Unlit"); // ?? Shader.Find("Unlit/Color");
            Material mat = new Material(arShader);

            // 1. Surface Type を Transparent (1) に変更
            mat.SetFloat("_Surface", 1.0f);

            // 2. Blend Mode を Alpha (0) に設定
            mat.SetFloat("_Blend", 0.0f);

            // 3. GPUに「アルファブレンド」を行うよう指示 (SrcAlpha, OneMinusSrcAlpha)
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            // 4. 深度（奥行き）の書き込みをオフにする（透明物が重なった時の不具合防止）
            mat.SetInt("_ZWrite", 0);

            // 5. レンダリング順序を「透明物用」の番号(3000)に移動
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // 6. URP固有のキーワードを有効化
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            lineRenderer.material = mat;
        }

        lineRenderer.startColor = RouteColor;
        lineRenderer.endColor = RouteColor;
        
        // 線の幅 (0.3mはARナビでは標準的で良い設定です)
        lineRenderer.startWidth = 0.3f;
        lineRenderer.endWidth = 0.3f;

        // 重なり順の優先度を少し上げる
        lineRenderer.sortingOrder = 5;

        DebugTextManager.Set("LineRenderer", "RouteLineRenderer initialized (Standby).");
    }

    /// <summary>
    /// 経路をローカル座標で描画（ARナビの基本）
    /// </summary>
    public void DrawRouteLocal(List<Vector3> localPositions)
    {
        // ローカル描画時は、親（アンカー）が存在することを確認
        if (transform.parent == null)
        {
            Debug.LogWarning("DrawRouteLocal called, but LineRenderer has no parent anchor!");
        }
        DrawInternal(localPositions, useWorldSpace: false);
    }

    /// <summary>
    /// 経路をワールド座標で描画（デバッグ用）
    /// </summary>
    // public void DrawRoute(List<Vector3> worldPositions)
    // {
    //     DrawInternal(worldPositions, useWorldSpace: true);
    // }

    private void DrawInternal(List<Vector3> positions, bool useWorldSpace)
    {
        if (!ValidateLineRenderer()) return;

        if (positions == null || positions.Count < 2)
        {
            lineRenderer.positionCount = 0;
            lineRenderer.enabled = false;
            return;
        }

        // --- 修正ポイント③: 描画タイミングで座標系を確定させる ---
        lineRenderer.useWorldSpace = useWorldSpace;
        lineRenderer.positionCount = positions.Count;

        for (int i = 0; i < positions.Count; i++)
        {
            lineRenderer.SetPosition(i, positions[i]);
        }

        lineRenderer.enabled = true;

        // デバッグログの出力
        LogDebugInfo(positions, useWorldSpace);
    }

    private void LogDebugInfo(List<Vector3> positions, bool isWorld)
    {
        Vector3 min = positions[0];
        Vector3 max = positions[0];
        foreach (var p in positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        DebugTextManager.Set("Range", 
            $"描画: {positions.Count}点 / {(isWorld ? "World" : "Local")}\n" +
            $"Range: ({min.x:F1},{min.z:F1}) to ({max.x:F1},{max.z:F1})");
    }

    private bool ValidateLineRenderer()
    {
        if (lineRenderer == null) InitializeLineRenderer();
        return lineRenderer != null;
    }

    /// <summary>
    /// 線の透明度（アルファ値）を動的に変更する
    /// </summary>
    /// <param name="alpha">0.0 (透明) ～ 1.0 (不透明)</param>
    public void SetAlpha(float alpha)
    {
        if (lineRenderer == null) return;

        // 現在のカラーを取得してAlphaだけ上書き
        Color c = lineRenderer.startColor;
        c.a = alpha;

        lineRenderer.startColor = c;
        lineRenderer.endColor = c;
    }
}