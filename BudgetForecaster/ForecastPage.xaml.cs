namespace BudgetForecaster;

public partial class ForecastPage : ContentPage
{
    private readonly ForecastViewModel _vm;

    public ForecastPage(ForecastViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
