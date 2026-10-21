using System;
using System.Linq;
using UnityEditor;

namespace Gigaduck.LightmapWorkbench
{
    /// <summary>Snapshots the eight FBX lightmap UV generation settings and applies only explicitly changed fields.</summary>
    [Serializable]
    internal struct LightmapUVSettings
    {
        public bool generate;
        public float hardAngle, angleError, areaError, packMargin, minResolution, minScale;
        public ModelImporterSecondaryUVMarginMethod marginMethod;

        internal static LightmapUVSettings Read(ModelImporter importer) => new LightmapUVSettings
        {
            generate = importer.generateSecondaryUV,
            hardAngle = importer.secondaryUVHardAngle,
            angleError = importer.secondaryUVAngleDistortion,
            areaError = importer.secondaryUVAreaDistortion,
            marginMethod = importer.secondaryUVMarginMethod,
            packMargin = importer.secondaryUVPackMargin,
            minResolution = importer.secondaryUVMinLightmapResolution,
            minScale = importer.secondaryUVMinObjectScale
        };

        internal void Validate()
        {
            if (new[] { hardAngle, angleError, areaError, packMargin, minResolution, minScale }
                .Any(v => float.IsNaN(v) || float.IsInfinity(v)) ||
                hardAngle < 0 || hardAngle > 180 || angleError < 1 || angleError > 75 ||
                areaError < 1 || areaError > 75 || packMargin < 1 || packMargin > 64 ||
                minResolution <= 0 || minScale <= 0 ||
                !Enum.IsDefined(typeof(ModelImporterSecondaryUVMarginMethod), marginMethod))
                throw new ArgumentOutOfRangeException(nameof(LightmapUVSettings), "Invalid lightmap UV settings. Resolution and scale must be positive and finite.");
        }

        internal void ApplyTo(ModelImporter importer, LightmapUVSettings baseline)
        {
            Validate();
            // Preserve every field the user did not edit, including changes made in another Inspector.
            if (generate != baseline.generate) importer.generateSecondaryUV = generate;
            if (hardAngle != baseline.hardAngle) importer.secondaryUVHardAngle = hardAngle;
            if (angleError != baseline.angleError) importer.secondaryUVAngleDistortion = angleError;
            if (areaError != baseline.areaError) importer.secondaryUVAreaDistortion = areaError;
            if (marginMethod != baseline.marginMethod) importer.secondaryUVMarginMethod = marginMethod;
            if (packMargin != baseline.packMargin) importer.secondaryUVPackMargin = packMargin;
            if (minResolution != baseline.minResolution) importer.secondaryUVMinLightmapResolution = minResolution;
            if (minScale != baseline.minScale) importer.secondaryUVMinObjectScale = minScale;
        }
    }
}
