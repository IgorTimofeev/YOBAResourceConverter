using Newtonsoft.Json;
using System.Configuration;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace YobaResourceConverter;

public partial class App : Application {
	public App() {
		// Loading settings
		if (File.Exists(Constants.SettingsPath)) {
			try {
				Settings = JsonConvert.DeserializeObject<SettingsJSON>(File.ReadAllText(Constants.SettingsPath)) ?? new();
			}
			catch (Exception ex) {
				Settings = new();
				MessageBox.Show(ex.Message);
			}
		}
		else {
			Settings = new();
		}
	}

	public static SettingsJSON Settings;

	protected override void OnExit(ExitEventArgs e) {
		// Saving settings
		Directory.CreateDirectory(Constants.AppDataPath);

		File.WriteAllText(Constants.SettingsPath, JsonConvert.SerializeObject(Settings!, Formatting.Indented));

		base.OnExit(e);
	}

	public static string Decapitalize(string s) {
		return s.Length > 1 ? $"{char.ToLower(s[0])}{s[1..]}" : s.ToLower();
	}

	public static string Capitalize(string s) {
		return s.Length > 1 ? $"{char.ToUpper(s[0])}{s[1..]}" : s.ToUpper();
	}

	public static string ConvertFileNameClassName(string fileNameWithoutExtension) {
		return Capitalize(fileNameWithoutExtension);
	}

	public static (string, string) ConvertFileNameToHeaderFileNameAndClassName(string fileName, string typeSuffix) {
		var basis = $"{GetHeaderNameRegex().Replace(fileName, "")}{typeSuffix}";

		return ($"{basis}.h", ConvertFileNameClassName(basis));
	}

	[GeneratedRegex(@"\W+")]
	public static partial Regex GetHeaderNameRegex();
}

