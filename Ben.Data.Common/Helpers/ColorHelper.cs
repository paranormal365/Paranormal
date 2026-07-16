using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace Ben.Data.Common.Helpers;

public class ColorHelper
{
    public const string LIGHT = "#ffffff";
    public const string DARK = "#4f4f4f";
    public const int BRIGHTNESS_LEVEL = 130;

    public string HexColor { get; private set; }
    public Color ColorObj { get; private set; }

    #region Constructors
    public ColorHelper() 
    {
        HexColor = DARK;
        ColorObj = GetColor(HexColor);
    }

    public ColorHelper(string hexColor)
    {
        HexColor = hexColor;
        ColorObj = GetColor(hexColor);
    }

    public ColorHelper(Color color)
    {
        ColorObj = color;
        HexColor = GetHex(color);
    }

    /// <summary>
    /// 0 - 255 values to populate each color portion to create this instance
    /// </summary>
    /// <param name="red">0-255 Red Value</param>
    /// <param name="green">0-255 Green Value</param>
    /// <param name="blue">0-255 Blue Value</param>
    /// <param name="alpha">(Optional) 0-255 Alpha Transparency Value</param>
    public ColorHelper(int red, int green, int blue, int alpha = 255)
    {
        ColorObj = Color.FromArgb(red, green, blue);
        if (alpha < 255)
        {
            ColorObj = Color.FromArgb(alpha, ColorObj);
        }
        HexColor = GetHex(ColorObj);
    }

    /// <summary>
    /// Specify color parts between 0 - 1.  This is a percentage of each part.
    /// </summary>
    /// <param name="red">0-1 Red Value</param>
    /// <param name="green">0-1 Green Value</param>
    /// <param name="blue">0-1 Blue Value</param>
    /// <param name="alpha">(Optional) 0-1 Alpha Percent</param>
    public ColorHelper(float red, float green, float blue, float alpha = 1)
    {
        int redV = int.Parse(
            (red < 1f
            ? "1"
            : (255 * red).ToString())
            );
        int greenV = int.Parse(
            (green < 1f
            ? "255"
            : (255 * green).ToString())
            );
        int blueV = int.Parse(
            (blue < 1f
            ? "255"
            : (255 * blue).ToString())
            );
        int alphaV = 255;
        if (alpha < 1)
        {
            alphaV = int.Parse(
                (alpha < 1f
                ? "255"
                : (255 * alpha).ToString())
                );
        }

        ColorObj = Color.FromArgb(redV, greenV, blueV);
        if (alphaV < 255)
        {
            ColorObj = Color.FromArgb(alphaV, ColorObj);
        }
        HexColor = GetHex(ColorObj);
    }

    #endregion

    public int GetBrightness()
    {
        int brightness = 128;
        if (!ColorObj.IsEmpty)
        {
            brightness = (int)Math.Sqrt(
                ColorObj.R * ColorObj.R * .241 +
                ColorObj.G * ColorObj.G * .691 +
                ColorObj.B * ColorObj.B * .068
                );
        }
        return brightness;
    }

    /// <summary>
    /// Whatever color this is currently, it will return the dark color if it 
    /// is bright and return the light color if dark - like used on buttons.
    /// </summary>
    /// <param name="rtnDarkColor">Optional color to return if bright</param>
    /// <param name="rtnLightColor">Optional color to return if darkS</param>
    /// <returns></returns>
    public string GetHexColorBasedOnBrightness(string rtnDarkColor = "", string rtnLightColor = "")
    {
        if (string.IsNullOrEmpty(rtnDarkColor))
        {
            rtnDarkColor = DARK;
        }
        if (string.IsNullOrEmpty(rtnLightColor))
        {
            rtnLightColor = LIGHT;
        }

        if (GetBrightness() > BRIGHTNESS_LEVEL)
        {
            // It is bright so return the dark color
            return rtnDarkColor;
        }
        else
        {
            // It is darker so return the lighter color
            return rtnLightColor;
        }

    }

    /// <summary>
    /// Gets the RGBA representation of the color to use against a button - like the text
    /// </summary>
    /// <param name="rtnDarkRgba"></param>
    /// <param name="rtnLightRgba"></param>
    /// <returns></returns>
    public string GetRgbaColorBasedOnBrightness(string rtnDarkRgba = "", string rtnLightRgba = "")
    {
        if (string.IsNullOrEmpty(rtnDarkRgba))
        {
            rtnDarkRgba = new ColorHelper(DARK).GetRgba();
        }
        if (string.IsNullOrEmpty(rtnLightRgba))
        {
            rtnLightRgba = new ColorHelper(LIGHT).GetRgba();
        }

        if (GetBrightness() > BRIGHTNESS_LEVEL)
        {
            // Bright so return dark
            return rtnDarkRgba;
        }
        else
        {
            return rtnLightRgba;
        }
    }

    /// <summary>
    /// Convert a 6-digit or 8-digit hex string as used in web work
    /// </summary>
    /// <param name="hexStr"></param>
    /// <returns></returns>
    public static Color GetColor(string hexStr)
    {
        if (hexStr.IndexOf("#") != -1)
        {
            hexStr = hexStr.Replace("#", "");
        }

        return GetColorInstance(hexStr);
    }

    /// <summary>
    /// Get the string rep of rgba for the current color.
    /// </summary>
    /// <returns>rgba(1,0, 1.0, 1.0, 1.0);</returns>
    public string GetRgba()
    {
        return ConvertColorToRgba(ColorObj);
    }

    #region Private Methods

    /// <summary>
    /// Accepts hex color. If specifying 8 character hex string, it will determine alpha as well.
    /// </summary>
    /// <param name="hex">6 or 8 character hex string as used in web</param>
    /// <returns></returns>
    private static Color GetColorInstance(string hex)
    {
        int red = 0;
        int green = 0;
        int blue = 0;
        int alpha = 255;
        red = int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.AllowHexSpecifier);
        green = int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.AllowHexSpecifier);
        blue = int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.AllowHexSpecifier);
        Color baseColor = Color.FromArgb(red, green, blue);
        if (hex.Length > 6)
        {
            alpha = int.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.AllowHexSpecifier);
            baseColor = Color.FromArgb(alpha, baseColor);
        }
        return baseColor;
    }

    private static string GetHex(Color color)
    {
        string hex = $"#{color.R.ToString("X2")}{color.G.ToString("X2")}{color.B.ToString("X2")}";
        if (color.A < 255)
        {
            hex += color.A.ToString("X2");
        }
        return hex;
    }

    private string ConvertColorToRgba(Color color)
    {
        float red = ConvertIntColorToFloatColor(int.Parse(color.R.ToString()));
        float green = ConvertIntColorToFloatColor(int.Parse(color.G.ToString()));
        float blue = ConvertIntColorToFloatColor(int.Parse(color.B.ToString()));
        float alpha = ConvertIntColorToFloatColor(int.Parse(color.A.ToString()));

        return $"rgba({red}, {green}, {blue}, {alpha});";
    }

    private float ConvertIntColorToFloatColor(int colorVal)
    {
        float val = (float)Math.Round(((decimal)(colorVal / 255)), 4);
        return val > 1
            ? 1
            : val;
    }

    #endregion
}
