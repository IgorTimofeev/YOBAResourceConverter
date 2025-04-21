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

public partial class SettingsPage : UserControl {
	public SettingsPage() {
		InitializeComponent();

		YobaPathTextBox.Text = App.Settings.YobaPath;
	}

	private void OnYobaPathTextBoxTextChanged(object sender, TextChangedEventArgs e) {
		if (YobaPathTextBox.IsFocused)
			App.Settings.YobaPath = string.IsNullOrWhiteSpace(YobaPathTextBox.Text) ? null : YobaPathTextBox.Text;
	}
}