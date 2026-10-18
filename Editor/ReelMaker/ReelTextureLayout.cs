using System;

namespace AX.UdonSlot.REV1_0.Editor.ReelMaker
{
    // 座標は画像の左上基準。端数は各コマの境界に分配し、余白を必ず確保する。
    internal static class ReelTextureLayout
    {
        internal struct Cell
        {
            public int X, Y, Width, Height;
        }

        internal static string Validate(int width, int height, int count, int bottom, int side)
        {
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height > 16777216)
                return "出力サイズは各辺1～8192 px、合計16,777,216ピクセル以内にしてください。";
            if (count <= 0) return "3リールすべてに1つ以上の図柄を設定してください。";
            if (bottom < 0 || side < 0) return "余白は0 px以上にしてください。";
            if (width / 3L - 2L * side < 1 || height / (long)count - bottom < 1)
                return "余白を除いた1コマの幅・高さが1 px以上になるように設定してください。";
            return null;
        }

        internal static Cell GetCell(int width, int height, int count, int column, int row, int bottom, int side)
        {
            if (Validate(width, height, count, bottom, side) != null || column < 0 || column >= 3 || row < 0 || row >= count)
                throw new ArgumentOutOfRangeException("リール画像の配置設定が不正です。");
            int left = (int)((long)column * width / 3);
            int right = (int)((long)(column + 1) * width / 3);
            int top = (int)((long)row * height / count);
            int next = (int)((long)(row + 1) * height / count);
            return new Cell { X = left + side, Y = top, Width = right - left - side * 2, Height = next - top - bottom };
        }
    }
}
