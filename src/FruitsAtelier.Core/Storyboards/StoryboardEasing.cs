// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See Core/Conversion/LICENCE.osu-framework.txt for the full licence text.

using System;

namespace FruitsAtelier.Core
{
    /// <summary>
    /// An easing function corresponding to one of the <see cref="StoryboardEasingKind"/> types.
    /// </summary>
    public readonly struct StoryboardEasing
    {
        private const double elastic_const = 2 * Math.PI / .3;
        private const double elastic_const2 = .3 / 4;

        private const double back_const = 1.70158;
        private const double back_const2 = back_const * 1.525;

        private const double bounce_const = 1 / 2.75;

        // constants used to fix expo and elastic curves to start/end at 0/1
        private static readonly double expo_offset = Math.Pow(2, -10);
        private static readonly double elastic_offset_full = Math.Pow(2, -11);
        private static readonly double elastic_offset_half = Math.Pow(2, -10) * Math.Sin((.5 - elastic_const2) * elastic_const);
        private static readonly double elastic_offset_quarter = Math.Pow(2, -10) * Math.Sin((.25 - elastic_const2) * elastic_const);
        private static readonly double in_out_elastic_offset = Math.Pow(2, -10) * Math.Sin((1 - elastic_const2 * 1.5) * elastic_const / 1.5);

        private readonly StoryboardEasingKind easing;

        /// <summary>
        /// Creates a new <see cref="DefaultEasingFunction"/> for an <see cref="StoryboardEasingKind"/>.
        /// </summary>
        /// <param name="easing">The <see cref="StoryboardEasingKind"/> type.</param>
        public StoryboardEasing(StoryboardEasingKind easing)
        {
            this.easing = easing;
        }

        public double ApplyEasing(double time)
        {
            switch (easing)
            {
                default:
                    return time;

                case StoryboardEasingKind.In:
                case StoryboardEasingKind.InQuad:
                    return time * time;

                case StoryboardEasingKind.Out:
                case StoryboardEasingKind.OutQuad:
                    return time * (2 - time);

                case StoryboardEasingKind.InOutQuad:
                    if (time < .5) return time * time * 2;

                    return --time * time * -2 + 1;

                case StoryboardEasingKind.InCubic:
                    return time * time * time;

                case StoryboardEasingKind.OutCubic:
                    return --time * time * time + 1;

                case StoryboardEasingKind.InOutCubic:
                    if (time < .5) return time * time * time * 4;

                    return --time * time * time * 4 + 1;

                case StoryboardEasingKind.InQuart:
                    return time * time * time * time;

                case StoryboardEasingKind.OutQuart:
                    return 1 - --time * time * time * time;

                case StoryboardEasingKind.InOutQuart:
                    if (time < .5) return time * time * time * time * 8;

                    return --time * time * time * time * -8 + 1;

                case StoryboardEasingKind.InQuint:
                    return time * time * time * time * time;

                case StoryboardEasingKind.OutQuint:
                    return --time * time * time * time * time + 1;

                case StoryboardEasingKind.InOutQuint:
                    if (time < .5) return time * time * time * time * time * 16;

                    return --time * time * time * time * time * 16 + 1;

                case StoryboardEasingKind.InSine:
                    return 1 - Math.Cos(time * Math.PI * .5);

                case StoryboardEasingKind.OutSine:
                    return Math.Sin(time * Math.PI * .5);

                case StoryboardEasingKind.InOutSine:
                    return .5 - .5 * Math.Cos(Math.PI * time);

                case StoryboardEasingKind.InExpo:
                    return Math.Pow(2, 10 * (time - 1)) + expo_offset * (time - 1);

                case StoryboardEasingKind.OutExpo:
                    return -Math.Pow(2, -10 * time) + 1 + expo_offset * time;

                case StoryboardEasingKind.InOutExpo:
                    if (time < .5) return .5 * (Math.Pow(2, 20 * time - 10) + expo_offset * (2 * time - 1));

                    return 1 - .5 * (Math.Pow(2, -20 * time + 10) + expo_offset * (-2 * time + 1));

                case StoryboardEasingKind.InCirc:
                    return 1 - Math.Sqrt(1 - time * time);

                case StoryboardEasingKind.OutCirc:
                    return Math.Sqrt(1 - --time * time);

                case StoryboardEasingKind.InOutCirc:
                    if ((time *= 2) < 1) return .5 - .5 * Math.Sqrt(1 - time * time);

                    return .5 * Math.Sqrt(1 - (time -= 2) * time) + .5;

                case StoryboardEasingKind.InElastic:
                    return -Math.Pow(2, -10 + 10 * time) * Math.Sin((1 - elastic_const2 - time) * elastic_const) + elastic_offset_full * (1 - time);

                case StoryboardEasingKind.OutElastic:
                    return Math.Pow(2, -10 * time) * Math.Sin((time - elastic_const2) * elastic_const) + 1 - elastic_offset_full * time;

                case StoryboardEasingKind.OutElasticHalf:
                    return Math.Pow(2, -10 * time) * Math.Sin((.5 * time - elastic_const2) * elastic_const) + 1 - elastic_offset_half * time;

                case StoryboardEasingKind.OutElasticQuarter:
                    return Math.Pow(2, -10 * time) * Math.Sin((.25 * time - elastic_const2) * elastic_const) + 1 - elastic_offset_quarter * time;

                case StoryboardEasingKind.InOutElastic:
                    if ((time *= 2) < 1)
                    {
                        return -.5 * (Math.Pow(2, -10 + 10 * time) * Math.Sin((1 - elastic_const2 * 1.5 - time) * elastic_const / 1.5)
                                      - in_out_elastic_offset * (1 - time));
                    }

                    return .5 * (Math.Pow(2, -10 * --time) * Math.Sin((time - elastic_const2 * 1.5) * elastic_const / 1.5)
                                 - in_out_elastic_offset * time) + 1;

                case StoryboardEasingKind.InBack:
                    return time * time * ((back_const + 1) * time - back_const);

                case StoryboardEasingKind.OutBack:
                    return --time * time * ((back_const + 1) * time + back_const) + 1;

                case StoryboardEasingKind.InOutBack:
                    if ((time *= 2) < 1) return .5 * time * time * ((back_const2 + 1) * time - back_const2);

                    return .5 * ((time -= 2) * time * ((back_const2 + 1) * time + back_const2) + 2);

                case StoryboardEasingKind.InBounce:
                    time = 1 - time;
                    if (time < bounce_const)
                        return 1 - 7.5625 * time * time;
                    if (time < 2 * bounce_const)
                        return 1 - (7.5625 * (time -= 1.5 * bounce_const) * time + .75);
                    if (time < 2.5 * bounce_const)
                        return 1 - (7.5625 * (time -= 2.25 * bounce_const) * time + .9375);

                    return 1 - (7.5625 * (time -= 2.625 * bounce_const) * time + .984375);

                case StoryboardEasingKind.OutBounce:
                    if (time < bounce_const)
                        return 7.5625 * time * time;
                    if (time < 2 * bounce_const)
                        return 7.5625 * (time -= 1.5 * bounce_const) * time + .75;
                    if (time < 2.5 * bounce_const)
                        return 7.5625 * (time -= 2.25 * bounce_const) * time + .9375;

                    return 7.5625 * (time -= 2.625 * bounce_const) * time + .984375;

                case StoryboardEasingKind.InOutBounce:
                    if (time < .5) return .5 - .5 * new StoryboardEasing(StoryboardEasingKind.OutBounce).ApplyEasing(1 - time * 2);

                    return new StoryboardEasing(StoryboardEasingKind.OutBounce).ApplyEasing((time - .5) * 2) * .5 + .5;

                case StoryboardEasingKind.OutPow10:
                    return --time * Math.Pow(time, 10) + 1;
            }
        }
    }
}
