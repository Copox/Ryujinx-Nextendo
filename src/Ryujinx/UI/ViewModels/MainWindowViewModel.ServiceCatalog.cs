using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
namespace Ryujinx.Ava.UI.ViewModels
{
    public partial class MainWindowViewModel
    {
        [RelayCommand]
        private Task RefreshServiceInformationAsync() => Ryujinx.Ava.Common.NextendoServiceCatalog.RefreshNowAsync();
    }
}
