using UnityEngine;

namespace Gigaduck.PlayFromView
{
    /// <summary>What a placer did, for the console line and for tests.</summary>
    public readonly struct PlacementReport
    {
        /// <summary>Name of the object that was moved.</summary>
        public string Target { get; }

        /// <summary>Distance in metres between the player's camera and the requested position afterwards.</summary>
        public float PositionError { get; }

        /// <summary>Angle in degrees between the player's camera and the requested rotation afterwards.</summary>
        public float AngleError { get; }

        public PlacementReport(string target, float positionError, float angleError)
        {
            Target = target;
            PositionError = positionError;
            AngleError = angleError;
        }
    }

    /// <summary>
    /// Puts a player rig at a Scene view pose. Different rigs keep their look angles in different
    /// places, so a placer knows one kind of rig; register more with
    /// <see cref="PlayFromView.RegisterPlacer"/>. The highest <see cref="Priority"/> placer that
    /// finds a player wins.
    /// </summary>
    public interface IPlayFromViewPlacer
    {
        /// <summary>Unique name. Registering the same name again replaces the earlier placer.</summary>
        string Name { get; }

        /// <summary>Higher runs first. The generic placer is 0; rig-specific placers should be above it.</summary>
        int Priority { get; }

        /// <summary>
        /// Looks for this placer's kind of player and, if it exists, moves it so its camera matches
        /// <paramref name="pose"/>. Return false when there is no such player (yet); the tool keeps
        /// trying for a few seconds because players are sometimes spawned after the scene loads.
        /// </summary>
        bool TryPlace(in ViewPose pose, out PlacementReport report);
    }
}
