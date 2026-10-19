using System.Windows.Controls;
using UltimatePCOptimizerPro.ViewModels;

namespace UltimatePCOptimizerPro.Views;

public partial class TweakListView : UserControl
{
    public TweakListView() => InitializeComponent();

    private void OnProfilePicked(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is TweakListViewModel vm && ProfilePicker.SelectedItem is string name)
            vm.LoadProfileCommand.Execute(name);
    }
}
