using System;
using Sanderling.Settings;

namespace Sanderling.Physics
{
    /// <summary>
    /// The user's simplified navigation volume: a vertical capsule, in model units.
    ///
    ///            ___   foot + Height
    ///           (   )
    ///           |   |  capsule segment = vertical line, radius = Radius
    ///           |   |
    ///           (___)  foot + StepHeight
    ///                  <- below this: handled by ground detection (stairs, kerbs, ramps)
    ///   ground ======  foot
    ///
    /// The capsule starts at the step height, so steps are climbed by the ground rules instead of being
    /// treated as walls, while everything above the step height (walls, columns, furniture, railings,
    /// low ceilings) is handled by collision.
    /// </summary>
    internal sealed class NavigationBody
    {
        public double Radius { get; private set; }
        public double Height { get; private set; }
        public double StepHeight { get; private set; }
        public double EyeHeight { get; private set; }

        /// <summary>Largest horizontal distance moved per collision substep (prevents passing through thin walls).</summary>
        public double MaxSubstepLength { get { return Radius * 0.5; } }

        public static NavigationBody Create(NavigationSettings nav, CollisionSettings collision, double metersToModel)
        {
            NavigationBody body = new NavigationBody();
            body.Radius = collision.BodyRadius * metersToModel;
            body.EyeHeight = nav.EyeHeight * metersToModel;
            body.StepHeight = nav.MaxStepHeight * metersToModel;

            // The body must reach above the eyes, otherwise the camera could look through ceilings
            double minHeight = (nav.EyeHeight + 0.05) * metersToModel;
            body.Height = Math.Max(collision.BodyHeight * metersToModel, minHeight);
            return body;
        }

        /// <summary>Lower end of the capsule segment for a given foot height.</summary>
        public double SegmentBottom(double foot)
        {
            return foot + StepHeight + Radius;
        }

        /// <summary>Upper end of the capsule segment (never below the lower end).</summary>
        public double SegmentTop(double foot)
        {
            return Math.Max(SegmentBottom(foot), foot + Height - Radius);
        }
    }
}
