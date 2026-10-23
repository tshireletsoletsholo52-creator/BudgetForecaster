namespace BudgetForecaster;

public partial class StatementPage : ContentPage
{
    private readonly StatementViewModel _vm;

    public StatementPage(StatementViewModel vm)
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
