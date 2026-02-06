using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[RequireComponent(typeof(CanvasRenderer))]
public class RouteDrawer : MaskableGraphic
{
    public List<Vector2> routePoints = new List<Vector2>();
    public float lineWidth = 4f;

    // 角マーカー用
    public List<Vector2> cornerPoints = new List<Vector2>();
    public float markerSize = 8f;
    public Color markerColor = Color.red;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        int vertCount = 0;

        // ライン描画（2点以上なら描画）
        if (routePoints != null && routePoints.Count >= 2)
        {
            for (int i = 0; i < routePoints.Count - 1; i++)
            {
                Vector2 p1 = routePoints[i];
                Vector2 p2 = routePoints[i + 1];

                Vector2 dir = (p2 - p1).normalized;
                Vector2 normal = new Vector2(-dir.y, dir.x) * (lineWidth / 2f);

                UIVertex[] verts = new UIVertex[4];
                verts[0] = UIVertex.simpleVert;
                verts[1] = UIVertex.simpleVert;
                verts[2] = UIVertex.simpleVert;
                verts[3] = UIVertex.simpleVert;

                verts[0].position = p1 + normal;
                verts[1].position = p1 - normal;
                verts[2].position = p2 - normal;
                verts[3].position = p2 + normal;

                for (int v = 0; v < 4; v++)
                {
                    verts[v].color = color;
                    vh.AddVert(verts[v]);
                }

                vh.AddTriangle(vertCount, vertCount + 1, vertCount + 2);
                vh.AddTriangle(vertCount, vertCount + 2, vertCount + 3);

                vertCount += 4;
            }
        }

        // 角マーカーを描画（routePointsの有無に関係なく表示）
        if (cornerPoints != null && cornerPoints.Count > 0)
        {
            float half = markerSize / 2f;
            foreach (var p in cornerPoints)
            {
                UIVertex[] mverts = new UIVertex[4];
                mverts[0] = UIVertex.simpleVert;
                mverts[1] = UIVertex.simpleVert;
                mverts[2] = UIVertex.simpleVert;
                mverts[3] = UIVertex.simpleVert;

                // 四角（矩形）を描画
                mverts[0].position = p + new Vector2(-half, -half);
                mverts[1].position = p + new Vector2(-half, half);
                mverts[2].position = p + new Vector2(half, half);
                mverts[3].position = p + new Vector2(half, -half);

                for (int v = 0; v < 4; v++)
                {
                    mverts[v].color = markerColor;
                    vh.AddVert(mverts[v]);
                }

                vh.AddTriangle(vertCount, vertCount + 1, vertCount + 2);
                vh.AddTriangle(vertCount, vertCount + 2, vertCount + 3);

                vertCount += 4;
            }
        }
    }

    public void SetRoute(List<Vector2> points)
    {
        routePoints = points ?? new List<Vector2>();
        SetVerticesDirty();
    }

    // MapTileUI から渡す UI (ピクセル) 座標の角リストを設定
    public void SetCorners(List<Vector2> points)
    {
        cornerPoints = points ?? new List<Vector2>();
        SetVerticesDirty();
    }

    public void ClearCorners()
    {
        cornerPoints.Clear();
        SetVerticesDirty();
    }
}
