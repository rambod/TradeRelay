namespace TradeRelay.Desktop.Services;

internal interface IUiDispatcher
{
    void Post(Action action);
}
