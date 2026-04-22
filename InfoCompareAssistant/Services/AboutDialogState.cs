namespace InfoCompareAssistant.Services;

/// <summary>
/// 在多个 InteractiveServer 根组件之间共享「关于」弹窗开关（避免静态 Layout 与交互岛之间的 EventCallback 不生效）。
/// 本应用为 Photino 单机窗口，在 <see cref="Program"/> 中注册为 Singleton；若改为多用户 Web 托管需重新评估生命周期。
/// </summary>
public sealed class AboutDialogState
{
    public bool IsOpen { get; private set; }

    public event Action? Changed;

    public void Open()
    {
        if (IsOpen)
            return;
        IsOpen = true;
        Changed?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        Changed?.Invoke();
    }
}
