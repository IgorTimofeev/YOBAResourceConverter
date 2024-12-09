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
		addTextBoxRenderCallback(GlyphsFromTextBox, o => App.Settings.Font.From = o);
		addTextBoxRenderCallback(GlyphsToTextBox, o => App.Settings.Font.To = o);
	}

	FormattedText[]? GlyphsFormattedTexts = null;
	Typeface? GlyphsTypeface = null;
	RenderTargetBitmap? GlyphsBitmap = null;

	const int GLYPHS_SPACING = 2;

	int
		GlyphsTotal = 94,
		GlyphsWidthTotal = 1,
		GlyphsHeightTotal = 1;

	readonly DispatcherTimer RenderTimer;

	public ObservableCollection<FontFamily> FontFamilies { get; set; } = [];

	void UpdateVisualsFromSettings() {
		GlyphsFromTextBox.Text = App.Settings.Font.From.ToString();
		GlyphsToTextBox.Text = App.Settings.Font.To.ToString();
		FontSizeTextBox.Text = App.Settings.Font.Size.ToString();

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
		if (!int.TryParse(FontSizeTextBox.Text, out App.Settings.Font.Size))
			App.Settings.Font.Size = 16;

		if (!int.TryParse(GlyphsFromTextBox.Text, out App.Settings.Font.From))
			App.Settings.Font.From = 32;

		if (!int.TryParse(GlyphsToTextBox.Text, out App.Settings.Font.To))
			App.Settings.Font.To = 126;

		GlyphsTotal = App.Settings.Font.To - App.Settings.Font.From + 1;

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

		GlyphsWidthTotal = 1;
		GlyphsHeightTotal = 1;

		using (var drawingContext = drawingVisual.RenderOpen()) {
			for (int i = 0; i < GlyphsTotal; i++) {
				GlyphsFormattedTexts[i] = formattedText = new(
					((char) (App.Settings.Font.From + i)).ToString(),
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

				GlyphsWidthTotal += width + GLYPHS_SPACING;
				GlyphsHeightTotal = Math.Max(GlyphsHeightTotal, height);

				if (width > 0) {
					drawingContext.DrawText(formattedText, new(x, 0));
				}

				x += width + GLYPHS_SPACING;
			}
		}

		if (GlyphsHeightTotal > 256) {
			MessageBox.Show($"Retarded font size, pixel height is {GlyphsHeightTotal}, decrease pls");
			return;
		}

		// Rendering
		GlyphsBitmap = new(
			GlyphsWidthTotal,
			GlyphsHeightTotal,
			96,
			96,
			PixelFormats.Pbgra32
		);

		GlyphsBitmap.Render(drawingVisual);

		PreviewImage.Source = GlyphsBitmap;
		PreviewImage.Height = GlyphsHeightTotal;
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
			FileName = $"{className}.h",
			Filter = "Header files|*.h"
		};

		if (dialog.ShowDialog() != true)
			return;

		// Maybe user had changed name via dialog
		className = Path.GetFileNameWithoutExtension(dialog.FileName);

		// Bitmap
		int x = 0;

		StringBuilder
			glyphsSB = new(),
			bitmapSB = new("\t\t\t");

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
				bitmapSB.Append($"{Environment.NewLine}\t\t\t");
			}
			else {
				bitmapSB.Append(' ');
			}
		}

		FormattedText formattedText;
		int width;

		for (int i = 0; i < GlyphsFormattedTexts!.Length; i++) {
			formattedText = GlyphsFormattedTexts[i];

			width = (int) Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace);

			// Header glyph
			if (i > 0)
				glyphsSB.AppendLine();

			glyphsSB.Append($"\t\t\tyoba::Glyph({bitmapGlyphBitIndex}, {width}){(i < GlyphsFormattedTexts.Length - 1 ? "," : "")} // {(formattedText.Text == "\\" ? "backslash" : formattedText.Text)}");

			if (width > 0) {
				pixelStride = width * 4;
				pixelBuffer = new byte[pixelStride * GlyphsHeightTotal];

				GlyphsBitmap.CopyPixels(new(x, 0, width, GlyphsHeightTotal), pixelBuffer, pixelStride, 0);

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

				bitmapGlyphBitIndex += width * GlyphsHeightTotal;
			}

			x += width + GLYPHS_SPACING;
		}

		// Last byte
		if (bitmapByteBitIndex > 0)
			flushBitmapByte();

		// Saving
		Directory.CreateDirectory(Path.GetDirectoryName(dialog.FileName) ?? "");

		using FileStream fileStream = new(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
		using StreamWriter streamWriter = new(fileStream, Encoding.UTF8);

		await streamWriter.WriteAsync($$"""
class {{className}} : public yoba::Font {
	public:
		{{className}}() : yoba::Font(
			{{App.Settings.Font.From}},
			{{App.Settings.Font.To}},
			{{GlyphsHeightTotal}},
			_glyphs,
			_bitmap
		) {
			
		}

	private:
		PROGMEM const yoba::Glyph _glyphs[{{GlyphsTotal}}] = {
{{glyphsSB}}
		};

		PROGMEM const uint8_t _bitmap[{{bitmapBytesTotal}}] = {
{{bitmapSB}}
		};
};
""");
	}
}