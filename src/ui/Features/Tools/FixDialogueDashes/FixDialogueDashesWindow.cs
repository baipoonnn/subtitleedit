using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Tools.FixDialogueDashes;

public class FixDialogueDashesWindow : Window
{
    public FixDialogueDashesWindow(FixDialogueDashesViewModel vm)
    {
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = Se.Language.Tools.FixDialogueDashes.Title;
        CanResize = true;
        Width = 1000;
        Height = 700;
        MinWidth = 700;
        MinHeight = 400;
        vm.Window = this;
        DataContext = vm;

        var buttonOk = UiUtil.MakeButtonOk(vm.OkCommand);
        var buttonCancel = UiUtil.MakeButtonCancel(vm.CancelCommand);
        var panelButtons = UiUtil.MakeButtonBar(buttonOk, buttonCancel);

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Margin = UiUtil.MakeWindowMargin(),
            ColumnSpacing = 10,
            RowSpacing = 10,
            Width = double.NaN,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        grid.Add(MakeCandidatesView(vm), 0);
        grid.Add(MakeSelectionButtonsView(vm), 1);
        grid.Add(panelButtons, 2);

        Content = grid;

        Activated += delegate { buttonOk.Focus(); };
        KeyDown += vm.KeyDown;

        Closing += delegate { UiUtil.SaveWindowPosition(this); };
        Loaded += delegate { UiUtil.RestoreWindowPosition(this); };
    }

    private static Grid MakeCandidatesView(FixDialogueDashesViewModel vm)
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            ColumnSpacing = 10,
            RowSpacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var labelInfo = UiUtil.MakeLabel()
            .WithBindText(vm, nameof(vm.CandidatesInfo))
            .WithMarginTop(10)
            .WithMarginLeft(10);

        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserResizeColumns = true,
            CanUserSortColumns = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Width = double.NaN,
            Height = double.NaN,
            DataContext = vm,
            ItemsSource = vm.Candidates,
            Columns =
            {
                new DataGridTemplateColumn
                {
                    Header = Se.Language.Tools.FixDialogueDashes.ColumnApply,
                    CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
                    CellTemplate = new FuncDataTemplate<FixDialogueDashesCandidate>((_, _) =>
                    {
                        return new Border
                        {
                            Background = Brushes.Transparent,
                            Padding = new Thickness(4),
                            Child = new CheckBox
                            {
                                Focusable = false,
                                [!ToggleButton.IsCheckedProperty] = new Binding(nameof(FixDialogueDashesCandidate.IsSelected))
                                {
                                    Mode = BindingMode.TwoWay,
                                },
                                HorizontalAlignment = HorizontalAlignment.Center,
                            },
                        };
                    }),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                },
                new DataGridTextColumn
                {
                    Header = Se.Language.General.NumberSymbol,
                    Binding = new Binding(nameof(FixDialogueDashesCandidate.Number)),
                    CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
                    IsReadOnly = true,
                },
                new DataGridTextColumn
                {
                    Header = Se.Language.Tools.FixDialogueDashes.ColumnOriginal,
                    Binding = new Binding(nameof(FixDialogueDashesCandidate.OriginalTextDisplay)),
                    CellTheme = UiUtil.DataGridNoBorderCellTheme,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    IsReadOnly = true,
                },
                new DataGridTextColumn
                {
                    Header = Se.Language.Tools.FixDialogueDashes.ColumnFixed,
                    Binding = new Binding(nameof(FixDialogueDashesCandidate.FixedTextDisplay)),
                    CellTheme = UiUtil.DataGridNoBorderCellTheme,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    IsReadOnly = true,
                },
            },
        };
        _ = new DataGridCheckboxMultiSelect<FixDialogueDashesCandidate>(dataGrid,
            item => item.IsSelected, (item, v) => item.IsSelected = v);

        grid.Add(labelInfo, 0);
        grid.Add(UiUtil.MakeBorderForControlNoPadding(dataGrid), 1);

        return grid;
    }

    private static StackPanel MakeSelectionButtonsView(FixDialogueDashesViewModel vm)
    {
        return UiUtil.MakeButtonBar(
            UiUtil.MakeButton(Se.Language.Tools.FixDialogueDashes.SelectAll, vm.SelectAllCommand),
            UiUtil.MakeButton(Se.Language.Tools.FixDialogueDashes.InverseSelection, vm.InverseSelectionCommand));
    }
}
