using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RenOff.App;

public partial class MainWindow : Window
{
    private System.Windows.Point _dragStartPoint;

    public MainWindow()
    {
        InitializeComponent();
        var icon = App.GetAppIconImageSource();
        if (icon is not null)
        {
            Icon = icon;
        }
        DataContext = new MainViewModel();
        Closing += OnClosing;
        Activated += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.MarkListViewed();
            }
        };
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (App.IsExiting || App.IsRecreatingWindow) return;

        if (DataContext is MainViewModel vm && vm.CloseToTrayEnabled)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void ItemsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStartPoint = e.GetPosition(null);

    private void ItemsListBox_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not System.Windows.Controls.ListBox listBox) return;

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var listBoxItem = FindAncestor<System.Windows.Controls.ListBoxItem>((DependencyObject)e.OriginalSource);
        if (listBoxItem?.DataContext is not RenOffItemViewModel itemVm) return;

        System.Windows.DragDrop.DoDragDrop(
            listBox,
            new System.Windows.DataObject(typeof(RenOffItemViewModel), itemVm),
            System.Windows.DragDropEffects.Move);
    }

    private void ItemsListBox_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(RenOffItemViewModel))
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void ItemsListBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not System.Windows.Controls.ListBox listBox) return;
        if (!e.Data.GetDataPresent(typeof(RenOffItemViewModel))) return;

        var dragged = e.Data.GetData(typeof(RenOffItemViewModel)) as RenOffItemViewModel;
        if (dragged is null) return;

        var targetItem = FindAncestor<System.Windows.Controls.ListBoxItem>((DependencyObject)e.OriginalSource);
        var targetVm = targetItem?.DataContext as RenOffItemViewModel;

        var targetIndex = targetVm is null ? vm.Items.Count - 1 : vm.Items.IndexOf(targetVm);
        vm.MoveItem(dragged, targetIndex);
        listBox.SelectedItem = dragged;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
