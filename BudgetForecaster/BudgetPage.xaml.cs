namespace BudgetForecaster;

public partial class BudgetPage : ContentPage
{
    private readonly BudgetViewModel _vm;

    public BudgetPage(BudgetViewModel vm)
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
