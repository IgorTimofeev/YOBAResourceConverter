using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace YobaResourceConverter;

public class FontSettingsJSON {
	public string Family = "Arial";
	public string? Namespace = null;
	public int Size = 16;
	public int From = 32;
	public int To = 126;
}

public enum ImageSettingsMode : byte {
	RGB565,
	Palette
}

public class ImageSettingsJSON {
	public ImageSettingsMode Mode = ImageSettingsMode.RGB565;

	public int[] Palette = [
		0x000000,
		-0xFFFFFF,
		0x000000
	];

	public string[]? Files = null;

	public string? Namespace = null;
}

public class WindowSettingsJSON {
	public int
		X = 0,
		Y = 0,
		Width = 0,
		Height = 0;
}

public class SettingsJSON {
	public WindowSettingsJSON Window = new();
	public FontSettingsJSON Font = new();
	public ImageSettingsJSON Image = new();
	public byte TabIndex = 0;
	public string? YobaPath = null;
}
