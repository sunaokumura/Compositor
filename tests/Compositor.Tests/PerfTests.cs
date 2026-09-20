using Compositor;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Compositor.Tests;

public class PerfTests
{
    readonly ITestOutputHelper outp;
    public PerfTests(ITestOutputHelper o) { outp = o; }

    static SKBitmap Solid(int w, int h, SKColor c)
    {
        var b = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(c);
        return b;
    }

    [Fact]
    public void Compose_4K_3Layers_Completes()
    {
        var doc = new Document { Width = 3840, Height = 2160 };
        doc.Layers.Add(new Layer { Name = "base", Bitmap = Solid(3840, 2160, SKColors.White) });
        doc.Layers.Add(new Layer { Name = "red", Bitmap = Solid(1920, 1080, new SKColor(255, 0, 0)), Position = new SKPoint(100, 100) });
        doc.Layers.Add(new Layer { Name = "blue", Bitmap = Solid(800, 600, new SKColor(0, 0, 255)), Position = new SKPoint(500, 500), Opacity = 0.5f });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var flat = doc.Compose();
        sw.Stop();
        outp.WriteLine($"4K compose 3 layers: {sw.ElapsedMilliseconds}ms size={flat.Width}x{flat.Height}");
        Assert.Equal(3840, flat.Width);
        // CI/WSLソフトウェア描画でも5秒以内に終わること（実用レベルの上限目安）
        Assert.True(sw.ElapsedMilliseconds < 5000, $"4K compose took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Stress_ComposeLoop_NoLeak_NoCrash()
    {
        // 100時間相当の自動操作の縮小版：1000回合成＋Undo/Redo往復を連続実行し、
        // クラッシュ0・メモリの異常増加なしを確認する。
        var doc = new Document { Width = 800, Height = 600 };
        doc.Layers.Add(new Layer { Name = "a", Bitmap = Solid(800, 600, SKColors.White) });
        doc.Layers.Add(new Layer { Name = "b", Bitmap = Solid(400, 300, new SKColor(255, 0, 0)), Position = new SKPoint(10, 10) });
        var stack = new UndoStack();
        long memBefore = System.GC.GetTotalMemory(true);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            using var flat = doc.Compose();
            if (i % 100 == 0)
            {
                stack.Push(doc);
                doc.Layers[1].Position = new SKPoint(i % 400, (i * 7) % 300);
                stack.Undo(doc);
                stack.Redo(doc);
            }
        }
        sw.Stop();
        long memAfter = System.GC.GetTotalMemory(true);
        outp.WriteLine($"stress 1000 composes: {sw.ElapsedMilliseconds}ms mem {memBefore / 1024}KB -> {memAfter / 1024}KB");
        // 1000回で50MB以上の増加はリーク疑い
        Assert.True(memAfter - memBefore < 50 * 1024 * 1024, $"possible leak: +{(memAfter - memBefore) / 1024}KB");
    }
}
