using NexusLauncher.App.Models;
using NexusLauncher.App.ViewModels;

namespace NexusLauncher.UnitTests.App;

public sealed class StoreViewModelTests
{
    [Fact]
    public void Changing_store_scope_clears_results_from_the_previous_provider()
    {
        var viewModel = new StoreViewModel();
        var package = new StorePackage
        {
            Name = "Hades",
            Id = "1145360",
            Source = "Steam Store",
            Kind = StorePackageKind.Game,
            Action = StorePackageAction.OpenExternalStore,
            StoreUrl = "https://store.steampowered.com/app/1145360/"
        };
        viewModel.Packages.Add(package);
        viewModel.SelectedPackage = package;

        viewModel.SelectedScope = StoreViewModel.SoftwareScope;

        Assert.Equal(StoreViewModel.SoftwareScope, viewModel.SelectedScope);
        Assert.Empty(viewModel.Packages);
        Assert.Null(viewModel.SelectedPackage);
        Assert.Equal("Install selected", viewModel.PrimaryActionLabel);
    }
}
