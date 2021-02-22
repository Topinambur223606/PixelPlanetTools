using PixelPlanetUtils.Canvas.Colors.Enums;
using System;
using System.Linq;

namespace PixelPlanetUtils.Canvas.Colors
{
    public class ColorNameResolver
    {
        private readonly Type colorEnum;

        private readonly int padLength;

        public ColorNameResolver(CanvasType canvas)
        {
            switch (canvas)
            {
                case CanvasType.Earth:
                    colorEnum = typeof(EarthPixelColor);
                    break;
                case CanvasType.Moon:
                    colorEnum = typeof(MoonPixelColor);
                    break;
                case CanvasType.Voxel:
                    colorEnum = typeof(ThreeDimVoxelColor);
                    break;
                case CanvasType.Covid:
                    colorEnum = typeof(CovidPixelColor);
                    break;
                case CanvasType.OneBit:
                    colorEnum = typeof(MonochromePixelColor);
                    break;
                case CanvasType.PixelZoneMirror:
                case CanvasType.PixelCanvasMirror:
                    colorEnum = typeof(EmptyEnum);
                    break;
            }
            padLength = Enum.GetNames(colorEnum).Max(c => c.Length);

        }

        public string GetName(byte color)
        {
            string value = Enum.GetName(colorEnum, color) ?? color.ToString();
            return value.PadRight(padLength);
        }
    }
}
