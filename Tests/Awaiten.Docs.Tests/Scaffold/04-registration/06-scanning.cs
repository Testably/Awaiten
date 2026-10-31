namespace Scaffold;

public sealed class DiscontinuedFlatWhite : IDrink;

// A type in another assembly, as far as the page is concerned.
public static class SeasonalDrinks;

public interface IViewModel;
public interface IMainViewModel;
public sealed class MainViewModel : IViewModel, IMainViewModel;

public interface IView<TViewModel>
{
	object? DataContext { get; set; }
}

public sealed class MainView : IView<IMainViewModel>
{
	public object? DataContext { get; set; }
}
