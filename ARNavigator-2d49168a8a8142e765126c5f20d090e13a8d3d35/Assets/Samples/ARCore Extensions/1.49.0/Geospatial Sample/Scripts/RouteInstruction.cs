public class RouteInstruction
{
    public string text;      // "右折です" などのテキスト
    public int sign;         // 方向フラグ (-3:鋭い左, -2:左, 2:右, 4:目的地など)
    public double distance;  // その区間の長さ
    public int lastNodeIndex; // この指示が終了するノードのインデックス
}