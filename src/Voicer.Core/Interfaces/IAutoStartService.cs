namespace Voicer.Core.Interfaces;

public interface IAutoStartService
{
    bool IsEnabled();
    void SetEnabled(bool enable);
}
