namespace MeuApp.Shared.Services;

public interface IThemeService
{
    bool IsDarkMode { get; }
    string CurrentTheme { get; } // "light" ou "dark"
    event Action? OnThemeChanged;
    Task InicializarTemaAsync();
    Task AlternarTemaAsync();
    Task DefinirTemaAsync(string tema);
}
