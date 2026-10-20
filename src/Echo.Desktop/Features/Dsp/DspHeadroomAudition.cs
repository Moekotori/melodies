using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    private double? headroomRestore;
    [ObservableProperty] private bool headroomListening;
    public async Task SetHeadroomListenAsync(bool listen)
    {
        if (session is null) return;
        double target;
        if (listen)
        {
            if (!CanEdit || SelectedModule.Id != "headroom" || headroomRestore is not null) return;
            target = 0; headroomRestore = Parameter("headroomDb");
        }
        else
        {
            if (headroomRestore is not { } restore) return;
            target = restore; headroomRestore = null;
        }
        try
        {
            await session.DspAsync("dsp.setHeadroom", Json(new JsonArray(target)), token: CancellationToken.None);
            if (!disposed) HeadroomListening = headroomRestore is not null;
        }
        catch (Exception error) { if (!disposed) Message = "余量试听失败：" + error.Message; }
    }
}
