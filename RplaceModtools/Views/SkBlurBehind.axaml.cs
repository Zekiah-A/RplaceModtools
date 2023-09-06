using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace RplaceModtools.Views;

/// <summary>
/// Credit goes to  @kekekeks on github (https://github.com/kekekeks), where a majority of the blur logic was
/// sourced from https://gist.github.com/kekekeks/ac06098a74fe87d49a9ff9ea37fa67bc. Multiple adaptions have been made.
/// </summary>
public partial class SkBlurBehind : UserControl
{
    public static readonly StyledProperty<ExperimentalAcrylicMaterial?> MaterialProperty = AvaloniaProperty
        .Register<SkBlurBehind, ExperimentalAcrylicMaterial?>("Material");

    public ExperimentalAcrylicMaterial? Material
    {
        get => GetValue(MaterialProperty);
        set => SetValue(MaterialProperty, value);
    }
    
    public static readonly DirectProperty<SkBlurBehind, float> BlurRadiusProperty =
        AvaloniaProperty.RegisterDirect<SkBlurBehind, float>(nameof(BlurRadius),
            instance => instance.BlurRadius,
            (instance, value) => instance.BlurRadius = value);

    private float blurRadius = 3.0f;
    
    public float BlurRadius
    {
        get => blurRadius;
        set
        {
            SetAndRaise(BlurRadiusProperty, ref blurRadius, value);
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    private static readonly ImmutableExperimentalAcrylicMaterial DefaultAcrylicMaterial = 
        (ImmutableExperimentalAcrylicMaterial) new ExperimentalAcrylicMaterial()
        {
            MaterialOpacity = 0.5,
            TintColor = Colors.Azure,
            TintOpacity = 0.5,
            PlatformTransparencyCompensationLevel = 0
        }.ToImmutable();
    
    static SkBlurBehind()
    {
        AffectsRender<SkBlurBehind>(MaterialProperty);
    }
    
    private static SKShader? sAcrylicNoiseShader;

    private class BlurBehindRenderOperation : ICustomDrawOperation
    {
        public Rect Bounds => bounds.Inflate(4);

        private readonly ImmutableExperimentalAcrylicMaterial material;
        private readonly Rect bounds;
        private readonly SkBlurBehind parent;

        public BlurBehindRenderOperation(ImmutableExperimentalAcrylicMaterial material, Rect bounds, SkBlurBehind parent)
        {
            this.material = material;
            this.bounds = bounds;
            this.parent = parent;
        }
        
        public void Dispose()
        {
            
        }

        public bool HitTest(Point p) => bounds.Contains(p);

        private static SKColorFilter CreateAlphaColorFilter(double opacity)
        {
            opacity = Math.Clamp(opacity, 0, 1);
            
            // 256x256
            var colour = new byte[256];
            var alpha = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                colour[i] = (byte) i;
                alpha[i] = (byte) (i * opacity);
            }

            return SKColorFilter.CreateTable(alpha, colour, colour, colour);
        }
        
        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null)
            {
                Console.WriteLine("[ERROR] Could not get ISkiaSharpApiLeaseFeature feature. Perhaps rendering backend is not skia?");
                return;
            }
            using var skia = leaseFeature.Lease();
            if (!skia.SkCanvas.TotalMatrix.TryInvert(out var currentInvertedTransform))
            {
                return;
            }

            if (skia.SkSurface is null)
            {
                return;
            }
            
            using var backgroundSnapshot = skia.SkSurface.Snapshot();
            using var backdropShader = SKShader.CreateImage(backgroundSnapshot, SKShaderTileMode.Clamp,
                SKShaderTileMode.Clamp, currentInvertedTransform);
            
            // Hack to allow for editor preview blur
            if (skia.GrContext == null)
            {
                using var filter = SKImageFilter.CreateBlur(parent.blurRadius, parent.blurRadius, SKShaderTileMode.Clamp);
                using var temporaryBlur = new SKPaint
                {
                    Shader = backdropShader,
                    ImageFilter = filter
                };
                
                skia.SkCanvas.DrawRect(0, 0, (float)bounds.Width, (float)bounds.Height, temporaryBlur);
                return;
            }
            
            using var blurred = SKSurface.Create(skia.GrContext, false, new SKImageInfo(
                (int)Math.Ceiling(bounds.Width),
                (int)Math.Ceiling(bounds.Height), SKImageInfo.PlatformColorType, SKAlphaType.Premul));
            using(var filter = SKImageFilter.CreateBlur(parent.blurRadius, parent.blurRadius, SKShaderTileMode.Clamp))
            using (var blurPaint = new SKPaint
            {
                Shader = backdropShader,
                ImageFilter = filter
            })
            blurred.Canvas.DrawRect(0, 0, (float)bounds.Width, (float)bounds.Height, blurPaint);
            
            using (var blurSnap = blurred.Snapshot())
            using (var blurSnapShader = SKShader.CreateImage(blurSnap))
            {
                using (var blurSnapPaint = new SKPaint
                {
                    Shader = blurSnapShader,
                    IsAntialias = true
                })
                    
                skia.SkCanvas.DrawRect(0, 0, (float)bounds.Width, (float)bounds.Height, blurSnapPaint);
            }

            using var acrylicPaint = new SKPaint();
            acrylicPaint.IsAntialias = true;
            
            const double noiseOpacity = 0.0225;

            var tintColor = material.TintColor;
            var tint = new SKColor(tintColor.R, tintColor.G, tintColor.B, tintColor.A);

            if (sAcrylicNoiseShader == null)
            {
                using var stream = typeof(SkiaPlatform).Assembly.GetManifestResourceStream("Avalonia.Skia.Assets.NoiseAsset_256X256_PNG.png");
                using var bitmap = SKBitmap.Decode(stream);
                
                sAcrylicNoiseShader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat)
                    .WithColorFilter(CreateAlphaColorFilter(noiseOpacity));
            }
            
            using var backdrop = SKShader.CreateColor(new SKColor(material.MaterialColor.R, material.MaterialColor.G,
                material.MaterialColor.B, material.MaterialColor.A));
            using var tintShader = SKShader.CreateColor(tint);
            using var effectiveTint = SKShader.CreateCompose(backdrop, tintShader);
            using (var compose = SKShader.CreateCompose(effectiveTint, sAcrylicNoiseShader))
            {
                acrylicPaint.Shader = compose;
                acrylicPaint.IsAntialias = true;
                skia.SkCanvas.DrawRect(0, 0, (float)bounds.Width, (float)bounds.Height, acrylicPaint);
            }
        }

        public bool Equals(ICustomDrawOperation? other)
        {
            return other is BlurBehindRenderOperation op && op.bounds == bounds && op.material.Equals(material);
        }
    }

    public override void Render(DrawingContext context)
    {
        var material = Material != null
            ? (ImmutableExperimentalAcrylicMaterial) Material.ToImmutable()
            : DefaultAcrylicMaterial;
        
        context.Custom(new BlurBehindRenderOperation(material, new Rect(default, Bounds.Size), this));
    }
}