using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ScreenshotTool.Shell;

public partial class SettingsWindow : Window
{
    private const uint DefaultHotkeyModifiers = 0x0002 | 0x0004;
    private const uint DefaultHotkeyKey = 0x41;

    private uint _hotkeyModifiers;
    private uint _hotkeyKey;

    public SettingsWindow(AppSettings currentSettings)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);

        InitializeComponent();
        LoadSettings(currentSettings);
    }

    public AppSettings? ResultSettings { get; private set; }

    private void LoadSettings(AppSettings settings)
    {
        _hotkeyModifiers = settings.HotkeyModifiers;
        _hotkeyKey = settings.HotkeyKey;

        CtrlModifierCheckBox.IsChecked = (_hotkeyModifiers & 0x0002) != 0;
        AltModifierCheckBox.IsChecked = (_hotkeyModifiers & 0x0001) != 0;
        ShiftModifierCheckBox.IsChecked = (_hotkeyModifiers & 0x0004) != 0;
        WinModifierCheckBox.IsChecked = (_hotkeyModifiers & 0x0008) != 0;

        HotkeyKeyTextBox.Text = FormatKey(_hotkeyKey);

        SaveDirectoryTextBox.Text = settings.DefaultSaveDirectory ?? string.Empty;

        SelectOrAddComboBoxItemByTag(
            StrokeColorComboBox,
            settings.StrokeColor,
            $"自定义 {settings.StrokeColor}");
        SelectOrAddComboBoxItemByTag(
            StrokeThicknessComboBox,
            settings.StrokeThickness.ToString(CultureInfo.InvariantCulture),
            $"自定义 {settings.StrokeThickness.ToString(CultureInfo.InvariantCulture)} px");
    }

    private void HotkeyKeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key))
        {
            return;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return;
        }

        _hotkeyKey = virtualKey;
        HotkeyKeyTextBox.Text = FormatKey(_hotkeyKey);
        e.Handled = true;
    }

    private void ResetHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _hotkeyModifiers = DefaultHotkeyModifiers;
        _hotkeyKey = DefaultHotkeyKey;
        CtrlModifierCheckBox.IsChecked = true;
        AltModifierCheckBox.IsChecked = false;
        ShiftModifierCheckBox.IsChecked = true;
        WinModifierCheckBox.IsChecked = false;
        HotkeyKeyTextBox.Text = FormatKey(_hotkeyKey);
    }

    private void BrowseDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择默认保存目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(SaveDirectoryTextBox.Text)
                ? SaveDirectoryTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SaveDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSettings(out var settings, out var errorMessage))
        {
            MessageBox.Show(this, errorMessage, "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultSettings = settings;
        DialogResult = true;
    }

    private bool TryBuildSettings(out AppSettings settings, out string errorMessage)
    {
        errorMessage = string.Empty;

        var hotkeyModifiers = BuildHotkeyModifiers();
        var hotkeyKey = _hotkeyKey;
        if (hotkeyKey == 0)
        {
            settings = new AppSettings();
            errorMessage = "请先设置热键主键。";
            return false;
        }

        if (StrokeColorComboBox.SelectedItem is not ComboBoxItem colorItem || colorItem.Tag is not string strokeColor)
        {
            settings = new AppSettings();
            errorMessage = "请选择默认描边颜色。";
            return false;
        }

        if (StrokeThicknessComboBox.SelectedItem is not ComboBoxItem thicknessItem ||
            thicknessItem.Tag is not string thicknessText ||
            !double.TryParse(thicknessText, NumberStyles.Float, CultureInfo.InvariantCulture, out var strokeThickness) ||
            strokeThickness <= 0)
        {
            settings = new AppSettings();
            errorMessage = "请选择有效的描边线宽。";
            return false;
        }

        var saveDirectory = SaveDirectoryTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(saveDirectory) && !Directory.Exists(saveDirectory))
        {
            settings = new AppSettings();
            errorMessage = "默认保存目录不存在，请选择一个有效的文件夹。";
            return false;
        }

        settings = new AppSettings
        {
            HotkeyModifiers = hotkeyModifiers,
            HotkeyKey = hotkeyKey,
            DefaultSaveDirectory = string.IsNullOrWhiteSpace(saveDirectory) ? null : saveDirectory,
            StrokeColor = strokeColor,
            StrokeThickness = strokeThickness
        };
        return true;
    }

    private uint BuildHotkeyModifiers()
    {
        var modifiers = 0u;
        if (CtrlModifierCheckBox.IsChecked == true)
        {
            modifiers |= 0x0002;
        }

        if (AltModifierCheckBox.IsChecked == true)
        {
            modifiers |= 0x0001;
        }

        if (ShiftModifierCheckBox.IsChecked == true)
        {
            modifiers |= 0x0004;
        }

        if (WinModifierCheckBox.IsChecked == true)
        {
            modifiers |= 0x0008;
        }

        return modifiers;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin or
            Key.System;

    private static string FormatKey(uint virtualKey)
    {
        if (virtualKey == 0)
        {
            return "未设置";
        }

        var key = KeyInterop.KeyFromVirtualKey((int)virtualKey);
        return key switch
        {
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            _ => key.ToString()
        };
    }

    private static void SelectOrAddComboBoxItemByTag(
        System.Windows.Controls.ComboBox comboBox,
        string tagValue,
        string customContent)
    {
        foreach (var item in comboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (item.Tag?.ToString() == tagValue)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        var customItem = new System.Windows.Controls.ComboBoxItem
        {
            Content = customContent,
            Tag = tagValue
        };
        comboBox.Items.Insert(0, customItem);
        comboBox.SelectedItem = customItem;
    }
}
