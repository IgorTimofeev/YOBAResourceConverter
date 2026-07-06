using ColorEx;
using ColorEx.Wpf;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace YobaResourceConverter;

[Flags]
public enum ImageFlags : byte {
	None = 0b000000_0000,
	RGB565 = 0b0000_0001,
	Palette8Bit = 0b0000_0010,
	Alpha1Bit = 0b0000_0100,
}

class ImageData {
	public ImageFlags Flags = ImageFlags.None;
	public int Width = 0;
	public int Height = 0;
	public byte[] Bitmap = [];
}

public partial class ImagePage : UserControl {
	public ImagePage() {
		InitializeComponent();

		if (!DesignerProperties.GetIsInDesignMode(this)) {
			// Namespace
			NamespaceTextBox.Text = App.Settings.Image.Namespace;

			// Mode
			ModeComboBox.SelectedIndex = (byte) App.Settings.Image.Mode;

			// Endianness
			EndiannessComboBox.SelectedIndex = (byte) App.Settings.Image.Endianness;

			// Path
			UpdatePathTextBoxText();

			// Palette
			PaletteTextBox.Text = string.Join(", ", App.Settings.Image.Palette.Select(o => $"{(o >= 0 ? "" : '-')}0x{Math.Abs(o):X6}"));
			PaletteTextBox.TextChanged += (s, e) => EnqueueParsePalette();
			ParsePalette();
		}

		ParsePaletteTimer = new(
			TimeSpan.FromMilliseconds(500),
			DispatcherPriority.ApplicationIdle,
			(s, e) => {
				ParsePaletteTimer!.Stop();

				ParsePalette();
			},
			Dispatcher
		);

		ParsePaletteTimer.Stop();
	}

	readonly DispatcherTimer ParsePaletteTimer;

	Color?[] PaletteColors = [];

	void ParsePalette() {
		App.Settings.Image.Palette = [..
			PaletteTextBox.Text
			.Replace("0x", "")
			.Split(
				[',', ' '],
				StringSplitOptions.RemoveEmptyEntries
			)
			.Select(
				stringColor => {
					var negative = false;

					// Hex numbers can't be parsed with trailing sign :(
					if (stringColor.StartsWith('-') && stringColor.Length > 1) {
						stringColor = stringColor[1..];
						negative = true;
					}

					int.TryParse(
						stringColor,
						NumberStyles.HexNumber,
						CultureInfo.CurrentUICulture,
						out var intColor
					);

					if (negative)
						intColor *= -1;

					return intColor;
				}
			)
		];

		PaletteColors = App.Settings.Image.Palette.Select(o => o >= 0 ? (Color?) ((uint) o).ToColor().ChangeAlpha(0xFF) : null).ToArray();
	}

	void EnqueueParsePalette() {
		ParsePaletteTimer.Stop();
		ParsePaletteTimer.Start();
	}

	private void OnPathButtonClick(object sender, RoutedEventArgs e) {
		OpenFileDialog dialog = new() {
			Multiselect = true,
			Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp"
		};

		if (dialog.ShowDialog() != true)
			return;

		App.Settings.Image.Files = dialog.FileNames;
		UpdatePathTextBoxText();
	}

	void UpdatePathTextBoxText() {
		PathTextBox.Text = App.Settings.Image.Files is null ? null : string.Join(", ", App.Settings.Image.Files.Select(o => Path.GetFileName(o)));
	}

	void OnNamespaceTextBoxTextChanged(object sender, TextChangedEventArgs e) {
		if (NamespaceTextBox.IsFocused)
			App.Settings.Image.Namespace = string.IsNullOrWhiteSpace(NamespaceTextBox.Text) ? null : NamespaceTextBox.Text;
	}

	static void WriteBits(ImageData imageData, ref int bitmapByteIndex, ref byte bitmapBitIndex, byte value, byte count) {
		for (byte valueBitIndex = 0; valueBitIndex < count; valueBitIndex++) {
			// 0000 0000
			// ---- -2--
			imageData.Bitmap[bitmapByteIndex] =
				// Value bit == 1 ?
				((value >> valueBitIndex) & 1) == 1
				? (byte) (imageData.Bitmap[bitmapByteIndex] | (1 << bitmapBitIndex))
				: (byte) (imageData.Bitmap[bitmapByteIndex] & ~(1 << bitmapBitIndex));

			bitmapBitIndex++;

			if (bitmapBitIndex > 7) {
				bitmapBitIndex = 0;
				bitmapByteIndex++;
			}
		}
	}

	static int RGB888ToRGB565LE(byte r, byte g, byte b) {
		return ((r & 0b11111000) << 8) | ((g & 0b11111100) << 3) | (b >> 3);
	}

	static int RGB888ToRGB565BE(byte r, byte g, byte b) {
		return BinaryPrimitives.ReverseEndianness((ushort) RGB888ToRGB565LE(r, g, b));
	}

	static int RGB888ToRGB565(byte r, byte g, byte b) {
		return
			App.Settings.Image.Endianness is ImageSettingsEndianness.LittleEndian
			? RGB888ToRGB565LE(r, g, b)
			: RGB888ToRGB565BE(r, g, b);
	}

	static void ConvertRGB565(ImageData imageData, byte[] pixels) {
		imageData.Flags |= ImageFlags.RGB565;

		int bitmapByteIndex = 0;

		// Checking for non-max alphas
		int transparentPixelsCount = 0;

		for (int oc = 0; oc < pixels.Length; oc += 4) {
			if (pixels[oc + 3] == 0) {
				transparentPixelsCount++;
			}
		}

		var totalPixelsCount = imageData.Width * imageData.Height;

		// Have transparent pixels
		if (transparentPixelsCount > 0) {
			imageData.Flags |= ImageFlags.Alpha1Bit;

			byte bitmapBitIndex = 0;
			var nonTransparentPixelsCount = totalPixelsCount - transparentPixelsCount;
			var bitsCount = transparentPixelsCount * 1 + nonTransparentPixelsCount * (1 + 8 * 2);

			imageData.Bitmap = new byte[(int) Math.Ceiling(bitsCount / 8d)];

			for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex += 4) {
				// Transparent
				if (pixels[pixelIndex + 3] == 0) {
					WriteBits(imageData, ref bitmapByteIndex, ref bitmapBitIndex, 0, 1);
				}
				// Non-transparent
				else {
					WriteBits(imageData, ref bitmapByteIndex, ref bitmapBitIndex, 1, 1);

					var RGB565 = RGB888ToRGB565(
						pixels[pixelIndex + 2],
						pixels[pixelIndex + 1],
						pixels[pixelIndex]
					);

					WriteBits(imageData, ref bitmapByteIndex, ref bitmapBitIndex, (byte) (RGB565 & 0xFF), 8);
					WriteBits(imageData, ref bitmapByteIndex, ref bitmapBitIndex, (byte) ((RGB565 >> 8) & 0xFF), 8);
				}
			}
		}
		// Haven't
		else {
			imageData.Bitmap = new byte[totalPixelsCount * 2];

			for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex += 4) {
				var RGB565 = RGB888ToRGB565(
					pixels[pixelIndex + 2],
					pixels[pixelIndex + 1],
					pixels[pixelIndex]
				);

				imageData.Bitmap[bitmapByteIndex++] = (byte) (RGB565 & 0xFF);
				imageData.Bitmap[bitmapByteIndex++] = (byte) ((RGB565 >> 8) & 0xFF);
			}
		}
	}

	int FindClosestPaletteIndex(byte[] pixels, int pixelIndex) {
		double
			closestDelta = double.MaxValue,
			delta;

		int
			closestIndex = 0,
			deltaR,
			deltaG,
			deltaB;

		Color? paletteColor;

		for (int paletteIndex = 0; paletteIndex < PaletteColors.Length; paletteIndex++) {
			paletteColor = PaletteColors[paletteIndex];

			if (paletteColor is null)
				continue;

			deltaR = paletteColor.Value.R - pixels[pixelIndex + 2];
			deltaG = paletteColor.Value.G - pixels[pixelIndex + 1];
			deltaB = paletteColor.Value.B - pixels[pixelIndex];

			delta = Math.Sqrt(deltaR * deltaR + deltaG * deltaG + deltaB * deltaB);

			if (delta < closestDelta) {
				closestDelta = delta;
				closestIndex = paletteIndex;
			}
		}

		return closestIndex;
	}

	void ConvertPalette8(ImageData imageData, byte[] pixels) {
		imageData.Flags |= ImageFlags.Palette8Bit;

		int bitmapByteIndex = 0;

		// Checking for non-max alphas
		int transparentPixelsCount = 0;

		for (int oc = 0; oc < pixels.Length; oc += 4) {
			if (pixels[oc + 3] == 0) {
				transparentPixelsCount++;
			}
		}

		var totalPixelsCount = imageData.Width * imageData.Height;

		// Have transparent pixels
		if (transparentPixelsCount > 0) {
			imageData.Flags |= ImageFlags.Alpha1Bit;

			byte bitmapBitIndex = 0;
			var nonTransparentPixelsCount = totalPixelsCount - transparentPixelsCount;
			var bitsCount = transparentPixelsCount * 1 + nonTransparentPixelsCount * (1 + 8);

			imageData.Bitmap = new byte[(int) Math.Ceiling(bitsCount / 8d)];

			for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex += 4) {
				// Transparent
				if (pixels[pixelIndex + 3] == 0) {
					WriteBits(imageData, ref bitmapByteIndex, ref bitmapBitIndex, 0, 1);
				}
				// Non-transparent
				else {
					WriteBits(imageData, ref bitmapByteIndex, ref bitmapBitIndex, 1, 1);
					WriteBits(imageData, ref bitmapByteIndex, ref bitmapBitIndex, (byte) FindClosestPaletteIndex(pixels, pixelIndex), 8);
				}
			}
		}
		// Haven't
		else {
			imageData.Bitmap = new byte[totalPixelsCount];

			for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex += 4) {
				imageData.Bitmap[bitmapByteIndex] = (byte) FindClosestPaletteIndex(pixels, pixelIndex);
				bitmapByteIndex++;
			}
		}
	}

	ImageData Convert(string imageFileName) {
		BitmapImage bitmapImage = new(new Uri(imageFileName, UriKind.Absolute));

		var stride = bitmapImage.PixelWidth * 4;
		var pixels = new byte[stride * bitmapImage.PixelHeight];

		bitmapImage.CopyPixels(pixels, stride, 0);

		ImageData imageData = new() {
			Flags = ImageFlags.None,
			Width = bitmapImage.PixelWidth,
			Height = bitmapImage.PixelHeight,
		};

		// Mode
		switch (App.Settings.Image.Mode) {
			case ImageSettingsMode.RGB565:
				ConvertRGB565(imageData, pixels);
				break;

			default:
				ConvertPalette8(imageData, pixels);
				break;
		}

		return imageData;
	}

	async Task ExportHeaderAsync(string headerFolderName, string imageFileName) {
		if (!File.Exists(imageFileName))
			return;

		var imageData = Convert(imageFileName);

		var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(imageFileName);
		var (headerFileName, className) = App.ConvertFileNameToHeaderFileNameAndClassName(fileNameWithoutExtension, "Image");

		var haveUserNamespace = !string.IsNullOrWhiteSpace(App.Settings.Image.Namespace);
		var userNamespaceIsYoba = App.Settings.Image.Namespace == "YOBA";
		var yobaNamespacePrefix = haveUserNamespace ? string.Empty : "YOBA::";

		var globalTabulation = haveUserNamespace ? "\t" : string.Empty;
		var privateFieldsTabulation = new string('\t', haveUserNamespace ? 4 : 3);

		using FileStream fileStream = new(Path.Combine(headerFolderName, headerFileName), FileMode.Create, FileAccess.Write, FileShare.None);
		using BufferedStream bufferedStream = new(fileStream, 8192);
		using StreamWriter streamWriter = new(bufferedStream, Encoding.UTF8);

		// Includes
		await streamWriter.WriteAsync($$"""
#pragma once

#include <{{(string.IsNullOrEmpty(App.Settings.YobaPath) ? "YOBA/" : App.Settings.YobaPath)}}core.h>


""");

		// Namespace
		if (haveUserNamespace) {
			await streamWriter.WriteAsync($$"""
namespace {{App.Settings.Image.Namespace}} {

""");

			if (!userNamespaceIsYoba) {
				await streamWriter.WriteAsync($$"""
	using namespace YOBA;


""");
			}
		}

		// Class
		await streamWriter.WriteAsync($$"""
{{globalTabulation}}class {{className}} : public {{yobaNamespacePrefix}}Image {
{{globalTabulation}}	public:
{{globalTabulation}}		constexpr {{className}}() : {{yobaNamespacePrefix}}Image(
{{globalTabulation}}			
""");

		// Flags
		var haveFlags = false;

		async Task writeFlagAsync(ImageFlags flag, string name) {
			if (!imageData.Flags.HasFlag(flag))
				return;

			if (haveFlags) {
				await streamWriter.WriteAsync(" | ");
			}
			else {
				haveFlags = true;
			}

			await streamWriter.WriteAsync($"{yobaNamespacePrefix}ImageFlags::{name}");
		}

		await writeFlagAsync(ImageFlags.RGB565, "RGB565");
		await writeFlagAsync(ImageFlags.Palette8Bit, "palette8Bit");
		await writeFlagAsync(ImageFlags.Alpha1Bit, "alpha1Bit");

		if (haveFlags) {
			await streamWriter.WriteAsync($$"""
,

""");
		}

		// Rest
		await streamWriter.WriteAsync($$"""
{{globalTabulation}}			{{yobaNamespacePrefix}}Size({{imageData.Width}}, {{imageData.Height}}),
{{globalTabulation}}			_bitmap
{{globalTabulation}}		) {
{{globalTabulation}}			
{{globalTabulation}}		}
{{globalTabulation}}	
{{globalTabulation}}	private:
{{globalTabulation}}		constexpr static uint8_t _bitmap[{{imageData.Bitmap.Length}}] = {

""");

		await streamWriter.WriteAsync(privateFieldsTabulation);

		int lineCounter = 0;

		for (int bi = 0; bi < imageData.Bitmap.Length; bi++) {
			if (lineCounter > 0)
				await streamWriter.WriteAsync(' ');

			await streamWriter.WriteAsync("0x");
			await streamWriter.WriteAsync(imageData.Bitmap[bi].ToString("X2"));

			if (bi < imageData.Bitmap.Length - 1) {
				await streamWriter.WriteAsync(',');

				lineCounter++;

				if (lineCounter >= 16) {
					await streamWriter.WriteLineAsync();
					await streamWriter.WriteAsync(privateFieldsTabulation);

					lineCounter = 0;
				}
			}
		}

		await streamWriter.WriteAsync($$"""

{{globalTabulation}}		};
{{globalTabulation}}};
}
""");
	}

	async void OnSaveButtonClick(object sender, RoutedEventArgs e) {
		if (App.Settings.Image.Files is null)
			return;

		OpenFolderDialog dialog = new() {
			Title = "Export images"
		};

		if (dialog.ShowDialog() != true)
			return;

		await Task.WhenAll(App.Settings.Image.Files.Select(imageFileName => ExportHeaderAsync(
			dialog.FolderName,
			imageFileName
		)));
	}

	void OnModeComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e) {
		if (ModeComboBox.SelectedIndex < 0)
			return;

		App.Settings.Image.Mode = (ImageSettingsMode) ModeComboBox.SelectedIndex;

		switch (App.Settings.Image.Mode) {
			case ImageSettingsMode.RGB565:
				PaletteTitle.Visibility = Visibility.Collapsed;
				PaletteTextBox.Visibility = Visibility.Collapsed;

				break;

			// Palette
			default:
				PaletteTitle.Visibility = Visibility.Visible;
				PaletteTextBox.Visibility = Visibility.Visible;

				break;
		}
	}

	private void OnEndiannessComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e) {
		if (EndiannessComboBox.SelectedIndex < 0)
			return;

		App.Settings.Image.Endianness = (ImageSettingsEndianness) EndiannessComboBox.SelectedIndex;
	}
}