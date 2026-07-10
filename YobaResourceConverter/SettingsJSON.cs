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
	public string CharacterSet = " !\"#$%&'()*+,-./0123456789:;<=?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~¢£¥§©®°×÷ΔπАБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЫЬЭЮЯабвгдежзийклмнопрстуфхцчшщыьэюя•€™√✓";
	public int Size = 16;
}

public enum ImageSettingsMode : byte {
	RGB565,
	RGB888,
	Palette
}

public enum ImageSettingsEndianness : byte {
	LittleEndian,
	BigEndian
}

public class ImageSettingsJSON {
	public ImageSettingsMode Mode = ImageSettingsMode.RGB565;
	public ImageSettingsEndianness Endianness = ImageSettingsEndianness.BigEndian;

	public int[] Palette = [
		0x000000,
		-0xFFFFFF,
		0x000000
	];

	public string[]? Files = null;

	public string? Namespace = null;
	public string? CommonHeaderPath = null;
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
