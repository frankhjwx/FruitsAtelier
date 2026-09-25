using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private void DrawCatcherTrail(ICanvas c, CatchTrail trail, float fieldLeft, float fieldWidth, float catchY)
    {
        float age = (float)(playhead - trail.TimeMs);
        float progress = Math.Clamp(age / (trail.AfterImage ? 1200 : 800), 0, 1);
        float eased = progress * progress;
        uint colour = trail.AfterImage ? HyperDashAfterImageColour : trail.Hyper ? HyperDashColour : 0xFFFFFF;
        DrawCatcherBody(c, fieldLeft + (float)trail.X * fieldWidth / 512,
            catchY - (trail.AfterImage ? 10 * eased * fieldWidth / 512 : 0),
            fieldWidth * (trail.AfterImage ? .95f + .25f * eased : 1), colour,
            trail.AfterImage ? 1 - progress : .4f * MathF.Pow(1 - progress, 5), true, trail.FacingLeft);
    }

    private void DrawCatcherBody(ICanvas c, float bodyX, float plateY, float drawWidth, uint color,
        float opacity, bool additive, bool facingLeft)
    {
        if (opacity <= 0) return;
        if (skin?.DrawCatcher(c, bodyX, plateY, drawWidth, PreviewCircleSize, color, opacity, additive, facingLeft) == true) return;
        float size = CatchSize.CatcherWidth(PreviewCircleSize) * drawWidth / 512;
        if (color == 0xFFFFFF) color = 0xB5C9D0;
        float plateHeight = Math.Max(2, size * .055f);
        if (opacity == 1) c.Fill(new(bodyX - size / 2, plateY, size, plateHeight), color, 2);
        else c.Line(bodyX - size / 2, plateY + plateHeight / 2, bodyX + size / 2, plateY + plateHeight / 2, color, plateHeight, opacity);
        c.Circle(bodyX, plateY + size * .25f, size * .12f, color, opacity: opacity);
        Segment(-.35f, .08f, .35f, .08f, .05f);
        Segment(0, .37f, 0, .63f, .08f);
        Segment(0, .43f, -.3f, .12f, .06f);
        Segment(0, .43f, .3f, .12f, .06f);
        Segment(0, .62f, -.15f, .88f, .07f);
        Segment(0, .62f, .15f, .88f, .07f);
        void Segment(float x1, float y1, float x2, float y2, float weight)
            => c.Line(bodyX + size * x1, plateY + size * y1, bodyX + size * x2, plateY + size * y2, color, Math.Max(2, size * weight), opacity);
    }
}
