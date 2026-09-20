using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

/// <summary>t_548a86a5: 選択系 (楕円・投繩・Wand・加減算・画素移動・選択消去・切抜コピー).</summary>
public class SelectionToolsTests
{
    static SKBitmap Solid(int w, int h, SKColor c)
    {
        var b = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(c);
        return b;
    }

    [Fact]
    public void DragBox_Square_And_FromCenter()
    {
        // Shift正方形: (10,10)->(20,30) は 20x20 になる (Mac DragBox.rect相当)
        var r = SelectionTools.DragBoxRect(new SKPoint(10, 10), new SKPoint(20, 30), square: true, fromCenter: false);
        Assert.Equal(20, r.Width);
        Assert.Equal(20, r.Height);
        // Option中心: anchor(50,50)->(60,55) は (40,45,60,55)
        var c = SelectionTools.DragBoxRect(new SKPoint(50, 50), new SKPoint(60, 55), square: false, fromCenter: true);
        Assert.Equal(new SKRect(40, 45, 60, 55), c);
    }

    [Fact]
    public void Ellipse_Selection_ContainsCenter_NotCorner()
    {
        var doc = new Document { Width = 100, Height = 100 };
        SelectionTools.SetRectSelection(doc, new SKRect(10, 10, 50, 50), SelectionMode.Replace, SelectionKind.Ellipse);
        Assert.NotNull(doc.Selection);
        Assert.Equal(SelectionKind.Ellipse, doc.SelKind);
        Assert.True(doc.InsideSelection(new SKPoint(30, 30)));    // 中心は内側
        Assert.False(doc.InsideSelection(new SKPoint(11, 11)));   // 矩形角は楕円外
        Assert.False(doc.InsideSelection(new SKPoint(90, 90)));
    }

    [Fact]
    public void Lasso_Confirm_Polygon_And_TinyRejected()
    {
        var doc = new Document { Width = 100, Height = 100 };
        var tri = new List<SKPoint> { new(10, 10), new(60, 10), new(35, 60) };
        Assert.True(SelectionTools.ConfirmPolygon(doc, tri, SelectionMode.Replace, SelectionKind.Freehand));
        Assert.Equal(SelectionKind.Freehand, doc.SelKind);
        Assert.NotNull(doc.SelectionPolygon);
        Assert.True(doc.InsideSelection(new SKPoint(35, 25)));
        Assert.False(doc.InsideSelection(new SKPoint(80, 80)));
        // 極小ドラフトは確定しない
        var doc2 = new Document { Width = 100, Height = 100 };
        Assert.False(SelectionTools.ConfirmPolygon(doc2,
            new List<SKPoint> { new(5, 5), new(6, 5), new(5, 6) }, SelectionMode.Replace, SelectionKind.Polygon));
        Assert.Null(doc2.Selection);
    }

    [Fact]
    public void Wand_Contiguous_FillsConnectedRegionOnly()
    {
        var doc = new Document { Width = 20, Height = 20 };
        var bmp = Solid(20, 20, SKColors.Blue);
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = SKColors.Red })
            canvas.DrawRect(0, 0, 10, 20, paint);   // 左半分=赤
        int n = SelectionTools.WandSelect(doc, bmp, new SKPoint(2, 5), tolerance: 10, sampleRadius: 0, contiguous: true, SelectionMode.Replace);
        Assert.Equal(200, n);                        // 10x20 の赤領域のみ
        Assert.Equal(SelectionKind.Wand, doc.SelKind);
        Assert.True(doc.InsideSelection(new SKPoint(2, 5)));
        Assert.False(doc.InsideSelection(new SKPoint(15, 5)));
    }

    [Fact]
    public void Wand_Global_MatchesDisconnectedPixels()
    {
        var doc = new Document { Width = 20, Height = 20 };
        var bmp = Solid(20, 20, SKColors.White);
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(1, 1, 4, 4, paint);      // 離れた2つの赤矩形
            canvas.DrawRect(14, 14, 4, 4, paint);
        }
        int n = SelectionTools.WandSelect(doc, bmp, new SKPoint(2, 2), tolerance: 10, sampleRadius: 0, contiguous: false, SelectionMode.Replace);
        Assert.Equal(32, n);                         // 両方とも選択
        Assert.True(doc.InsideSelection(new SKPoint(15, 15)));
    }

    [Fact]
    public void Selection_Add_Subtract()
    {
        var doc = new Document { Width = 100, Height = 100 };
        SelectionTools.SetRectSelection(doc, new SKRect(10, 10, 30, 30), SelectionMode.Replace);
        SelectionTools.SetRectSelection(doc, new SKRect(20, 20, 50, 50), SelectionMode.Add);
        Assert.NotNull(doc.Selection);
        Assert.Equal(10, doc.Selection.Value.Left);  // 和集合の外接
        Assert.Equal(50, doc.Selection.Value.Right);
        Assert.True(doc.InsideSelection(new SKPoint(12, 12)));
        Assert.True(doc.InsideSelection(new SKPoint(45, 45)));
        // 減算: 後半を削ると前半だけ残る
        SelectionTools.SetRectSelection(doc, new SKRect(20, 0, 100, 100), SelectionMode.Subtract);
        Assert.NotNull(doc.Selection);
        Assert.False(doc.InsideSelection(new SKPoint(45, 45)));
        Assert.True(doc.InsideSelection(new SKPoint(12, 12)));
        // 全消しは選択なしになる (明示的empty)
        SelectionTools.SetRectSelection(doc, new SKRect(0, 0, 100, 100), SelectionMode.Subtract);
        Assert.Null(doc.Selection);
    }

    [Fact]
    public void PixelMove_Cut_Move_Commit()
    {
        var doc = new Document { Width = 40, Height = 40 };
        var l = new Layer { Name = "m", Bitmap = Solid(40, 40, SKColors.Red) };
        doc.Layers.Add(l);
        SelectionTools.SetRectSelection(doc, new SKRect(0, 0, 10, 10), SelectionMode.Replace);
        var f = SelectionTools.BeginPixelMove(doc, l, duplicate: false);
        Assert.NotNull(f);
        Assert.Equal(0, l.Bitmap.GetPixel(5, 5).Alpha);       // 切出し元は透明
        Assert.Equal(255, l.Bitmap.GetPixel(20, 20).Alpha);   // 外は不変
        SelectionTools.MoveFloating(f, 20, 0);
        SelectionTools.CommitPixelMove(doc, l, f);
        var moved = l.Bitmap.GetPixel(25, 5);
        Assert.Equal(255, moved.Alpha);                        // 移動先に合成
        Assert.Equal(255, moved.Red);
        Assert.NotNull(doc.Selection);                         // 枠も追従
        Assert.Equal(20, doc.Selection.Value.Left);
    }

    [Fact]
    public void DeleteSelection_ClearsInside_Only()
    {
        var doc = new Document { Width = 20, Height = 20 };
        var l = new Layer { Name = "d", Bitmap = Solid(20, 20, SKColors.Red) };
        doc.Layers.Add(l);
        Assert.Equal(0, SelectionTools.DeleteSelection(doc, l));   // 選択なしは0
        SelectionTools.SetRectSelection(doc, new SKRect(0, 0, 10, 10), SelectionMode.Replace);
        Assert.True(SelectionTools.DeleteSelection(doc, l) > 0);
        Assert.Equal(0, l.Bitmap.GetPixel(5, 5).Alpha);
        Assert.Equal(255, l.Bitmap.GetPixel(15, 15).Alpha);
    }

    [Fact]
    public void CopySelection_CropsToBounds_WithOrigin()
    {
        var doc = new Document { Width = 40, Height = 40 };
        var l = new Layer { Name = "c", Bitmap = Solid(40, 40, SKColors.Green) };
        doc.Layers.Add(l);
        SelectionTools.SetRectSelection(doc, new SKRect(10, 10, 30, 25), SelectionMode.Replace);
        var copied = SelectionTools.CopySelection(doc, l);
        Assert.NotNull(copied);
        Assert.Equal(20, copied.Value.bmp.Width);
        Assert.Equal(15, copied.Value.bmp.Height);
        Assert.Equal(new SKPoint(10, 10), copied.Value.origin);
        Assert.Equal(255, copied.Value.bmp.GetPixel(0, 0).Alpha);
    }
}
