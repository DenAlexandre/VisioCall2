using CommunityToolkit.Mvvm.Input;
using Visio.Models;

namespace Visio.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}