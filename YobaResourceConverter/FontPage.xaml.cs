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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace YobaResourceConverter;

public partial class FontPage : UserControl {
	public FontPage() {
		InitializeComponent();

		if (!DesignerProperties.GetIsInDesignMode(this)) {
			UpdateVisualsFromSettings();
			Render();
		}

		// Rendering callbacks
		RenderTimer = new(
			TimeSpan.FromMilliseconds(500),
			DispatcherPriority.ApplicationIdle,
			(s, e) => {
				RenderTimer!.Stop();

				Render();
			},
			Dispatcher
		);

		RenderTimer.Stop();

		FontFamilyComboBox.SelectionChanged += (s, e) => {
			if (FontFamilyComboBox.SelectedItem is not FontFamily fontFamily)
				return;

			App.Settings.Font.Family = fontFamily.Source;

			EnqueueRender();
		};

		CharacterSetTextBox.TextChanged += (s, e) => {
			if (CharacterSetTextBox.Text.Length == 0)
				return;

			App.Settings.Font.CharacterSet = CharacterSetTextBox.Text;

			EnqueueRender();
		};

		void addTextBoxRenderCallback(TextBox textBox, Action<int> valueSetter) {
			textBox.TextChanged += (s, e) => {
				if (
					textBox.Text.Length == 0
					|| !int.TryParse(textBox.Text, out var value)
				)
					return;

				valueSetter(value);

				EnqueueRender();
			};
		}

		addTextBoxRenderCallback(FontSizeTextBox, o => App.Settings.Font.Size = o);
	}

	FormattedText[]? GlyphsFormattedTexts = null;
	Typeface? GlyphsTypeface = null;
	RenderTargetBitmap? GlyphsBitmap = null;

	const int GLYPHS_SPACING = 2;

	int
		GlyphsTotal = 94,
		GlyphsWidth = 1,
		GlyphsConstantWidth = -1,
		GlyphsMaxHeight = 1;

	bool GlyphsIsConstantWidth = false;

	readonly DispatcherTimer RenderTimer;

	public ObservableCollection<FontFamily> FontFamilies { get; set; } = [];

	void UpdateVisualsFromSettings() {
		CharacterSetTextBox.Text = App.Settings.Font.CharacterSet;
		FontSizeTextBox.Text = App.Settings.Font.Size.ToString();
		NamespaceTextBox.Text = App.Settings.Font.Namespace;

		// Font families
		int settingsFontFamilyCounter = 0;
		int settingsFontFamilyIndex = 0;

		foreach (var fontFamily in Fonts.SystemFontFamilies.OrderBy(o => o.Source)) {
			FontFamilies.Add(fontFamily);

			if (fontFamily.Source == App.Settings.Font.Family)
				settingsFontFamilyIndex = settingsFontFamilyCounter;

			settingsFontFamilyCounter++;
		}

		FontFamilyComboBox.ItemsSource = FontFamilies;
		FontFamilyComboBox.SelectedIndex = settingsFontFamilyIndex;
	}

	void Render() {
		GlyphsTotal = App.Settings.Font.CharacterSet.Length;

		if (GlyphsTotal <= 0) {
			return;
		}

		GlyphsFormattedTexts = new FormattedText[GlyphsTotal];
		FormattedText formattedText;

		GlyphsTypeface = new(
			(FontFamily) FontFamilyComboBox.SelectedItem,
			FontStyles.Normal,
			FontWeights.Normal,
			FontStretches.Normal
		);

		DrawingVisual drawingVisual = new();

		int x = 0;
		int width;
		int height;

		GlyphsWidth = 1;
		GlyphsMaxHeight = 1;
		GlyphsConstantWidth = -1;
		GlyphsIsConstantWidth = true;

		using (var drawingContext = drawingVisual.RenderOpen()) {
			for (int i = 0; i < GlyphsTotal; i++) {
				GlyphsFormattedTexts[i] = formattedText = new(
					App.Settings.Font.CharacterSet[i].ToString(),
					CultureInfo.CurrentUICulture,
					FlowDirection.LeftToRight,
					GlyphsTypeface,
					App.Settings.Font.Size,
					(SolidColorBrush) Application.Current.FindResource("ThemeFg1"),
					new NumberSubstitution(),
					TextFormattingMode.Display,
					VisualTreeHelper.GetDpi(this).PixelsPerDip
				);

				width = (int) Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace);
				height = (int) Math.Ceiling(formattedText.Height);

				// Checking if all glyphs have same fixed width, i.e. is font monospaced or not
				if (GlyphsConstantWidth < 0) {
					GlyphsConstantWidth = width;
				}
				else {
					if (width != GlyphsConstantWidth) {
						GlyphsIsConstantWidth = false;
					}
				}

				// Computing total size
				GlyphsWidth += width + GLYPHS_SPACING;
				GlyphsMaxHeight = Math.Max(GlyphsMaxHeight, height);

				if (width > 0) {
					drawingContext.DrawText(formattedText, new(x, 0));
				}

				x += width + GLYPHS_SPACING;
			}
		}

		if (GlyphsMaxHeight > 256) {
			MessageBox.Show($"Retarded font size, pixel height is {GlyphsMaxHeight}, decrease pls");
			return;
		}

		// Rendering
		GlyphsBitmap = new(
			GlyphsWidth,
			GlyphsMaxHeight,
			96,
			96,
			PixelFormats.Pbgra32
		);

		GlyphsBitmap.Render(drawingVisual);

		PreviewImage.Source = GlyphsBitmap;
		PreviewImage.Height = GlyphsMaxHeight;
	}

	void OnNamespaceTextBoxTextChanged(object sender, TextChangedEventArgs e) {
		if (NamespaceTextBox.IsFocused)
			App.Settings.Font.Namespace = string.IsNullOrWhiteSpace(NamespaceTextBox.Text) ? null : NamespaceTextBox.Text;
	}

	void EnqueueRender() {
		RenderTimer.Stop();
		RenderTimer.Start();
	}

	async void OnSaveButtonClick(object sender, RoutedEventArgs e) {
		if (GlyphsBitmap is null)
			return;

		var className = $"{App.GetHeaderNameRegex().Replace(GlyphsTypeface!.FontFamily.ToString(), "")}{App.Settings.Font.Size}Font";

		SaveFileDialog dialog = new() {
			Title = "Export font",
			FileName = $"{className}.hpp",
			Filter = "C++ header files|*.hpp"
		};

		if (dialog.ShowDialog() != true)
			return;

		var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(dialog.FileName);

		// Maybe user had changed name via dialog
		className = App.ConvertFileNameToClassName(fileNameWithoutExtension);

		var haveUserNamespace = !string.IsNullOrWhiteSpace(App.Settings.Font.Namespace);
		var userNamespaceIsYoba = App.Settings.Font.Namespace == "YOBA";
		var yobaNamespacePrefix = userNamespaceIsYoba ? string.Empty : "YOBA::";

		var globalTabulation = haveUserNamespace ? "\t" : string.Empty;
		var privateFieldsTabulation = new string('\t', haveUserNamespace ? 4 : 3);

		// Bitmap
		int x = 0;

		StringBuilder
			glyphsSB = new(),
			bitmapSB = new(privateFieldsTabulation),
			mappingSwitchSB = new();

		int bitmapGlyphBitIndex = 0;
		int bitmapByteIndex = 0;
		int bitmapByte = 0;
		int bitmapBytesTotal = 0;
		int bitmapByteBitIndex = 0;

		byte[] pixelBuffer;
		int pixelStride;

		void flushBitmapByte() {
			bitmapSB.Append($"0x{bitmapByte:X2},");
			bitmapByte = 0;
			bitmapBytesTotal++;

			// Bytes per line
			bitmapByteIndex++;

			if (bitmapByteIndex > 15) {
				bitmapByteIndex = 0;
				bitmapSB.Append($"{Environment.NewLine}{privateFieldsTabulation}");
			}
			else {
				bitmapSB.Append(' ');
			}
		}

		FormattedText formattedText;
		int width;

		var glyphClassName = GlyphsIsConstantWidth ? "ConstantWidthGlyph" : "VariableWidthGlyph";

		// Converting
		for (int i = 0; i < GlyphsFormattedTexts!.Length; i++) {
			formattedText = GlyphsFormattedTexts[i];

			width = (int) Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace);

			if (i > 0) {
				glyphsSB.AppendLine();
			}

			if (GlyphsIsConstantWidth) {
				glyphsSB.Append($"{privateFieldsTabulation}{{}}");
			}
			else {
				glyphsSB.Append($"{privateFieldsTabulation}{{ {bitmapGlyphBitIndex}, {width} }}");
			}

			var charName = formattedText.Text switch {
				"\\" => "Backslash",
				" " => "Whitespace",
				_ => formattedText.Text
			};

			if (i < GlyphsFormattedTexts.Length - 1)
				glyphsSB.Append(',');

			glyphsSB.Append($" // {charName}");

			// Pixels
			if (width > 0) {
				pixelStride = width * 4;
				pixelBuffer = new byte[pixelStride * GlyphsMaxHeight];

				GlyphsBitmap.CopyPixels(
					new(
						x,
						0,
						width,
						GlyphsMaxHeight
					),
					pixelBuffer,
					pixelStride,
					0
				);

				for (int j = 0; j < pixelBuffer.Length; j += 4) {
					// If alpha has value - there's definitely some pixel data
					if (pixelBuffer[j + 3] > 127)
						bitmapByte |= 1 << bitmapByteBitIndex;

					// Flushing byte if required
					bitmapByteBitIndex += 1;

					if (bitmapByteBitIndex > 7) {
						flushBitmapByte();
						bitmapByteBitIndex = 0;
					}
				}

				bitmapGlyphBitIndex += width * GlyphsMaxHeight;
			}

			x += width + GLYPHS_SPACING;

			// Switch
			mappingSwitchSB
				.AppendLine($$"""
{{globalTabulation}}				case {{(int) formattedText.Text[0]}}: return {{i}}; // {{charName}}
""");
		}

		// Last byte
		if (bitmapByteBitIndex > 0)
			flushBitmapByte();

		// Mapping switch default branch
		mappingSwitchSB
			.Append($$"""
{{globalTabulation}}				default: return -1;
""");


		// Saving
		Directory.CreateDirectory(Path.GetDirectoryName(dialog.FileName) ?? string.Empty);

		using FileStream fileStream = new(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
		using BufferedStream bufferedStream = new(fileStream, 8192);
		using StreamWriter streamWriter = new(bufferedStream, Encoding.UTF8);

		await streamWriter.WriteAsync($$"""
// This file was generated automatically and does not require manual editing.
// If you need a conversion tool, here is a link: https://github.com/IgorTimofeev/YOBAResourceConverter

#pragma once

#include <{{(string.IsNullOrEmpty(App.Settings.YobaPath) ? "YOBA/" : App.Settings.YobaPath)}}Core.hpp>


""");

		if (haveUserNamespace) {
			await streamWriter.WriteAsync($$"""
namespace {{App.Settings.Font.Namespace}} {

""");
		}

		await streamWriter.WriteAsync($$"""
{{globalTabulation}}class {{className}} : public {{yobaNamespacePrefix}}Font {
{{globalTabulation}}	public:
{{globalTabulation}}		constexpr {{className}}() : {{yobaNamespacePrefix}}Font(
{{globalTabulation}}			{{(GlyphsIsConstantWidth ? GlyphsConstantWidth : 0)}},
{{globalTabulation}}			{{GlyphsMaxHeight}},
{{globalTabulation}}			_glyphs,
{{globalTabulation}}			_bitmap
{{globalTabulation}}		) {
{{globalTabulation}}			
{{globalTabulation}}		}
{{globalTabulation}}
{{globalTabulation}}	private:
{{globalTabulation}}		constexpr static {{yobaNamespacePrefix}}{{glyphClassName}} _glyphs[{{GlyphsTotal}}] {
{{glyphsSB}}
{{globalTabulation}}		};
{{globalTabulation}}		
{{globalTabulation}}		constexpr int32_t getGlyphIndex(const uint32_t codepoint) const override {
{{globalTabulation}}			switch (codepoint) {
{{mappingSwitchSB}}
{{globalTabulation}}			}
{{globalTabulation}}		}
{{globalTabulation}}		
{{globalTabulation}}		constexpr static uint8_t _bitmap[{{bitmapBytesTotal}}] {
{{bitmapSB}}
{{globalTabulation}}		};
{{globalTabulation}}};
""");

		if (haveUserNamespace) {
			await streamWriter.WriteAsync($"{Environment.NewLine}}}");
		}
	}
}