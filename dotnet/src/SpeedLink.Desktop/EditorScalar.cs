using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private bool scalarDraft, updatingScalar, revertingTab, revertingBaseline;
    private bool ScalarMap => table is { Count: 1 };

    private void RefreshScalar()
    {
        if (!ScalarMap || document is null || table is null) return;
        updatingScalar = true;
        try
        {
            double value = document.Read(table, 0), reference = document.Read(table, 0, Baseline), delta = value - reference;
            ScalarUnits.Text = table.IsChoice ? "" : table.Units;
            ScalarInput.Text = ScalarInput.IsKeyboardFocused ? value.ToString("R", CultureInfo.InvariantCulture) : table.Compact(value);
            ScalarInput.ToolTip = "Stored value: " + value.ToString("R", CultureInfo.InvariantCulture) + " " + table.Units;
            ScalarInput.Visibility = table.IsChoice ? Visibility.Collapsed : Visibility.Visible;
            ScalarChoice.Visibility = table.IsChoice ? Visibility.Visible : Visibility.Collapsed;
            ScalarActions.Visibility = Visibility.Collapsed;
            ScalarChoice.ItemsSource = ChoiceBox.ItemsSource;
            ScalarChoice.SelectedItem = ChoiceBox.SelectedItem;
            string State(double number) => table.IsChoice && ScalarChoice.ItemsSource is IEnumerable<TuningState> states
                ? states.FirstOrDefault(s => s.Value == number)?.Name ?? number.ToString("G10", CultureInfo.InvariantCulture)
                : table.Compact(number) + " " + table.Units;
            ScalarReference.Text = State(reference);
            ScalarReference.ToolTip = reference.ToString("R", CultureInfo.InvariantCulture) + " " + table.Units;
            bool changed = document.CellChanged(table, 0, Baseline);
            ScalarDifference.Text = !changed ? "Unchanged" : table.IsChoice ? "Changed"
                : "Δ " + (delta > 0 ? "+" : "") + table.Compact(delta) + " " + table.Units;
            ScalarDifference.ToolTip = "Difference from the selected baseline";
            ScalarDifference.Visibility = Visibility.Visible;
            ScalarDifference.Foreground = changed ? UiTheme.Amber : UiTheme.Muted;
            ScalarError.Visibility = Visibility.Collapsed; scalarDraft = false;
            ScalarApply.IsEnabled = ScalarCancel.IsEnabled = false;
        }
        finally { updatingScalar = false; }
    }
    private void ScalarInput_Focused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ScalarMap || table is null || document is null || scalarDraft) return;
        updatingScalar = true;
        ScalarInput.Text = document.Read(table, 0).ToString("R", CultureInfo.InvariantCulture); ScalarInput.SelectAll();
        updatingScalar = false;
    }
    private void ScalarInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (updatingScalar || refreshing || !ScalarMap) return;
        scalarDraft = true; ScalarApply.IsEnabled = ScalarCancel.IsEnabled = true;
        ScalarActions.Visibility = Visibility.Visible; ScalarDifference.Visibility = Visibility.Collapsed;
        ScalarError.Visibility = Visibility.Collapsed;
    }
    private bool CommitScalar()
    {
        if (!ScalarMap || !scalarDraft || table is null || document is null) return true;
        try
        {
            if (!double.TryParse(ScalarInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                throw new ArgumentException("Enter a finite number using a decimal point.");
            document.Apply([new(table, 0, value)], "Edit " + table.Name);
            RefreshScalar(); ClearPendingEntryStatus(); return true;
        }
        catch (ArgumentException error)
        {
            ScalarError.Text = error.Message; ScalarError.Visibility = Visibility.Visible;
            ScalarInput.Focus(); return false;
        }
    }
    private void ScalarApply_Click(object sender, RoutedEventArgs e) => CommitScalar();
    private void ScalarCancel_Click(object sender, RoutedEventArgs e)
    {
        RefreshScalar();
        ClearPendingEntryStatus();
    }
    private void ScalarInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitScalar(); e.Handled = true; }
        else if (e.Key == Key.Escape) { ScalarCancel_Click(sender, e); ScalarInput.SelectAll(); e.Handled = true; }
    }
    private void ScalarChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!updatingScalar && !refreshing && ScalarMap && table is not null && document is not null && ScalarChoice.SelectedItem is TuningState state)
            Attempt(() => document.Apply([new(table, 0, state.Value)], "Switch " + table.Name));
    }
}
