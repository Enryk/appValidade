using Microsoft.JSInterop;

namespace MeuApp.Shared.Services;

public class ThemeService : IThemeService
{
    private readonly IJSRuntime _jsRuntime;
    private string _currentTheme = "light";
    private bool _inicializado = false;

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public bool IsDarkMode => _currentTheme == "dark";
    public string CurrentTheme => _currentTheme;
    public event Action? OnThemeChanged;

    public async Task InicializarTemaAsync()
    {
        if (_inicializado) return;

        try
        {
            var temaSalvo = await _jsRuntime.InvokeAsync<string?>("validataTheme.getTheme");
            if (!string.IsNullOrWhiteSpace(temaSalvo) && (temaSalvo == "light" || temaSalvo == "dark"))
            {
                _currentTheme = temaSalvo;
            }
            else
            {
                _currentTheme = "light";
            }

            await AplicarTemaNoDocumentoAsync(_currentTheme);
            _inicializado = true;
            OnThemeChanged?.Invoke();
        }
        catch
        {
            try
            {
                var temaFallback = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "validata_theme");
                if (!string.IsNullOrWhiteSpace(temaFallback) && (temaFallback == "light" || temaFallback == "dark"))
                {
                    _currentTheme = temaFallback;
                }
                else
                {
                    _currentTheme = "light";
                }
                await AplicarTemaNoDocumentoAsync(_currentTheme);
                _inicializado = true;
                OnThemeChanged?.Invoke();
            }
            catch
            {
                _currentTheme = "light";
            }
        }
    }

    public async Task AlternarTemaAsync()
    {
        var novoTema = IsDarkMode ? "light" : "dark";
        await DefinirTemaAsync(novoTema);
    }

    public async Task DefinirTemaAsync(string tema)
    {
        if (tema != "light" && tema != "dark")
            tema = "light";

        _currentTheme = tema;

        try
        {
            await _jsRuntime.InvokeVoidAsync("validataTheme.setTheme", _currentTheme);
        }
        catch
        {
            await AplicarTemaNoDocumentoAsync(_currentTheme);
        }

        OnThemeChanged?.Invoke();
    }

    private async Task AplicarTemaNoDocumentoAsync(string tema)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("eval", $"document.documentElement.setAttribute('data-theme', '{tema}'); try {{ localStorage.setItem('validata_theme', '{tema}'); }} catch(e){{}}");
        }
        catch
        {
            // Silencioso em render estático/SSR
        }
    }
}
