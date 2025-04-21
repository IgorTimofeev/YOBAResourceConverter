using Microsoft.Win32;
using System;
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
using ColorEx;
using ColorEx.Wpf;
using Newtonsoft.Json.Linq;

namespace YobaResourceConverter;

public partial class ImagePage : UserControl {
	public ImagePage() {
		InitializeComponent();

		if (!DesignerProperties.GetIsInDesignMode(this)) {
			// Namespace
			NamespaceTextBox.Text = App.Settings.Image.Namespace;

			// Mode
			ModeComboBox.SelectedIndex = (byte) App.Settings.Image.Mode;

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

	int
		ExportWidth = 0,
		ExportHeight = 0;

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

	byte[] Convert(string imageFileName) {
		BitmapImage originalImage = new(new Uri(imageFileName, UriKind.Absolute));

		// Conversion itself
		ExportWidth = originalImage.PixelWidth;
		ExportHeight = originalImage.PixelHeight;

		var stride = ExportWidth * 4;
		var pixels = new byte[stride * ExportHeight];

		originalImage.CopyPixels(pixels, stride, 0);

		int
			exportBitmapIndex = 0,
			closestIndex,
			deltaR,
			deltaG,
			deltaB;

		double
			closestDelta,
			delta;

		Color? paletteColor;
		Color originalColor;

		var bitmap = new byte[ExportWidth * ExportHeight];

		for (int oc = 0; oc < pixels.Length; oc += 4) {
			originalColor = Color.FromArgb(
				// No alphas? :((
				0xFF,
				pixels[oc + 2],
				pixels[oc + 1],
				pixels[oc]
			);

			closestDelta = int.MaxValue;
			closestIndex = 0;

			for (int pi = 0; pi < PaletteColors.Length; pi++) {
				paletteColor = PaletteColors[pi];

				if (paletteColor is null)
					continue;

				deltaR = paletteColor.Value.R - originalColor.R;
				deltaG = paletteColor.Value.G - originalColor.G;
				deltaB = paletteColor.Value.B - originalColor.B;

				delta = Math.Sqrt(deltaR * deltaR + deltaG * deltaG + deltaB * deltaB);

				if (delta < closestDelta) {
					closestDelta = delta;
					closestIndex = pi;
				}
			}

			paletteColor = PaletteColors[closestIndex]!;

			// Updating pixels with closest color data
			pixels[oc + 3] = 0xFF;
			pixels[oc + 2] = paletteColor.Value.R;
			pixels[oc + 1] = paletteColor.Value.G;
			pixels[oc] = paletteColor.Value.B;

			bitmap[exportBitmapIndex] = (byte) closestIndex;
			exportBitmapIndex++;
		}

		return bitmap;
	}

	async Task ExportHeaderAsync(string headerFolderName, string imageFileName) {
		if (!File.Exists(imageFileName))
			return;

		var bitmap = Convert(imageFileName);

		var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(imageFileName);
		var (headerFileName, className) = App.ConvertFileNameToHeaderFileNameAndClassName(fileNameWithoutExtension, "Image");

		var haveUserNamespace = !string.IsNullOrWhiteSpace(App.Settings.Image.Namespace);
		var userNamespaceIsYoba = App.Settings.Image.Namespace?.Equals("yoba", StringComparison.OrdinalIgnoreCase) is true;
		var yobaNamespacePrefix = userNamespaceIsYoba ? string.Empty : "yoba::";

		var globalTabulation = haveUserNamespace ? "\t" : string.Empty;
		var privateFieldsTabulation = new string('\t', haveUserNamespace ? 4 : 3);

		using FileStream fileStream = new(Path.Combine(headerFolderName, headerFileName), FileMode.Create, FileAccess.Write, FileShare.None);
		using BufferedStream bufferedStream = new(fileStream, 8192);
		using StreamWriter streamWriter = new(bufferedStream, Encoding.UTF8);

		await streamWriter.WriteAsync($$"""
#pragma once


""");

		if (!string.IsNullOrEmpty(App.Settings.YobaPath)) {
			await streamWriter.WriteAsync($$"""
#include "{{App.Settings.YobaPath}}main.h"


""");
		}

		if (haveUserNamespace) {
			await streamWriter.WriteAsync($$"""
namespace {{App.Settings.Image.Namespace}} {

""");
		}

		await streamWriter.WriteAsync($$"""
{{globalTabulation}}class {{className}} : public {{yobaNamespacePrefix}}Image {
{{globalTabulation}}	public:
{{globalTabulation}}		{{className}}() : {{yobaNamespacePrefix}}Image({{yobaNamespacePrefix}}Size({{ExportWidth}}, {{ExportHeight}}), _bitmap) {
{{globalTabulation}}			
{{globalTabulation}}		}
{{globalTabulation}}	
{{globalTabulation}}	private:
{{globalTabulation}}		constexpr static const uint8_t _bitmap[{{bitmap.Length}}] = {

""");

		await streamWriter.WriteAsync(privateFieldsTabulation);

		int lineCounter = 0;

		for (int bi = 0; bi < bitmap.Length; bi++) {
			if (lineCounter > 0)
				await streamWriter.WriteAsync(' ');

			await streamWriter.WriteAsync("0x");
			await streamWriter.WriteAsync(bitmap[bi].ToString("X2"));

			if (bi < bitmap.Length - 1) {
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
}